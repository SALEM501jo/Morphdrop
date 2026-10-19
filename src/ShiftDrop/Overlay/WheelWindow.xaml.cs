using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ShiftDrop.Actions;
using ShiftDrop.Interop;
using static ShiftDrop.Interop.NativeMethods;

namespace ShiftDrop.Overlay;

/// <summary>
/// The radial menu: a small topmost, non-activating, layered window that is an OLE drop target.
/// When Shift is pressed mid-drag it is placed under the cursor as an invisible "ghost", and it only shows
/// itself once the drag really enters it carrying files. Other drags (text selection, moving windows,
/// rubber-band selection) never deliver a DragEnter, so they never see it.
/// </summary>
internal partial class WheelWindow : Window
{
    private enum Phase { Hidden, Ghost, Active }

    private static readonly TimeSpan GhostTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan DropTimeout = TimeSpan.FromSeconds(1);

    private readonly ActionRunner _runner;
    private readonly Action<EditAction, string> _openEditor;
    private readonly Action<IFileAction, string[]> _run;
    private readonly DropTargetHelper _dropHelper = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly IntPtr _hwnd;
    private Phase _phase = Phase.Hidden;
    private int _fadeGeneration;
    private string[] _files = Array.Empty<string>();
    private IReadOnlyList<ActionPage> _pages = Array.Empty<ActionPage>();
    private int _pageIndex;
    private string _hubTitle = "";
    private string _hubSubtitle = "";

    /// <param name="openEditor">Opens an Edit page tool for a file; called after the drop has completed.</param>
    /// <param name="run">Runs a dropped action (the app makes sure FFmpeg is there for video and audio).</param>
    /// <summary>The pages the Settings window leaves on; null shows them all.</summary>
    public IReadOnlySet<string>? EnabledPages { get; set; }

    /// <summary>The trigger key's name, for the hub's "Shift → Tools" hint.</summary>
    public string TriggerName { get; set; } = "Shift";

    public Settings.ThemeChoice Theme { get; set; } = Settings.ThemeChoice.System;

    public WheelWindow(ActionRunner runner, Action<EditAction, string> openEditor, Action<IFileAction, string[]> run)
    {
        _runner = runner;
        _openEditor = openEditor;
        _run = run;
        InitializeComponent();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _hideTimer.Tick += (_, _) =>
        {
            Log.Info($"Wheel hidden by timeout while {_phase}");
            HideNow();
        };

        SourceInitialized += (_, _) => MakeNonActivating();
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Shows and hides the window once off-screen so the first real use doesn't pay for setup.</summary>
    public void Prewarm()
    {
        Show();
        Hide();
    }

    private IReadOnlyList<IFileAction> Actions => _pages.Count > 0 ? _pages[_pageIndex].Actions : Array.Empty<IFileAction>();

    /// <summary>
    /// Shift was pressed mid-drag. The first press puts the (still invisible) wheel under the cursor; pressing it
    /// again while the wheel is open turns to the next page.
    /// </summary>
    public void OnShiftPressed(POINT cursor)
    {
        switch (_phase)
        {
            case Phase.Hidden:
                ShowGhost(cursor);
                break;
            case Phase.Active when _pages.Count > 1:
                NextPage();
                break;
        }
    }

    private void ShowGhost(POINT cursor)
    {
        _phase = Phase.Ghost;
        _fadeGeneration++;
        Wheel.BeginAnimation(OpacityProperty, null);
        Wheel.Opacity = 0;
        Wheel.Highlight(WheelGeometry.OutsideIndex);

        if (!IsVisible)
            Show();
        CenterOn(cursor);
        StartHideTimer(GhostTimeout);
        Log.Info($"Shift pressed mid-drag at ({cursor.X}, {cursor.Y}); waiting for the drag to enter the wheel");
    }

    /// <summary>The mouse button was released (seen by the hook). OLE normally follows with Drop or DragLeave.</summary>
    public void OnDragEnded()
    {
        switch (_phase)
        {
            case Phase.Ghost:
                HideNow(); // the drag never reached the wheel
                break;
            case Phase.Active:
                StartHideTimer(DropTimeout); // safety net in case neither Drop nor DragLeave arrives
                break;
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _hideTimer.Stop();
        string[]? files = ReadFiles(e.Data);
        if (files is null)
        {
            Log.Info("A drag without files entered the wheel; hiding it");
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideNow();
            return;
        }

        if (_phase != Phase.Active)
            Open(files);
        UpdateHover(e);
        _dropHelper.DragEnter(_hwnd, e.Data, e.Effects);
        KeepAboveDragImage();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_phase == Phase.Active)
        {
            UpdateHover(e);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        _dropHelper.DragOver(e.Effects);
        if (_phase == Phase.Active)
            KeepAboveDragImage();
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dropHelper.DragLeave();
        if (_phase == Phase.Active)
            FadeOut();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        int index = _phase == Phase.Active
            ? WheelGeometry.HitTest(e.GetPosition(Wheel), Actions.Count)
            : WheelGeometry.OutsideIndex;

        if (index >= 0)
        {
            IFileAction action = Actions[index];
            e.Effects = CopyEffect(e.AllowedEffects);
            Log.Info($"Dropped on '{action.Label}': {action.Description} for {_files.Length} item(s)");
            if (action is EditAction edit)
            {
                // Editors take one file; open it once the drop has returned, so the drag source isn't kept waiting.
                if (_files.FirstOrDefault(file => File.Exists(file) && edit.AppliesTo(file)) is { } file)
                    Dispatcher.BeginInvoke(() => _openEditor(edit, file));
            }
            else
            {
                string[] files = _files;
                // May ask a question first, so not inside the drop, and only once the wheel has faded away.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _run(action, files));
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            Log.Info("Dropped on the hub: cancelled");
        }

        e.Handled = true;
        _dropHelper.Drop(e.Data, e.Effects);
        FadeOut();
    }

    private void Open(string[] files)
    {
        _files = files;
        _pages = ActionCatalog.For(files, EnabledPages);
        Wheel.SetDarkTheme(UseDarkTheme());
        _pageIndex = 0;
        (_hubTitle, _hubSubtitle) = Describe(files, _pages.Count);
        ShowPage();
        Wheel.SetHub(_hubTitle, _hubSubtitle);

        _phase = Phase.Active;
        _fadeGeneration++;
        Wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        Wheel.PlayOpenAnimation();
        string pages = _pages.Count == 0 ? "no actions" : string.Join(", ", _pages.Select(page => $"{page.Name} ({page.Actions.Count})"));
        Log.Info($"Wheel opened for {files.Length} item(s) starting with '{Path.GetFileName(files[0])}': {pages}");
    }

    private void NextPage()
    {
        _pageIndex = (_pageIndex + 1) % _pages.Count;
        ShowPage();
        Wheel.PlayPageTurnAnimation();

        // Highlight the slice under the cursor right away; the next DragOver updates the drop effect.
        GetCursorPos(out POINT cursor);
        UpdateHighlight(WheelGeometry.HitTest(Wheel.PointFromScreen(new Point(cursor.X, cursor.Y)), Actions.Count));
        Log.Info($"Turned to the {_pages[_pageIndex].Name} page");
    }

    private void ShowPage()
    {
        Wheel.SetSlices(Actions.Select(action => new WheelItem(action.Label, action.Glyph)).ToList());
        Wheel.SetHint(_pages.Count > 1 ? $"{TriggerName} → {_pages[(_pageIndex + 1) % _pages.Count].Name}" : null);
    }

    private void UpdateHover(DragEventArgs e)
    {
        int index = WheelGeometry.HitTest(e.GetPosition(Wheel), Actions.Count);
        UpdateHighlight(index);
        e.Effects = index >= 0 ? CopyEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
    }

    private void UpdateHighlight(int index)
    {
        Wheel.Highlight(index);
        Wheel.SetHub(_hubTitle, index switch
        {
            >= 0 => Actions[index] is ICombiningAction combining && combining.Combines(_files)
                ? combining.CombinedDescription(_files.Length)
                : Actions[index].Description,
            WheelGeometry.HubIndex when Actions.Count > 0 => "Release here to cancel",
            _ => _hubSubtitle,
        });
    }

    /// <summary>"System" follows Windows' app mode, read on each opening so a change applies right away.</summary>
    private bool UseDarkTheme() => Theme switch
    {
        Settings.ThemeChoice.Dark => true,
        Settings.ThemeChoice.Light => false,
        _ => Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0,
    };

    private void FadeOut()
    {
        _phase = Phase.Hidden;
        _hideTimer.Stop();
        int generation = ++_fadeGeneration;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            if (generation == _fadeGeneration)
                Hide();
        };
        Wheel.BeginAnimation(OpacityProperty, fade);
    }

    private void HideNow()
    {
        _phase = Phase.Hidden;
        _hideTimer.Stop();
        _fadeGeneration++;
        Wheel.BeginAnimation(OpacityProperty, null);
        Wheel.Opacity = 0;
        Hide();
    }

    private void StartHideTimer(TimeSpan delay)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = delay;
        _hideTimer.Start();
    }

    private void MakeNonActivating()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    /// <summary>Centres the window on a point in physical pixels, kept inside that monitor's work area.</summary>
    private void CenterOn(POINT point)
    {
        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref monitor);
        RECT work = monitor.rcWork;

        // The second pass uses the size WPF settled on if the move crossed onto a monitor with another DPI.
        for (int pass = 0; pass < 2; pass++)
        {
            GetWindowRect(_hwnd, out RECT bounds);
            int x = Math.Clamp(point.X - bounds.Width / 2, work.Left, Math.Max(work.Left, work.Right - bounds.Width));
            int y = Math.Clamp(point.Y - bounds.Height / 2, work.Top, Math.Max(work.Top, work.Bottom - bounds.Height));
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// The shell draws the dragged file's thumbnail in its own topmost window ("SysDragImage"), which would sit on
    /// the hub's text. IDropTargetHelper.Show(false) doesn't hide Explorer's thumbnail, so the open wheel keeps
    /// itself above that window instead; the nearly opaque hub and slices then hide it.
    /// </summary>
    private void KeepAboveDragImage()
    {
        IntPtr dragImage = FindWindow("SysDragImage", null);
        if (dragImage == IntPtr.Zero || !IsWindowVisible(dragImage))
            return;

        // Only move when the thumbnail is above the wheel, so repeated calls during the drag cost nothing.
        for (IntPtr above = NativeMethods.GetWindow(_hwnd, GW_HWNDPREV); above != IntPtr.Zero; above = NativeMethods.GetWindow(above, GW_HWNDPREV))
        {
            if (above == dragImage)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }
        }
    }

    private static string[]? ReadFiles(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
                ? files
                : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    // Never answer Move: the drag source (e.g. Explorer) would then delete the original file.
    private static DragDropEffects CopyEffect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private static (string Title, string Subtitle) Describe(string[] files, int pageCount)
    {
        if (files.Length > 1)
            return ($"{files.Length} items", pageCount > 0 ? "Drop on an action" : "Nothing to do with these yet");

        string path = files[0];
        if (Directory.Exists(path))
            return ("Folder", "Folders aren't supported yet");

        string size;
        try
        {
            size = ByteSize.Format(new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            size = "File";
        }

        string extension = Path.GetExtension(path);
        return (size, pageCount > 0
            ? Path.GetFileName(path)
            : $"Nothing for {(extension.Length > 0 ? extension : "these")} files yet");
    }
}
