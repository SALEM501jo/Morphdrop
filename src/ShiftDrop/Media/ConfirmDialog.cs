using System.Windows;
using System.Windows.Controls;

namespace ShiftDrop.Media;

/// <summary>
/// A small yes/no question in ShiftDrop's own window. Unlike a "default desktop" MessageBox it keeps the app's UI
/// thread running while it waits, so the wheel can finish closing behind it; being topmost keeps it in sight
/// even though ShiftDrop runs in the background.
/// </summary>
internal static class ConfirmDialog
{
    public static bool Ask(string message, string yes, string no)
    {
        var yesButton = new Button { Content = yes, IsDefault = true, MinWidth = 96, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var noButton = new Button { Content = no, IsCancel = true, MinWidth = 96, Padding = new Thickness(12, 5, 12, 5) };
        var window = new Window
        {
            Title = "ShiftDrop",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = true,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            Content = new StackPanel
            {
                Margin = new Thickness(22, 20, 22, 18),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 18, 0, 0),
                        Children = { yesButton, noButton },
                    },
                },
            },
        };
        Settings.AppTheme.Attach(window);
        yesButton.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        noButton.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        yesButton.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => window.Activate();
        return window.ShowDialog() == true;
    }
}
