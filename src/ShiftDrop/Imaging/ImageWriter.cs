using ImageMagick;

namespace ShiftDrop.Imaging;

internal static class ImageWriter
{
    public const uint JpegQuality = 92;
    public const uint WebPQuality = 85;
    public const uint AvifQuality = 70;

    // libaom's default speed takes ~6 s for a 12 MP photo; 6 is ~5x faster for about the same size.
    private const string AvifSpeed = "6";

    private const double A4LongSideInches = 11.69;
    private const double A4ShortSideInches = 8.27;

    /// <summary>Whether converting needs every frame: animations, icons (to pick the largest) and TIFF pages to PDF.</summary>
    public static bool NeedsAllFrames(ImageFormat source, ImageFormat target) =>
        source.Kind == ImageKind.Ico || KeepsFrames(source, target);

    public static void Write(MagickImageCollection images, ImageFormat source, ImageFormat target, Stream output, bool lossless = false)
    {
        if (target.Kind == ImageKind.Ico)
        {
            IcoWriter.Write(ImageReader.Largest(images), output);
            return;
        }

        if (images.Count > 1 && KeepsFrames(source, target))
        {
            if (source.SupportsAnimation)
                images.Coalesce();
            foreach (IMagickImage<byte> frame in images)
                Prepare(frame, source, target, lossless);
            if (target.Kind == ImageKind.Gif)
                images.Optimize();
            images.Write(output, target.MagickFormat);
            return;
        }

        IMagickImage<byte> image = source.Kind == ImageKind.Ico ? ImageReader.Largest(images) : images[0];
        Prepare(image, source, target, lossless);
        image.Write(output, target.MagickFormat);
    }

    /// <summary>Settings for the target format. A JPEG saved as JPEG again keeps its own quality.</summary>
    internal static void Prepare(IMagickImage<byte> image, ImageFormat source, ImageFormat target, bool lossless = false)
    {
        if (!target.SupportsTransparency && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Alpha(AlphaOption.Off);
        }

        switch (target.Kind)
        {
            case ImageKind.Jpeg:
                if (source.Kind != ImageKind.Jpeg || image.Quality == 0)
                    image.Quality = JpegQuality;
                break;
            case ImageKind.WebP:
                if (lossless)
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                else
                    image.Quality = WebPQuality;
                break;
            case ImageKind.Avif:
                image.Quality = AvifQuality;
                image.Settings.SetDefine("heic:speed", AvifSpeed); // the AVIF encoder lives in the HEIC coder
                break;
            case ImageKind.Tiff:
                image.Settings.Compression = CompressionMethod.LZW;
                break;
            case ImageKind.Pdf:
                FitOnA4Page(image);
                image.Settings.Compression = source.Kind is ImageKind.Jpeg or ImageKind.Heic or ImageKind.Avif or ImageKind.WebP
                    ? CompressionMethod.JPEG // photos
                    : CompressionMethod.Zip; // screenshots and graphics stay lossless
                image.Quality = JpegQuality;
                break;
        }
    }

    private static bool KeepsFrames(ImageFormat source, ImageFormat target) =>
        (source.SupportsAnimation && target.SupportsAnimation)
        || (source.Kind == ImageKind.Tiff && target.Kind == ImageKind.Pdf);

    /// <summary>Picks a resolution that makes the page about A4-sized while keeping every pixel.</summary>
    private static void FitOnA4Page(IMagickImage<byte> image)
    {
        double longSide = Math.Max(image.Width, image.Height);
        double shortSide = Math.Min(image.Width, image.Height);
        double dpi = Math.Max(72, Math.Max(longSide / A4LongSideInches, shortSide / A4ShortSideInches));
        image.Density = new Density(dpi, dpi, DensityUnit.PixelsPerInch);
    }
}
