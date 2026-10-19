using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Morphdrop.Editing;

internal sealed record BackgroundPreset(string Name, Color From, Color To)
{
    public Brush CreateBrush()
    {
        Brush brush = From == To ? new SolidColorBrush(From) : new LinearGradientBrush(From, To, 45);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Padding is a share of the image's long side and corners a share of its short side, so a preview and the full-size render look the same.</summary>
internal sealed record BackgroundOptions(BackgroundPreset Background, double Padding, double Corners, bool Shadow, double? AspectRatio);

/// <summary>Puts an image on a background with padding, rounded corners and a shadow, like a presentation screenshot.</summary>
internal static class BackgroundComposer
{
    public static IReadOnlyList<BackgroundPreset> Presets { get; } = new[]
    {
        new BackgroundPreset("Sunset", Color.FromRgb(0xFF, 0x7A, 0x45), Color.FromRgb(0x8E, 0x7C, 0xF0)),
        new BackgroundPreset("Ocean", Color.FromRgb(0x21, 0x93, 0xB0), Color.FromRgb(0x6D, 0xD5, 0xED)),
        new BackgroundPreset("Mint", Color.FromRgb(0x43, 0xE9, 0x7B), Color.FromRgb(0x38, 0xF9, 0xD7)),
        new BackgroundPreset("Peach", Color.FromRgb(0xFF, 0xDD, 0xE1), Color.FromRgb(0xEE, 0x9C, 0xA7)),
        new BackgroundPreset("Night", Color.FromRgb(0x23, 0x25, 0x26), Color.FromRgb(0x41, 0x43, 0x45)),
        new BackgroundPreset("White", Colors.White, Colors.White),
        new BackgroundPreset("Light gray", Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0xF2, 0xF2, 0xF2)),
        new BackgroundPreset("Black", Colors.Black, Colors.Black),
    };

    public static BackgroundOptions Default { get; } = new(Presets[0], Padding: 0.08, Corners: 0.03, Shadow: true, AspectRatio: null);

    public static Size CanvasSize(double width, double height, BackgroundOptions options)
    {
        double padding = Math.Round(Math.Max(width, height) * options.Padding);
        double canvasWidth = width + 2 * padding;
        double canvasHeight = height + 2 * padding;
        if (options.AspectRatio is double ratio)
        {
            if (canvasWidth / canvasHeight < ratio)
                canvasWidth = canvasHeight * ratio;
            else
                canvasHeight = canvasWidth / ratio;
        }

        return new Size(Math.Ceiling(canvasWidth), Math.Ceiling(canvasHeight));
    }

    /// <summary>The composed picture, laid out at its own size in pixels; must be built on a UI (STA) thread.</summary>
    public static FrameworkElement Compose(BitmapSource image, double width, double height, BackgroundOptions options)
    {
        Size canvas = CanvasSize(width, height, options);
        double padding = Math.Max(width, height) * options.Padding;

        var picture = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(Math.Min(width, height) * options.Corners),
            Background = new ImageBrush(image) { Stretch = Stretch.Fill },
        };
        if (options.Shadow && padding > 0)
        {
            picture.Effect = new DropShadowEffect
            {
                BlurRadius = Math.Max(6, padding * 0.6),
                ShadowDepth = padding * 0.12,
                Direction = 270,
                Opacity = 0.35,
            };
        }

        var root = new Grid
        {
            Width = canvas.Width,
            Height = canvas.Height,
            Background = options.Background.CreateBrush(),
            Children = { picture },
        };
        root.Measure(canvas);
        root.Arrange(new Rect(canvas));
        return root;
    }

    /// <summary>Renders the composition at full size and returns it as PNG bytes (UI thread).</summary>
    public static byte[] RenderPng(BitmapSource image, BackgroundOptions options)
    {
        FrameworkElement root = Compose(image, image.PixelWidth, image.PixelHeight, options);
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)root.Width, (int)root.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
