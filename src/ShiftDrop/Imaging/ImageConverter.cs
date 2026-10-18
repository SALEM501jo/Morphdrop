using ImageMagick;
using ShiftDrop.Actions;

namespace ShiftDrop.Imaging;

internal static class ImageConverter
{
    /// <summary>Writes the image in <paramref name="target"/> format next to the original and returns the new path.</summary>
    public static string Convert(string inputPath, ImageFormat target)
    {
        ImageFormat source = ImageReader.Identify(inputPath);
        using MagickImageCollection images = ImageReader.Read(inputPath, source, ImageWriter.NeedsAllFrames(source, target));
        return OutputFile.Save(inputPath, suffix: null, target.Extension, output => ImageWriter.Write(images, source, target, output));
    }
}
