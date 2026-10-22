using System.Diagnostics;
using Morphdrop.Actions;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Morphdrop.Tray;

/// <summary>The notification-area icon, Morphdrop's only permanent UI.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _icon;
    private const int RecentCount = 8;

    private readonly Forms.ToolStripMenuItem _hint = new("Drag a file, then press Shift") { Enabled = false };
    private readonly Forms.ToolStripMenuItem _recent = new("Recent") { Enabled = false };
    private readonly List<string> _recentOutputs = new();
    private string? _revealOnClick;

    public TrayIcon()
    {
        _icon = CreateIcon(Forms.SystemInformation.SmallIconSize.Width);

        var pause = new Forms.ToolStripMenuItem("Pause") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => PausedChanged?.Invoke(pause.Checked);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(_hint);
        _menu.Items.Add(_recent);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke()) { Font = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold) });
        _menu.Items.Add(pause);
        _menu.Items.Add("Open log", null, (_, _) => OpenLog());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Morphdrop",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.BalloonTipClicked += (_, _) => RevealOutput();
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public event Action<bool>? PausedChanged;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    /// <summary>With "only problems" chosen, results and progress notices stay quiet; errors are always shown.</summary>
    public bool OnlyNotifyProblems { get; set; }

    public void SetTriggerName(string key) => _hint.Text = $"Drag a file, then press {key}";

    /// <summary>A short status in the icon's tooltip (e.g. progress), or null for the plain name.</summary>
    public void SetStatus(string? status) => _notifyIcon.Text = status is null ? "Morphdrop" : Truncate($"Morphdrop – {status}", 127);

    /// <summary>Shown when a job is still running after a moment; the result notification replaces it.</summary>
    public void ShowWorking(IFileAction action, IReadOnlyList<string> inputs)
    {
        if (OnlyNotifyProblems)
            return;

        string what = inputs.Count == 1 ? Path.GetFileName(inputs[0]) : $"{inputs.Count} files";
        Show($"{action.Description}…", $"Working on {what}. You'll get a notification when it's done.", Forms.ToolTipIcon.None, reveal: null);
    }

    public void ShowMessage(string title, string text) => Show(title, text, Forms.ToolTipIcon.Info, reveal: null);

    public void ShowError(string title, string text) => Show(title, text, Forms.ToolTipIcon.Error, reveal: null);

    public void ShowResult(ActionResult result)
    {
        foreach ((_, string output) in result.Outputs)
            Remember(output);

        if (result.Errors.Count > 0)
        {
            (string input, Exception error) = result.Errors[0];
            string more = result.Errors.Count > 1 ? $" ({result.Errors.Count} files)" : "";
            ShowError($"{result.Action.Description} failed{more}", $"{Path.GetFileName(input)}: {error.Message}");
        }
        else if (OnlyNotifyProblems)
        {
            return;
        }
        else if (result.Outputs.Count == 1)
        {
            (string input, string output) = result.Outputs[0];
            string detail = result.Action is Text.TextAction ? "The text is on the clipboard too. " : SizeChange(input, output);
            Show($"Saved {Path.GetFileName(output)}", $"{detail}Click to show it in File Explorer.", Forms.ToolTipIcon.Info, output);
        }
        else if (result.Outputs.Count > 1)
        {
            Show($"Saved {result.Outputs.Count} files", "Click to show them in File Explorer.", Forms.ToolTipIcon.Info, result.Outputs[^1].Output);
        }
        else if (result.Skipped.Count > 0)
        {
            (string input, string reason) = result.Skipped[0];
            ShowMessage($"Nothing to do for {Path.GetFileName(input)}", reason);
        }
    }

    private static string SizeChange(string input, string output)
    {
        try
        {
            if (Directory.Exists(output))
                return $"{Directory.GetFiles(output).Length} pages. ";
            return $"{ByteSize.Format(new FileInfo(input).Length)} → {ByteSize.Format(new FileInfo(output).Length)}. ";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private void Show(string title, string text, Forms.ToolTipIcon icon, string? reveal)
    {
        _revealOnClick = reveal;
        _notifyIcon.ShowBalloonTip(5000, Truncate(title, 63), Truncate(text.Length > 0 ? text : " ", 255), icon);
    }

    private void RevealOutput()
    {
        if (_revealOnClick is { } path)
            Reveal(path);
    }

    private static void Reveal(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Start("explorer.exe", $"/select,\"{path}\"");
    }

    /// <summary>Keeps the last few results in the Recent menu, newest first; clicking one shows it in File Explorer.</summary>
    private void Remember(string output)
    {
        _recentOutputs.Remove(output);
        _recentOutputs.Insert(0, output);
        if (_recentOutputs.Count > RecentCount)
            _recentOutputs.RemoveAt(_recentOutputs.Count - 1);

        _recent.DropDownItems.Clear();
        foreach (string path in _recentOutputs)
        {
            var item = new Forms.ToolStripMenuItem(Path.GetFileName(path), null, (_, _) => Reveal(path)) { ToolTipText = path };
            _recent.DropDownItems.Add(item);
        }

        _recent.Enabled = true;
    }

    private static void OpenLog()
    {
        if (File.Exists(Log.FilePath))
            Start(Log.FilePath, null, useShell: true);
    }

    private static void Start(string file, string? arguments, bool useShell = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments ?? "") { UseShellExecute = useShell })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not start {file}", ex);
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>The logo from the embedded .ico, at the size Windows asks for (each size is drawn for it).</summary>
    private static Drawing.Icon CreateIcon(int size)
    {
        using Stream stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Morphdrop.ico")
            ?? throw new InvalidOperationException("The Morphdrop.ico resource is missing.");
        return new Drawing.Icon(stream, new Drawing.Size(size, size));
    }
}
