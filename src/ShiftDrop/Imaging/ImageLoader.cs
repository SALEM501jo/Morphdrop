using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShiftDrop.Imaging;

internal static class ImageLoader
{
    /// <summary>
    /// Decodes an image with Windows' built-in codecs and turns it upright according to its EXIF orientation
    /// (phone photos are usually stored sideways). Icons give their largest image, other formats their first frame.
    /// </summary>
    public static BitmapSource Load(string path)
    {
        try
        {
            return Decode(path, BitmapCreateOptions.PreservePixelFormat);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(MissingCodecMessage(path), ex);
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            // Some files carry a broken color profile that WIC refuses; the pixels are usually fine without it.
            return Decode(path, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile);
        }
    }

    internal static BitmapSource ApplyOrientation(BitmapSource image, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        if (transform is null)
            return image;

        var upright = new TransformedBitmap(image, transform);
        upright.Freeze();
        return upright;
    }

    private static BitmapSource Decode(string path, BitmapCreateOptions options)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, options, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder is IconBitmapDecoder
            ? decoder.Frames.MaxBy(f => f.PixelWidth * f.PixelHeight)!
            : decoder.Frames[0];
        return ApplyOrientation(frame, ReadOrientation(frame));
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value
                ? value
                : 1;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return 1; // the format has no metadata of this kind
        }
    }

    private static string MissingCodecMessage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string? extensionName = extension switch
        {
            ".heic" or ".heif" => "HEIF Image Extensions",
            ".webp" => "WebP Image Extensions",
            ".avif" => "AV1 Video Extension",
            _ => null,
        };
        return extensionName is null
            ? $"Windows has no decoder for {extension} files."
            : $"Windows can't read {extension} files yet. Install \"{extensionName}\" from the Microsoft Store and try again.";
    }
}
