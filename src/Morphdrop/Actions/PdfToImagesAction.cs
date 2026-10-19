using ImageMagick;
using Morphdrop.Imaging;

namespace Morphdrop.Actions;

/// <summary>
/// Saves every page of a PDF as an image: a one-page PDF becomes "name.png" next to it, a longer one a folder
/// "name (pages)" holding "page 01.png", "page 02.png"…
/// </summary>
internal sealed class PdfToImagesAction : IFileAction
{
    private readonly ImageFormat _target;

    public PdfToImagesAction(ImageFormat target) => _target = target;

    public string Label => _target.Name;

    public string? Glyph => null;

    public string Description => $"Save the pages as {_target.Name}";

    public bool AppliesTo(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public string Run(string inputPath)
    {
        if (!PdfPages.IsPdf(inputPath))
            throw new NotSupportedException($"{Path.GetFileName(inputPath)} isn't a PDF file.");

        string? folder = null;
        string? single = null;
        try
        {
            PdfPages.Render(inputPath, (index, count, png) =>
            {
                byte[] image = Encode(png);
                if (count == 1)
                {
                    single = OutputFile.Save(inputPath, suffix: null, _target.Extension, output => output.Write(image));
                    return;
                }

                folder ??= OutputFile.CreateUniqueFolder(inputPath, "pages");
                string digits = new string('0', Math.Max(2, count.ToString().Length));
                File.WriteAllBytes(Path.Combine(folder, $"page {(index + 1).ToString(digits)}{_target.Extension}"), image);
            });
        }
        catch when (folder is not null)
        {
            Directory.Delete(folder, recursive: true); // don't leave half the pages behind
            throw;
        }

        return single ?? folder ?? throw new NothingToDoException("The PDF has no pages.");
    }

    private byte[] Encode(byte[] png)
    {
        if (_target.Kind == ImageKind.Png)
            return png;

        using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
        ImageWriter.Prepare(image, ImageFormat.Png, _target);
        return image.ToByteArray(_target.MagickFormat);
    }
}
