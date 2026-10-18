using System.Windows.Media.Imaging;
using ShiftDrop.Actions;

namespace ShiftDrop.Imaging;

internal static class ImageConverter
{
    /// <summary>Writes the image in <paramref name="target"/> format next to the original and returns the new path.</summary>
    public static string Convert(string inputPath, ImageFormat target)
    {
        BitmapSource image = ImageLoader.Load(inputPath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        string outputPath;
        using (FileStream output = OutputFile.CreateUnique(directory, Path.GetFileNameWithoutExtension(inputPath), target.Extension, out outputPath))
        {
            try
            {
                ImageEncoder.Save(image, target, output);
            }
            catch
            {
                output.Dispose();
                TryDelete(outputPath);
                throw;
            }
        }

        return outputPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave the partial file; the error is reported anyway.
        }
    }
}
