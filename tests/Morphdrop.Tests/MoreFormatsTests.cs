using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Imaging;

namespace Morphdrop.Tests;

public sealed class MoreFormatsTests : IDisposable
{
    private const string Circle = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="32" viewBox="0 0 64 32">
          <circle cx="32" cy="16" r="12" fill="#e5483b"/>
        </svg>
        """;

    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-formats-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void AnSvg_BecomesASharpPngWithATransparentBackground()
    {
        string svg = Write("logo.svg", Circle);

        string png = ImageConverter.Convert(svg, ImageFormat.Png);

        using var image = new MagickImage(png);
        Assert.Equal((2048u, 1024u), (image.Width, image.Height)); // drawn big, not at 64 × 32
        using IPixelCollection<byte> pixels = image.GetPixels();
        Assert.Equal(0, pixels.GetPixel(10, 10)!.GetChannel(3)); // transparent corner
        IPixel<byte> middle = pixels.GetPixel(1024, 512)!;
        Assert.True(middle.GetChannel(0) > 200 && middle.GetChannel(1) < 120, "the red circle");
    }

    [Fact]
    public void AnSvg_GetsTheConvertSlicesButNoResize()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("logo.svg");

        Assert.Contains(pages[0].Actions, action => action.Label == "PNG");
        Assert.DoesNotContain(pages.SelectMany(page => page.Actions), action => action.Label == "50%");
    }

    [Fact]
    public void AnSvgDeclaringEntities_IsRefused()
    {
        string svg = Write("trap.svg", """
            <?xml version="1.0"?>
            <!DOCTYPE svg [ <!ENTITY a "aaaaaaaaaa"> ]>
            <svg xmlns="http://www.w3.org/2000/svg"><text>&a;</text></svg>
            """);

        Assert.Null(FormatDetector.Detect(svg));
    }

    [Fact]
    public void ATextFileNamedSvg_IsNotAnImage()
    {
        string notes = Write("notes.svg", "Shopping list: milk, eggs");

        Assert.Null(FormatDetector.Detect(notes));
    }

    [Theory]
    [InlineData("photo.cr2", new byte[] { (byte)'I', (byte)'I', (byte)'*', 0, 8, 0, 0, 0 })]
    [InlineData("photo.nef", new byte[] { (byte)'M', (byte)'M', 0, (byte)'*', 0, 0, 0, 8 })]
    [InlineData("photo.DNG", new byte[] { (byte)'I', (byte)'I', (byte)'*', 0, 8, 0, 0, 0 })]
    [InlineData("photo.rw2", new byte[] { (byte)'I', (byte)'I', (byte)'U', 0, 8, 0, 0, 0 })]
    [InlineData("photo.raf", new byte[] { (byte)'F', (byte)'U', (byte)'J', (byte)'I', (byte)'F', (byte)'I', (byte)'L', (byte)'M' })]
    public void CameraRawFiles_AreRecognizedByExtensionAndLayout(string name, byte[] header)
    {
        string raw = Write(name, header.Concat(new byte[64]).ToArray());

        Assert.Equal(ImageKind.Raw, FormatDetector.Detect(raw)?.Kind);
    }

    [Fact]
    public void AJpegNamedCr2_IsStillAJpeg()
    {
        string jpeg = Write("photo.cr2", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(new byte[64]).ToArray());

        Assert.Equal(ImageKind.Jpeg, FormatDetector.Detect(jpeg)?.Kind);
    }

    [Fact]
    public void ATiffIsStillATiff()
    {
        string tiff = Write("scan.tif", new byte[] { (byte)'I', (byte)'I', (byte)'*', 0, 8, 0, 0, 0 }.Concat(new byte[64]).ToArray());

        Assert.Equal(ImageKind.Tiff, FormatDetector.Detect(tiff)?.Kind);
    }

    [Fact]
    public void JpegXl_IsOnTheConvertPage()
    {
        string png = Path.Combine(_folder, "a.png");
        using (var image = new MagickImage(MagickColors.Orange, 32, 32))
            image.Write(png);

        string jxl = ImageConverter.Convert(png, ImageFormat.Jxl);

        Assert.Equal(".jxl", Path.GetExtension(jxl));
        Assert.Equal(ImageKind.Jxl, FormatDetector.Detect(jxl)?.Kind);
        Assert.Contains(ActionCatalog.ForFileName("a.png")[0].Actions, action => action.Label == "JXL");
    }
}
