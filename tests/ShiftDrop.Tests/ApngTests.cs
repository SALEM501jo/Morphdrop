using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Imaging;
using static ShiftDrop.Tests.ApngBuilder;

namespace ShiftDrop.Tests;

public sealed class ApngTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string Save(string name, byte[] png)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, png);
        return path;
    }

    private string ThreeColors() => Save("blink.png", Build(20, 20,
        new Frame("red", 20, 20, DelayHundredths: 25),
        new Frame("lime", 20, 20, DelayHundredths: 25),
        new Frame("blue", 20, 20, DelayHundredths: 25)));

    private static IMagickColor<byte> Pixel(IMagickImage<byte> image, int x, int y)
    {
        using IPixelCollection<byte> pixels = image.GetPixels();
        return pixels.GetPixel(x, y).ToColor()!;
    }

    private static void AssertNear(IMagickColor<byte> expected, IMagickColor<byte> actual, int tolerance = 8)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(near, $"Expected {expected} but got {actual}");
    }

    private static MagickImageCollection ReadCoalesced(string path)
    {
        var frames = new MagickImageCollection(path);
        frames.Coalesce();
        return frames;
    }

    [Fact]
    public void AnimatedPngs_AreRecognized()
    {
        string animated = ThreeColors();
        string still = MagickImages.Gradient(Path.Combine(_folder, "still.png"), 20, 20, MagickFormat.Png);

        Assert.True(ApngReader.IsAnimated(animated));
        Assert.True(ApngReader.IsAnimated(File.ReadAllBytes(animated)));
        Assert.False(ApngReader.IsAnimated(still));
    }

    [Theory]
    [InlineData("WEBP")]
    [InlineData("GIF")]
    public void ConvertingAnApng_KeepsEveryFrameAndItsTiming(string name)
    {
        ImageFormat format = ImageFormat.WheelTargets.Single(f => f.Name == name);

        string output = ImageConverter.Convert(ThreeColors(), format);

        using MagickImageCollection frames = ReadCoalesced(output);
        Assert.Equal(3, frames.Count);
        AssertNear(MagickColors.Red, Pixel(frames[0], 10, 10)); // WebP is lossy: #FF0000 may come back as #FF0100
        AssertNear(MagickColors.Lime, Pixel(frames[1], 10, 10));
        AssertNear(MagickColors.Blue, Pixel(frames[2], 10, 10));
        Assert.All(frames, frame => Assert.Equal(25u, frame.AnimationDelay));
    }

    [Fact]
    public void ConvertingAnApngToJpg_TakesTheFirstFrame()
    {
        string jpg = ImageConverter.Convert(ThreeColors(), ImageFormat.Jpeg);

        TestImages.AssertClose(TestImages.PureRed, TestImages.Read(jpg)[10, 10], tolerance: 8);
    }

    [Fact]
    public void PartialFrames_AreDrawnOverThePreviousOne()
    {
        byte[] png = Build(20, 20,
            new Frame("red", 20, 20),
            new Frame("blue", 10, 10, X: 10, Y: 10));

        using MagickImageCollection frames = ApngReader.Read(png);

        Assert.Equal(MagickColors.Red, Pixel(frames[1], 5, 5));
        Assert.Equal(MagickColors.Blue, Pixel(frames[1], 15, 15));
    }

    [Fact]
    public void DisposeBackground_ClearsTheFrameArea()
    {
        byte[] png = Build(20, 20,
            new Frame("red", 20, 20, Dispose: DisposeBackground),
            new Frame("blue", 10, 10, X: 10, Y: 10));

        using MagickImageCollection frames = ApngReader.Read(png);

        Assert.Equal(0, Pixel(frames[1], 5, 5).A);
        Assert.Equal(MagickColors.Blue, Pixel(frames[1], 15, 15));
    }

    [Fact]
    public void DisposePrevious_RestoresTheCanvas()
    {
        byte[] png = Build(20, 20,
            new Frame("red", 20, 20),
            new Frame("blue", 10, 10, Dispose: DisposePrevious),
            new Frame("lime", 1, 1, X: 19, Y: 19, Blend: BlendOver));

        using MagickImageCollection frames = ApngReader.Read(png);

        Assert.Equal(MagickColors.Blue, Pixel(frames[1], 5, 5));
        Assert.Equal(MagickColors.Red, Pixel(frames[2], 5, 5)); // the blue square was rolled back
        Assert.Equal(MagickColors.Lime, Pixel(frames[2], 19, 19));
    }

    [Fact]
    public void CompressingOrResizingAnApng_IsRefusedInsteadOfStoppingTheAnimation()
    {
        string apng = ThreeColors();

        Assert.Throws<NothingToDoException>(() => ImageTools.Compress(apng));
        Assert.Throws<NothingToDoException>(() => ImageTools.Resize(apng, ResizeMode.Half));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void CleaningAnApng_KeepsTheAnimation()
    {
        string apng = ThreeColors();
        File.WriteAllBytes(apng, AddTextChunk(File.ReadAllBytes(apng), "Software", "Some editor"));

        string output = ImageTools.RemoveMetadata(apng);

        Assert.True(ApngReader.IsAnimated(output));
        Assert.Equal(3, ApngReader.Read(File.ReadAllBytes(output)).Count);
        Assert.DoesNotContain("tEXt", PngChunks.TypesIn(output));
    }

    private static byte[] AddTextChunk(byte[] png, string keyword, string text)
    {
        List<PngChunks.Chunk> chunks = PngChunks.Parse(png);
        using var output = new MemoryStream();
        output.Write(PngChunks.Signature);
        foreach (PngChunks.Chunk chunk in chunks)
        {
            if (chunk.Type == "IEND")
                PngChunks.Write(output, "tEXt", System.Text.Encoding.Latin1.GetBytes($"{keyword}\0{text}"));
            output.Write(png, chunk.Start, chunk.TotalSize);
        }

        return output.ToArray();
    }
}
