using System.Text;

namespace ShiftDrop.Imaging;

/// <summary>Chunk-level PNG cleanup. The image data is always copied byte for byte.</summary>
internal static class PngMetadata
{
    // Ancillary chunks that change how the image looks or animates; any other ancillary chunk is metadata.
    private static readonly HashSet<string> KeptAncillaryChunks = new(StringComparer.Ordinal)
    {
        "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "hIST", "sPLT", // appearance
        "acTL", "fcTL", "fdAT", // animation (APNG)
        "cICP", "mDCv", "cLLi", // HDR
    };

    // Chunks ImageMagick adds to every PNG it writes: a background hint, chromaticities, a modification time and
    // its private orientation chunk. They're kept only when the original image already had them.
    private static readonly HashSet<string> WriterChunks = new(StringComparer.Ordinal) { "bKGD", "cHRM", "tIME", "orNT" };

    /// <summary>
    /// Removes text (tEXt, zTXt, iTXt: often software, author or AI-prompt details), EXIF, timestamps and unknown
    /// ancillary chunks. Chunks that change how the image looks or animates stay.
    /// </summary>
    public static (byte[] Data, bool Removed) Remove(byte[] png) =>
        Filter(png, chunk => char.IsUpper(chunk[0]) || KeptAncillaryChunks.Contains(chunk));

    /// <summary>
    /// Removes what ImageMagick adds on its own when writing a PNG (see <see cref="WriterChunks"/>, plus its
    /// "date:create"-style text), so a converted or compressed file carries no more metadata than its source.
    /// </summary>
    /// <param name="sourceChunks">The chunk types of the original PNG, or null when the source wasn't a PNG.</param>
    public static byte[] RemoveWriterChunks(byte[] png, IReadOnlySet<string>? sourceChunks)
    {
        string? textKeyword = null;
        return Filter(png, (type, data) =>
        {
            if (WriterChunks.Contains(type))
                return sourceChunks?.Contains(type) == true;
            if (type is "tEXt" or "zTXt" or "iTXt")
            {
                textKeyword = Encoding.Latin1.GetString(data[..Math.Max(0, data.IndexOf((byte)0))]);
                return !textKeyword.StartsWith("date:", StringComparison.Ordinal);
            }

            return true;
        }).Data;
    }

    private static (byte[] Data, bool Removed) Filter(byte[] png, Func<string, bool> keep) =>
        Filter(png, (type, _) => keep(type));

    private static (byte[] Data, bool Removed) Filter(byte[] png, KeepChunk keep)
    {
        List<PngChunks.Chunk> chunks = PngChunks.Parse(png);
        using var output = new MemoryStream(png.Length);
        output.Write(PngChunks.Signature);
        bool removed = false;
        foreach (PngChunks.Chunk chunk in chunks)
        {
            if (keep(chunk.Type, png.AsSpan(chunk.Offset, chunk.Length)))
                output.Write(png, chunk.Start, chunk.TotalSize);
            else
                removed = true;
        }

        PngChunks.Chunk last = chunks[^1];
        removed |= last.Start + last.TotalSize < png.Length; // data after IEND
        return (output.ToArray(), removed);
    }

    private delegate bool KeepChunk(string type, ReadOnlySpan<byte> data);
}
