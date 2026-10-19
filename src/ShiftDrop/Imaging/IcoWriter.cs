using ImageMagick;

namespace ShiftDrop.Imaging;

/// <summary>
/// Writes a Windows .ico holding the standard icon sizes as PNG-compressed images (supported since Windows Vista).
/// The picture is fitted into each square, centred on transparency, and never scaled up past its own size.
/// </summary>
internal static class IcoWriter
{
    public static readonly int[] StandardSizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static void Write(IMagickImage<byte> image, Stream output)
    {
        uint largest = Math.Max(image.Width, image.Height);
        int[] sizes = StandardSizes.Where(size => size <= largest).DefaultIfEmpty(StandardSizes[0]).ToArray();
        byte[][] entries = sizes.Select(size => RenderEntry(image, (uint)size)).ToArray();
        WriteEntries(sizes, entries, output);
    }

    /// <summary>Writes the .ico container around PNG images that are already square (sizes[i] × sizes[i]).</summary>
    public static void WriteEntries(IReadOnlyList<int> sizes, IReadOnlyList<byte[]> entries, Stream output)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)entries.Count);

        int offset = 6 + 16 * entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            byte dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 means 256
            writer.Write(dimension); // width
            writer.Write(dimension); // height
            writer.Write((byte)0); // palette size
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(entries[i].Length);
            writer.Write(offset);
            offset += entries[i].Length;
        }

        foreach (byte[] entry in entries)
            writer.Write(entry);
    }

    private static byte[] RenderEntry(IMagickImage<byte> image, uint size)
    {
        using IMagickImage<byte> entry = image.Clone();
        entry.Alpha(AlphaOption.Set);
        entry.Resize(new MagickGeometry(size, size));
        entry.Extent(size, size, Gravity.Center, MagickColors.Transparent);
        return PngMetadata.RemoveWriterChunks(entry.ToByteArray(MagickFormat.Png32), sourceChunks: null);
    }
}
