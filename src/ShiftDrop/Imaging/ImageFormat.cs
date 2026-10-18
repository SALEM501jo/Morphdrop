using ImageMagick;

namespace ShiftDrop.Imaging;

internal enum ImageKind { Png, Jpeg, WebP, Avif, Heic, Jxl, Psd, Pdf, Ico, Tiff, Gif, Bmp }

/// <summary>An image format ShiftDrop knows, and what it can do with it.</summary>
internal sealed class ImageFormat
{
    private readonly string[] _extensions;

    private ImageFormat(
        ImageKind kind, string name, MagickFormat magickFormat, bool canRead, bool canWrite,
        bool transparency, bool animation, params string[] extensions)
    {
        Kind = kind;
        Name = name;
        MagickFormat = magickFormat;
        CanRead = canRead;
        CanWrite = canWrite;
        SupportsTransparency = transparency;
        SupportsAnimation = animation;
        _extensions = extensions;
    }

    public static ImageFormat Png { get; } = new(ImageKind.Png, "PNG", MagickFormat.Png, true, true, true, false, ".png");
    public static ImageFormat Jpeg { get; } = new(ImageKind.Jpeg, "JPG", MagickFormat.Jpeg, true, true, false, false, ".jpg", ".jpeg", ".jpe", ".jfif");
    public static ImageFormat WebP { get; } = new(ImageKind.WebP, "WEBP", MagickFormat.WebP, true, true, true, true, ".webp");
    public static ImageFormat Avif { get; } = new(ImageKind.Avif, "AVIF", MagickFormat.Avif, true, true, true, false, ".avif");

    // Read only: the libheif bundled with Magick.NET has no HEVC encoder (x265 is GPL).
    public static ImageFormat Heic { get; } = new(ImageKind.Heic, "HEIC", MagickFormat.Heic, true, false, true, false, ".heic", ".heif");
    public static ImageFormat Jxl { get; } = new(ImageKind.Jxl, "JXL", MagickFormat.Jxl, true, true, true, false, ".jxl");

    // Read only: writing would flatten the layers.
    public static ImageFormat Psd { get; } = new(ImageKind.Psd, "PSD", MagickFormat.Psd, true, false, true, false, ".psd");

    // Write only: reading PDFs needs Ghostscript.
    public static ImageFormat Pdf { get; } = new(ImageKind.Pdf, "PDF", MagickFormat.Pdf, false, true, true, false, ".pdf");
    public static ImageFormat Ico { get; } = new(ImageKind.Ico, "ICO", MagickFormat.Ico, true, true, true, false, ".ico");
    public static ImageFormat Tiff { get; } = new(ImageKind.Tiff, "TIFF", MagickFormat.Tiff, true, true, true, false, ".tiff", ".tif");
    public static ImageFormat Gif { get; } = new(ImageKind.Gif, "GIF", MagickFormat.Gif, true, true, true, true, ".gif");

    // Most apps ignore transparency in BMP files, so it is treated as opaque.
    public static ImageFormat Bmp { get; } = new(ImageKind.Bmp, "BMP", MagickFormat.Bmp, true, true, false, false, ".bmp", ".dib");

    public static IReadOnlyList<ImageFormat> All { get; } = new[] { Png, Jpeg, WebP, Avif, Heic, Jxl, Psd, Pdf, Ico, Tiff, Gif, Bmp };

    /// <summary>
    /// The wheel's conversion targets, clockwise from the top. JPEG XL can be written too, but stays off the
    /// wheel until more apps can open it.
    /// </summary>
    public static IReadOnlyList<ImageFormat> WheelTargets { get; } = new[] { Png, Jpeg, WebP, Avif, Pdf, Ico, Tiff, Gif, Bmp };

    public ImageKind Kind { get; }

    /// <summary>Short name, as shown on the wheel.</summary>
    public string Name { get; }

    public MagickFormat MagickFormat { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public bool SupportsTransparency { get; }

    public bool SupportsAnimation { get; }

    /// <summary>Extension of the files ShiftDrop writes, with the dot.</summary>
    public string Extension => _extensions[0];

    /// <summary>The format a file claims to be by its extension. The content may disagree; see <see cref="FormatDetector"/>.</summary>
    public static ImageFormat? FromPath(string path)
    {
        string extension = Path.GetExtension(path);
        return All.FirstOrDefault(format => format._extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public override string ToString() => Name;
}
