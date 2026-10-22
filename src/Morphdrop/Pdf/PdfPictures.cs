using ImageMagick;
using Morphdrop.Imaging;

namespace Morphdrop.Pdf;

/// <summary>A picture ready to go into a PDF: JPEG or PNG bytes, upright, with its size in pixels.</summary>
internal sealed record PdfPicture(byte[] Data, int Width, int Height);

internal static class PdfPictures
{
    /// <summary>
    /// JPEG photos that are already upright go in exactly as they are (no quality lost). Anything else is turned
    /// upright and stored as JPEG (photos) or PNG (graphics, transparency).
    /// </summary>
    public static PdfPicture Prepare(string path)
    {
        ImageFormat source = ImageReader.Identify(path);
        if (source.Kind == ImageKind.Jpeg)
        {
            // Look at the tags without decoding: the reader below already turns the picture upright.
            using var header = new MagickImage();
            header.Ping(path, new MagickReadSettings { Format = MagickFormat.Jpeg });
            byte[] original = File.ReadAllBytes(path);
            if (header.Orientation is OrientationType.TopLeft or OrientationType.Undefined
                && header.ColorSpace is ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray
                && PdfCanRead(original))
                return new PdfPicture(original, (int)header.Width, (int)header.Height);
        }

        using MagickImageCollection frames = ImageReader.Read(path, source, allFrames: source.Kind == ImageKind.Ico);
        IMagickImage<byte> image = source.Kind == ImageKind.Ico ? ImageReader.Largest(frames) : frames[0];
        image.Strip();
        bool photo = !image.HasAlpha && source.Kind is ImageKind.Jpeg or ImageKind.Heic or ImageKind.Avif or ImageKind.Jxl or ImageKind.WebP or ImageKind.Tiff or ImageKind.Raw;
        if (photo)
        {
            image.ColorSpace = ColorSpace.sRGB;
            image.Quality = 92;
            return new PdfPicture(image.ToByteArray(MagickFormat.Jpeg), (int)image.Width, (int)image.Height);
        }

        return new PdfPicture(image.ToByteArray(MagickFormat.Png32), (int)image.Width, (int)image.Height);
    }

    /// <summary>
    /// PDFsharp's JPEG reader turns some valid files down (WhatsApp's, with an extra header block); those are
    /// re-saved as a plain JPEG instead of failing the whole PDF.
    /// </summary>
    private static bool PdfCanRead(byte[] jpeg)
    {
        try
        {
            using var stream = new MemoryStream(jpeg);
            using PdfSharp.Drawing.XImage image = PdfSharp.Drawing.XImage.FromStream(stream);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
