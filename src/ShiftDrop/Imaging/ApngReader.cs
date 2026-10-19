using System.Buffers.Binary;
using ImageMagick;

namespace ShiftDrop.Imaging;

/// <summary>
/// Decodes animated PNGs. ImageMagick reads only the first frame of an APNG (its APNG coder needs FFmpeg), so
/// every frame is rebuilt here as a standalone PNG, decoded, and composited the way the APNG spec says
/// (blend and dispose operations), giving full-size frames ready to be written as GIF or WebP.
/// </summary>
internal static class ApngReader
{
    private const byte DisposeNone = 0;
    private const byte DisposeBackground = 1;
    private const byte DisposePrevious = 2;
    private const byte BlendSource = 0;

    // Animation control chunks, which a frame rebuilt as a still PNG must not carry.
    private static readonly HashSet<string> AnimationChunks = new(StringComparer.Ordinal) { "acTL", "fcTL", "fdAT" };

    private sealed record Frame(uint Width, uint Height, int X, int Y, uint Delay, byte Dispose, byte Blend)
    {
        public List<(int Offset, int Length)> Data { get; } = new();
    }

    /// <summary>Whether the file is an animated PNG (an acTL chunk before the image data). Reads chunk headers only.</summary>
    public static bool IsAnimated(string path)
    {
        foreach (string type in PngChunks.TypesIn(path))
        {
            if (type == "acTL")
                return true;
            if (type == "IDAT")
                return false;
        }

        return false;
    }

    public static bool IsAnimated(byte[] png)
    {
        foreach (PngChunks.Chunk chunk in PngChunks.Parse(png))
        {
            if (chunk.Type == "acTL")
                return true;
            if (chunk.Type == "IDAT")
                return false;
        }

        return false;
    }

    public static MagickImageCollection Read(byte[] png)
    {
        List<PngChunks.Chunk> chunks = PngChunks.Parse(png);
        PngChunks.Chunk header = chunks[0];
        if (header.Type != "IHDR" || header.Length != 13)
            throw new InvalidDataException("The PNG file is damaged.");

        uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(header.Offset));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(header.Offset + 4));
        uint plays = 0;
        var frames = new List<Frame>();
        var sharedChunks = new List<PngChunks.Chunk>(); // palette, transparency, color information…
        bool imageDataSeen = false;

        foreach (PngChunks.Chunk chunk in chunks.Skip(1))
        {
            ReadOnlySpan<byte> data = png.AsSpan(chunk.Offset, chunk.Length);
            switch (chunk.Type)
            {
                case "acTL" when chunk.Length >= 8:
                    plays = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    break;
                case "fcTL" when chunk.Length >= 26:
                    frames.Add(ReadFrameControl(data));
                    break;
                case "IDAT":
                    imageDataSeen = true;
                    frames.LastOrDefault()?.Data.Add((chunk.Offset, chunk.Length)); // without an fcTL it's a hidden default image
                    break;
                case "fdAT" when chunk.Length > 4 && frames.Count > 0:
                    frames[^1].Data.Add((chunk.Offset + 4, chunk.Length - 4)); // skip the sequence number
                    break;
                case "IEND":
                    break;
                default:
                    if (!imageDataSeen && !AnimationChunks.Contains(chunk.Type))
                        sharedChunks.Add(chunk);
                    break;
            }
        }

        if (frames.Count == 0 || frames.All(frame => frame.Data.Count == 0))
            throw new InvalidDataException("The animated PNG has no frames.");

        var images = new MagickImageCollection();
        IMagickImage<byte> canvas = new MagickImage(MagickColors.Transparent, width, height);
        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                Frame frame = frames[i];
                if (frame.Data.Count == 0)
                    continue;

                // "Previous" on the first frame means "background" (APNG spec).
                byte dispose = i == 0 && frame.Dispose == DisposePrevious ? DisposeBackground : frame.Dispose;
                IMagickImage<byte>? saved = dispose == DisposePrevious ? canvas.Clone() : null;

                using (var image = new MagickImage(BuildFramePng(png, header, sharedChunks, frame), new MagickReadSettings { Format = MagickFormat.Png }))
                    canvas.Composite(image, frame.X, frame.Y, frame.Blend == BlendSource ? CompositeOperator.Copy : CompositeOperator.Over);

                IMagickImage<byte> output = canvas.Clone();
                output.AnimationDelay = frame.Delay;
                output.AnimationIterations = plays;
                output.GifDisposeMethod = GifDisposeMethod.None;
                images.Add(output);

                if (dispose == DisposeBackground)
                {
                    using var clear = new MagickImage(MagickColors.Transparent, frame.Width, frame.Height);
                    canvas.Composite(clear, frame.X, frame.Y, CompositeOperator.Copy);
                }
                else if (saved is not null)
                {
                    canvas.Dispose();
                    canvas = saved;
                }
            }

            return images;
        }
        catch
        {
            images.Dispose();
            throw;
        }
        finally
        {
            canvas.Dispose();
        }
    }

    private static Frame ReadFrameControl(ReadOnlySpan<byte> data)
    {
        // sequence(4) width(4) height(4) x(4) y(4) delay numerator(2) denominator(2) dispose(1) blend(1)
        ushort numerator = BinaryPrimitives.ReadUInt16BigEndian(data[20..]);
        ushort denominator = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
        double seconds = numerator / (double)(denominator == 0 ? 100 : denominator);
        return new Frame(
            BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
            (int)BinaryPrimitives.ReadUInt32BigEndian(data[12..]),
            (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]),
            (uint)Math.Round(seconds * 100), // ImageMagick counts in 1/100 s
            data[24],
            data[25]);
    }

    /// <summary>A standalone PNG for one frame: the file's header at the frame's size, the shared chunks, the frame's data.</summary>
    private static byte[] BuildFramePng(byte[] png, PngChunks.Chunk header, List<PngChunks.Chunk> sharedChunks, Frame frame)
    {
        using var output = new MemoryStream();
        output.Write(PngChunks.Signature);

        var ihdr = png.AsSpan(header.Offset, header.Length).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), frame.Height);
        PngChunks.Write(output, "IHDR", ihdr);

        foreach (PngChunks.Chunk chunk in sharedChunks)
            output.Write(png, chunk.Start, chunk.TotalSize);

        using var data = new MemoryStream();
        foreach ((int offset, int length) in frame.Data)
            data.Write(png, offset, length);
        PngChunks.Write(output, "IDAT", data.ToArray());
        PngChunks.Write(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }
}
