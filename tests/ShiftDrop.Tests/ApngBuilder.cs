using System.Buffers.Binary;
using ImageMagick;
using ShiftDrop.Imaging;

namespace ShiftDrop.Tests;

/// <summary>Writes animated PNGs for tests (ImageMagick can't write APNG without FFmpeg).</summary>
internal static class ApngBuilder
{
    public const byte DisposeNone = 0, DisposeBackground = 1, DisposePrevious = 2;
    public const byte BlendSource = 0, BlendOver = 1;

    public sealed record Frame(string Color, uint Width, uint Height, int X = 0, int Y = 0,
        byte Dispose = DisposeNone, byte Blend = BlendSource, ushort DelayHundredths = 10);

    /// <summary>Builds an APNG; the first frame must cover the whole canvas, as the spec requires for an animated IDAT.</summary>
    public static byte[] Build(uint width, uint height, params Frame[] frames)
    {
        using var output = new MemoryStream();
        output.Write(PngChunks.Signature);

        byte[][] encoded = frames.Select(Encode).ToArray();
        byte[] ihdr = ChunkData(encoded[0], "IHDR").Single();
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        PngChunks.Write(output, "IHDR", ihdr);

        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, (uint)frames.Length); // plays = 0: loop forever
        PngChunks.Write(output, "acTL", actl);

        uint sequence = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            Frame frame = frames[i];
            var fctl = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(fctl, sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(4), frame.Width);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(8), frame.Height);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(12), (uint)frame.X);
            BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(16), (uint)frame.Y);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), frame.DelayHundredths);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), 100);
            fctl[24] = frame.Dispose;
            fctl[25] = frame.Blend;
            PngChunks.Write(output, "fcTL", fctl);

            byte[] data = ChunkData(encoded[i], "IDAT").SelectMany(bytes => bytes).ToArray();
            if (i == 0)
            {
                PngChunks.Write(output, "IDAT", data);
            }
            else
            {
                var fdat = new byte[data.Length + 4];
                BinaryPrimitives.WriteUInt32BigEndian(fdat, sequence++);
                data.CopyTo(fdat, 4);
                PngChunks.Write(output, "fdAT", fdat);
            }
        }

        PngChunks.Write(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    // Every frame as 8-bit RGBA, so all frames share the header's bit depth and color type.
    private static byte[] Encode(Frame frame)
    {
        using var image = new MagickImage(new MagickColor(frame.Color), frame.Width, frame.Height);
        image.Alpha(AlphaOption.Set);
        return image.ToByteArray(MagickFormat.Png32);
    }

    private static IEnumerable<byte[]> ChunkData(byte[] png, string type) =>
        PngChunks.Parse(png).Where(chunk => chunk.Type == type).Select(chunk => png.AsSpan(chunk.Offset, chunk.Length).ToArray());
}
