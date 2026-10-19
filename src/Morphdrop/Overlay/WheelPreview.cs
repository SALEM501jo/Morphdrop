using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;

namespace Morphdrop.Overlay;

/// <summary>
/// Renders every page of the wheel for a PNG, in the light and dark themes, to one image without showing any window
/// (<c>Morphdrop.exe --render-wheel out.png</c>). Handy for checking the design and for the README.
/// </summary>
internal static class WheelPreview
{
    private const double Margin = 40;
    private const double Scale = 2;

    public static void Render(string outputPath)
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("photo.png");
        var rows = new StackPanel { Margin = new Thickness(Margin / 2) };
        foreach (bool dark in new[] { false, true })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < pages.Count; i++)
            {
                var wheel = new WheelView { Margin = new Thickness(Margin / 2) };
                wheel.SetDarkTheme(dark);
                wheel.SetSlices(pages[i].Actions.Select(action => new WheelItem(action.Label, action.Glyph)).ToList());
                wheel.Highlight(0);
                wheel.SetHub("1.2 MB", pages[i].Actions[0].Description);
                wheel.SetHint(pages.Count > 1 ? $"Shift → {pages[(i + 1) % pages.Count].Name}" : null);
                row.Children.Add(wheel);
            }

            rows.Children.Add(row);
        }

        var scene = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0x45), Color.FromRgb(0x8E, 0x7C, 0xF0), 20),
            Child = rows,
        };
        scene.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = scene.DesiredSize;
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
