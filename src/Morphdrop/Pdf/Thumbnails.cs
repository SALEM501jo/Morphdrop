using System.Windows.Media.Imaging;
using ImageMagick;
using Morphdrop.Imaging;

namespace Morphdrop.Pdf;

/// <summary>Small, upright, frozen pictures for the PDF windows (made off the UI thread).</summary>
internal static class Thumbnails
{
    /// <summary>A picture file, shrunk to fit <paramref name="maxSide"/>, plus its full size in pixels.</summary>
    public static (BitmapSource Picture, int Width, int Height) OfPicture(string path, uint maxSide)
    {
        ImageFormat format = ImageReader.Identify(path);
        using MagickImageCollection frames = ImageReader.Read(path, format, allFrames: format.Kind == ImageKind.Ico);
        IMagickImage<byte> image = format.Kind == ImageKind.Ico ? ImageReader.Largest(frames) : frames[0];
        int width = (int)image.Width, height = (int)image.Height;
        image.Resize(new MagickGeometry(maxSide, maxSide) { Greater = true });
        return (FromPng(image.ToByteArray(MagickFormat.Png32)), width, height);
    }

    public static BitmapSource FromPng(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
