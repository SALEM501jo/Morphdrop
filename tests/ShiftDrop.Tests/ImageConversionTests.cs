using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ImageMagick;
using ShiftDrop.Imaging;
using static ShiftDrop.Tests.TestImages;

namespace ShiftDrop.Tests;

public sealed class ImageConversionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string InFolder(string name) => Path.Combine(_folder, name);

    private string HalfTransparentPng(string name = "half.png") =>
        WritePng(InFolder(name), 16, 16, (x, _) => x < 8 ? Clear : Red);

    [Fact]
    public void PngToJpg_PutsTransparentAreasOnWhite()
    {
        string jpg = ImageConverter.Convert(HalfTransparentPng(), ImageFormat.Jpeg);

        Assert.Equal(InFolder("half.jpg"), jpg);
        Pixels pixels = Read(jpg);
        Assert.Equal((16, 16), (pixels.Width, pixels.Height));
        AssertClose(White, pixels[2, 8]);
        AssertClose(Red, pixels[13, 8]);
    }

    [Fact]
    public void PngToTiff_KeepsTransparency()
    {
        Pixels pixels = Read(ImageConverter.Convert(HalfTransparentPng(), ImageFormat.Tiff));

        Assert.Equal(0, pixels[2, 8].A);
        AssertClose(Red, pixels[13, 8], tolerance: 0);
    }

    [Theory]
    [InlineData("BMP")]
    [InlineData("GIF")]
    [InlineData("JPG")]
    [InlineData("TIFF")]
    public void ClassicFormats_RoundTripTheImage(string name)
    {
        string png = WritePng(InFolder("wide.png"), 40, 10, (x, _) => x < 20 ? Blue : Red);
        ImageFormat format = ImageFormat.WheelTargets.Single(f => f.Name == name);

        string output = ImageConverter.Convert(png, format);

        Assert.Equal(format.Extension, Path.GetExtension(output));
        Pixels pixels = Read(output); // decoded by Windows (WIC), independently of ImageMagick
        Assert.Equal((40, 10), (pixels.Width, pixels.Height));
        AssertClose(Blue, pixels[5, 5]);
        AssertClose(Red, pixels[35, 5]);
    }

    [Theory]
    [InlineData("WEBP", MagickFormat.WebP)]
    [InlineData("AVIF", MagickFormat.Avif)]
    public void ModernFormats_KeepSizeAndTransparency(string name, MagickFormat expected)
    {
        ImageFormat format = ImageFormat.WheelTargets.Single(f => f.Name == name);

        string output = ImageConverter.Convert(HalfTransparentPng(), format);

        Assert.Equal((16u, 16u, expected), MagickImages.Info(output));
        Assert.True(MagickImages.Pixel(output, 2, 8).A < 20);
        Assert.True(MagickImages.Pixel(output, 13, 8).A > 235);
    }

    [Fact]
    public void Avif_CanBeReadToo()
    {
        string avif = MagickImages.Gradient(InFolder("photo.avif"), 64, 48, MagickFormat.Avif);

        Pixels pixels = Read(ImageConverter.Convert(avif, ImageFormat.Png));

        Assert.Equal((64, 48), (pixels.Width, pixels.Height));
    }

    [Fact]
    public void AMislabeledFile_IsReadByItsContent()
    {
        string png = HalfTransparentPng("actually-a-png.png");
        string mislabeled = InFolder("actually-a-png.jpg");
        File.Move(png, mislabeled);

        Pixels pixels = Read(ImageConverter.Convert(mislabeled, ImageFormat.Png));

        Assert.Equal(0, pixels[2, 8].A);
        AssertClose(Red, pixels[13, 8], tolerance: 0);
    }

    [Fact]
    public void ExifOrientation_IsApplied()
    {
        // Stored 40x20 (red left, blue right) with "rotate 90° clockwise to display".
        string jpg = InFolder("sideways.jpg");
        using (var image = new MagickImage(MagickColors.Red, 40, 20))
        {
            using var blue = new MagickImage(MagickColors.Blue, 20, 20);
            image.Composite(blue, 20, 0, CompositeOperator.Over);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = OrientationType.RightTop;
            image.Write(jpg, MagickFormat.Jpeg);
        }

        Pixels upright = Read(ImageConverter.Convert(jpg, ImageFormat.Png));

        Assert.Equal((20, 40), (upright.Width, upright.Height));
        AssertClose(PureRed, upright[10, 5]); // the left side ends up on top
        AssertClose(PureBlue, upright[10, 35]);
    }

    [Fact]
    public void AnimatedGifToWebP_KeepsEveryFrame()
    {
        string gif = MagickImages.Animation(InFolder("blink.gif"), MagickFormat.Gif, "red", "lime", "blue");

        string webp = ImageConverter.Convert(gif, ImageFormat.WebP);

        Assert.Equal(3, MagickImages.FrameCount(webp));
    }

    [Fact]
    public void AnimatedGifToPng_TakesTheFirstFrame()
    {
        string gif = MagickImages.Animation(InFolder("blink.gif"), MagickFormat.Gif, "red", "lime", "blue");

        AssertClose(PureRed, Read(ImageConverter.Convert(gif, ImageFormat.Png))[10, 10], tolerance: 0);
    }

    [Fact]
    public void PhotoToPdf_GetsAboutAnA4Page()
    {
        string jpg = MagickImages.Gradient(InFolder("scan.jpg"), 3000, 2000, MagickFormat.Jpeg);

        string pdf = ImageConverter.Convert(jpg, ImageFormat.Pdf);

        string text = Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        Assert.StartsWith("%PDF-", text);
        Assert.Single(Regex.Matches(text, @"/Type\s*/Page(?!s)"));
        Match box = Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(box.Success, "no MediaBox");
        Assert.InRange(double.Parse(box.Groups[1].Value), 838, 846); // A4 landscape is 842 x 595 pt
        Assert.InRange(double.Parse(box.Groups[2].Value), 500, 600);
    }

    [Fact]
    public void MultiPageTiffToPdf_KeepsEveryPage()
    {
        string tiff = InFolder("pages.tiff");
        using (var pages = new MagickImageCollection())
        {
            pages.Add(new MagickImage(MagickColors.White, 100, 140));
            pages.Add(new MagickImage(MagickColors.LightGray, 100, 140));
            pages.Write(tiff, MagickFormat.Tiff);
        }

        string text = Encoding.Latin1.GetString(File.ReadAllBytes(ImageConverter.Convert(tiff, ImageFormat.Pdf)));

        Assert.Equal(2, Regex.Matches(text, @"/Type\s*/Page(?!s)").Count);
    }

    [Fact]
    public void ExistingFiles_AreNeverOverwritten()
    {
        string png = HalfTransparentPng("photo.png");
        File.WriteAllText(InFolder("photo.jpg"), "keep me");

        string first = ImageConverter.Convert(png, ImageFormat.Jpeg);
        string second = ImageConverter.Convert(png, ImageFormat.Jpeg);

        Assert.Equal(InFolder("photo (1).jpg"), first);
        Assert.Equal(InFolder("photo (2).jpg"), second);
        Assert.Equal("keep me", File.ReadAllText(InFolder("photo.jpg")));
    }

    [Fact]
    public void PngToIco_HoldsEveryStandardSizeUpToTheImageSize()
    {
        string png = WritePng(InFolder("logo.png"), 300, 200, (_, _) => Red);

        string ico = ImageConverter.Convert(png, ImageFormat.Ico);

        using FileStream file = File.OpenRead(ico);
        var frames = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
        Assert.Equal(IcoWriter.StandardSizes, frames.Select(f => f.PixelWidth).Order());
        Assert.All(frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));

        // 300x200 fitted into a square: transparent bands above and below, the picture in the middle.
        Pixels largest = Read(frames.MaxBy(f => f.PixelWidth)!);
        Assert.Equal(0, largest[128, 5].A);
        AssertClose(Red, largest[128, 128]);
    }

    [Fact]
    public void SmallImagesAreNotScaledUpInIcons()
    {
        string png = WritePng(InFolder("small.png"), 40, 40, (_, _) => Blue);

        using FileStream file = File.OpenRead(ImageConverter.Convert(png, ImageFormat.Ico));
        var sizes = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Select(f => f.PixelWidth);
        Assert.Equal(new[] { 16, 24, 32 }, sizes.Order());
    }

    [Fact]
    public void IconsConvertFromTheirLargestImage()
    {
        string png = WritePng(InFolder("app.png"), 64, 64, (_, _) => Blue);
        string ico = ImageConverter.Convert(png, ImageFormat.Ico);

        Pixels pixels = Read(ImageConverter.Convert(ico, ImageFormat.Png));

        Assert.Equal((64, 64), (pixels.Width, pixels.Height));
    }

    [Fact]
    public void AnUnreadableFile_FailsWithoutLeavingOutput()
    {
        string bad = InFolder("bad.png");
        File.WriteAllText(bad, "this is not an image");

        Assert.Throws<NotSupportedException>(() => ImageConverter.Convert(bad, ImageFormat.Jpeg));
        Assert.False(File.Exists(InFolder("bad.jpg")));
    }
}
