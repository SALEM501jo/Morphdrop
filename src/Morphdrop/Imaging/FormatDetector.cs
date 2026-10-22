using System.Buffers.Binary;

namespace Morphdrop.Imaging;

/// <summary>
/// Identifies an image by its first bytes. A mislabeled file (say a PNG saved as .jpg) still converts, and only
/// decoders on Morphdrop's own list ever see a file, never ImageMagick's script-like formats (MVG, MSL, SVG…).
/// </summary>
internal static class FormatDetector
{
    private const int HeaderLength = 64;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] IcoSignature = { 0x00, 0x00, 0x01, 0x00 };
    private static readonly byte[] JxlCodestream = { 0xFF, 0x0A };
    private static readonly byte[] JxlContainer = { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A };

    private const int SvgSniffLength = 4096;

    public static ImageFormat? Detect(string path)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        ImageFormat? named = ImageFormat.FromPath(path);

        // Camera RAW files are mostly TIFFs inside; trust a RAW extension when the header fits one of the layouts.
        if (named?.Kind == ImageKind.Raw && LooksLikeRaw(header[..read]))
            return ImageFormat.Raw;

        // SVG is text, so it has no signature: an .svg must really start like one (an .svgz is gzip).
        if (named?.Kind == ImageKind.Svg)
        {
            stream.Position = 0;
            return IsSvg(stream) ? ImageFormat.Svg : null;
        }

        return Detect(header[..read]);
    }

    internal static bool LooksLikeRaw(ReadOnlySpan<byte> header) =>
        header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8)           // DNG, CR2, NEF, ARW, PEF, SRW…
        || header.StartsWith("IIRO"u8) || header.StartsWith("IIRS"u8) || header.StartsWith("MMOR"u8) // Olympus ORF
        || header.StartsWith("IIU\0"u8)                                        // Panasonic RW2
        || header.StartsWith("FUJIFILM"u8)                                    // Fuji RAF
        || header.StartsWith("FOVb"u8)                                        // Sigma X3F
        || header.StartsWith("\0MRM"u8)                                        // Minolta MRW
        || (header.Length >= 12 && header[4..12].SequenceEqual("ftypcrx "u8)) // Canon CR3
        || (header.Length >= 14 && header[6..14].SequenceEqual("HEAPCCDR"u8)); // Canon CRW

    /// <summary>
    /// An SVG document: after an optional XML declaration, comments and doctype, an &lt;svg&gt; element. Files that
    /// declare XML entities are refused; that is how SVG files attack XML parsers. An .svgz just has to be gzip.
    /// </summary>
    internal static bool IsSvg(Stream stream)
    {
        var start = new byte[SvgSniffLength];
        int read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        if (read >= 2 && start[0] == 0x1F && start[1] == 0x8B)
            return true;

        string text = System.Text.Encoding.UTF8.GetString(start, 0, read);
        if (text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            return false;
        int svg = text.IndexOf("<svg", StringComparison.Ordinal);
        if (svg < 0)
            return false;
        string before = text[..svg].TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return before.Length == 0 || before.StartsWith("<?xml", StringComparison.Ordinal) || before.StartsWith("<!--", StringComparison.Ordinal)
            || before.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
    }

    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PngSignature))
            return ImageFormat.Png;
        if (header.StartsWith(JpegSignature))
            return ImageFormat.Jpeg;
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
            return ImageFormat.Gif;
        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.WebP;
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
            return DetectHeifFamily(header);
        if (header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8))
            return ImageFormat.Tiff;
        if (header.StartsWith("8BPS"u8))
            return ImageFormat.Psd;
        if (header.StartsWith(JxlContainer) || header.StartsWith(JxlCodestream))
            return ImageFormat.Jxl;
        if (header.StartsWith(IcoSignature))
            return ImageFormat.Ico;
        if (header.StartsWith("BM"u8))
            return ImageFormat.Bmp;
        return null;
    }

    /// <summary>HEIC and AVIF share the ISO media file layout; the brands in the "ftyp" box tell them apart.</summary>
    private static ImageFormat? DetectHeifFamily(ReadOnlySpan<byte> header)
    {
        int boxEnd = Math.Min(header.Length, (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(header)));
        bool heif = false;
        for (int offset = 8; offset + 4 <= boxEnd; offset += 4)
        {
            if (offset == 12)
                continue; // minor version, not a brand

            ReadOnlySpan<byte> brand = header.Slice(offset, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
                return ImageFormat.Avif;
            heif |= brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
                || brand.SequenceEqual("hevx"u8) || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8)
                || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8);
        }

        return heif ? ImageFormat.Heic : null; // e.g. MP4 and MOV videos also start with "ftyp"
    }
}
