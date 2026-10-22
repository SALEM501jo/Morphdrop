using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Morphdrop.Fonts;

/// <summary>
/// WOFF 2.0 (W3C Recommendation): the tables compressed together with Brotli, the glyph outlines ("glyf" and
/// "loca") stored in WOFF2's compact form. Reading also handles the compact advance widths ("hmtx") some encoders write.
/// </summary>
internal static class Woff2
{
    public const uint Signature = 0x774F4632; // "wOF2"
    private const int HeaderLength = 48;

    private static readonly string[] KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG",
        "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC",
        "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc",
        "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat",
        "Gloc", "Feat", "Sill",
    };

    public static byte[] Encode(Sfnt font)
    {
        byte[]? glyf = null;
        if (font.Tables.ContainsKey("glyf") && font.Tables.ContainsKey("loca") && font.Tables.ContainsKey("head") && font.Tables.ContainsKey("maxp"))
            glyf = GlyfTransform.Encode(font);

        var directory = new MemoryStream();
        var data = new MemoryStream();
        foreach ((string tag, byte[] table) in font.Tables) // ordinal order puts glyf before loca, as WOFF2 wants
        {
            int known = Array.IndexOf(KnownTags, tag);
            bool outline = tag is "glyf" or "loca";
            int version = outline ? (glyf is null ? 3 : 0) : 0; // 3 is "untransformed" for glyf and loca
            directory.WriteByte((byte)((version << 6) | (known >= 0 ? known : 63)));
            if (known < 0)
                directory.Write(Encoding.ASCII.GetBytes(tag));
            WriteBase128(directory, (uint)table.Length);
            if (outline && glyf is not null)
            {
                byte[] stored = tag == "glyf" ? glyf : Array.Empty<byte>(); // loca is rebuilt from glyf
                WriteBase128(directory, (uint)stored.Length);
                data.Write(stored);
            }
            else
            {
                data.Write(table);
            }
        }

        byte[] compressed = Brotli(data.ToArray());
        byte[] sfnt = font.Write();
        int total = Sfnt.Padded(HeaderLength + (int)directory.Length + compressed.Length);
        var woff2 = new byte[total];
        Span<byte> header = woff2;
        BinaryPrimitives.WriteUInt32BigEndian(header, Signature);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], Sfnt.U32(sfnt, 0));
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)total);
        BinaryPrimitives.WriteUInt16BigEndian(header[12..], (ushort)font.Tables.Count);
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], (uint)sfnt.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], (uint)compressed.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header[24..], 1); // version 1.0
        directory.ToArray().CopyTo(woff2, HeaderLength);
        compressed.CopyTo(woff2, HeaderLength + (int)directory.Length);
        return woff2;
    }

    public static Sfnt Decode(byte[] woff2)
    {
        if (woff2.Length < HeaderLength || Sfnt.U32(woff2, 0) != Signature)
            throw new InvalidDataException("This isn't a WOFF2 font.");

        uint flavor = Sfnt.U32(woff2, 4);
        if (flavor == 0x74746366)
            throw new NotSupportedException("WOFF2 font collections can't be converted yet.");

        int count = Sfnt.U16(woff2, 12);
        int compressedLength = checked((int)Sfnt.U32(woff2, 20));
        int at = HeaderLength;
        var entries = new List<(string Tag, int Version, uint Length, uint Stored, bool Transformed)>();
        for (int i = 0; i < count; i++)
        {
            byte flags = woff2[at++];
            int known = flags & 0x3F;
            string tag = known == 63 ? Sfnt.Tag(woff2, at) : KnownTags[known];
            if (known == 63)
                at += 4;
            int version = flags >> 6;
            uint length = ReadBase128(woff2, ref at);
            bool transformed = tag is "glyf" or "loca" ? version != 3 : version != 0;
            uint stored = transformed ? ReadBase128(woff2, ref at) : length;
            entries.Add((tag, version, length, stored, transformed));
        }

        byte[] data = Unbrotli(woff2.AsSpan(at, compressedLength).ToArray(), checked((int)entries.Sum(entry => (long)entry.Stored)));
        var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int offset = 0;
        foreach ((string tag, _, _, uint stored, _) in entries)
        {
            raw[tag] = data.AsSpan(offset, (int)stored).ToArray();
            offset += (int)stored;
        }

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string tag, int _, uint _, uint _, bool _) in entries)
            tables[tag] = raw[tag];

        short[]? xMins = null;
        if (entries.Any(entry => entry.Tag == "glyf" && entry.Transformed))
        {
            (byte[] glyf, byte[] loca, int indexFormat, short[] mins) = GlyfTransform.Decode(raw["glyf"]);
            tables["glyf"] = glyf;
            tables["loca"] = loca;
            xMins = mins;
            if (tables.TryGetValue("head", out byte[]? head) && head.Length >= 54)
            {
                head = (byte[])head.Clone();
                BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)indexFormat);
                tables["head"] = head;
            }
        }

        if (entries.Any(entry => entry.Tag == "hmtx" && entry.Transformed))
            tables["hmtx"] = DecodeHmtx(raw["hmtx"], tables, xMins ?? throw new InvalidDataException("The WOFF2 font's widths depend on outlines it doesn't have."));

        foreach ((string tag, _, _, _, bool transformed) in entries)
        {
            if (transformed && tag is not ("glyf" or "loca" or "hmtx"))
                throw new NotSupportedException($"The WOFF2 font stores its {tag} table in a form Morphdrop can't read.");
        }

        return new Sfnt(flavor, tables);
    }

    private static byte[] DecodeHmtx(byte[] data, SortedDictionary<string, byte[]> tables, short[] xMins)
    {
        int metrics = Sfnt.U16(tables["hhea"], 34);
        int glyphs = Sfnt.U16(tables["maxp"], 4);
        byte flags = data[0];
        int at = 1;
        var advances = new ushort[metrics];
        for (int i = 0; i < metrics; i++, at += 2)
            advances[i] = Sfnt.U16(data, at);
        var bearings = new short[glyphs];
        for (int i = 0; i < metrics; i++)
        {
            if ((flags & 1) == 0)
            {
                bearings[i] = (short)Sfnt.U16(data, at);
                at += 2;
            }
            else
            {
                bearings[i] = xMins[i];
            }
        }

        for (int i = metrics; i < glyphs; i++)
        {
            if ((flags & 2) == 0)
            {
                bearings[i] = (short)Sfnt.U16(data, at);
                at += 2;
            }
            else
            {
                bearings[i] = xMins[i];
            }
        }

        var hmtx = new byte[metrics * 4 + (glyphs - metrics) * 2];
        for (int i = 0; i < metrics; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), advances[i]);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(i * 4 + 2), bearings[i]);
        }

        for (int i = metrics; i < glyphs; i++)
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(metrics * 4 + (i - metrics) * 2), bearings[i]);
        return hmtx;
    }

    private static byte[] Brotli(byte[] data)
    {
        var output = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        if (!BrotliEncoder.TryCompress(data, output, out int written, quality: 11, window: 22))
            throw new InvalidOperationException("Brotli compression failed.");
        return output[..written];
    }

    private static byte[] Unbrotli(byte[] data, int length)
    {
        var output = new byte[length];
        using var brotli = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress);
        brotli.ReadExactly(output);
        return output;
    }

    internal static void WriteBase128(Stream stream, uint value)
    {
        int size = 1;
        for (uint rest = value >> 7; rest != 0; rest >>= 7)
            size++;
        for (int i = size - 1; i >= 0; i--)
            stream.WriteByte((byte)(((value >> (7 * i)) & 0x7Fu) | (i > 0 ? 0x80u : 0u)));
    }

    internal static uint ReadBase128(byte[] data, ref int at)
    {
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = data[at++];
            if (i == 0 && b == 0x80)
                throw new InvalidDataException("The WOFF2 font is damaged (a number with leading zeros).");
            if ((value & 0xFE000000) != 0)
                throw new InvalidDataException("The WOFF2 font is damaged (a number too big).");
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("The WOFF2 font is damaged (a number too long).");
    }
}

/// <summary>WOFF2's compact form of TrueType outlines (W3C WOFF2, section 5): separate streams and "triplet" coordinates.</summary>
internal static class GlyfTransform
{
    private const ushort ArgsAreWords = 0x0001, HaveScale = 0x0008, MoreComponents = 0x0020, HaveXyScale = 0x0040, HaveTwoByTwo = 0x0080, HaveInstructions = 0x0100;
    private const byte OnCurve = 0x01, XShort = 0x02, YShort = 0x04, Repeat = 0x08, XSame = 0x10, YSame = 0x20, OverlapSimple = 0x40;

    public static byte[] Encode(Sfnt font)
    {
        byte[] glyf = font.Tables["glyf"], loca = font.Tables["loca"], head = font.Tables["head"];
        int glyphs = Sfnt.U16(font.Tables["maxp"], 4);
        int indexFormat = (short)Sfnt.U16(head, 50);
        var offsets = new int[glyphs + 1];
        for (int i = 0; i <= glyphs; i++)
            offsets[i] = indexFormat == 0 ? Sfnt.U16(loca, i * 2) * 2 : (int)Sfnt.U32(loca, i * 4);

        var contours = new MemoryStream();
        var points = new MemoryStream();
        var flags = new MemoryStream();
        var glyph = new MemoryStream();
        var composite = new MemoryStream();
        var boxes = new MemoryStream();
        var instructions = new MemoryStream();
        var boxBitmap = new byte[4 * ((glyphs + 31) >> 5)];
        var overlapBitmap = new byte[(glyphs + 7) >> 3];
        bool anyOverlap = false;

        for (int g = 0; g < glyphs; g++)
        {
            int start = offsets[g], end = offsets[g + 1];
            short count = end - start >= 10 ? (short)Sfnt.U16(glyf, start) : (short)0;
            if (count == 0)
            {
                WriteI16(contours, 0);
                continue;
            }

            if (count < 0)
            {
                WriteI16(contours, -1);
                boxBitmap[g >> 3] |= (byte)(0x80 >> (g & 7));
                boxes.Write(glyf, start + 2, 8);
                (int componentsEnd, bool haveInstructions) = ScanComponents(glyf, start + 10);
                composite.Write(glyf, start + 10, componentsEnd - (start + 10));
                if (haveInstructions)
                {
                    int length = Sfnt.U16(glyf, componentsEnd);
                    Write255(glyph, length);
                    instructions.Write(glyf, componentsEnd + 2, length);
                }

                continue;
            }

            WriteI16(contours, count);
            int at = start + 10;
            int previousEnd = -1;
            for (int c = 0; c < count; c++, at += 2)
            {
                int endPoint = Sfnt.U16(glyf, at);
                Write255(points, endPoint - previousEnd);
                previousEnd = endPoint;
            }

            int pointCount = previousEnd + 1;
            int instructionLength = Sfnt.U16(glyf, at);
            int instructionsAt = at + 2;
            at = instructionsAt + instructionLength;

            var pointFlags = new byte[pointCount];
            for (int p = 0; p < pointCount;)
            {
                byte flag = glyf[at++];
                pointFlags[p++] = flag;
                if ((flag & Repeat) != 0)
                {
                    for (int r = glyf[at++]; r > 0 && p < pointCount; r--)
                        pointFlags[p++] = flag;
                }
            }

            if ((pointFlags[0] & OverlapSimple) != 0)
            {
                overlapBitmap[g >> 3] |= (byte)(0x80 >> (g & 7));
                anyOverlap = true;
            }

            var dx = new int[pointCount];
            var dy = new int[pointCount];
            at = ReadDeltas(glyf, at, pointFlags, dx, XShort, XSame);
            ReadDeltas(glyf, at, pointFlags, dy, YShort, YSame);

            int x = 0, y = 0, xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            for (int p = 0; p < pointCount; p++)
            {
                WriteTriplet(flags, glyph, (pointFlags[p] & OnCurve) != 0, dx[p], dy[p]);
                x += dx[p];
                y += dy[p];
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
            }

            Write255(glyph, instructionLength);
            instructions.Write(glyf, instructionsAt, instructionLength);

            // The box is worked out again when reading; only a box that differs is stored.
            if (pointCount == 0 || (short)Sfnt.U16(glyf, start + 2) != xMin || (short)Sfnt.U16(glyf, start + 4) != yMin
                || (short)Sfnt.U16(glyf, start + 6) != xMax || (short)Sfnt.U16(glyf, start + 8) != yMax)
            {
                boxBitmap[g >> 3] |= (byte)(0x80 >> (g & 7));
                boxes.Write(glyf, start + 2, 8);
            }
        }

        var output = new MemoryStream();
        WriteU16(output, 0);
        WriteU16(output, (ushort)(anyOverlap ? 1 : 0));
        WriteU16(output, (ushort)glyphs);
        WriteU16(output, (ushort)indexFormat);
        foreach (long size in new[] { contours.Length, points.Length, flags.Length, glyph.Length, composite.Length, boxBitmap.Length + boxes.Length, instructions.Length })
            WriteU32(output, (uint)size);
        foreach (MemoryStream stream in new[] { contours, points, flags, glyph, composite })
            stream.WriteTo(output);
        output.Write(boxBitmap);
        boxes.WriteTo(output);
        instructions.WriteTo(output);
        if (anyOverlap)
            output.Write(overlapBitmap);
        return output.ToArray();
    }

    /// <summary>Rebuilds glyf and loca; also returns each glyph's xMin (compact widths derive side bearings from it).</summary>
    public static (byte[] Glyf, byte[] Loca, int IndexFormat, short[] XMins) Decode(byte[] data)
    {
        int optionFlags = Sfnt.U16(data, 2);
        int glyphs = Sfnt.U16(data, 4);
        int indexFormat = Sfnt.U16(data, 6);
        var sizes = new int[7];
        for (int i = 0; i < 7; i++)
            sizes[i] = checked((int)Sfnt.U32(data, 8 + i * 4));
        var starts = new int[8];
        starts[0] = 36;
        for (int i = 0; i < 7; i++)
            starts[i + 1] = checked(starts[i] + sizes[i]);
        if (starts[7] > data.Length)
            throw new InvalidDataException("The WOFF2 font's outlines are damaged.");

        var contours = new Reader(data, starts[0], sizes[0]);
        var points = new Reader(data, starts[1], sizes[1]);
        var flags = new Reader(data, starts[2], sizes[2]);
        var glyph = new Reader(data, starts[3], sizes[3]);
        var composite = new Reader(data, starts[4], sizes[4]);
        int bitmapLength = 4 * ((glyphs + 31) >> 5);
        int bitmapAt = starts[5];
        var boxes = new Reader(data, starts[5] + bitmapLength, sizes[5] - bitmapLength);
        var instructions = new Reader(data, starts[6], sizes[6]);
        int overlapAt = (optionFlags & 1) != 0 ? starts[7] : -1;

        var glyf = new MemoryStream();
        var offsets = new int[glyphs + 1];
        var xMins = new short[glyphs];
        for (int g = 0; g < glyphs; g++)
        {
            offsets[g] = (int)glyf.Length;
            bool hasBox = (data[bitmapAt + (g >> 3)] & (0x80 >> (g & 7))) != 0;
            short count = contours.I16();
            if (count == 0)
                continue;

            if (count < 0)
            {
                if (!hasBox)
                    throw new InvalidDataException("The WOFF2 font's outlines are damaged (a composite glyph without a box).");
                byte[] box = boxes.Bytes(8);
                xMins[g] = (short)Sfnt.U16(box, 0);
                int componentsStart = composite.Position;
                (int componentsEnd, bool haveInstructions) = ScanComponents(data, componentsStart);
                composite.Position = componentsEnd;
                WriteI16(glyf, -1);
                glyf.Write(box);
                glyf.Write(data, componentsStart, componentsEnd - componentsStart);
                if (haveInstructions)
                {
                    int length = Read255(glyph);
                    WriteU16(glyf, (ushort)length);
                    glyf.Write(instructions.Bytes(length));
                }

                Pad(glyf);
                continue;
            }

            var ends = new int[count];
            int total = 0;
            for (int c = 0; c < count; c++)
            {
                total += Read255(points);
                ends[c] = total - 1;
            }

            var xs = new int[total];
            var ys = new int[total];
            var onCurve = new bool[total];
            int x = 0, y = 0;
            for (int p = 0; p < total; p++)
            {
                byte flag = flags.Byte();
                onCurve[p] = (flag & 0x80) == 0;
                (int deltaX, int deltaY) = ReadTriplet(flag & 0x7F, glyph);
                x += deltaX;
                y += deltaY;
                xs[p] = x;
                ys[p] = y;
            }

            int instructionLength = Read255(glyph);
            byte[] code = instructions.Bytes(instructionLength);
            short xMin, yMin, xMax, yMax;
            if (hasBox)
            {
                byte[] box = boxes.Bytes(8);
                (xMin, yMin, xMax, yMax) = ((short)Sfnt.U16(box, 0), (short)Sfnt.U16(box, 2), (short)Sfnt.U16(box, 4), (short)Sfnt.U16(box, 6));
            }
            else
            {
                (xMin, yMin, xMax, yMax) = ((short)xs.Min(), (short)ys.Min(), (short)xs.Max(), (short)ys.Max());
            }

            xMins[g] = xMin;
            bool overlap = overlapAt >= 0 && (data[overlapAt + (g >> 3)] & (0x80 >> (g & 7))) != 0;
            WriteSimple(glyf, ends, xs, ys, onCurve, code, (xMin, yMin, xMax, yMax), overlap);
        }

        offsets[glyphs] = (int)glyf.Length;
        if (indexFormat == 0 && offsets[glyphs] > 0x1FFFE)
            indexFormat = 1; // too big for short offsets
        var loca = new byte[(glyphs + 1) * (indexFormat == 0 ? 2 : 4)];
        for (int i = 0; i <= glyphs; i++)
        {
            if (indexFormat == 0)
                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            else
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)offsets[i]);
        }

        return (glyf.ToArray(), loca, indexFormat, xMins);
    }

    private static void WriteSimple(MemoryStream glyf, int[] ends, int[] xs, int[] ys, bool[] onCurve, byte[] code, (short XMin, short YMin, short XMax, short YMax) box, bool overlap)
    {
        WriteI16(glyf, (short)ends.Length);
        WriteI16(glyf, box.XMin);
        WriteI16(glyf, box.YMin);
        WriteI16(glyf, box.XMax);
        WriteI16(glyf, box.YMax);
        foreach (int end in ends)
            WriteU16(glyf, (ushort)end);
        WriteU16(glyf, (ushort)code.Length);
        glyf.Write(code);

        var flagBytes = new MemoryStream();
        var xBytes = new MemoryStream();
        var yBytes = new MemoryStream();
        int previousX = 0, previousY = 0;
        for (int p = 0; p < xs.Length; p++)
        {
            int dx = xs[p] - previousX, dy = ys[p] - previousY;
            previousX = xs[p];
            previousY = ys[p];
            byte flag = onCurve[p] ? OnCurve : (byte)0;
            if (p == 0 && overlap)
                flag |= OverlapSimple;
            flag |= Coordinate(xBytes, dx, XShort, XSame);
            flag |= Coordinate(yBytes, dy, YShort, YSame);
            flagBytes.WriteByte(flag);
        }

        flagBytes.WriteTo(glyf);
        xBytes.WriteTo(glyf);
        yBytes.WriteTo(glyf);
        Pad(glyf);
    }

    private static byte Coordinate(MemoryStream bytes, int delta, byte shortFlag, byte sameFlag)
    {
        if (delta == 0)
            return sameFlag;
        if (Math.Abs(delta) < 256)
        {
            bytes.WriteByte((byte)Math.Abs(delta));
            return (byte)(shortFlag | (delta > 0 ? sameFlag : 0));
        }

        WriteI16(bytes, (short)delta);
        return 0;
    }

    private static int ReadDeltas(byte[] glyf, int at, byte[] pointFlags, int[] deltas, byte shortFlag, byte sameFlag)
    {
        for (int p = 0; p < pointFlags.Length; p++)
        {
            byte flag = pointFlags[p];
            if ((flag & shortFlag) != 0)
            {
                int value = glyf[at++];
                deltas[p] = (flag & sameFlag) != 0 ? value : -value;
            }
            else if ((flag & sameFlag) != 0)
            {
                deltas[p] = 0;
            }
            else
            {
                deltas[p] = (short)Sfnt.U16(glyf, at);
                at += 2;
            }
        }

        return at;
    }

    /// <summary>Where a composite glyph's components end, and whether instructions follow them.</summary>
    private static (int End, bool HaveInstructions) ScanComponents(byte[] data, int at)
    {
        bool instructions = false;
        ushort flags;
        do
        {
            flags = Sfnt.U16(data, at);
            instructions |= (flags & HaveInstructions) != 0;
            at += 4; // flags, glyph index
            at += (flags & ArgsAreWords) != 0 ? 4 : 2;
            at += (flags & HaveScale) != 0 ? 2 : (flags & HaveXyScale) != 0 ? 4 : (flags & HaveTwoByTwo) != 0 ? 8 : 0;
        }
        while ((flags & MoreComponents) != 0);
        return (at, instructions);
    }

    /// <summary>WOFF2's triplet encoding of a point's movement (section 5.2), as written by the reference encoder.</summary>
    private static void WriteTriplet(MemoryStream flags, MemoryStream data, bool onCurve, int x, int y)
    {
        int absX = Math.Abs(x), absY = Math.Abs(y);
        int curveBit = onCurve ? 0 : 128;
        int xSign = x < 0 ? 0 : 1, ySign = y < 0 ? 0 : 1;
        int signs = xSign + 2 * ySign;
        if (x == 0 && absY < 1280)
        {
            flags.WriteByte((byte)(curveBit + ((absY & 0xF00) >> 7) + ySign));
            data.WriteByte((byte)(absY & 0xFF));
        }
        else if (y == 0 && absX < 1280)
        {
            flags.WriteByte((byte)(curveBit + 10 + ((absX & 0xF00) >> 7) + xSign));
            data.WriteByte((byte)(absX & 0xFF));
        }
        else if (absX < 65 && absY < 65)
        {
            flags.WriteByte((byte)(curveBit + 20 + ((absX - 1) & 0x30) + (((absY - 1) & 0x30) >> 2) + signs));
            data.WriteByte((byte)((((absX - 1) & 0xF) << 4) | ((absY - 1) & 0xF)));
        }
        else if (absX < 769 && absY < 769)
        {
            flags.WriteByte((byte)(curveBit + 84 + 12 * (((absX - 1) & 0x300) >> 8) + (((absY - 1) & 0x300) >> 6) + signs));
            data.WriteByte((byte)((absX - 1) & 0xFF));
            data.WriteByte((byte)((absY - 1) & 0xFF));
        }
        else if (absX < 4096 && absY < 4096)
        {
            flags.WriteByte((byte)(curveBit + 120 + signs));
            data.WriteByte((byte)(absX >> 4));
            data.WriteByte((byte)(((absX & 0xF) << 4) | (absY >> 8)));
            data.WriteByte((byte)(absY & 0xFF));
        }
        else
        {
            flags.WriteByte((byte)(curveBit + 124 + signs));
            data.WriteByte((byte)(absX >> 8));
            data.WriteByte((byte)(absX & 0xFF));
            data.WriteByte((byte)(absY >> 8));
            data.WriteByte((byte)(absY & 0xFF));
        }
    }

    private static (int X, int Y) ReadTriplet(int flag, Reader data)
    {
        static int WithSign(int flag, int value) => (flag & 1) != 0 ? value : -value;

        if (flag < 10)
            return (0, WithSign(flag, ((flag & 14) << 7) + data.Byte()));
        if (flag < 20)
            return (WithSign(flag, (((flag - 10) & 14) << 7) + data.Byte()), 0);
        if (flag < 84)
        {
            int b0 = flag - 20, b1 = data.Byte();
            return (WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4)), WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F)));
        }

        if (flag < 120)
        {
            int b0 = flag - 84, b1 = data.Byte(), b2 = data.Byte();
            return (WithSign(flag, 1 + ((b0 / 12) << 8) + b1), WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + b2));
        }

        if (flag < 124)
        {
            int b1 = data.Byte(), b2 = data.Byte(), b3 = data.Byte();
            return (WithSign(flag, (b1 << 4) + (b2 >> 4)), WithSign(flag >> 1, ((b2 & 0x0F) << 8) + b3));
        }

        int c1 = data.Byte(), c2 = data.Byte(), c3 = data.Byte(), c4 = data.Byte();
        return (WithSign(flag, (c1 << 8) + c2), WithSign(flag >> 1, (c3 << 8) + c4));
    }

    /// <summary>WOFF2's 255UInt16: one byte for small numbers, more for bigger ones.</summary>
    private static void Write255(MemoryStream stream, int value)
    {
        if (value < 253)
        {
            stream.WriteByte((byte)value);
        }
        else if (value < 506)
        {
            stream.WriteByte(255);
            stream.WriteByte((byte)(value - 253));
        }
        else if (value < 762)
        {
            stream.WriteByte(254);
            stream.WriteByte((byte)(value - 506));
        }
        else
        {
            stream.WriteByte(253);
            WriteU16(stream, (ushort)value);
        }
    }

    private static int Read255(Reader data)
    {
        int code = data.Byte();
        return code switch
        {
            253 => data.U16(),
            255 => 253 + data.Byte(),
            254 => 506 + data.Byte(),
            _ => code,
        };
    }

    private static void Pad(MemoryStream stream)
    {
        while (stream.Length % 4 != 0)
            stream.WriteByte(0);
    }

    private static void WriteI16(Stream stream, short value) => WriteU16(stream, (ushort)value);

    private static void WriteU16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        WriteU16(stream, (ushort)(value >> 16));
        WriteU16(stream, (ushort)value);
    }

    /// <summary>Reads one of the transform's streams, refusing to run past its end.</summary>
    private sealed class Reader(byte[] data, int start, int length)
    {
        private readonly int _end = start + length;

        public int Position { get; set; } = start;

        public byte Byte()
        {
            if (Position >= _end)
                throw new InvalidDataException("The WOFF2 font's outlines are damaged (a stream ends early).");
            return data[Position++];
        }

        public ushort U16() => (ushort)((Byte() << 8) | Byte());

        public short I16() => (short)U16();

        public byte[] Bytes(int count)
        {
            if (Position + count > _end)
                throw new InvalidDataException("The WOFF2 font's outlines are damaged (a stream ends early).");
            byte[] bytes = data.AsSpan(Position, count).ToArray();
            Position += count;
            return bytes;
        }
    }
}
