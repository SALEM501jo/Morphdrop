using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Imaging;

namespace ShiftDrop.Tests;

public sealed class ImageToolsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string InFolder(string name) => Path.Combine(_folder, name);

    [Fact]
    public void HalfSize_HalvesBothSides()
    {
        string png = MagickImages.Gradient(InFolder("a.png"), 400, 300, MagickFormat.Png);

        string output = ImageTools.Resize(png, ResizeMode.Half);

        Assert.Equal(InFolder("a (50%).png"), output);
        Assert.Equal((200u, 150u, MagickFormat.Png), MagickImages.Info(output));
    }

    [Fact]
    public void FitFullHd_ShrinksTheLongSideTo1920()
    {
        string jpg = MagickImages.Gradient(InFolder("wide.jpeg"), 4000, 1000, MagickFormat.Jpeg);

        string output = ImageTools.Resize(jpg, ResizeMode.FitFullHd);

        Assert.Equal(InFolder("wide (1920px).jpeg"), output); // keeps the original extension
        Assert.Equal((1920u, 480u, MagickFormat.Jpeg), MagickImages.Info(output));
    }

    [Fact]
    public void FitFullHd_LeavesSmallImagesAlone()
    {
        string jpg = MagickImages.Gradient(InFolder("small.jpg"), 800, 600, MagickFormat.Jpeg);

        Assert.Throws<NothingToDoException>(() => ImageTools.Resize(jpg, ResizeMode.FitFullHd));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ResizingAnAnimation_KeepsEveryFrame()
    {
        string gif = MagickImages.Animation(InFolder("blink.gif"), MagickFormat.Gif, "red", "lime", "blue");

        string output = ImageTools.Resize(gif, ResizeMode.Half);

        Assert.Equal(3, MagickImages.FrameCount(output));
        Assert.Equal((10u, 10u), (MagickImages.Info(output).Width, MagickImages.Info(output).Height));
    }

    [Fact]
    public void CompressingAHighQualityJpeg_MakesItSmaller()
    {
        string jpg = MagickImages.Gradient(InFolder("big.jpg"), 1200, 900, MagickFormat.Jpeg, image => image.Quality = 100);

        string output = ImageTools.Compress(jpg);

        Assert.Equal(InFolder("big (compressed).jpg"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(jpg).Length * 0.8);
        Assert.Equal((1200u, 900u, MagickFormat.Jpeg), MagickImages.Info(output));
    }

    [Fact]
    public void CompressingTwice_FindsNothingMoreToDo()
    {
        string jpg = MagickImages.Gradient(InFolder("big.jpg"), 1200, 900, MagickFormat.Jpeg, image => image.Quality = 100);
        string once = ImageTools.Compress(jpg);

        Assert.Throws<NothingToDoException>(() => ImageTools.Compress(once));
    }

    [Fact]
    public void CompressingAPng_IsLossless()
    {
        string png = MagickImages.Gradient(InFolder("raw.png"), 600, 400, MagickFormat.Png,
            image => image.Settings.SetDefine(MagickFormat.Png, "compression-level", 0));

        string output = ImageTools.Compress(png);

        Assert.True(new FileInfo(output).Length < new FileInfo(png).Length / 2);
        Assert.True(MagickImages.SamePixels(png, output));
    }

    [Fact]
    public void CompressingALossyWebP_MakesItSmaller()
    {
        string webp = MagickImages.Logo(InFolder("pic.webp"), MagickFormat.WebP, image => image.Quality = 95);
        Assert.False(ImageTools.WebPIsLossless(File.ReadAllBytes(webp)));

        string output = ImageTools.Compress(webp);

        Assert.True(new FileInfo(output).Length < new FileInfo(webp).Length * 0.9);
        Assert.Equal(MagickImages.Info(webp), MagickImages.Info(output));
    }

    [Fact]
    public void ALosslessWebP_StaysLossless()
    {
        // ImageMagick writes quality-100 WebP files losslessly.
        string webp = MagickImages.Logo(InFolder("pic.webp"), MagickFormat.WebP, image => image.Quality = 100);
        Assert.True(ImageTools.WebPIsLossless(File.ReadAllBytes(webp)));

        string output = ImageTools.Resize(webp, ResizeMode.Half);

        Assert.True(ImageTools.WebPIsLossless(File.ReadAllBytes(output)));
    }

    [Fact]
    public void WebPLosslessness_IsReadFromTheFile()
    {
        string lossy = MagickImages.Gradient(InFolder("lossy.webp"), 32, 32, MagickFormat.WebP);
        string lossless = MagickImages.Gradient(InFolder("lossless.webp"), 32, 32, MagickFormat.WebP,
            image => image.Settings.SetDefine(MagickFormat.WebP, "lossless", true));

        Assert.False(ImageTools.WebPIsLossless(File.ReadAllBytes(lossy)));
        Assert.True(ImageTools.WebPIsLossless(File.ReadAllBytes(lossless)));
    }

    [Fact]
    public void CleaningAJpeg_RemovesLocationButKeepsOrientationAndPixels()
    {
        string jpg = MagickImages.Gradient(InFolder("trip.jpg"), 300, 200, MagickFormat.Jpeg, image =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "PhoneMaker");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(31, 1), new Rational(57, 1), new Rational(0, 1) });
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = OrientationType.RightTop;
            image.Comment = "secret comment";
        });

        string output = ImageTools.RemoveMetadata(jpg);

        Assert.Equal(InFolder("trip (no metadata).jpg"), output);
        using var cleaned = new MagickImage(output);
        IExifProfile? exif = cleaned.GetExifProfile();
        Assert.NotNull(exif);
        Assert.Equal((ushort)6, exif.GetValue(ExifTag.Orientation)?.Value);
        Assert.Null(exif.GetValue(ExifTag.GPSLatitude));
        Assert.Null(exif.GetValue(ExifTag.Make));
        Assert.Null(cleaned.Comment);
        Assert.True(MagickImages.SamePixels(jpg, output)); // not re-encoded
    }

    [Fact]
    public void CleaningTwice_FindsNothingMoreToDo()
    {
        string jpg = MagickImages.Gradient(InFolder("trip.jpg"), 300, 200, MagickFormat.Jpeg, image =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)8);
            exif.SetValue(ExifTag.Model, "Phone 15");
            image.SetProfile(exif);
            image.Orientation = OrientationType.LeftBottom;
        });
        string once = ImageTools.RemoveMetadata(jpg);

        Assert.Throws<NothingToDoException>(() => ImageTools.RemoveMetadata(once));
    }

    [Fact]
    public void CleaningAPng_RemovesTextChunksButKeepsPixels()
    {
        // ImageMagick writes date:create / date:modify text chunks into every PNG.
        string png = MagickImages.Gradient(InFolder("shot.png"), 120, 80, MagickFormat.Png);
        Assert.Contains("tEXt", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(png)));

        string output = ImageTools.RemoveMetadata(png);

        Assert.DoesNotContain("tEXt", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)));
        Assert.True(MagickImages.SamePixels(png, output));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 }, 6)]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 3, 0, 0, 0, 0, 0, 0 }, 3)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0, 200, 0, 0, 0 }, 1)] // IFD offset out of range
    [InlineData(new byte[] { 1, 2, 3 }, 1)]
    public void ExifOrientation_IsReadInBothByteOrders(byte[] tiff, int expected)
    {
        Assert.Equal(expected, JpegMetadata.ReadOrientation(tiff));
    }
}
