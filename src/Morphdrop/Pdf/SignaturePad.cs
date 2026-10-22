using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Morphdrop.Settings;

namespace Morphdrop.Pdf;

/// <summary>
/// Draw a signature with the mouse, pen or finger. It becomes a transparent PNG, trimmed to the ink, and can be kept
/// on this PC (in Morphdrop's own folder, never uploaded) for the next time.
/// </summary>
internal sealed class SignaturePad : Window
{
    private const double RenderScale = 3; // sharp when printed

    private readonly InkCanvas _ink = new()
    {
        Width = 560,
        Height = 200,
        Background = Brushes.White,
        DefaultDrawingAttributes = new DrawingAttributes { Color = Color.FromRgb(0x1A, 0x23, 0x5C), Width = 3, Height = 3, FitToCurve = true },
    };

    private readonly CheckBox _remember = new() { Content = "Remember my signature on this PC", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };

    private SignaturePad()
    {
        Title = "Draw your signature";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AppTheme.Attach(this);

        var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 8, 0) };
        clear.SetResourceReference(StyleProperty, "ActionButton");
        clear.Click += (_, _) => _ink.Strokes.Clear();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        var use = new Button { Content = "Use this signature", IsDefault = true };
        use.SetResourceReference(StyleProperty, "AccentButton");
        use.Click += (_, _) =>
        {
            if (_ink.Strokes.Count > 0)
                DialogResult = true;
        };

        var line = new Border { Height = 1, Background = Brushes.Gainsboro, Margin = new Thickness(30, -50, 30, 49), IsHitTestVisible = false };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "Sign above the line.", Margin = new Thickness(0, 0, 0, 8) },
                new Border { BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Child = new Grid { Children = { _ink, line } } },
                _remember,
                new DockPanel
                {
                    Margin = new Thickness(0, 14, 0, 0),
                    Children =
                    {
                        clear,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, use } },
                    },
                },
            },
        };
    }

    public static string SavedPath => Path.Combine(AppPaths.DataFolder, "signature.png");

    public static byte[]? Saved => File.Exists(SavedPath) ? File.ReadAllBytes(SavedPath) : null;

    /// <summary>Asks for a signature; returns it as a PNG, or null when cancelled.</summary>
    public static byte[]? Ask(Window owner)
    {
        var pad = new SignaturePad { Owner = owner };
        if (pad.ShowDialog() != true)
            return null;

        byte[] png = Render(pad._ink.Strokes);
        if (pad._remember.IsChecked == true)
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllBytes(SavedPath, png);
        }
        else if (File.Exists(SavedPath))
        {
            File.Delete(SavedPath); // "don't remember" also forgets an older one
        }

        return png;
    }

    /// <summary>The strokes on a transparent background, cropped to the ink with a little room around it.</summary>
    internal static byte[] Render(StrokeCollection strokes)
    {
        Rect bounds = strokes.GetBounds();
        bounds.Inflate(6, 6);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(RenderScale, RenderScale));
            context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            strokes.Draw(context);
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * RenderScale), (int)Math.Ceiling(bounds.Height * RenderScale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
