using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Interop;

namespace ShiftDrop.Imaging;

internal static class ImageConverter
{
    /// <summary>Writes the image in <paramref name="target"/> format next to the original and returns the new path.</summary>
    public static string Convert(string inputPath, ImageFormat target)
    {
        ImageFormat source = ImageReader.Identify(inputPath);
        using MagickImageCollection images = ImageReader.Read(inputPath, source, ImageWriter.NeedsAllFrames(source, target));
        IReadOnlySet<string>? sourceChunks = source.Kind == ImageKind.Png ? PngChunks.TypesIn(inputPath) : null;
        return OutputFile.Save(inputPath, suffix: null, target.Extension,
            output => ImageWriter.Write(images, source, target, output, sourcePngChunks: sourceChunks));
    }

    /// <summary>
    /// One PDF with a page per image, in File Explorer's name order ("scan 2" before "scan 10"), each page about
    /// A4-sized at the image's full resolution. Saved next to the first image as "name (N pages).pdf".
    /// </summary>
    public static string CombineToPdf(IReadOnlyList<string> inputPaths)
    {
        string[] ordered = inputPaths.Order(NaturalOrder.Instance).ToArray();
        using var pages = new MagickImageCollection();
        foreach (string path in ordered)
        {
            ImageFormat source = ImageReader.Identify(path);
            using MagickImageCollection images = ImageReader.Read(path, source, allFrames: source.Kind == ImageKind.Ico);
            IMagickImage<byte> page = (source.Kind == ImageKind.Ico ? ImageReader.Largest(images) : images[0]).Clone();
            ImageWriter.Prepare(page, source, ImageFormat.Pdf);
            pages.Add(page);
        }

        return OutputFile.Save(ordered[0], $"{ordered.Length} pages", ImageFormat.Pdf.Extension,
            output => pages.Write(output, MagickFormat.Pdf));
    }

    /// <summary>File Explorer's sort order: digits compare as numbers.</summary>
    internal sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new();

        public int Compare(string? x, string? y) =>
            NativeMethods.StrCmpLogicalW(Path.GetFileName(x) ?? "", Path.GetFileName(y) ?? "");
    }
}
