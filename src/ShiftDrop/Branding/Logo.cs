using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShiftDrop.Imaging;

namespace ShiftDrop.Branding;

/// <summary>
/// ShiftDrop's logo, drawn in code: the wheel itself (four slices, the top one lit like a hovered slice) around a
/// drop arrow, on a rounded square in the brand gradient. Every size is drawn directly, so the 16 px tray icon stays
/// as crisp as the 512 px one. <c>ShiftDrop.exe --render-logo folder</c> writes the .ico and PNGs.
/// </summary>
internal static class Logo
{
    public static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static readonly Color GradientStart = Color.FromRgb(0xFF, 0x7A, 0x45);
    private static readonly Color GradientEnd = Color.FromRgb(0x8E, 0x7C, 0xF0);

    /// <summary>The logo in a 100 × 100 box. Tiny sizes get thicker strokes and no arrow, which wouldn't read.</summary>
    public static Drawing Create(int pixelSize = 512)
    {
        bool tiny = pixelSize <= 24;
        var group = new DrawingGroup();

        // A dark tile with a faint wash of the brand gradient, so the orange slice stands out at every size.
        var tile = new LinearGradientBrush(Color.FromRgb(0x2A, 0x24, 0x3A), Color.FromRgb(0x1B, 0x1A, 0x22), new Point(0, 0), new Point(1, 1));
        group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(new Rect(0, 0, 100, 100), 23, 23)));

        // The wheel: four thick slices with gaps, the top one lit in the accent like a hovered slice.
        double inner = tiny ? 16 : 19;
        double outer = 41;
        double gap = tiny ? 9 : 7;
        var accent = new LinearGradientBrush(GradientStart, Color.FromRgb(0xFF, 0x4E, 0x3A), new Point(0, 0), new Point(1, 1));
        var slice = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xFA));
        for (int i = 0; i < 4; i++)
        {
            double middle = -90 + i * 90;
            group.Children.Add(new GeometryDrawing(i == 0 ? accent : slice, null, Slice(inner, outer, middle - 45 + gap, middle + 45 - gap)));
        }

        if (!tiny)
        {
            // A file dropping in, in the gradient's violet on the white hub.
            var hub = new SolidColorBrush(Colors.White);
            group.Children.Add(new GeometryDrawing(hub, null, new EllipseGeometry(new Point(50, 50), 14, 14)));
            var arrowPen = new Pen(new SolidColorBrush(GradientEnd), 4.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var arrow = new PathGeometry(new[]
            {
                new PathFigure(new Point(50, 42), new[] { new LineSegment(new Point(50, 57), true) }, false),
                new PathFigure(new Point(44, 51.5), new PathSegment[] { new LineSegment(new Point(50, 57.5), true), new LineSegment(new Point(56, 51.5), true) }, false),
            });
            group.Children.Add(new GeometryDrawing(null, arrowPen, arrow));
        }

        group.Freeze();
        return group;
    }

    /// <summary>A ring sector between two radii, from one angle to another (degrees, clockwise from the right).</summary>
    private static Geometry Slice(double inner, double outer, double fromDegrees, double toDegrees)
    {
        static Point At(double r, double degrees) => new(50 + r * Math.Cos(degrees * Math.PI / 180), 50 + r * Math.Sin(degrees * Math.PI / 180));

        var figure = new PathFigure { StartPoint = At(outer, fromDegrees), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(outer, toDegrees), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(inner, toDegrees), true));
        figure.Segments.Add(new ArcSegment(At(inner, fromDegrees), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        return new PathGeometry(new[] { figure });
    }

    public static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
            context.DrawDrawing(Create(size));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] RenderPng(int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(size)));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Writes ShiftDrop.ico (every icon size, each drawn at its own size) and logo-512.png.</summary>
    public static void WriteAssets(string folder)
    {
        Directory.CreateDirectory(folder);
        using (FileStream ico = File.Create(Path.Combine(folder, "ShiftDrop.ico")))
            IcoWriter.WriteEntries(IconSizes, IconSizes.Select(RenderPng).ToArray(), ico);
        File.WriteAllBytes(Path.Combine(folder, "logo-512.png"), RenderPng(512));
    }
}
