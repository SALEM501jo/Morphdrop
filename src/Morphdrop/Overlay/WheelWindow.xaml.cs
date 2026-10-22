using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Morphdrop.Actions;
using Morphdrop.Interop;
using static Morphdrop.Interop.NativeMethods;

namespace Morphdrop.Overlay;

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
    private static readonly TimeSpan ClickTimeout = TimeSpan.FromSeconds(30);

    private readonly ActionRunner _runner;
    private readonly Action<EditAction, string> _openEditor;
    private readonly Action<IFileAction, string[]> _run;
    private readonly DropTargetHelper _dropHelper = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly IntPtr _hwnd;
    private Phase _state = Phase.Hidden;
    private int _fadeGeneration;
    private string[] _files = Array.Empty<string>();
    private BrowserDrop? _browser; // a picture or link dragged out of a browser, saved on drop
    private bool _clickMode; // opened from the right-click menu: a slice is picked with a click, not a drop
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
        MouseMove += (_, e) =>
        {
            if (_clickMode && _phase == Phase.Active)
                UpdateHighlight(WheelGeometry.HitTest(e.GetPosition(Wheel), Actions.Count));
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_clickMode)
                ClickAt(e.GetPosition(Wheel));
        };
        MouseRightButtonUp += (_, _) =>
        {
            if (_clickMode)
                FadeOut();
        };
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

    /// <summary>Raised when the wheel opens for a drag or closes; the mouse hook turns pages with the scroll wheel meanwhile.</summary>
    public event Action<bool>? ActiveChanged;

    private Phase _phase
    {
        get => _state;
        set
        {
            bool wasActive = _state == Phase.Active;
            _state = value;
            if (wasActive != (value == Phase.Active))
                ActiveChanged?.Invoke(value == Phase.Active);
        }
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
        BrowserDrop? browser = files is null ? BrowserDrop.Read(e.Data) : null;
        if (files is null && browser is null)
        {
            Log.Info($"A drag without files entered the wheel ({string.Join(", ", SafeFormats(e.Data))}); hiding it");
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideNow();
            return;
        }

        if (_phase != Phase.Active)
            Open(files ?? new[] { Path.Combine(BrowserDrop.DownloadsFolder(), browser!.FileName) }, browser);
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
            if (_browser is { } browser)
            {
                DropFromBrowser(browser, action, e.Data);
            }
            else
            {
                Act(action, _files);
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

    private void Open(string[] files, BrowserDrop? browser = null)
    {
        _files = files;
        _browser = browser;
        _pages = browser is null
            ? ActionCatalog.For(files, EnabledPages)
            : ActionCatalog.ForFileName(browser.FileName).Where(page => EnabledPages is null || EnabledPages.Contains(page.Name)).ToList();
        Wheel.SetDarkTheme(UseDarkTheme());
        _pageIndex = 0;
        (_hubTitle, _hubSubtitle) = browser is null
            ? Describe(files, _pages.Count)
            : (browser.Link is null ? "From the browser" : "Link", browser.FileName);
        ShowPage();
        Wheel.SetHub(_hubTitle, _hubSubtitle);

        _phase = Phase.Active;
        _fadeGeneration++;
        Wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        Wheel.PlayOpenAnimation();
        string pages = _pages.Count == 0 ? "no actions" : string.Join(", ", _pages.Select(page => $"{page.Name} ({page.Actions.Count})"));
        Log.Info($"Wheel opened for {files.Length} item(s) starting with '{Path.GetFileName(files[0])}': {pages}");
    }

    /// <summary>
    /// Runs the picked slice once the drop (or click) has returned, so the drag source isn't kept waiting. Editors take
    /// one file; the rest may ask a question first, so they wait until the wheel has faded away.
    /// </summary>
    private void Act(IFileAction action, string[] files)
    {
        if (action is EditAction edit)
        {
            if (files.FirstOrDefault(file => File.Exists(file) && edit.AppliesTo(file)) is { } file)
                Dispatcher.BeginInvoke(() => _openEditor(edit, file));
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _run(action, files));
        }
    }

    /// <summary>The right-click menu's way in: the wheel opens at the cursor, and a click on a slice runs it.</summary>
    public void ShowForClick(string[] files)
    {
        GetCursorPos(out POINT cursor);
        _hideTimer.Stop();
        _fadeGeneration++;
        Wheel.BeginAnimation(OpacityProperty, null);
        Wheel.Opacity = 0;
        if (!IsVisible)
            Show();
        CenterOn(cursor);
        _clickMode = true;
        Open(files);
        UpdateHighlight(WheelGeometry.HitTest(Wheel.PointFromScreen(new Point(cursor.X, cursor.Y)), Actions.Count));
        StartHideTimer(ClickTimeout);
    }

    /// <summary>A mouse button went down anywhere (seen by the hook): outside the open wheel, that closes it.</summary>
    public void OnButtonPressed(POINT point)
    {
        if (!_clickMode || _phase != Phase.Active)
            return;
        if (WheelGeometry.HitTest(Wheel.PointFromScreen(new Point(point.X, point.Y)), Actions.Count) == WheelGeometry.OutsideIndex)
            FadeOut();
    }

    private void ClickAt(Point point)
    {
        int index = WheelGeometry.HitTest(point, Actions.Count);
        if (index >= 0)
        {
            IFileAction action = Actions[index];
            Log.Info($"Clicked '{action.Label}': {action.Description} for {_files.Length} item(s)");
            Act(action, _files);
        }

        FadeOut();
    }

    /// <summary>
    /// A browser's picture is saved inside the drop (its data is gone afterwards); a link is downloaded after it. Either
    /// lands in Downloads and then gets the action, like a file dropped from Explorer.
    /// </summary>
    private void DropFromBrowser(BrowserDrop browser, IFileAction action, IDataObject data)
    {
        Task<string> saving;
        try
        {
            saving = browser.SaveAsync(data);
        }
        catch (Exception ex)
        {
            saving = Task.FromException<string>(ex);
        }

        saving.ContinueWith(task =>
        {
            if (task.Exception?.GetBaseException() is { } failure)
            {
                Log.Error($"Couldn't save '{browser.FileName}' from the browser", failure);
                _runner.RunJob(action, browser.FileName, () => throw new InvalidDataException($"Couldn't get it from the browser: {failure.Message}"));
                return;
            }

            string saved = task.Result;
            Log.Info($"Saved '{saved}' from the browser");
            if (action is EditAction edit)
                _openEditor(edit, saved);
            else
                _run(action, new[] { saved });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static IEnumerable<string> SafeFormats(IDataObject data)
    {
        try
        {
            return data.GetFormats();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>The scroll wheel turned mid-drag: down for the next page, up for the previous one.</summary>
    public void OnScrolled(int direction)
    {
        if (_phase == Phase.Active && _pages.Count > 1)
            NextPage(direction < 0 ? 1 : -1);
    }

    private void NextPage(int step = 1)
    {
        _pageIndex = ((_pageIndex + step) % _pages.Count + _pages.Count) % _pages.Count;
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
        string turn = _clickMode ? "Scroll" : TriggerName;
        Wheel.SetHint(_pages.Count > 1 ? $"{turn} → {_pages[(_pageIndex + 1) % _pages.Count].Name}" : null);
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
    private bool UseDarkTheme() => Settings.AppTheme.Resolve(Theme);

    private void FadeOut()
    {
        _clickMode = false;
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
        _clickMode = false;
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
            return ("Folder", pageCount > 0 ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : "Nothing to do with folders yet");

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
