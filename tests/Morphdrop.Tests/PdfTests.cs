using System.Windows.Media;
using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Imaging;
using static Morphdrop.Tests.TestImages;

namespace Morphdrop.Tests;

public sealed class PdfTests : IDisposable
{
    private static readonly Color PureLime = Color.FromRgb(0, 255, 0);

    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    /// <summary>A PDF with one solid-color page per color, written by ImageMagick.</summary>
    private string Pdf(string name, params string[] colors)
    {
        using var pages = new MagickImageCollection();
        foreach (string color in colors)
            pages.Add(new MagickImage(new MagickColor(color), 300, 400) { Density = new Density(72, 72, DensityUnit.PixelsPerInch) });
        string path = InFolder(name);
        pages.Write(path, MagickFormat.Pdf);
        return path;
    }

    private static Color Center(string image)
    {
        Pixels pixels = Read(image);
        return pixels[pixels.Width / 2, pixels.Height / 2];
    }

    [Fact]
    public void AMultiPagePdf_BecomesAFolderWithAnImagePerPage()
    {
        string pdf = Pdf("report.pdf", "red", "lime", "blue");

        string folder = new PdfToImagesAction(ImageFormat.Png).Run(pdf);

        Assert.Equal(InFolder("report (pages)"), folder);
        string[] pages = Directory.GetFiles(folder).Order().ToArray();
        Assert.Equal(new[] { "page 01.png", "page 02.png", "page 03.png" }, pages.Select(Path.GetFileName));
        AssertClose(PureRed, Center(pages[0]), tolerance: 30);
        AssertClose(PureLime, Center(pages[1]), tolerance: 30);
        AssertClose(PureBlue, Center(pages[2]), tolerance: 30);
    }

    [Fact]
    public void PagesAreRenderedAt200Dpi()
    {
        string pdf = Pdf("letter.pdf", "white"); // 300 x 400 pt = 4.17 x 5.56 inches

        Pixels page = Read(new PdfToImagesAction(ImageFormat.Png).Run(pdf));

        Assert.InRange(page.Width, 830, 836);
        Assert.InRange(page.Height, 1108, 1114);
    }

    [Fact]
    public void AOnePagePdf_BecomesASingleImageNextToIt()
    {
        string pdf = Pdf("receipt.pdf", "blue");

        string jpg = new PdfToImagesAction(ImageFormat.Jpeg).Run(pdf);

        Assert.Equal(InFolder("receipt.jpg"), jpg);
        AssertClose(PureBlue, Center(jpg), tolerance: 30);
    }

    [Fact]
    public void ADamagedPdf_FailsWithoutLeavingAnything()
    {
        string pdf = InFolder("broken.pdf");
        File.WriteAllText(pdf, "%PDF-1.7\nthis is not really a PDF");

        Assert.Throws<InvalidDataException>(() => new PdfToImagesAction(ImageFormat.Png).Run(pdf));
        Assert.Single(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void ImagesCombine_IntoOnePdf_InFileExplorerOrder()
    {
        string[] images =
        {
            WritePng(InFolder("scan 10.png"), 60, 80, (_, _) => PureBlue),
            WritePng(InFolder("scan 2.png"), 60, 80, (_, _) => PureLime),
            MagickImages.Gradient(InFolder("scan 1.jpg"), 60, 80, MagickFormat.Jpeg, image => image.Colorize(MagickColors.Red, new Percentage(100))),
        };

        string pdf = ImageConverter.CombineToPdf(images);

        Assert.Equal(InFolder("scan 1 (3 pages).pdf"), pdf);
        var colors = new List<Color>();
        PdfPages.Render(pdf, (_, _, png) =>
        {
            string page = InFolder($"rendered {colors.Count}.png");
            File.WriteAllBytes(page, png);
            colors.Add(Center(page));
        });
        Assert.Equal(3, colors.Count);
        AssertClose(PureRed, colors[0], tolerance: 40); // scan 1, scan 2, scan 10, not the drop order
        AssertClose(PureLime, colors[1], tolerance: 40);
        AssertClose(PureBlue, colors[2], tolerance: 40);
    }

    [Fact]
    public async Task DroppingSeveralImagesOnPdf_MakesOneFile()
    {
        string[] images = Enumerable.Range(1, 3)
            .Select(i => WritePng(InFolder($"photo {i}.png"), 40, 40, (_, _) => Red))
            .ToArray();
        var runner = new ActionRunner();
        var completed = new TaskCompletionSource<ActionResult>();
        runner.Completed += r => completed.TrySetResult(r); // posted to the caller's context, after the job

        await runner.Run(new ConvertImageAction(ImageFormat.Pdf), images)!;
        ActionResult result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(InFolder("photo 1 (3 pages).pdf"), Assert.Single(result.Outputs).Output);
        Assert.Single(Directory.GetFiles(_folder, "*.pdf"));
    }

    [Fact]
    public void APdf_OffersImageFormats()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("report.pdf");

        ActionPage convert = Assert.Single(pages);
        Assert.Equal(new[] { "PNG", "JPG", "WEBP" }, convert.Actions.Select(a => a.Label));
        Assert.Equal("Save the pages as PNG", convert.Actions[0].Description);
    }

    [Fact]
    public void ThePdfSlice_SaysItCombinesWhenSeveralFilesAreDragged()
    {
        var pdf = new ConvertImageAction(ImageFormat.Pdf);

        Assert.False(pdf.Combines(new[] { "a.png" }));
        Assert.True(pdf.Combines(new[] { "a.png", "b.png" }));
        Assert.Equal("Combine into one 2-page PDF", pdf.CombinedDescription(2));
    }
}
