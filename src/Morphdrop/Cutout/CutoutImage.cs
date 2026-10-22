using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Imaging;

namespace Morphdrop.Cutout;

/// <summary>Turns a mask (0 = background, 255 = keep) into transparency and saves the cut-out as a PNG.</summary>
internal static class CutoutImage
{
    /// <summary>A copy of <paramref name="frame"/> whose alpha is the mask, stretched to the frame's size.</summary>
    public static MagickImage Apply(IMagickImage<byte> frame, byte[] mask, int maskWidth, int maskHeight)
    {
        var image = (MagickImage)frame.Clone();
        using var alpha = new MagickImage();
        alpha.ReadPixels(mask, new PixelReadSettings((uint)maskWidth, (uint)maskHeight, StorageType.Char, "R"));
        alpha.ColorType = ColorType.Grayscale;
        if (alpha.Width != image.Width || alpha.Height != image.Height)
        {
            alpha.FilterType = FilterType.Triangle; // smooth, without the ringing (a light halo) Lanczos leaves
            alpha.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        }

        if (image.HasAlpha)
        {
            // Keep what was already transparent in the original.
            using IMagickImage<byte> existing = image.Separate(Channels.Alpha).First();
            alpha.Composite(existing, CompositeOperator.Multiply);
        }

        image.Alpha(AlphaOption.Set);
        image.Composite(alpha, CompositeOperator.CopyAlpha);
        return image;
    }

    /// <summary>Saves "name (no background).png" next to the original (or in the chosen folder).</summary>
    public static string Save(string originalPath, ImageFormat source, MagickImage cutout)
    {
        ImageWriter.Prepare(cutout, source, ImageFormat.Png);
        byte[] png = PngMetadata.RemoveWriterChunks(cutout.ToByteArray(MagickFormat.Png), sourceChunks: null);
        return OutputFile.Save(originalPath, "no background", ".png", output => output.Write(png));
    }
}
