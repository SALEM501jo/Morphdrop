using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Imaging;

namespace Morphdrop.Demo;

/// <summary>
/// The files the video shows. The photo and the invoice are drawn here (no third-party pictures), and every result of
/// an image action is made by Morphdrop itself, so the sizes in the notifications are real. The document, video and
/// sound files are only placeholders for their Explorer icons.
/// </summary>
internal sealed class DemoAssets
{
    private DemoAssets(string folder)
    {
        Folder = folder;
        Sunset = Path.Combine(folder, "sunset.jpg");
        Invoice = Path.Combine(folder, "invoice.png");
        Report = Path.Combine(folder, "report.docx");
        ReportPdf = Path.Combine(folder, "report.pdf");
        Clip = Path.Combine(folder, "clip.mov");
        ClipGif = Path.Combine(folder, "clip.gif");
        Song = Path.Combine(folder, "song.wav");
        SongMp3 = Path.Combine(folder, "song.mp3");
    }

    public string Folder { get; }

    public string Sunset { get; }

    public string SunsetWebP { get; private set; } = "";

    public string SunsetCompressed { get; private set; } = "";

    public string SunsetCropped { get; private set; } = "";

    public string SunsetBackground { get; private set; } = "";

    public string Invoice { get; }

    public string InvoiceRedacted { get; private set; } = "";

    /// <summary>The email and phone lines, and the card line, in invoice pixels (the preview is the same size).</summary>
    public Rect ContactArea { get; private set; }

    public Rect CardArea { get; private set; }

    public string Report { get; }

    public string ReportPdf { get; }

    public string Clip { get; }

    public string ClipGif { get; }

    public string Song { get; }

    public string SongMp3 { get; }

    public static DemoAssets Prepare(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var assets = new DemoAssets(folder);
        Painter.Sunset(assets.Sunset, 4032, 3024);
        assets.SunsetWebP = new ConvertImageAction(ImageFormat.WebP).Run(assets.Sunset);
        assets.SunsetCompressed = ImageTools.Compress(assets.Sunset);

        using (EditableImage image = EditableImage.Load(assets.Sunset))
        {
            var crop = new CropEditor(image);
            var surface = EditorControl.Field<ImageSurface>(crop, "_surface");
            EditorControl.Call(crop, "Update", EditorControl.CropRect(surface.Bounds, EditorControl.CropTopLeft, EditorControl.CropBottomRight));
            assets.SunsetCropped = crop.PrepareSave()();

            var background = new BackgroundEditor(image);
            EditorControl.Call(background, "Change", EditorControl.FinalBackground);
            assets.SunsetBackground = background.PrepareSave()();
        }

        (assets.ContactArea, assets.CardArea) = Painter.Invoice(assets.Invoice);
        using (EditableImage image = EditableImage.Load(assets.Invoice))
        {
            var redact = new RedactEditor(image);
            foreach (Rect area in new[] { assets.ContactArea, assets.CardArea })
            {
                EditorControl.Call(redact, "BeginBox", area.TopLeft);
                EditorControl.Call(redact, "EndBox", area.BottomRight);
            }

            assets.InvoiceRedacted = redact.PrepareSave()();
        }

        // Typical sizes, so the hub reads like it would for real files.
        Placeholder(assets.Report, 845_312);
        Placeholder(assets.Clip, 18_421_760);
        Placeholder(assets.Song, 31_752_192);
        foreach (string result in new[] { assets.ReportPdf, assets.ClipGif, assets.SongMp3 })
            Placeholder(result, 0);
        return assets;
    }

    private static void Placeholder(string path, long length)
    {
        using FileStream file = File.Create(path);
        file.SetLength(length);
    }
}

internal static class Painter
{
    /// <summary>A sunset over mountains and a lake, with a little grain so it encodes like a photo.</summary>
    public static void Sunset(string path, int width, int height)
    {
        double w = width, h = height;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x23, 0x55), 0));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(0x5E, 0x3C, 0x8F), 0.3));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0x5F, 0x6A), 0.52));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x9E, 0x5E), 0.64));
            sky.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD1, 0x8A), 0.76));
            dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));

            var sun = new Point(0.62 * w, 0.6 * h);
            dc.DrawEllipse(Radial(Color.FromArgb(0xB0, 0xFF, 0xC8, 0x7A)), null, sun, 0.45 * w, 0.45 * w);
            dc.DrawEllipse(Radial(Color.FromArgb(0xFF, 0xFF, 0xF0, 0xC8), 0.55), null, sun, 0.075 * w, 0.075 * w);

            Ridge(dc, w, h, 0.56, 0.07, 7, Color.FromRgb(0x8A, 0x4F, 0x8C), 0.79);
            Ridge(dc, w, h, 0.63, 0.06, 11, Color.FromRgb(0x5A, 0x33, 0x72), 0.79);
            Ridge(dc, w, h, 0.7, 0.045, 3, Color.FromRgb(0x2E, 0x1B, 0x4A), 0.79);

            var lake = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            lake.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0x8A, 0x6E), 0));
            lake.GradientStops.Add(new GradientStop(Color.FromRgb(0x6B, 0x3E, 0x7A), 0.45));
            lake.GradientStops.Add(new GradientStop(Color.FromRgb(0x1C, 0x14, 0x38), 1));
            dc.DrawRectangle(lake, null, new Rect(0, 0.765 * h, w, 0.235 * h));

            // The sun's reflection: short bright strokes that thin out towards the shore.
            var random = new Random(5);
            for (int i = 0; i < 60; i++)
            {
                double y = 0.775 * h + i * 0.0036 * h;
                double half = (0.07 - i * 0.0009) * w * (0.6 + random.NextDouble() * 0.6);
                double x = 0.62 * w + (random.NextDouble() - 0.5) * 0.02 * w;
                var stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Max(20, 190 - i * 3), 0xFF, 0xE2, 0xB0));
                dc.DrawRoundedRectangle(stroke, null, new Rect(x - half, y, half * 2, 0.0022 * h), 0.001 * h, 0.001 * h);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        var grain = new Random(11);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int noise = grain.Next(-7, 8);
            for (int c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Clamp(pixels[i + c] + noise, 0, 255);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 72, 72, PixelFormats.Bgra32, null, pixels, stride)));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    private static RadialGradientBrush Radial(Color color, double solidUntil = 0)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(color, solidUntil));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        return brush;
    }

    private static void Ridge(DrawingContext dc, double w, double h, double baseLine, double amplitude, int seed, Color color, double bottom)
    {
        var random = new Random(seed);
        double[] phase = Enumerable.Range(0, 4).Select(_ => random.NextDouble() * Math.PI * 2).ToArray();
        var figure = new PathFigure { StartPoint = new Point(0, bottom * h), IsClosed = true, IsFilled = true };
        for (int i = 0; i <= 200; i++)
        {
            double x = i / 200.0;
            double y = baseLine
                - amplitude * (0.55 * Math.Sin(x * 5.1 + phase[0]) + 0.3 * Math.Sin(x * 11.3 + phase[1])
                               + 0.12 * Math.Sin(x * 27 + phase[2]) + 0.05 * Math.Sin(x * 61 + phase[3]));
            figure.Segments.Add(new LineSegment(new Point(x * w, y * h), false));
        }

        figure.Segments.Add(new LineSegment(new Point(w, bottom * h), false));
        dc.DrawGeometry(new SolidColorBrush(color), null, new PathGeometry(new[] { figure }));
    }

    /// <summary>A made-up invoice for the Redact scene. Returns the contact lines' and card line's areas.</summary>
    public static (Rect Contact, Rect Card) Invoice(string path)
    {
        const int width = 1200, height = 1500;
        var visual = new DrawingVisual();
        Rect contact, card;
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0x65, 0x40)), null, new Rect(0, 0, width, 18));

            Text(dc, "Lumen & Pine Studio", 36, 90, 96, bold: true);
            Text(dc, "hello@lumenpine.example", 22, 90, 146, gray: true);
            Text(dc, "INVOICE", 54, 1110, 82, bold: true, right: true);
            Text(dc, "#1042  ·  Oct 19, 2026", 22, 1110, 152, gray: true, right: true);

            Text(dc, "BILLED TO", 18, 90, 270, bold: true, gray: true);
            Text(dc, "Jordan Lee", 30, 90, 302);
            Rect email = Text(dc, "jordan.lee@example.com", 26, 90, 350);
            Rect phone = Text(dc, "+1 (555) 013-4567", 26, 90, 392);
            contact = Rect.Inflate(Rect.Union(email, phone), 14, 10);

            var line = new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)), 2);
            Text(dc, "Description", 20, 90, 530, bold: true, gray: true);
            Text(dc, "Amount", 20, 1110, 530, bold: true, gray: true, right: true);
            dc.DrawLine(line, new Point(90, 570), new Point(1110, 570));
            (string Item, string Amount)[] rows =
            {
                ("Brand identity refresh", "3,200.00"),
                ("Website design, 8 pages", "4,800.00"),
                ("Product photos, half day", "650.00"),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                Text(dc, rows[i].Item, 26, 90, 600 + i * 70);
                Text(dc, rows[i].Amount, 26, 1110, 600 + i * 70, right: true);
            }

            dc.DrawLine(line, new Point(90, 820), new Point(1110, 820));
            Text(dc, "Total", 30, 90, 850, bold: true);
            Text(dc, "$8,650.00", 30, 1110, 850, bold: true, right: true);

            Text(dc, "PAID WITH", 18, 90, 1000, bold: true, gray: true);
            Rect number = Text(dc, "Visa  4242 4242 4242 4242   exp 08/29", 26, 90, 1034);
            card = Rect.Inflate(number, 14, 10);

            Text(dc, "Thank you for your business!", 24, 600, 1360, gray: true, center: true);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(path))
            encoder.Save(file);
        return (contact, card);
    }

    private static Rect Text(DrawingContext dc, string text, double size, double x, double y, bool bold = false, bool gray = false, bool right = false, bool center = false)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(gray ? Color.FromRgb(0x80, 0x80, 0x80) : Color.FromRgb(0x1E, 0x1E, 0x24)), 1);
        double left = right ? x - formatted.Width : center ? x - formatted.Width / 2 : x;
        dc.DrawText(formatted, new Point(left, y));
        return new Rect(left, y, formatted.Width, formatted.Height);
    }
}
