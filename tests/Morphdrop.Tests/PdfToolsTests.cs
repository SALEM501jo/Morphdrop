using System.Windows;
using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Imaging;
using Morphdrop.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Tests;

public sealed class PdfToolsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-pdftools-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    private string Photo(string name, uint width, uint height, string color = "SteelBlue", OrientationType orientation = OrientationType.TopLeft)
    {
        using var image = new MagickImage(new MagickColor(color), width, height);
        image.AddNoise(NoiseType.Gaussian); // photo-like, so JPEG sizes behave like real ones
        if (orientation != OrientationType.TopLeft)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(exif);
            image.Orientation = orientation;
        }

        image.Quality = 95;
        string path = InFolder(name);
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    /// <summary>A PDF with numbered solid pages, written by PDFsharp from pictures.</summary>
    private string Pdf(string name, params string[] colors)
    {
        var pages = colors.Select((color, i) => new PicturePage(Photo($"{name}-{i}.jpg", 300, 400, color))).ToList();
        using PdfDocument document = PictureDocument.Create(pages, PageOptions.Default with { Margin = PageMargin.None });
        string path = InFolder(name);
        document.Save(path);
        return path;
    }

    private static List<MagickImage> Render(string pdf)
    {
        var pages = new List<MagickImage>();
        Morphdrop.Imaging.PdfPages.Render(pdf, (_, _, png) => pages.Add(new MagickImage(png)));
        return pages;
    }

    private static int PageCount(string pdf)
    {
        using PdfDocument document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>The share of pixels in a region (fractions of the page) that aren't (nearly) white.</summary>
    private static double Ink(MagickImage page, double left, double top, double right, double bottom)
    {
        using IPixelCollection<byte> pixels = page.GetPixels();
        int dark = 0, total = 0;
        for (int y = (int)(top * page.Height); y < (int)(bottom * page.Height); y += 2)
        {
            for (int x = (int)(left * page.Width); x < (int)(right * page.Width); x += 2)
            {
                IPixel<byte> pixel = pixels.GetPixel(x, y)!;
                total++;
                if (pixel.GetChannel(0) + pixel.GetChannel(1) + pixel.GetChannel(2) < 3 * 200)
                    dark++;
            }
        }

        return (double)dark / Math.Max(1, total);
    }

    [Theory]
    [InlineData("مرحبا بالعالم", true)]
    [InlineData("שלום", true)]
    [InlineData("123 مرحبا", true)]
    [InlineData("Hello مرحبا", false)]
    [InlineData("Holiday 2026", false)]
    public void RightToLeft_IsDecidedByTheFirstLetter(string text, bool rightToLeft) =>
        Assert.Equal(rightToLeft, PdfText.IsRightToLeft(text));

    [Fact]
    public void APictureWithCaptions_SitsAsOneBlockInTheMiddleOfThePage()
    {
        PageLayout layout = PageLayout.Compute(new Size(400, 300), PageOptions.Default with { Orientation = PageOrientation.Portrait }, _ => 20, _ => 30);

        Assert.Equal(595.28, layout.Page.Width, 2);
        Assert.Equal(layout.Picture.Top - PageLayout.Gap - 20, layout.Above.Top, 3);
        Assert.Equal(layout.Picture.Bottom + PageLayout.Gap, layout.Below.Top, 3);
        double blockTop = layout.Above.Top, blockBottom = layout.Below.Bottom;
        Assert.Equal(layout.Page.Height - blockBottom, blockTop, 1); // centered
        Assert.Equal(400.0 / 300, layout.Picture.Width / layout.Picture.Height, 3); // not stretched
    }

    [Fact]
    public void AWidePicture_TurnsThePageOnItsSide()
    {
        PageLayout layout = PageLayout.Compute(new Size(1600, 900), PageOptions.Default, _ => 0, _ => 0);

        Assert.True(layout.Page.Width > layout.Page.Height);
    }

    [Fact]
    public void ThePictureShapedPage_GrowsToFitItsText()
    {
        var options = PageOptions.Default with { Paper = PaperSize.Picture, Margin = PageMargin.None };

        PageLayout plain = PageLayout.Compute(new Size(1000, 1000), options, _ => 0, _ => 0);
        PageLayout captioned = PageLayout.Compute(new Size(1000, 1000), options, _ => 0, _ => 40);

        Assert.Equal(plain.Page.Width, plain.Page.Height, 1);
        Assert.Equal(plain.Page.Height + 40 + PageLayout.Gap, captioned.Page.Height, 1);
    }

    [Fact]
    public void PicturesWithText_BecomeAPdfWithAPageEach_AndTheTextIsOnThePage()
    {
        var pages = new[]
        {
            new PicturePage(Photo("a.jpg", 800, 600, "White"), Above: "رحلة الصيف 2026", Below: "Summer trip, day one"),
            new PicturePage(Photo("b.jpg", 600, 800, "White")),
        };

        string pdf = PictureDocument.Save(pages, PageOptions.Default with { PageNumbers = true });

        Assert.Equal(InFolder("a (2 pages).pdf"), pdf);
        Assert.Equal(2, PageCount(pdf));
        List<MagickImage> rendered = Render(pdf);
        Assert.True(rendered[0].Width > rendered[0].Height, "the wide photo gets a landscape page");
        PageLayout layout = PictureDocument.Layout(pages[0], new Size(800, 600), PageOptions.Default with { PageNumbers = true });
        double Fraction(double value, double of) => value / of;
        Assert.True(Ink(rendered[0], 0.2, Fraction(layout.Above.Top, layout.Page.Height), 0.8, Fraction(layout.Above.Bottom, layout.Page.Height)) > 0.02, "the text above is drawn");
        Assert.True(Ink(rendered[0], 0.2, Fraction(layout.Below.Top, layout.Page.Height), 0.8, Fraction(layout.Below.Bottom, layout.Page.Height)) > 0.02, "the text below is drawn");
        Assert.True(Ink(rendered[0], 0.4, 0.93, 0.6, 1) > 0.002, "the page number is drawn");
        rendered.ForEach(page => page.Dispose());
    }

    [Fact]
    public void AnUprightJpeg_GoesIntoThePdfUnchanged()
    {
        string photo = Photo("photo.jpg", 640, 480);

        PdfPicture picture = PdfPictures.Prepare(photo);

        Assert.Equal(File.ReadAllBytes(photo), picture.Data);
    }

    [Fact]
    public void AWhatsAppJpeg_StillMakesAPdf()
    {
        // WhatsApp puts its own header block ("JFXX…WA") first; PDFsharp's JPEG reader refuses that.
        byte[] plain = File.ReadAllBytes(Photo("plain.jpg", 640, 480));
        byte[] whatsApp = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x12, (byte)'J', (byte)'F', (byte)'X', (byte)'X', 0, (byte)'W', (byte)'A' }
            .Concat(new byte[9]).Concat(plain[2..]).ToArray();
        string path = InFolder("WhatsApp Image.jpeg");
        File.WriteAllBytes(path, whatsApp);

        string pdf = PictureDocument.Save(new[] { new PicturePage(path, Above: "Trip") }, PageOptions.Default);

        Assert.Equal(1, PageCount(pdf));
    }

    [Fact]
    public void ASidewaysPhonePhoto_IsTurnedUpright()
    {
        string photo = Photo("phone.jpg", 640, 480, orientation: OrientationType.RightTop); // stored on its side

        PdfPicture picture = PdfPictures.Prepare(photo);

        Assert.Equal((480, 640), (picture.Width, picture.Height));
    }

    [Fact]
    public void Split_SavesEveryPageAsItsOwnPdf()
    {
        string pdf = Pdf("report.pdf", "Red", "Lime", "Blue");

        string folder = PdfTools.Split(pdf);

        Assert.Equal(InFolder("report (pages)"), folder);
        string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToArray()!;
        Assert.Equal(new[] { "page 01.pdf", "page 02.pdf", "page 03.pdf" }, files);
        Assert.All(Directory.GetFiles(folder), file => Assert.Equal(1, PageCount(file)));
    }

    [Fact]
    public void Split_HasNothingToDoForOnePage()
    {
        string pdf = Pdf("one.pdf", "Red");

        Assert.Throws<NothingToDoException>(() => PdfTools.Split(pdf));
    }

    [Fact]
    public void PageNumbers_AreAddedAtTheBottomOfEveryPage()
    {
        string pdf = Pdf("report.pdf", "White", "White");

        string numbered = PdfTools.AddPageNumbers(pdf);

        Assert.Equal(InFolder("report (numbered).pdf"), numbered);
        List<MagickImage> pages = Render(numbered);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.True(Ink(page, 0.4, 0.9, 0.6, 1) > 0.002, "a number at the bottom"));
        Assert.All(pages, page => Assert.True(Ink(page, 0, 0, 1, 0.85) < 0.001, "nothing drawn elsewhere"));
        pages.ForEach(page => page.Dispose());
    }

    [Fact]
    public void Compress_ShrinksBigPhotos()
    {
        var pages = new[] { new PicturePage(Photo("big.jpg", 4000, 3000)) };
        using (PdfDocument document = PictureDocument.Create(pages, PageOptions.Default))
            document.Save(InFolder("scan.pdf"));

        string compressed = PdfTools.Compress(InFolder("scan.pdf"));

        Assert.Equal(InFolder("scan (compressed).pdf"), compressed);
        Assert.True(new FileInfo(compressed).Length < new FileInfo(InFolder("scan.pdf")).Length / 2);
        Assert.Equal(1, PageCount(compressed));
    }

    [Fact]
    public void Compress_LeavesAPdfWithoutPhotosAlone()
    {
        string pdf = InFolder("text.pdf");
        using (var document = new PdfDocument())
        {
            using (var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(document.AddPage()))
                graphics.DrawRectangle(PdfSharp.Drawing.XBrushes.Navy, 50, 50, 200, 100);
            document.Save(pdf);
        }

        Assert.Throws<NothingToDoException>(() => PdfTools.Compress(pdf));
    }

    [Fact]
    public void TheOrganizer_SavesPagesInTheirNewOrder_Turned_AndMergedWithPictures()
    {
        string first = Pdf("first.pdf", "Red", "Lime");
        string second = Pdf("second.pdf", "Blue");
        string picture = Photo("photo.jpg", 400, 300, "Yellow");

        string merged = PageAssembly.Save(new[]
        {
            new AssembledPage(second, 0, 0),
            new AssembledPage(first, 1, 90),
            new AssembledPage(picture, -1, 0),
        });

        Assert.Equal(InFolder("second (merged).pdf"), merged);
        List<MagickImage> pages = Render(merged);
        Assert.Equal(3, pages.Count);
        Assert.True(pages[0].GetPixels().GetPixel((int)pages[0].Width / 2, (int)pages[0].Height / 2)!.GetChannel(2) > 200, "blue first");
        Assert.True(pages[1].Width > pages[1].Height, "the green page is turned on its side");
        Assert.True(pages[2].Width > pages[2].Height, "the picture gets a page of its own shape");
        pages.ForEach(page => page.Dispose());
    }

    [Fact]
    public void OrganizingOnePdf_SavesAnEditedCopy()
    {
        string pdf = Pdf("notes.pdf", "Red", "Lime", "Blue");

        string edited = PageAssembly.Save(new[] { new AssembledPage(pdf, 2, 0), new AssembledPage(pdf, 0, 0) });

        Assert.Equal(InFolder("notes (edited).pdf"), edited);
        Assert.Equal(2, PageCount(edited));
    }
}
