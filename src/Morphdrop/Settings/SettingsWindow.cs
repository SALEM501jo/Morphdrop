using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Morphdrop.Editing;
using Morphdrop.Tray;

namespace Morphdrop.Settings;

/// <summary>Morphdrop's settings. Every change is saved and applied at once; there is no Save button.</summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly SettingsStore _store;
    private readonly AutoStart _autoStart;
    private readonly TextBlock _folderText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };

    private SettingsWindow(SettingsStore store, AutoStart autoStart)
    {
        _store = store;
        _autoStart = autoStart;
        AppSettings settings = store.Current;

        Title = "Morphdrop settings";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AppTheme.Attach(this);
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var startWithWindows = new CheckBox { Content = "Start Morphdrop when I sign in to Windows", IsChecked = autoStart.IsEnabled };
        startWithWindows.Click += (_, _) => SetAutoStart(startWithWindows);

        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        page.Children.Add(Section("Key", "Press it while dragging a file to open the wheel, and again to turn its pages.",
            EditorUi.Row(
                Choice("key", "Shift", settings.TriggerKey == TriggerKey.Shift, s => s with { TriggerKey = TriggerKey.Shift }),
                Choice("key", "Ctrl", settings.TriggerKey == TriggerKey.Ctrl, s => s with { TriggerKey = TriggerKey.Ctrl }),
                Choice("key", "Alt", settings.TriggerKey == TriggerKey.Alt, s => s with { TriggerKey = TriggerKey.Alt }))));

        page.Children.Add(Section("Wheel pages", "At least one stays on.",
            EditorUi.Row(
                PageToggle("Convert", settings.ConvertPage, (s, on) => s with { ConvertPage = on }),
                PageToggle("Tools", settings.ToolsPage, (s, on) => s with { ToolsPage = on }),
                PageToggle("Edit", settings.EditPage, (s, on) => s with { EditPage = on }))));

        page.Children.Add(Section("Look", null,
            EditorUi.Row(
                Choice("theme", "Like Windows", settings.Theme == ThemeChoice.System, s => s with { Theme = ThemeChoice.System }),
                Choice("theme", "Light", settings.Theme == ThemeChoice.Light, s => s with { Theme = ThemeChoice.Light }),
                Choice("theme", "Dark", settings.Theme == ThemeChoice.Dark, s => s with { Theme = ThemeChoice.Dark }))));

        page.Children.Add(Section("Quality", "For JPG, WebP and AVIF results. Smaller files lose a little detail.",
            EditorUi.Row(
                Choice("quality", "High", settings.Quality == QualityPreset.High, s => s with { Quality = QualityPreset.High }),
                Choice("quality", "Balanced", settings.Quality == QualityPreset.Balanced, s => s with { Quality = QualityPreset.Balanced }),
                Choice("quality", "Smaller files", settings.Quality == QualityPreset.Smaller, s => s with { Quality = QualityPreset.Smaller }))));

        UpdateFolderText(settings.OutputFolder);
        page.Children.Add(Section("Save results", "Nothing is ever overwritten: a taken name gets (1), (2)…",
            EditorUi.Row(
                Choice("folder", "Next to the original", settings.OutputFolder is null, s => s with { OutputFolder = null }, onChecked: () => UpdateFolderText(null)),
                EditorUi.Button("Choose a folder…", ChooseFolder),
                _folderText)));

        page.Children.Add(Section("Notifications", null,
            EditorUi.Row(
                Choice("notify", "Every result", settings.Notifications == NotificationLevel.All, s => s with { Notifications = NotificationLevel.All }),
                Choice("notify", "Only problems", settings.Notifications == NotificationLevel.ErrorsOnly, s => s with { Notifications = NotificationLevel.ErrorsOnly }))));

        page.Children.Add(Section("Windows", null, startWithWindows));

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 90 };
        close.SetResourceReference(StyleProperty, "AccentButton");
        close.Click += (_, _) => Close();
        var openLog = EditorUi.Button("Open log", () => Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true })?.Dispose());
        var version = EditorUi.Label($"Morphdrop {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · changes apply right away");

        var footer = new DockPanel { Margin = new Thickness(24, 8, 24, 20), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        var update = EditorUi.Button("Check for updates", () => _ = Updates.CheckAsync(this));
        footer.Children.Add(EditorUi.Row(openLog, update, version));
        page.Children.Add(footer);
        Content = page;
    }

    /// <summary>Shows the window, or brings it forward if it is already open.</summary>
    public static void Show(SettingsStore store, AutoStart autoStart)
    {
        if (_open is null)
        {
            _open = new SettingsWindow(store, autoStart);
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }

        _open.Topmost = true; // Morphdrop runs in the background, so make sure the window is seen
        _open.Activate();
        _open.Topmost = false;
    }

    private static UIElement Section(string title, string? note, UIElement content)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        section.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
        section.Children.Add(content);
        if (note is not null)
        {
            var text = new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SubtleText");
            section.Children.Add(text);
        }
        return section;
    }

    private RadioButton Choice(string group, string text, bool isChecked, Func<AppSettings, AppSettings> change, Action? onChecked = null) =>
        EditorUi.Chip(text, group, isChecked, () =>
        {
            _store.Update(change);
            onChecked?.Invoke();
        });

    private CheckBox PageToggle(string name, bool isChecked, Func<AppSettings, bool, AppSettings> change)
    {
        var toggle = new CheckBox { Content = name, IsChecked = isChecked, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) =>
        {
            _store.Update(settings => change(settings, toggle.IsChecked == true));
            toggle.IsChecked = name switch // the store keeps at least one page on
            {
                "Convert" => _store.Current.ConvertPage,
                "Tools" => _store.Current.ToolsPage,
                _ => _store.Current.EditPage,
            };
        };
        return toggle;
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Save Morphdrop's results in", InitialDirectory = _store.Current.OutputFolder ?? "" };
        if (dialog.ShowDialog(this) != true)
            return;

        _store.Update(settings => settings with { OutputFolder = dialog.FolderName });
        UpdateFolderText(dialog.FolderName);
    }

    private void UpdateFolderText(string? folder)
    {
        _folderText.Text = folder is null ? "" : folder;
        _folderText.ToolTip = folder;
        _folderText.Margin = new Thickness(folder is null ? 0 : 4, 0, 0, 0);
    }

    private void SetAutoStart(CheckBox box)
    {
        try
        {
            _autoStart.SetEnabled(box.IsChecked == true);
            Log.Info(box.IsChecked == true ? "Start with Windows on" : "Start with Windows off");
        }
        catch (Exception ex)
        {
            Log.Error("Could not change Start with Windows", ex);
            box.IsChecked = _autoStart.IsEnabled;
            MessageBox.Show(this, ex.Message, "Morphdrop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
