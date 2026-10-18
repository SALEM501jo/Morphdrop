using System.Windows.Media.Imaging;
using ShiftDrop.Imaging;
using static ShiftDrop.Tests.TestImages;

namespace ShiftDrop.Tests;

public sealed class ImageConversionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string HalfTransparentPng(string name = "half.png") =>
        WritePng(Path.Combine(_folder, name), 16, 16, (x, _) => x < 8 ? Clear : Red);

    [Fact]
    public void PngToJpg_PutsTransparentAreasOnWhite()
    {
        string jpg = ImageConverter.Convert(HalfTransparentPng(), ImageFormat.Jpeg);

        Assert.Equal(Path.Combine(_folder, "half.jpg"), jpg);
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
    public void EveryFormat_RoundTripsTheImageSize(string name)
    {
        string png = WritePng(Path.Combine(_folder, "wide.png"), 40, 10, (x, _) => x < 20 ? Blue : Red);
        ImageFormat format = ImageFormat.Writable.Single(f => f.Name == name);

        string output = ImageConverter.Convert(png, format);

        Assert.Equal(format.Extension, Path.GetExtension(output));
        Pixels pixels = Read(output);
        Assert.Equal((40, 10), (pixels.Width, pixels.Height));
        AssertClose(Blue, pixels[5, 5]);
        AssertClose(Red, pixels[35, 5]);
    }

    [Fact]
    public void ExistingFiles_AreNeverOverwritten()
    {
        string png = HalfTransparentPng("photo.png");
        File.WriteAllText(Path.Combine(_folder, "photo.jpg"), "keep me");

        string first = ImageConverter.Convert(png, ImageFormat.Jpeg);
        string second = ImageConverter.Convert(png, ImageFormat.Jpeg);

        Assert.Equal(Path.Combine(_folder, "photo (1).jpg"), first);
        Assert.Equal(Path.Combine(_folder, "photo (2).jpg"), second);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_folder, "photo.jpg")));
    }

    [Fact]
    public void PngToIco_HoldsEveryStandardSizeUpToTheImageSize()
    {
        string png = WritePng(Path.Combine(_folder, "logo.png"), 300, 200, (x, y) => Red);

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
        string png = WritePng(Path.Combine(_folder, "small.png"), 40, 40, (x, y) => Blue);

        using FileStream file = File.OpenRead(ImageConverter.Convert(png, ImageFormat.Ico));
        var sizes = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Select(f => f.PixelWidth);
        Assert.Equal(new[] { 16, 24, 32 }, sizes.Order());
    }

    [Fact]
    public void AnUnreadableFile_FailsWithoutLeavingOutput()
    {
        string bad = Path.Combine(_folder, "bad.png");
        File.WriteAllText(bad, "this is not an image");

        Assert.ThrowsAny<Exception>(() => ImageConverter.Convert(bad, ImageFormat.Jpeg));
        Assert.False(File.Exists(Path.Combine(_folder, "bad.jpg")));
    }

    [Theory]
    [InlineData(1, 2, 1, "RB")]
    [InlineData(2, 2, 1, "BR")] // mirrored
    [InlineData(3, 2, 1, "BR")] // upside down
    [InlineData(6, 1, 2, "RB")] // rotate 90° clockwise: the left pixel ends up on top
    [InlineData(8, 1, 2, "BR")] // rotate 90° counter-clockwise
    public void ExifOrientation_TurnsTheImageUpright(int orientation, int width, int height, string order)
    {
        BitmapSource stored = Create(2, 1, (x, _) => x == 0 ? Red : Blue);

        Pixels upright = Read(ImageLoader.ApplyOrientation(stored, orientation));

        Assert.Equal((width, height), (upright.Width, upright.Height));
        AssertClose(order[0] == 'R' ? Red : Blue, upright[0, 0], tolerance: 0);
        AssertClose(order[1] == 'R' ? Red : Blue, width == 2 ? upright[1, 0] : upright[0, 1], tolerance: 0);
    }
}
