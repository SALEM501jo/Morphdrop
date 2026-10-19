using System.Buffers.Binary;
using System.Text;

namespace ShiftDrop.Imaging;

/// <summary>Reading and writing PNG chunks: length, four-letter type, data, CRC-32 of type and data.</summary>
internal static class PngChunks
{
    public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = CreateCrcTable();

    public readonly record struct Chunk(string Type, int Offset, int Length)
    {
        /// <summary>Where the whole chunk starts, including its length and type fields.</summary>
        public int Start => Offset - 8;

        public int TotalSize => Length + 12;
    }

    /// <summary>The chunks of a PNG up to and including IEND. Throws if the file isn't a well-formed PNG.</summary>
    public static List<Chunk> Parse(byte[] png)
    {
        if (!png.AsSpan().StartsWith(Signature))
            throw new InvalidDataException("This isn't a PNG file.");

        var chunks = new List<Chunk>();
        int position = Signature.Length;
        while (position + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            if (length > png.Length - position - 12)
                break;

            var chunk = new Chunk(Encoding.ASCII.GetString(png, position + 4, 4), position + 8, (int)length);
            chunks.Add(chunk);
            position += chunk.TotalSize;
            if (chunk.Type == "IEND")
                return chunks;
        }

        throw new InvalidDataException("The PNG file is damaged.");
    }

    /// <summary>The chunk types in a PNG file, read from the chunk headers only (the data is skipped).</summary>
    public static HashSet<string> TypesIn(string path)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[8];
        if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8 || !header.SequenceEqual(Signature))
            return types;

        while (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8)
        {
            string type = Encoding.ASCII.GetString(header[4..]);
            types.Add(type);
            if (type == "IEND")
                break;
            stream.Seek(BinaryPrimitives.ReadUInt32BigEndian(header) + 4L, SeekOrigin.Current); // data and CRC
        }

        return types;
    }

    public static void Write(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(field, (uint)data.Length);
        output.Write(field);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(field, crc);
        output.Write(field);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
