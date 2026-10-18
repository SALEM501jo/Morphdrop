using System.Buffers.Binary;

namespace ShiftDrop.Imaging;

/// <summary>
/// Removes metadata from a JPEG without re-encoding it: EXIF (camera, GPS location, thumbnails), XMP, IPTC,
/// comments, vendor segments and anything after the image (such as extra MPF pictures). JFIF, the ICC color
/// profile and Adobe color information stay. The EXIF orientation is written back on its own, so photos still
/// show upright. The compressed image data is copied byte for byte.
/// </summary>
internal static class JpegMetadata
{
    private const byte SOI = 0xD8;
    private const byte EOI = 0xD9;
    private const byte SOS = 0xDA;
    private const byte APP0 = 0xE0;
    private const byte APP1 = 0xE1;
    private const byte APP2 = 0xE2;
    private const byte APP14 = 0xEE;
    private const byte APP15 = 0xEF;
    private const byte COM = 0xFE;

    public static (byte[] Data, bool Removed) Remove(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != SOI)
            throw new InvalidDataException("This isn't a JPEG file.");

        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);
        bool removed = false;
        bool orientationWritten = false;
        int position = 2;

        while (true)
        {
            byte marker = ReadMarker(jpeg, ref position);
            if (marker == EOI)
            {
                WriteMarker(output, EOI);
                break;
            }

            if (IsStandalone(marker))
            {
                WriteMarker(output, marker);
                continue;
            }

            int length = SegmentLength(jpeg, position);
            ReadOnlySpan<byte> payload = jpeg.AsSpan(position + 2, length - 2);

            bool keep = Keep(marker, payload);
            if (marker == APP1 && payload.StartsWith("Exif\0\0"u8) && !orientationWritten)
            {
                ushort orientation = ReadOrientation(payload[6..]);
                if (orientation != 1)
                {
                    byte[] replacement = OrientationSegment(orientation);
                    output.Write(replacement);
                    orientationWritten = true;

                    // A file cleaned before already holds exactly this segment: nothing was removed.
                    keep = jpeg.AsSpan(position - 2, length + 2).SequenceEqual(replacement);
                    position += length;
                    removed |= !keep;
                    continue;
                }
            }

            if (keep)
                WriteSegment(output, marker, jpeg, position, length);
            else
                removed = true;

            position += length;
            if (marker == SOS)
            {
                removed |= CopyScans(jpeg, position, output);
                break;
            }
        }

        return (output.ToArray(), removed);
    }

    /// <summary>
    /// Copies the compressed data after a SOS header up to the end of the image, including the tables and scans of
    /// progressive JPEGs, still dropping any metadata segments found between scans. Returns whether anything was
    /// dropped, including data after the end-of-image marker.
    /// </summary>
    private static bool CopyScans(byte[] jpeg, int position, MemoryStream output)
    {
        bool removed = false;
        int dataStart = position;
        while (position < jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
            {
                position++;
                continue;
            }

            int next = position + 1;
            while (next < jpeg.Length && jpeg[next] == 0xFF)
                next++; // fill bytes
            if (next >= jpeg.Length)
                break;

            byte marker = jpeg[next];
            if (marker == 0x00 || marker is >= 0xD0 and <= 0xD7)
            {
                position = next + 1; // a stuffed 0xFF or a restart marker: still image data
                continue;
            }

            output.Write(jpeg, dataStart, position - dataStart);
            if (marker == EOI)
            {
                WriteMarker(output, EOI);
                return removed || next + 1 < jpeg.Length;
            }

            int length = SegmentLength(jpeg, next + 1);
            if (Keep(marker, jpeg.AsSpan(next + 3, length - 2)))
                WriteSegment(output, marker, jpeg, next + 1, length);
            else
                removed = true;

            position = next + 1 + length;
            dataStart = position;
        }

        // A truncated file: keep what is there and close the image properly.
        output.Write(jpeg, dataStart, jpeg.Length - dataStart);
        WriteMarker(output, EOI);
        return removed;
    }

    private static bool Keep(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        APP0 => payload.StartsWith("JFIF\0"u8),
        APP2 => payload.StartsWith("ICC_PROFILE\0"u8),
        APP14 => payload.StartsWith("Adobe"u8),
        >= APP0 and <= APP15 => false, // EXIF and XMP (APP1), MPF (APP2), IPTC (APP13), vendor data
        COM => false,
        _ => true, // tables, frame and scan headers
    };

    private static bool IsStandalone(byte marker) => marker == 0x01 || marker is >= 0xD0 and <= 0xD7;

    private static byte ReadMarker(byte[] jpeg, ref int position)
    {
        if (position >= jpeg.Length || jpeg[position] != 0xFF)
            throw Damaged();
        while (position < jpeg.Length && jpeg[position] == 0xFF)
            position++;
        if (position >= jpeg.Length)
            throw Damaged();
        return jpeg[position++];
    }

    /// <summary>The segment length at <paramref name="position"/>, which counts its own two bytes.</summary>
    private static int SegmentLength(byte[] jpeg, int position)
    {
        if (position + 2 > jpeg.Length)
            throw Damaged();
        int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position));
        if (length < 2 || position + length > jpeg.Length)
            throw Damaged();
        return length;
    }

    private static void WriteMarker(MemoryStream output, byte marker)
    {
        output.WriteByte(0xFF);
        output.WriteByte(marker);
    }

    private static void WriteSegment(MemoryStream output, byte marker, byte[] jpeg, int position, int length)
    {
        WriteMarker(output, marker);
        output.Write(jpeg, position, length);
    }

    /// <summary>Reads the Orientation tag (0x0112) from IFD0 of an EXIF block; 1 when absent or unreadable.</summary>
    internal static ushort ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;

        bool little = tiff[0] == 'I' && tiff[1] == 'I';
        if (!little && !(tiff[0] == 'M' && tiff[1] == 'M'))
            return 1;

        uint ifd = ReadUInt32(tiff, 4, little);
        if (ifd > tiff.Length - 2)
            return 1;

        int count = ReadUInt16(tiff, (int)ifd, little);
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                break;
            if (ReadUInt16(tiff, entry, little) == 0x0112 && ReadUInt16(tiff, entry + 2, little) == 3)
            {
                ushort value = ReadUInt16(tiff, entry + 8, little);
                return value is >= 1 and <= 8 ? value : (ushort)1;
            }
        }

        return 1;
    }

    /// <summary>A minimal EXIF segment holding nothing but the orientation.</summary>
    private static byte[] OrientationSegment(ushort orientation) => new byte[]
    {
        0xFF, APP1, 0x00, 0x22, // marker and length (34)
        (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
        (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // big-endian TIFF header, IFD0 at offset 8
        0x00, 0x01, // one entry
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00, // Orientation, SHORT, 1 value
        0x00, 0x00, 0x00, 0x00, // no further IFDs
    };

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
        : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])
        : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    private static InvalidDataException Damaged() => new("The JPEG file is damaged.");
}
