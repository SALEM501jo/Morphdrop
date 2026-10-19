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

    public static ImageFormat? Detect(string path)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        return Detect(header[..read]);
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
