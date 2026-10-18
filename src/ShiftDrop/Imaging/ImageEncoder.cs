using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShiftDrop.Imaging;

internal static class ImageEncoder
{
    public const int JpegQuality = 92;

    public static void Save(BitmapSource image, ImageFormat format, Stream output)
    {
        if (format.Kind == ImageKind.Ico)
        {
            IcoWriter.Write(image, output);
            return;
        }

        BitmapSource pixels = PreparePixels(image, format.SupportsTransparency);
        BitmapEncoder encoder;
        switch (format.Kind)
        {
            case ImageKind.Png:
                encoder = new PngBitmapEncoder();
                break;
            case ImageKind.Jpeg:
                encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
                break;
            case ImageKind.Bmp:
                encoder = new BmpBitmapEncoder();
                break;
            case ImageKind.Tiff:
                encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                break;
            case ImageKind.Gif:
                encoder = new GifBitmapEncoder();
                pixels = new FormatConvertedBitmap(pixels, PixelFormats.Indexed8, new BitmapPalette(pixels, 256), 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output format.");
        }

        encoder.Frames.Add(BitmapFrame.Create(pixels));
        encoder.Save(output);
    }

    /// <summary>
    /// Pixels the target can store: Bgr24 when the image is opaque, Bgra32 when it has transparency and the
    /// format keeps it, otherwise Bgr24 with the transparent areas composited onto white.
    /// </summary>
    internal static BitmapSource PreparePixels(BitmapSource image, bool keepTransparency)
    {
        if (!MayHaveTransparency(image))
            return Converted(image, PixelFormats.Bgr24);

        BitmapSource bgra = Converted(image, PixelFormats.Bgra32);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        var pixels = new byte[checked(width * 4 * height)];
        bgra.CopyPixels(pixels, width * 4, 0);

        if (IsOpaque(pixels))
            return Converted(bgra, PixelFormats.Bgr24);
        return keepTransparency ? bgra : FlattenOnWhite(pixels, width, height, image.DpiX, image.DpiY);
    }

    private static bool MayHaveTransparency(BitmapSource image)
    {
        PixelFormat format = image.Format;
        if (format == PixelFormats.Indexed1 || format == PixelFormats.Indexed2
            || format == PixelFormats.Indexed4 || format == PixelFormats.Indexed8)
        {
            return image.Palette?.Colors.Any(color => color.A < 255) ?? false;
        }

        return format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32
            || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64
            || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float;
    }

    private static bool IsOpaque(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 255)
                return false;
        }

        return true;
    }

    private static BitmapSource FlattenOnWhite(byte[] bgra, int width, int height, double dpiX, double dpiY)
    {
        var bgr = new byte[checked(width * 3 * height)];
        for (int source = 0, target = 0; source < bgra.Length; source += 4, target += 3)
        {
            int alpha = bgra[source + 3];
            int white = 255 * (255 - alpha);
            bgr[target] = (byte)((bgra[source] * alpha + white + 127) / 255);
            bgr[target + 1] = (byte)((bgra[source + 1] * alpha + white + 127) / 255);
            bgr[target + 2] = (byte)((bgra[source + 2] * alpha + white + 127) / 255);
        }

        BitmapSource flat = BitmapSource.Create(
            width, height, dpiX > 0 ? dpiX : 96, dpiY > 0 ? dpiY : 96, PixelFormats.Bgr24, null, bgr, width * 3);
        flat.Freeze();
        return flat;
    }

    private static BitmapSource Converted(BitmapSource image, PixelFormat format) =>
        image.Format == format ? image : new FormatConvertedBitmap(image, format, null, 0);
}
