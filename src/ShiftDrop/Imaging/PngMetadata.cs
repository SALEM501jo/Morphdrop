using System.Buffers.Binary;
using System.Text;

namespace ShiftDrop.Imaging;

/// <summary>
/// Removes metadata chunks from a PNG without re-encoding it: text (tEXt, zTXt, iTXt, which often hold software,
/// author or AI-prompt details), EXIF, timestamps and any unknown ancillary chunk. Chunks that change how the
/// image looks or animates stay, and the image data is copied byte for byte.
/// </summary>
internal static class PngMetadata
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly HashSet<string> KeptAncillaryChunks = new(StringComparer.Ordinal)
    {
        "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "hIST", "sPLT", // appearance
        "acTL", "fcTL", "fdAT", // animation (APNG)
        "cICP", "mDCv", "cLLi", // HDR
    };

    public static (byte[] Data, bool Removed) Remove(byte[] png)
    {
        if (!png.AsSpan().StartsWith(Signature))
            throw new InvalidDataException("This isn't a PNG file.");

        using var output = new MemoryStream(png.Length);
        output.Write(Signature);
        bool removed = false;
        int position = Signature.Length;

        while (position + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            if (length > png.Length - position - 12)
                break;

            string type = Encoding.ASCII.GetString(png, position + 4, 4);
            int chunkSize = 12 + (int)length; // length, type, data, CRC
            bool critical = char.IsUpper(type[0]); // IHDR, PLTE, IDAT, IEND and any future critical chunk
            if (critical || KeptAncillaryChunks.Contains(type))
                output.Write(png, position, chunkSize);
            else
                removed = true;

            position += chunkSize;
            if (type == "IEND")
                return (output.ToArray(), removed || position < png.Length);
        }

        throw new InvalidDataException("The PNG file is damaged.");
    }
}
