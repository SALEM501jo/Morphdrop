using System.Diagnostics;
using ShiftDrop.Actions;
using ShiftDrop.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ShiftDrop.Tray;

/// <summary>The notification-area icon, ShiftDrop's only permanent UI.</summary>
internal sealed class TrayIcon : IDisposable
{
    private static readonly Drawing.Color Accent = Drawing.Color.FromArgb(255, 95, 58);

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _icon;
    private string? _revealOnClick;

    public TrayIcon()
    {
        _icon = CreateIcon(Forms.SystemInformation.SmallIconSize.Width);

        var pause = new Forms.ToolStripMenuItem("Pause") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => PausedChanged?.Invoke(pause.Checked);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(new Forms.ToolStripMenuItem("Drag a file, then press Shift") { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(pause);
        _menu.Items.Add("Open log", null, (_, _) => OpenLog());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "ShiftDrop",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.BalloonTipClicked += (_, _) => RevealOutput();
    }

    public event Action<bool>? PausedChanged;

    public event Action? ExitRequested;

    public void ShowMessage(string title, string text) => Show(title, text, Forms.ToolTipIcon.Info, reveal: null);

    public void ShowError(string title, string text) => Show(title, text, Forms.ToolTipIcon.Error, reveal: null);

    public void ShowResult(ActionResult result)
    {
        if (result.Errors.Count > 0)
        {
            (string input, Exception error) = result.Errors[0];
            string more = result.Errors.Count > 1 ? $" ({result.Errors.Count} files)" : "";
            ShowError($"{result.Action.Description} failed{more}", $"{Path.GetFileName(input)}: {error.Message}");
        }
        else if (result.Outputs.Count == 1)
        {
            (string input, string output) = result.Outputs[0];
            Show($"Saved {Path.GetFileName(output)}", $"{SizeChange(input, output)}Click to show it in File Explorer.", Forms.ToolTipIcon.Info, output);
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
        if (_revealOnClick is { } path && File.Exists(path))
            Start("explorer.exe", $"/select,\"{path}\"");
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

    /// <summary>Four arcs around a dot: a tiny version of the wheel.</summary>
    private static Drawing.Icon CreateIcon(int size)
    {
        using var bitmap = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        using (var pen = new Drawing.Pen(Accent, size * 0.18f))
        using (var brush = new Drawing.SolidBrush(Accent))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float inset = size * 0.14f;
            var ring = new Drawing.RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
            for (int i = 0; i < 4; i++)
                graphics.DrawArc(pen, ring, -90 - 32 + i * 90, 64);

            float dot = size * 0.26f;
            graphics.FillEllipse(brush, (size - dot) / 2, (size - dot) / 2, dot, dot);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
