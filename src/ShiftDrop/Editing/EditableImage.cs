using System.Windows.Media.Imaging;
using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Imaging;

namespace ShiftDrop.Editing;

/// <summary>
/// An image opened in an editor: every frame at full size (upright), plus a smaller preview to draw on.
/// Editors measure on the preview; <see cref="PreviewScale"/> maps those measurements back to full size.
/// </summary>
internal sealed class EditableImage : IDisposable
{
    public const uint PreviewMaxSide = 1600;

    private EditableImage(string path, ImageFormat source, MagickImageCollection frames, BitmapSource preview)
    {
        Path = path;
        Source = source;
        Frames = frames;
        Preview = preview;
        PreviewScale = (double)frames[0].Width / preview.PixelWidth;
    }

    public string Path { get; }

    public ImageFormat Source { get; }

    public MagickImageCollection Frames { get; }

    public uint Width => Frames[0].Width;

    public uint Height => Frames[0].Height;

    /// <summary>Frozen, so it can be created on a background thread and shown on the UI thread.</summary>
    public BitmapSource Preview { get; }

    /// <summary>Full-size pixels per preview pixel (1 when the image is small).</summary>
    public double PreviewScale { get; }

    /// <summary>Format the edited copy is saved in: the original's, or JPG/PNG when that can't be written well.</summary>
    public ImageFormat OutputFormat => Source.Kind switch
    {
        ImageKind.Heic => ImageFormat.Jpeg,
        ImageKind.Psd or ImageKind.Ico => ImageFormat.Png,
        _ => Source,
    };

    /// <summary>Slow for big photos (decoding a 24 MP HEIC takes a moment): call it off the UI thread.</summary>
    public static EditableImage Load(string path)
    {
        ImageFormat source = ImageReader.Identify(path);
        if (source.Kind == ImageKind.Png && ApngReader.IsAnimated(path))
            throw new NothingToDoException("Animated PNGs can't be edited yet. Convert it to GIF or WebP first.");

        MagickImageCollection frames = ImageReader.Read(path, source, allFrames: source.SupportsAnimation);
        try
        {
            if (frames.Count > 1)
                frames.Coalesce();
            return new EditableImage(path, source, frames, CreatePreview(frames[0]));
        }
        catch
        {
            frames.Dispose();
            throw;
        }
    }

    /// <summary>Applies an edit to every frame and saves a copy as "name (suffix).ext" next to the original.</summary>
    public string Save(string suffix, Action<MagickImageCollection> edit)
    {
        edit(Frames);
        ImageFormat target = OutputFormat;
        string extension = target == Source ? System.IO.Path.GetExtension(Path) : target.Extension;
        bool lossless = Source.Kind == ImageKind.WebP && ImageTools.WebPIsLossless(Path);
        IReadOnlySet<string>? chunks = Source.Kind == ImageKind.Png ? PngChunks.TypesIn(Path) : null;
        return OutputFile.Save(Path, suffix, extension,
            output => ImageWriter.Write(Frames, Source, target, output, lossless, chunks));
    }

    /// <summary>Saves a picture composed elsewhere (e.g. the image on a background): photos as JPG, the rest as PNG.</summary>
    public string SaveComposite(string suffix, byte[] png)
    {
        bool photo = Source.Kind is ImageKind.Jpeg or ImageKind.Heic or ImageKind.Avif or ImageKind.Jxl
            || (Source.Kind == ImageKind.WebP && !ImageTools.WebPIsLossless(Path));
        ImageFormat target = photo ? (Source.CanWrite ? Source : ImageFormat.Jpeg) : ImageFormat.Png;

        using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
        ImageWriter.Prepare(image, ImageFormat.Png, target);
        byte[] encoded = image.ToByteArray(target.MagickFormat);
        if (target.Kind == ImageKind.Png)
            encoded = PngMetadata.RemoveWriterChunks(encoded, sourceChunks: null);
        string extension = target == Source ? System.IO.Path.GetExtension(Path) : target.Extension;
        return OutputFile.Save(Path, suffix, extension, output => output.Write(encoded));
    }

    /// <summary>The first frame at full size, for editors that recompose the picture (frozen).</summary>
    public BitmapSource FullSizeBitmap() => ToBitmap(Frames[0]);

    public void Dispose() => Frames.Dispose();

    private static BitmapSource CreatePreview(IMagickImage<byte> frame)
    {
        using IMagickImage<byte> preview = frame.Clone();
        preview.Resize(new MagickGeometry(PreviewMaxSide, PreviewMaxSide) { Greater = true });
        return ToBitmap(preview);
    }

    private static BitmapSource ToBitmap(IMagickImage<byte> image)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(image.ToByteArray(MagickFormat.Png32));
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
