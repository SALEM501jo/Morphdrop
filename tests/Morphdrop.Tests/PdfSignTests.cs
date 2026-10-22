using System.Windows;
using ImageMagick;
using Morphdrop.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Tests;

public sealed class PdfSignTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-sign-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    /// <summary>Blank white A4 pages; <paramref name="rotations"/> turns each one (0, 90…).</summary>
    private string BlankPdf(string name, params int[] rotations)
    {
        string path = InFolder(name);
        using var document = new PdfDocument();
        foreach (int rotation in rotations)
        {
            PdfPage page = document.AddPage();
            page.Rotate = rotation;
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(path);
        return path;
    }

    private static byte[] Square(MagickColor color)
    {
        using var image = new MagickImage(color, 60, 30);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>The center of the darkest blob on a rendered page, as fractions of its width and height.</summary>
    private static Point InkCenter(string pdf, int page)
    {
        Point center = default;
        int index = 0;
        Morphdrop.Imaging.PdfPages.Render(pdf, (i, _, png) =>
        {
            if (i != page)
                return;
            using var image = new MagickImage(png);
            using IPixelCollection<byte> pixels = image.GetPixels();
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = 0; y < image.Height; y += 2)
            {
                for (int x = 0; x < image.Width; x += 2)
                {
                    IPixel<byte> pixel = pixels.GetPixel(x, y)!;
                    if (pixel.GetChannel(0) + pixel.GetChannel(1) + pixel.GetChannel(2) < 300)
                    {
                        sx += x;
                        sy += y;
                        count++;
                    }
                }
            }

            center = count == 0 ? new Point(-1, -1) : new Point(sx / count / image.Width, sy / count / image.Height);
            index++;
        });
        return center;
    }

    [Fact]
    public void ALockedCopy_NeedsItsPassword_AndUnlocksWithIt()
    {
        string pdf = BlankPdf("contract.pdf", 0, 0);

        string locked = PdfSecurity.Lock(pdf, "s3cret!");

        Assert.Equal(InFolder("contract (locked).pdf"), locked);
        Assert.True(PdfSecurity.IsLocked(locked));
        Assert.False(PdfSecurity.IsLocked(pdf), "the original is untouched");
        Assert.False(PdfSecurity.CanOpen(locked, "wrong"));
        Assert.Throws<InvalidDataException>(() => PdfSecurity.Unlock(locked, "wrong"));

        string unlocked = PdfSecurity.Unlock(locked, "s3cret!");

        Assert.False(PdfSecurity.IsLocked(unlocked));
        using PdfDocument document = PdfReader.Open(unlocked, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public void ASignature_LandsWhereItWasPlaced()
    {
        string pdf = BlankPdf("form.pdf", 0, 0);
        var size = PdfStamps.PageSizes(pdf)[1];
        // Bottom right of the second page.
        var bounds = new Rect(size.Width * 0.7, size.Height * 0.8, 120, 60);

        string signed = PdfStamps.Save(pdf, new Stamp[] { new PictureStamp(1, bounds, Square(MagickColors.Navy)) });

        Assert.Equal(InFolder("form (signed).pdf"), signed);
        Assert.Equal(new Point(-1, -1), InkCenter(signed, 0)); // nothing on the first page
        Point center = InkCenter(signed, 1);
        Assert.InRange(center.X, (bounds.X + 60) / size.Width - 0.01, (bounds.X + 60) / size.Width + 0.01);
        Assert.InRange(center.Y, (bounds.Y + 30) / size.Height - 0.01, (bounds.Y + 30) / size.Height + 0.01);
    }

    [Fact]
    public void TypedText_IsWrittenOntoThePage()
    {
        string pdf = BlankPdf("form.pdf", 0);
        var size = PdfStamps.PageSizes(pdf)[0];

        string signed = PdfStamps.Save(pdf, new Stamp[] { new TextStamp(0, new Rect(100, 120, 200, 20), "Jordan Lee", 14) });

        Point center = InkCenter(signed, 0);
        Assert.InRange(center.X, 100 / size.Width, 300 / size.Width);
        Assert.InRange(center.Y, 115 / size.Height, 145 / size.Height);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void OnATurnedPage_AStampStillLandsWhereItWasSeen(int rotation)
    {
        string pdf = BlankPdf("turned.pdf", rotation);
        var size = PdfStamps.PageSizes(pdf)[0]; // as seen
        Assert.Equal(rotation != 180, size.Width > size.Height);
        var bounds = new Rect(size.Width * 0.1, size.Height * 0.1, 80, 40); // top left, as seen

        string signed = PdfStamps.Save(pdf, new Stamp[] { new PictureStamp(0, bounds, Square(MagickColors.Navy)) });

        Point center = InkCenter(signed, 0);
        Assert.InRange(center.X, (bounds.X + 40) / size.Width - 0.01, (bounds.X + 40) / size.Width + 0.01);
        Assert.InRange(center.Y, (bounds.Y + 20) / size.Height - 0.01, (bounds.Y + 20) / size.Height + 0.01);
    }
}
