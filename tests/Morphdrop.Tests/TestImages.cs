using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Morphdrop.Tests;

internal static class TestImages
{
    public static readonly Color Red = Color.FromRgb(220, 30, 30);
    public static readonly Color Blue = Color.FromRgb(30, 60, 220);
    public static readonly Color PureRed = Color.FromRgb(255, 0, 0); // ImageMagick's "red"
    public static readonly Color PureBlue = Color.FromRgb(0, 0, 255); // ImageMagick's "blue"
    public static readonly Color White = Colors.White;
    public static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);

    public static BitmapSource Create(int width, int height, Func<int, int, Color> pixel)
    {
        var data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = pixel(x, y);
                int i = (y * width + x) * 4;
                (data[i], data[i + 1], data[i + 2], data[i + 3]) = (color.B, color.G, color.R, color.A);
            }
        }

        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4);
        image.Freeze();
        return image;
    }

    public static string WritePng(string path, int width, int height, Func<int, int, Color> pixel)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Create(width, height, pixel)));
        using FileStream file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    public static Pixels Read(string path)
    {
        using FileStream file = File.OpenRead(path);
        BitmapFrame frame = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return Read(frame);
    }

    public static Pixels Read(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var data = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(data, bgra.PixelWidth * 4, 0);
        return new Pixels(bgra.PixelWidth, bgra.PixelHeight, data);
    }

    public static void AssertClose(Color expected, Color actual, int tolerance = 24)
    {
        bool close = Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(close, $"Expected {expected} but got {actual}");
    }

    internal sealed record Pixels(int Width, int Height, byte[] Data)
    {
        public Color this[int x, int y]
        {
            get
            {
                int i = (y * Width + x) * 4;
                return Color.FromArgb(Data[i + 3], Data[i + 2], Data[i + 1], Data[i]);
            }
        }
    }
}
