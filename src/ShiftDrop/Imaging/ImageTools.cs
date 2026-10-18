using System.Buffers.Binary;
using ImageMagick;
using ImageMagick.ImageOptimizers;
using ShiftDrop.Actions;

namespace ShiftDrop.Imaging;

internal enum ResizeMode { Half, FitFullHd }

/// <summary>The Tools page. Results keep the original's format and are saved as "name (what was done).ext".</summary>
internal static class ImageTools
{
    public const uint FullHd = 1920;
    public const uint CompressedJpegQuality = 80;
    public const uint CompressedWebPQuality = 75;

    // Smaller savings aren't worth a second file.
    private const double MinimumSavings = 0.03;

    public static string Resize(string inputPath, ResizeMode mode)
    {
        ImageFormat source = ImageReader.Identify(inputPath);
        using MagickImageCollection images = ImageReader.Read(inputPath, source, allFrames: source.SupportsAnimation);
        if (images.Count > 1)
            images.Coalesce();

        IMagickImage<byte> first = images[0];
        if (mode == ResizeMode.FitFullHd && first.Width <= FullHd && first.Height <= FullHd)
            throw new NothingToDoException($"It's already {FullHd} px or smaller.");

        foreach (IMagickImage<byte> frame in images)
        {
            if (mode == ResizeMode.Half)
                frame.Resize(new Percentage(50));
            else
                frame.Resize(new MagickGeometry(FullHd, FullHd) { Greater = true });
        }

        // HEIC can't be written, so resized HEIC photos are saved as JPG.
        ImageFormat target = source.CanWrite ? source : ImageFormat.Jpeg;
        string extension = target == source ? Path.GetExtension(inputPath) : target.Extension;
        bool lossless = source.Kind == ImageKind.WebP && WebPIsLossless(inputPath);
        string suffix = mode == ResizeMode.Half ? "50%" : $"{FullHd}px";
        return OutputFile.Save(inputPath, suffix, extension, output => ImageWriter.Write(images, source, target, output, lossless));
    }

    /// <summary>
    /// JPEG: lossless optimization, or quality 80 when that saves clearly more. PNG and GIF: lossless only.
    /// WebP: re-encoded at quality 75 (lossless files stay lossless).
    /// </summary>
    public static string Compress(string inputPath)
    {
        ImageFormat source = ImageReader.Identify(inputPath);
        byte[] original = File.ReadAllBytes(inputPath);
        byte[] smallest = source.Kind switch
        {
            ImageKind.Jpeg => CompressJpeg(original),
            ImageKind.Png => Optimize(new PngOptimizer { OptimalCompression = true }, original),
            ImageKind.Gif => Optimize(new GifOptimizer(), original),
            ImageKind.WebP => ReencodeWebP(original),
            _ => throw new NothingToDoException($"{source.Name} files can't be compressed yet."),
        };

        if (smallest.Length > original.Length * (1 - MinimumSavings))
            throw new NothingToDoException("It's already about as small as it gets.");
        return OutputFile.Save(inputPath, "compressed", Path.GetExtension(inputPath), output => output.Write(smallest));
    }

    public static string RemoveMetadata(string inputPath)
    {
        ImageFormat source = ImageReader.Identify(inputPath);
        byte[] original = File.ReadAllBytes(inputPath);
        (byte[] cleaned, bool removed) = source.Kind switch
        {
            ImageKind.Jpeg => JpegMetadata.Remove(original),
            ImageKind.Png => PngMetadata.Remove(original),
            _ => throw new NothingToDoException($"Removing metadata from {source.Name} files isn't supported yet."),
        };

        if (!removed)
            throw new NothingToDoException("There's no metadata to remove.");
        return OutputFile.Save(inputPath, "no metadata", Path.GetExtension(inputPath), output => output.Write(cleaned));
    }

    private static byte[] CompressJpeg(byte[] original)
    {
        byte[] lossless = Optimize(new JpegOptimizer { OptimalCompression = true, Progressive = true }, original);

        using var stream = new MemoryStream();
        stream.Write(original);
        stream.Position = 0;
        new JpegOptimizer { OptimalCompression = true, Progressive = true }.Compress(stream, CompressedJpegQuality);
        byte[] lossy = stream.ToArray();

        // Only give up quality for a real gain over the lossless result.
        return lossy.Length < lossless.Length * 0.9 ? lossy : lossless;
    }

    private static byte[] Optimize(IImageOptimizer optimizer, byte[] original)
    {
        using var stream = new MemoryStream();
        stream.Write(original);
        stream.Position = 0;
        optimizer.LosslessCompress(stream);
        return stream.ToArray();
    }

    private static byte[] ReencodeWebP(byte[] original)
    {
        bool lossless = WebPIsLossless(original);
        using var images = new MagickImageCollection(original, new MagickReadSettings { Format = MagickFormat.WebP });
        foreach (IMagickImage<byte> frame in images)
        {
            frame.Settings.SetDefine(MagickFormat.WebP, "method", 6);
            if (lossless)
                frame.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            else
                frame.Quality = CompressedWebPQuality;
        }

        return images.ToByteArray(MagickFormat.WebP);
    }

    private static bool WebPIsLossless(string path)
    {
        // Enough to get past a large ICC profile, which comes before the image data.
        var header = new byte[64 * 1024];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return WebPIsLossless(header.AsSpan(0, read));
    }

    /// <summary>Walks the RIFF chunks (and the first animation frame) to the first VP8 (lossy) or VP8L (lossless) image.</summary>
    internal static bool WebPIsLossless(ReadOnlySpan<byte> webp)
    {
        int position = 12;
        while (position + 8 <= webp.Length)
        {
            ReadOnlySpan<byte> fourCc = webp.Slice(position, 4);
            int size = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(webp[(position + 4)..]));
            if (fourCc.SequenceEqual("VP8L"u8))
                return true;
            if (fourCc.SequenceEqual("VP8 "u8))
                return false;

            // An animation frame holds a 16-byte header followed by its own image chunks.
            position = fourCc.SequenceEqual("ANMF"u8) ? position + 8 + 16 : position + 8 + size + (size & 1);
        }

        return false;
    }
}
