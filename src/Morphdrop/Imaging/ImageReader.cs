using ImageMagick;

namespace Morphdrop.Imaging;

internal static class ImageReader
{
    /// <summary>The file's real format, judged by its content.</summary>
    public static ImageFormat Identify(string path) =>
        FormatDetector.Detect(path) is { CanRead: true } format
            ? format
            : throw new NotSupportedException($"{Path.GetFileName(path)} isn't an image Morphdrop can read.");

    /// <summary>
    /// Reads all frames (animations, icons, multi-page TIFFs) or just the first, each turned upright according to
    /// its EXIF orientation. The decoder is chosen from <paramref name="format"/>, never guessed by ImageMagick.
    /// </summary>
    public static MagickImageCollection Read(string path, ImageFormat format, bool allFrames)
    {
        if (allFrames && format.Kind == ImageKind.Png && ApngReader.IsAnimated(path))
            return ApngReader.Read(File.ReadAllBytes(path));

        var settings = new MagickReadSettings { Format = format.MagickFormat };
        if (format.Kind == ImageKind.Svg)
        {
            // SVGs have no fixed size: draw them big enough to be sharp (longest side 2048 px), transparent around.
            settings.BackgroundColor = MagickColors.None;
            settings.Density = SvgDensity(path);
        }
        if (!allFrames)
        {
            settings.FrameIndex = 0;
            settings.FrameCount = 1;
        }

        var images = new MagickImageCollection();
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                images.Read(stream, settings);

            foreach (IMagickImage<byte> image in images)
                image.AutoOrient();
            return images;
        }
        catch
        {
            images.Dispose();
            throw;
        }
    }

    private static Density SvgDensity(string path)
    {
        using var probe = new MagickImage();
        probe.Ping(path, new MagickReadSettings { Format = MagickFormat.Rsvg });
        double scale = Math.Clamp(2048.0 / Math.Max(1, Math.Max(probe.Width, probe.Height)), 1, 32);
        return new Density(96 * scale);
    }

    public static IMagickImage<byte> Largest(MagickImageCollection images) =>
        images.MaxBy(image => (long)image.Width * image.Height)!;
}
