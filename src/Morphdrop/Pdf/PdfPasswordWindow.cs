using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Settings;

namespace Morphdrop.Pdf;

/// <summary>
/// The Password slice: a locked PDF asks for its password and saves an unlocked copy; any other PDF asks for a new
/// password (twice) and saves a locked copy. The original is never changed.
/// </summary>
internal sealed class PdfPasswordWindow : Window
{
    private readonly PasswordBox _password = Box();
    private readonly PasswordBox _repeat = Box();
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Foreground = EditorUi.Accent, Margin = new Thickness(0, 8, 0, 0) };

    private PdfPasswordWindow(IFileAction action, string path, bool locked, ActionRunner runner)
    {
        string name = Path.GetFileName(path);
        Title = locked ? $"Unlock {name}" : $"Lock {name}";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AppTheme.Attach(this);

        var explanation = new TextBlock
        {
            Text = locked
                ? $"{name} is locked. Type its password to save a copy that opens without one."
                : "Anyone who opens the locked copy will need this password. There is no way to recover a forgotten one.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        var fields = new StackPanel { Children = { explanation, Label("Password"), _password } };
        if (!locked)
        {
            fields.Children.Add(Label("Repeat it", top: 10));
            fields.Children.Add(_repeat);
        }

        fields.Children.Add(_problem);

        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        cancel.Click += (_, _) => Close();
        var confirm = new Button { Content = locked ? "Save unlocked copy" : "Save locked copy", IsDefault = true };
        confirm.SetResourceReference(StyleProperty, "AccentButton");
        confirm.Click += (_, _) =>
        {
            string password = _password.Password;
            if (password.Length == 0)
            {
                _problem.Text = "Type a password first.";
                return;
            }

            if (locked && !PdfSecurity.CanOpen(path, password))
            {
                _problem.Text = "That password doesn't open this PDF.";
                _password.SelectAll();
                _password.Focus();
                return;
            }

            if (!locked && password != _repeat.Password)
            {
                _problem.Text = "The two passwords aren't the same.";
                return;
            }

            runner.RunJob(action, path, () => locked ? PdfSecurity.Unlock(path, password) : PdfSecurity.Lock(path, password));
            Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Children =
            {
                fields,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { cancel, confirm } },
            },
        };
        Loaded += (_, _) => _password.Focus();
    }

    public static void Open(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        string path = files[0];
        bool locked;
        try
        {
            locked = PdfSecurity.IsLocked(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open '{path}' for the password", ex);
            runner.RunJob(action, path, () => throw new InvalidDataException($"Couldn't open the PDF: {ex.Message}"));
            return;
        }

        var window = new PdfPasswordWindow(action, path, locked, runner);
        window.Show();
        window.Topmost = true; // Morphdrop runs in the background, so make sure the window is seen
        window.Activate();
        window.Topmost = false;
    }

    private static PasswordBox Box()
    {
        var box = new PasswordBox { Padding = new Thickness(6, 4, 6, 4) };
        box.SetResourceReference(BackgroundProperty, "Theme.Panel");
        box.SetResourceReference(ForegroundProperty, "Theme.Text");
        box.SetResourceReference(BorderBrushProperty, "Theme.ButtonBorder");
        box.SetResourceReference(PasswordBox.CaretBrushProperty, "Theme.Text");
        return box;
    }

    private static TextBlock Label(string text, double top = 0) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 4) };
}
