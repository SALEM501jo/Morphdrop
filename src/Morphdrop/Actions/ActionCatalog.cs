using Morphdrop.Archives;
using Morphdrop.Imaging;
using Morphdrop.Documents;
using Morphdrop.Media;
using Morphdrop.Pdf;
using Morphdrop.Text;

namespace Morphdrop.Actions;

/// <summary>One ring of the wheel. Pressing Shift again while the wheel is open switches to the next page.</summary>
internal sealed record ActionPage(string Name, IReadOnlyList<IFileAction> Actions);

internal static class ActionCatalog
{
    // Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10) code points.
    private const string CompressGlyph = "";
    private const string HalfSizeGlyph = "";
    private const string FitGlyph = "";
    private const string PrivacyGlyph = "";
    private const string CropGlyph = "";
    private const string BackgroundGlyph = "";
    private const string RedactGlyph = "";
    private const string PdfGlyph = "";
    private const string LockGlyph = "";
    private const string SignGlyph = "";
    private const string ZipGlyph = "\uF012";
    private const string ExtractGlyph = "\uED25";
    private const string TextGlyph = "";
    private const string OrganizeGlyph = "";
    private const string SplitGlyph = "";
    private const string NumbersGlyph = "";
    private const string CutoutGlyph = ""; // a person

    private static readonly IFileAction[] ConvertActions =
        ImageFormat.WheelTargets.Select(format => (IFileAction)new ConvertImageAction(format))
            .Concat(new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.WebP }.Select(format => new PdfToImagesAction(format)))
            .Append(TextAction.ForPdfs())
            .Concat(MediaAction.ConvertActions())
            .Append(new DocumentToPdfAction())
            .Append(TextAction.ForDocuments())
            .Append(new FileAction("CSV", null, "Save the sheets as CSV", SpreadsheetCsv.Supports, SpreadsheetCsv.Convert))
            .Concat(Fonts.FontConverter.Actions())
            .ToArray();

    private static readonly IFileAction[] ToolActions = new IFileAction[]
    {
        ArchiveAction.Extract(ExtractGlyph),
        new ImageToolAction("Compress", CompressGlyph, "Make it smaller",
            format => format.Kind is ImageKind.Jpeg or ImageKind.Png or ImageKind.WebP or ImageKind.Gif,
            ImageTools.Compress),
        new ImageToolAction("50%", HalfSizeGlyph, "Resize to half the size",
            CanResize, path => ImageTools.Resize(path, ResizeMode.Half)),
        new ImageToolAction($"Max {ImageTools.FullHd}", FitGlyph, $"Shrink to fit {ImageTools.FullHd} px",
            CanResize, path => ImageTools.Resize(path, ResizeMode.FitFullHd)),
        new ImageToolAction("Clean", PrivacyGlyph, "Remove location & camera info",
            format => format.Kind is ImageKind.Jpeg or ImageKind.Png,
            ImageTools.RemoveMetadata),
        TextAction.ForPictures(TextGlyph),

        // PDFs: the organizer opens a window (several PDFs dropped together are merged in it); the rest run at once.
        new WindowAction("Organize", OrganizeGlyph, "Reorder, turn or delete pages", PdfToolAction.IsPdf,
            count => $"Merge {count} PDFs (you pick the order)", PageOrganizerWindow.Open),
        new PdfToolAction("Split", SplitGlyph, "Save each page as its own PDF", PdfTools.Split),
        new PdfToolAction("Numbers", NumbersGlyph, "Add page numbers", PdfTools.AddPageNumbers),
        new PdfToolAction("Compress", CompressGlyph, "Make the PDF smaller", PdfTools.Compress),
        new WindowAction("Sign", SignGlyph, "Sign it or fill it in", PdfToolAction.IsPdf, null, PdfSignWindow.Open),
        new WindowAction("Password", LockGlyph, "Lock it with a password, or unlock it", PdfToolAction.IsPdf, null, PdfPasswordWindow.Open),
    }.Concat(MediaAction.ToolActions(CompressGlyph)).Append(ArchiveAction.Zip(ZipGlyph)).ToArray();

    private static readonly IFileAction[] EditActions =
    {
        new EditAction(EditKind.Crop, "Crop", CropGlyph, "Crop it"),
        new EditAction(EditKind.Background, "Background", BackgroundGlyph, "Put it on a background"),
        new EditAction(EditKind.Redact, "Redact", RedactGlyph, "Hide private details"),
        new EditAction(EditKind.RemoveBackground, "Remove BG", CutoutGlyph, "Remove the background"),
        new WindowAction("PDF + text", PdfGlyph, "Make a PDF with text on its pages", IsPicture,
            count => $"Make a {count}-page PDF with text", PictureDocumentWindow.Open),
    };

    private static bool IsPicture(string path) =>
        ImageFormat.FromPath(path) is { CanRead: true } format && format.Kind is not (ImageKind.Pdf or ImageKind.Ico);

    /// <summary>The wheel's pages for a drop, picked from the first item. Anything (even a folder) can at least be zipped.</summary>
    public static IReadOnlyList<ActionPage> For(IReadOnlyList<string> paths, IReadOnlySet<string>? enabledPages = null)
    {
        string? first = paths.FirstOrDefault(path => File.Exists(path) || Directory.Exists(path));
        if (first is null)
            return Array.Empty<ActionPage>();

        IReadOnlyList<ActionPage> pages = ForFileName(first);
        return enabledPages is null ? pages : pages.Where(page => enabledPages.Contains(page.Name)).ToArray();
    }

    /// <summary>The pages for a file of this name. Only the extension matters, so the file needn't exist.</summary>
    public static IReadOnlyList<ActionPage> ForFileName(string path)
    {
        var pages = new List<ActionPage>();
        AddPage(pages, "Convert", ConvertActions, path);
        AddPage(pages, "Tools", ToolActions, path);
        AddPage(pages, "Edit", EditActions, path);
        return pages;
    }

    private static void AddPage(List<ActionPage> pages, string name, IFileAction[] actions, string path)
    {
        IFileAction[] applicable = actions.Where(action => action.AppliesTo(path)).ToArray();
        if (applicable.Length > 0)
            pages.Add(new ActionPage(name, applicable));
    }

    // Icons hold several sizes on purpose, and PSD layers would be flattened.
    private static bool CanResize(ImageFormat format) => format.Kind is not (ImageKind.Ico or ImageKind.Psd or ImageKind.Svg);
}
