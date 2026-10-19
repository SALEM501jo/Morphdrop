using System.Runtime.ExceptionServices;
using System.Windows;
using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Editing;
using ShiftDrop.Imaging;
using static ShiftDrop.Tests.TestImages;

namespace ShiftDrop.Tests;

public class CropMathTests
{
    private static readonly Size Bounds = new(400, 300);

    [Fact]
    public void Largest_CentresTheBiggestBoxOfThatShape()
    {
        Assert.Equal(new Rect(50, 0, 300, 300), CropMath.Largest(Bounds, 1));
        Assert.Equal(new Rect(0, 37.5, 400, 225), CropMath.Largest(Bounds, 16.0 / 9));
    }

    [Fact]
    public void Moving_StaysInsideTheImage()
    {
        Assert.Equal(new Rect(300, 0, 100, 100), CropMath.Move(new Rect(250, 50, 100, 100), 500, -500, Bounds));
    }

    [Fact]
    public void DraggingACorner_MovesOnlyThoseEdges()
    {
        Rect crop = CropMath.Resize(new Rect(0, 0, 400, 300), Edges.Left | Edges.Top, 40, 30, Bounds, ratio: null);

        Assert.Equal(new Rect(40, 30, 360, 270), crop);
    }

    [Fact]
    public void Edges_CantCrossOrGoBelowTheMinimum()
    {
        Rect crop = CropMath.Resize(new Rect(100, 100, 50, 50), Edges.Right, -500, 0, Bounds, ratio: null);

        Assert.Equal(CropMath.MinSize, crop.Width);
        Assert.Equal(100, crop.X);
    }

    [Fact]
    public void WithARatio_TheOtherSideFollows()
    {
        Rect crop = CropMath.Resize(new Rect(0, 0, 200, 200), Edges.Right | Edges.Bottom, -100, 0, Bounds, ratio: 1);

        Assert.Equal(new Rect(0, 0, 100, 100), crop);
    }

    [Fact]
    public void WithARatio_ADragOutOfTheImageIsIgnored()
    {
        var crop = new Rect(0, 0, 300, 300);

        Assert.Equal(crop, CropMath.Resize(crop, Edges.Right, 50, 0, Bounds, ratio: 1)); // 350 x 350 won't fit
    }
}

public sealed class ImageEditTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    [Fact]
    public void Cropping_SavesJustThatArea()
    {
        string png = WritePng(InFolder("half.png"), 40, 20, (x, _) => x < 20 ? Red : Blue);
        using EditableImage image = EditableImage.Load(png);

        string output = image.Save("cropped", frames => ImageEdits.Crop(frames, new PixelRect(25, 5, 10, 10)));

        Assert.Equal(InFolder("half (cropped).png"), output);
        Pixels pixels = Read(output);
        Assert.Equal((10, 10), (pixels.Width, pixels.Height));
        AssertClose(Blue, pixels[5, 5], tolerance: 0);
    }

    [Fact]
    public void CroppingAnAnimation_CropsEveryFrame()
    {
        string gif = MagickImages.Animation(InFolder("blink.gif"), MagickFormat.Gif, "red", "lime", "blue");
        using EditableImage image = EditableImage.Load(gif);

        string output = image.Save("cropped", frames => ImageEdits.Crop(frames, new PixelRect(0, 0, 10, 8)));

        Assert.Equal(3, MagickImages.FrameCount(output));
        Assert.Equal((10u, 8u, MagickFormat.Gif), MagickImages.Info(output));
    }

    [Fact]
    public void BlackBoxes_CoverTheAreaAndNothingElse()
    {
        string png = WritePng(InFolder("card.png"), 40, 40, (_, _) => Red);
        using EditableImage image = EditableImage.Load(png);

        string output = image.Save("redacted", frames => ImageEdits.Redact(frames, new[] { new PixelRect(10, 10, 20, 10) }, RedactStyle.BlackBox));

        Pixels pixels = Read(output);
        AssertClose(Colors_Black, pixels[15, 15], tolerance: 0);
        AssertClose(Colors_Black, pixels[29, 19], tolerance: 0);
        AssertClose(Red, pixels[5, 5], tolerance: 0);
        AssertClose(Red, pixels[15, 25], tolerance: 0);
    }

    [Fact]
    public void Pixelating_LeavesOnlyCoarseBlocks()
    {
        // A fine checkerboard: after pixelation the area must no longer alternate pixel by pixel.
        string png = WritePng(InFolder("text.png"), 80, 80, (x, y) => (x + y) % 2 == 0 ? Colors_Black : White);
        using EditableImage image = EditableImage.Load(png);

        string output = image.Save("redacted", frames => ImageEdits.Redact(frames, new[] { new PixelRect(0, 0, 80, 80) }, RedactStyle.Pixelate));

        Pixels pixels = Read(output);
        Assert.Equal(pixels[0, 0], pixels[1, 0]);
        Assert.Equal(pixels[0, 0], pixels[0, 1]);
    }

    [Fact]
    public void Redacting_RemovesTheExifThumbnailAndLocation()
    {
        string jpg = MagickImages.Photo(InFolder("id.jpg"), 400, 300, quality: 90, photo =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.Model, "Phone 15");
            photo.SetProfile(exif);
        });
        using EditableImage image = EditableImage.Load(jpg);

        string output = image.Save("redacted", frames => ImageEdits.Redact(frames, new[] { new PixelRect(0, 0, 50, 50) }, RedactStyle.BlackBox));

        using var redacted = new MagickImage(output);
        Assert.Null(redacted.GetExifProfile());
    }

    [Fact]
    public void Previews_AreShrunkAndMapBackToFullSize()
    {
        string jpg = MagickImages.Photo(InFolder("big.jpg"), 3200, 2400, quality: 85);
        using EditableImage image = EditableImage.Load(jpg);

        Assert.Equal(1600, image.Preview.PixelWidth);
        Assert.Equal(2.0, image.PreviewScale, precision: 3);
        Assert.Equal(new PixelRect(200, 100, 400, 300), PixelRect.FromPreview(new Rect(100, 50, 200, 150), image.PreviewScale));
    }

    [Fact]
    public void AnimatedPngs_AreNotOpened()
    {
        string apng = InFolder("ball.png");
        File.WriteAllBytes(apng, ApngBuilder.Build(10, 10, new ApngBuilder.Frame("red", 10, 10), new ApngBuilder.Frame("blue", 10, 10)));

        Assert.Throws<NothingToDoException>(() => EditableImage.Load(apng));
    }

    [Theory]
    [InlineData("photo.jpg", MagickFormat.Jpeg, ".jpg")]
    [InlineData("shot.png", MagickFormat.Png, ".png")]
    [InlineData("anim.gif", MagickFormat.Gif, ".png")] // a background on an animation keeps the first frame, as PNG
    public void ComposedPictures_AreSavedAsPhotoOrGraphic(string name, MagickFormat format, string expectedExtension)
    {
        string path = MagickImages.Gradient(InFolder(name), 40, 30, format);
        using EditableImage image = EditableImage.Load(path);
        byte[] png = new MagickImage(MagickColors.Orange, 60, 50).ToByteArray(MagickFormat.Png);

        string output = image.SaveComposite("background", png);

        Assert.Equal(expectedExtension, Path.GetExtension(output));
        Assert.Equal((60u, 50u), (MagickImages.Info(output).Width, MagickImages.Info(output).Height));
    }

    private static readonly System.Windows.Media.Color Colors_Black = System.Windows.Media.Colors.Black;
}

/// <summary>WPF needs a single-threaded apartment for anything visual.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

public sealed class BackgroundAndEditorTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    [Theory]
    [InlineData(1000, 500, null, 1160, 660)] // 8% of the long side on every side
    [InlineData(1000, 500, 1.0, 1160, 1160)]
    [InlineData(400, 800, 16.0 / 9, 1650, 928)] // 528 x 928 widened to 16:9
    public void CanvasSize_AddsPaddingThenWidensToTheRatio(double width, double height, double? ratio, double expectedWidth, double expectedHeight)
    {
        Size size = BackgroundComposer.CanvasSize(width, height, BackgroundComposer.Default with { AspectRatio = ratio });

        Assert.Equal(new Size(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void Rendering_PutsTheImageOnTheBackgroundWithRoundCorners()
    {
        string png = WritePng(Path.Combine(_folder, "shot.png"), 200, 100, (_, _) => Red);
        byte[] rendered = Array.Empty<byte>();
        var options = BackgroundComposer.Default with { Background = BackgroundComposer.Presets.Single(p => p.Name == "White"), Corners = 0.2, Shadow = false };

        Sta.Run(() =>
        {
            using EditableImage image = EditableImage.Load(png);
            rendered = BackgroundComposer.RenderPng(image.FullSizeBitmap(), options);
        });

        string output = Path.Combine(_folder, "rendered.png");
        File.WriteAllBytes(output, rendered);
        Pixels pixels = Read(output);
        Assert.Equal((232, 132), (pixels.Width, pixels.Height)); // 200 x 100 plus 16 px padding
        AssertClose(White, pixels[2, 2], tolerance: 0);
        AssertClose(Red, pixels[116, 66], tolerance: 0);
        AssertClose(White, pixels[17, 17], tolerance: 10); // the rounded corner leaves background showing
    }

    [Fact]
    public void Editors_BuildAndSave()
    {
        string jpg = MagickImages.Photo(Path.Combine(_folder, "photo.jpg"), 600, 400, quality: 90);
        string? saved = null;

        Sta.Run(() =>
        {
            using EditableImage forCrop = EditableImage.Load(jpg);
            var crop = new CropEditor(forCrop);
            Assert.False(crop.CanSave); // the box starts as the whole image

            var redact = new RedactEditor(EditableImage.Load(jpg));
            Assert.False(redact.CanSave); // nothing drawn yet

            using EditableImage forBackground = EditableImage.Load(jpg);
            var background = new BackgroundEditor(forBackground);
            Assert.True(background.CanSave);
            saved = background.PrepareSave()();
        });

        Assert.Equal(Path.Combine(_folder, "photo (background).jpg"), saved);
        (uint width, uint height, _) = MagickImages.Info(saved!);
        Assert.True(width > 600 && height > 400);
    }
}
