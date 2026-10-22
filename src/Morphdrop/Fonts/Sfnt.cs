using System.Buffers.Binary;
using System.Text;

namespace Morphdrop.Fonts;

/// <summary>An OpenType/TrueType font as its tables (TTF, OTF): read from and written to the sfnt file layout.</summary>
internal sealed class Sfnt
{
    public const uint TrueType = 0x00010000;
    public const uint OpenTypeCff = 0x4F54544F; // "OTTO"
    private const uint AppleTrue = 0x74727565; // "true"

    public Sfnt(uint flavor, SortedDictionary<string, byte[]> tables)
    {
        Flavor = flavor;
        Tables = tables;
    }

    /// <summary>TrueType outlines (0x00010000) or CFF outlines ("OTTO").</summary>
    public uint Flavor { get; }

    public SortedDictionary<string, byte[]> Tables { get; }

    public bool IsCff => Flavor == OpenTypeCff;

    /// <summary>The extension a font of this flavor is saved with.</summary>
    public string Extension => IsCff ? ".otf" : ".ttf";

    public static bool IsSfntFlavor(uint flavor) => flavor is TrueType or OpenTypeCff or AppleTrue;

    public static Sfnt Read(byte[] font)
    {
        uint flavor = U32(font, 0);
        if (flavor == 0x74746366) // "ttcf"
            throw new NotSupportedException("Font collections (.ttc) can't be converted yet.");
        if (!IsSfntFlavor(flavor))
            throw new InvalidDataException("This isn't a TrueType or OpenType font.");

        int count = U16(font, 4);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            string tag = Tag(font, record);
            int offset = checked((int)U32(font, record + 8));
            int length = checked((int)U32(font, record + 12));
            if (offset < 0 || length < 0 || offset + length > font.Length)
                throw new InvalidDataException($"The font's {tag} table runs past the end of the file.");
            tables[tag] = font.AsSpan(offset, length).ToArray();
        }

        return new Sfnt(flavor, tables);
    }

    /// <summary>The sfnt file: tables in tag order, 4-byte aligned, with checksums and head's checkSumAdjustment.</summary>
    public byte[] Write()
    {
        int count = Tables.Count;
        int headerLength = 12 + 16 * count;
        int total = headerLength + Tables.Values.Sum(table => Padded(table.Length));
        var font = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(font, Flavor == AppleTrue ? TrueType : Flavor);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), (ushort)count);
        int power = 1, log = 0;
        while (power * 2 <= count)
        {
            power *= 2;
            log++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(6), (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(8), (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(10), (ushort)(count * 16 - power * 16));

        int offset = headerLength, record = 12, headOffset = -1;
        foreach ((string tag, byte[] data) in Tables)
        {
            byte[] table = data;
            if (tag == "head" && table.Length >= 12)
            {
                table = (byte[])table.Clone();
                BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0); // checkSumAdjustment is counted as 0
                headOffset = offset;
            }

            Encoding.ASCII.GetBytes(tag.PadRight(4)).CopyTo(font, record);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 4), Checksum(table));
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 12), (uint)table.Length);
            table.CopyTo(font, offset);
            offset += Padded(table.Length);
            record += 16;
        }

        if (headOffset >= 0)
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), unchecked(0xB1B0AFBA - Checksum(font)));
        return font;
    }

    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int whole = data.Length / 4 * 4;
        for (int i = 0; i < whole; i += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        if (whole < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            data[whole..].CopyTo(last);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(last));
        }

        return sum;
    }

    public static int Padded(int length) => (length + 3) & ~3;

    public static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));

    public static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));

    public static string Tag(byte[] data, int at) => Encoding.ASCII.GetString(data, at, 4);
}
