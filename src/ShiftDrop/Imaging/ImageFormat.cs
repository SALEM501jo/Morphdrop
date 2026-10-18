namespace ShiftDrop.Imaging;

internal enum ImageKind { Png, Jpeg, Ico, Bmp, Tiff, Gif }

internal sealed class ImageFormat
{
    // Decoded by Windows itself (WIC). WebP, HEIC and AVIF need the free codec extensions from the Microsoft Store;
    // when one is missing, ImageLoader says so.
    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico",
        ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp",
    };

    private readonly string[] _aliases;

    private ImageFormat(ImageKind kind, string name, string extension, bool supportsTransparency, params string[] aliases)
    {
        Kind = kind;
        Name = name;
        Extension = extension;
        SupportsTransparency = supportsTransparency;
        _aliases = aliases;
    }

    public static ImageFormat Png { get; } = new(ImageKind.Png, "PNG", ".png", true);
    public static ImageFormat Jpeg { get; } = new(ImageKind.Jpeg, "JPG", ".jpg", false, ".jpeg", ".jpe", ".jfif");
    public static ImageFormat Ico { get; } = new(ImageKind.Ico, "ICO", ".ico", true);
    public static ImageFormat Bmp { get; } = new(ImageKind.Bmp, "BMP", ".bmp", false, ".dib");
    public static ImageFormat Tiff { get; } = new(ImageKind.Tiff, "TIFF", ".tiff", true, ".tif");
    public static ImageFormat Gif { get; } = new(ImageKind.Gif, "GIF", ".gif", false);

    /// <summary>Formats ShiftDrop can write, in wheel order (clockwise from the top).</summary>
    public static IReadOnlyList<ImageFormat> Writable { get; } = new[] { Png, Jpeg, Ico, Bmp, Tiff, Gif };

    public ImageKind Kind { get; }

    public string Name { get; }

    /// <summary>Extension of the files ShiftDrop writes, with the dot.</summary>
    public string Extension { get; }

    public bool SupportsTransparency { get; }

    public static bool CanRead(string path) => ReadableExtensions.Contains(Path.GetExtension(path));

    public static ImageFormat? FromPath(string path)
    {
        string extension = Path.GetExtension(path);
        return Writable.FirstOrDefault(format =>
            string.Equals(format.Extension, extension, StringComparison.OrdinalIgnoreCase)
            || format._aliases.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public override string ToString() => Name;
}
