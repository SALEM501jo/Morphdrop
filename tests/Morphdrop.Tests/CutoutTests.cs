using ImageMagick;
using ImageMagick.Drawing;
using Morphdrop.Cutout;

namespace Morphdrop.Tests;

/// <summary>Runs only where the cut-out model has been downloaded (or MORPHDROP_CUTOUT_MODEL points at it).</summary>
public sealed class CutoutFactAttribute : FactAttribute
{
    public CutoutFactAttribute()
    {
        if (!CutoutModel.IsAvailable)
            Skip = $"The cut-out model isn't downloaded; set {CutoutModel.OverrideVariable} to run this test.";
    }
}

public sealed class CutoutTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-cutout-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    [Fact]
    public void TheMask_BecomesTheTransparency_AtFullSize()
    {
        using var photo = new MagickImage(MagickColors.SteelBlue, 400, 200);
        // A 4 × 2 mask: the left half is removed, the right half kept.
        byte[] mask = { 0, 0, 255, 255, 0, 0, 255, 255 };

        using MagickImage cutout = CutoutImage.Apply(photo, mask, 4, 2);

        Assert.True(cutout.HasAlpha);
        Assert.Equal((uint)400, cutout.Width);
        using IPixelCollection<byte> pixels = cutout.GetPixels();
        Assert.Equal(0, pixels.GetPixel(20, 100)!.GetChannel(3));
        Assert.Equal(255, pixels.GetPixel(380, 100)!.GetChannel(3));
    }

    [Fact]
    public void WhatWasAlreadyTransparent_StaysTransparent()
    {
        using var logo = new MagickImage(MagickColors.Transparent, 10, 10);

        using MagickImage cutout = CutoutImage.Apply(logo, Enumerable.Repeat((byte)255, 100).ToArray(), 10, 10);

        using IPixelCollection<byte> pixels = cutout.GetPixels();
        Assert.Equal(0, pixels.GetPixel(5, 5)!.GetChannel(3));
    }

    [Fact]
    public void ACutout_IsSavedAsPngNextToThePhoto()
    {
        string photo = Path.Combine(_folder, "holiday.jpg");
        using (var image = new MagickImage(MagickColors.Orange, 8, 8))
            image.Write(photo);
        using var orange = new MagickImage(MagickColors.Orange, 8, 8);
        using MagickImage cutout = CutoutImage.Apply(orange, new byte[] { 0, 255, 0, 255 }, 2, 2);

        string saved = CutoutImage.Save(photo, Morphdrop.Imaging.ImageFormat.Jpeg, cutout);

        Assert.Equal(Path.Combine(_folder, "holiday (no background).png"), saved);
        using var written = new MagickImage(saved);
        Assert.Equal(MagickFormat.Png, written.Format);
        Assert.True(written.HasAlpha);
    }

    [Fact]
    public void TheMask_StretchesFromSurestBackgroundToSurestSubject()
    {
        byte[] mask = SubjectFinder.ToMask(new[] { 0.1f, 0.5f, 0.9f });

        Assert.Equal(new byte[] { 0, 128, 255 }, mask);
    }

    [CutoutFact]
    public void AnObjectOnAPlainBackground_IsTheSubject()
    {
        using var picture = new MagickImage(MagickColors.WhiteSmoke, 640, 480);
        new Drawables().FillColor(MagickColors.OrangeRed).Ellipse(320, 240, 120, 150, 0, 360).Draw(picture);

        byte[] mask = SubjectFinder.FindMask(picture, 640, 480);

        Assert.Equal(640 * 480, mask.Length);
        Assert.True(mask[240 * 640 + 320] > 200, "the middle of the object is kept");
        Assert.True(mask[10 * 640 + 10] < 50, "a corner of the background is removed");
    }

    /// <summary>Not a check: writes a cut-out of a real photo for a person to look at (MORPHDROP_CUTOUT_SAMPLE=photo).</summary>
    [CutoutFact]
    public void WriteASampleCutout()
    {
        if (Environment.GetEnvironmentVariable("MORPHDROP_CUTOUT_SAMPLE") is not { Length: > 0 } sample)
            return;

        using var photo = new MagickImage(sample);
        photo.AutoOrient();
        var started = System.Diagnostics.Stopwatch.StartNew();
        byte[] mask = SubjectFinder.FindMask(photo, (int)photo.Width, (int)photo.Height);
        File.WriteAllText(sample + ".time.txt", $"{started.Elapsed.TotalSeconds:0.0} s");

        using var alpha = new MagickImage();
        alpha.ReadPixels(mask, new PixelReadSettings(photo.Width, photo.Height, StorageType.Char, "R"));
        photo.Alpha(AlphaOption.Set);
        photo.Composite(alpha, CompositeOperator.CopyAlpha);
        photo.Write(sample + ".cutout.png");
    }
}
