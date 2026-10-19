using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShiftDrop.Settings;

/// <summary>
/// Light or dark colours for ShiftDrop's windows, following the Look setting ("Like Windows" tracks Windows' app
/// mode live). Windows pick the colours up through the Theme.* resources in App.xaml; title bars are switched too.
/// </summary>
internal static class AppTheme
{
    private const int DwmUseImmersiveDarkMode = 20;

    private static readonly Dictionary<string, (Color Light, Color Dark)> Palette = new()
    {
        ["Theme.Window"] = (Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0x20, 0x20, 0x20)),
        ["Theme.Panel"] = (Colors.White, Color.FromRgb(0x2B, 0x2B, 0x2B)),
        ["Theme.PanelBorder"] = (Color.FromRgb(0xE3, 0xE3, 0xE3), Color.FromRgb(0x3A, 0x3A, 0x3A)),
        ["Theme.Text"] = (Color.FromRgb(0x1F, 0x1F, 0x1F), Color.FromRgb(0xF2, 0xF2, 0xF2)),
        ["Theme.SubtleText"] = (Color.FromRgb(0x70, 0x70, 0x70), Color.FromRgb(0xA8, 0xA8, 0xA8)),
        ["Theme.Button"] = (Colors.White, Color.FromRgb(0x33, 0x33, 0x33)),
        ["Theme.ButtonBorder"] = (Color.FromRgb(0xD0, 0xD0, 0xD0), Color.FromRgb(0x4A, 0x4A, 0x4A)),
        ["Theme.Chip"] = (Color.FromRgb(0xEC, 0xEC, 0xEC), Color.FromRgb(0x3A, 0x3A, 0x3A)),
        ["Theme.ChipHover"] = (Color.FromRgb(0xE0, 0xE0, 0xE0), Color.FromRgb(0x46, 0x46, 0x46)),
    };

    private static readonly Lazy<ImageSource> WindowIcon = new(() => Branding.Logo.Render(64));

    private static ThemeChoice _choice = ThemeChoice.System;

    public static bool IsDark { get; private set; }

    /// <summary>Applies a Look choice now, and keeps "Like Windows" in step when Windows switches mode.</summary>
    public static void Use(ThemeChoice choice)
    {
        _choice = choice;
        Refresh();
    }

    public static void StartFollowingWindows() =>
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _choice == ThemeChoice.System)
                Application.Current?.Dispatcher.InvokeAsync(Refresh);
        };

    public static bool Resolve(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0,
    };

    /// <summary>Gives a window the theme's background, text colour and title bar, now and after every change.</summary>
    public static void Attach(Window window)
    {
        window.Icon = WindowIcon.Value;
        window.SetResourceReference(Window.BackgroundProperty, "Theme.Window");
        window.SetResourceReference(Window.ForegroundProperty, "Theme.Text");
        void Update() => SetTitleBar(window);
        window.SourceInitialized += (_, _) => Update();
        Changed += Update;
        window.Closed += (_, _) => Changed -= Update;
    }

    private static event Action? Changed;

    private static void Refresh()
    {
        IsDark = Resolve(_choice);
        if (Application.Current is null)
            return;

        foreach ((string key, (Color light, Color dark)) in Palette)
        {
            var brush = new SolidColorBrush(IsDark ? dark : light);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }

        Changed?.Invoke();
    }

    private static void SetTitleBar(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
