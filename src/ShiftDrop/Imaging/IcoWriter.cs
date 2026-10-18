using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShiftDrop.Imaging;

/// <summary>
/// Writes a Windows .ico holding the standard icon sizes as PNG-compressed images (supported since Windows Vista).
/// The picture is fitted into each square, centred on transparency, and never scaled up past its own size.
/// </summary>
internal static class IcoWriter
{
    public static readonly int[] StandardSizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static void Write(BitmapSource image, Stream output)
    {
        int largest = Math.Max(image.PixelWidth, image.PixelHeight);
        int[] sizes = StandardSizes.Where(size => size <= largest).DefaultIfEmpty(StandardSizes[0]).ToArray();

        // Scale in premultiplied alpha so transparent edges don't pick up dark fringes.
        BitmapSource premultiplied = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        byte[][] entries = sizes.Select(size => EncodePng(FitIntoSquare(premultiplied, size))).ToArray();

        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)entries.Length);

        int offset = 6 + 16 * entries.Length;
        for (int i = 0; i < entries.Length; i++)
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

    private static BitmapSource FitIntoSquare(BitmapSource premultiplied, int size)
    {
        double scale = (double)size / Math.Max(premultiplied.PixelWidth, premultiplied.PixelHeight);
        BitmapSource scaled = Math.Abs(scale - 1) < 1e-9
            ? premultiplied
            : new TransformedBitmap(premultiplied, new ScaleTransform(scale, scale));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

        int width = Math.Min(size, bgra.PixelWidth);
        int height = Math.Min(size, bgra.PixelHeight);
        var region = new byte[width * height * 4];
        bgra.CopyPixels(new Int32Rect(0, 0, width, height), region, width * 4, 0);

        var square = new byte[size * size * 4];
        int left = (size - width) / 2;
        int top = (size - height) / 2;
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(region, y * width * 4, square, ((top + y) * size + left) * 4, width * 4);

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, square, size * 4);
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }
}
