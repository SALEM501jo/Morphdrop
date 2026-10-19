using ImageMagick;

namespace ShiftDrop.Tests;

/// <summary>Test images made with ImageMagick, for formats WPF can't write (WebP, AVIF, animations, metadata).</summary>
internal static class MagickImages
{
    public static string Gradient(string path, uint width, uint height, MagickFormat format, Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage("gradient:#ff5f3a-#3a5fff", width, height);
        configure?.Invoke(image);
        image.Write(path, format);
        return path;
    }

    /// <summary>Blurred plasma: compresses like a real photo, unlike a smooth gradient.</summary>
    public static string Photo(string path, uint width, uint height, uint quality, Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage("plasma:#e8a87c-#41729f", width, height);
        image.Blur(0, 1.2);
        image.Quality = quality;
        configure?.Invoke(image);
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    /// <summary>ImageMagick's built-in 640x480 logo: a detailed, deterministic picture.</summary>
    public static string Logo(string path, MagickFormat format, Action<MagickImage>? configure = null)
    {
        using var image = new MagickImage("logo:");
        configure?.Invoke(image);
        image.Write(path, format);
        return path;
    }

    /// <summary>A 20x20 animation with one solid color per frame.</summary>
    public static string Animation(string path, MagickFormat format, params string[] colors)
    {
        using var frames = new MagickImageCollection();
        foreach (string color in colors)
        {
            var frame = new MagickImage(new MagickColor(color), 20, 20) { AnimationDelay = 10 };
            frames.Add(frame);
        }

        frames.Write(path, format);
        return path;
    }

    public static (uint Width, uint Height, MagickFormat Format) Info(string path)
    {
        var info = new MagickImageInfo(path);
        return (info.Width, info.Height, info.Format);
    }

    public static int FrameCount(string path)
    {
        using var frames = new MagickImageCollection(path);
        return frames.Count;
    }

    public static IMagickColor<byte> Pixel(string path, int x, int y)
    {
        using var image = new MagickImage(path);
        using IPixelCollection<byte> pixels = image.GetPixels();
        return pixels.GetPixel(x, y).ToColor()!;
    }

    public static bool SamePixels(string first, string second)
    {
        using var a = new MagickImage(first);
        using var b = new MagickImage(second);
        return a.Width == b.Width && a.Height == b.Height && a.Compare(b, ErrorMetric.Absolute) == 0;
    }
}
