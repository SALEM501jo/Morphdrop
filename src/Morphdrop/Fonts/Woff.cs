using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Morphdrop.Fonts;

/// <summary>WOFF 1.0: the font's tables, each compressed with zlib (W3C Recommendation, 2012).</summary>
internal static class Woff
{
    public const uint Signature = 0x774F4646; // "wOFF"
    private const int HeaderLength = 44;

    public static byte[] Encode(Sfnt font)
    {
        int count = font.Tables.Count;
        var entries = new List<(string Tag, byte[] Stored, int Length, uint Checksum)>();
        foreach ((string tag, byte[] table) in font.Tables)
        {
            byte[] compressed = Deflate(table);
            entries.Add((tag, compressed.Length < table.Length ? compressed : table, table.Length, Sfnt.Checksum(table)));
        }

        // The sfnt this was made from: what a reader will rebuild (needed in the header).
        byte[] sfnt = font.Write();
        int total = HeaderLength + 20 * count + entries.Sum(entry => Sfnt.Padded(entry.Stored.Length));
        var woff = new byte[total];
        Span<byte> header = woff;
        BinaryPrimitives.WriteUInt32BigEndian(header, Signature);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], Sfnt.U32(sfnt, 0));
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)total);
        BinaryPrimitives.WriteUInt16BigEndian(header[12..], (ushort)count);
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], (uint)sfnt.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header[20..], 1); // font version 1.0
        int offset = HeaderLength + 20 * count, record = HeaderLength;
        foreach ((string tag, byte[] stored, int length, uint checksum) in entries)
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(woff, record);
            BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(record + 4), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(record + 8), (uint)stored.Length);
            BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(record + 12), (uint)length);
            BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(record + 16), checksum);
            stored.CopyTo(woff, offset);
            offset += Sfnt.Padded(stored.Length);
            record += 20;
        }

        return woff;
    }

    public static Sfnt Decode(byte[] woff)
    {
        if (woff.Length < HeaderLength || Sfnt.U32(woff, 0) != Signature)
            throw new InvalidDataException("This isn't a WOFF font.");

        uint flavor = Sfnt.U32(woff, 4);
        int count = Sfnt.U16(woff, 12);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int record = HeaderLength + i * 20;
            string tag = Sfnt.Tag(woff, record);
            int offset = checked((int)Sfnt.U32(woff, record + 4));
            int stored = checked((int)Sfnt.U32(woff, record + 8));
            int length = checked((int)Sfnt.U32(woff, record + 12));
            if (offset + stored > woff.Length || stored > length || length > 256 * 1024 * 1024)
                throw new InvalidDataException("The WOFF font is damaged.");

            byte[] data = woff.AsSpan(offset, stored).ToArray();
            tables[tag] = stored < length ? Inflate(data, length) : data;
        }

        return new Sfnt(flavor, tables);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static byte[] Inflate(byte[] data, int length)
    {
        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        var table = new byte[length];
        zlib.ReadExactly(table);
        return table;
    }
}
