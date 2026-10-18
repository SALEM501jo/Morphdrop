using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShiftDrop.Overlay;

/// <summary>
/// Renders the wheel to a PNG without showing any window (<c>ShiftDrop.exe --render-wheel out.png</c>).
/// Handy for checking the design and for screenshots in the README.
/// </summary>
internal static class WheelPreview
{
    private const double Margin = 40;
    private const double Scale = 2;

    public static void Render(string outputPath)
    {
        var wheel = new WheelView();
        wheel.SetSlices(new[] { "JPG", "ICO", "BMP", "TIFF", "GIF" });
        wheel.SetHub("1.2 MB", "Convert to JPG");
        wheel.Highlight(0);

        var scene = new Grid
        {
            Width = WheelGeometry.Size + Margin * 2,
            Height = WheelGeometry.Size + Margin * 2,
            Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x45), Color.FromRgb(0x8E, 0x7C, 0xF0), 45),
            Children = { wheel },
        };
        var size = new Size(scene.Width, scene.Height);
        scene.Measure(size);
        scene.Arrange(new Rect(size));
        scene.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)(size.Width * Scale), (int)(size.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(scene);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(outputPath);
        encoder.Save(file);
    }
}
