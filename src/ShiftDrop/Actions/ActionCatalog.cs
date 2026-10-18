using ShiftDrop.Imaging;

namespace ShiftDrop.Actions;

/// <summary>One ring of the wheel. Pressing Shift again while the wheel is open switches to the next page.</summary>
internal sealed record ActionPage(string Name, IReadOnlyList<IFileAction> Actions);

internal static class ActionCatalog
{
    // Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10) code points.
    private const string CompressGlyph = "";
    private const string HalfSizeGlyph = "";
    private const string FitGlyph = "";
    private const string PrivacyGlyph = "";

    private static readonly IFileAction[] ConvertActions =
        ImageFormat.WheelTargets.Select(format => (IFileAction)new ConvertImageAction(format)).ToArray();

    private static readonly IFileAction[] ToolActions =
    {
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
    };

    /// <summary>The wheel's pages for a drop, picked from the first file. Folders and unknown types get none.</summary>
    public static IReadOnlyList<ActionPage> For(IReadOnlyList<string> paths)
    {
        string? first = paths.FirstOrDefault(File.Exists);
        return first is null ? Array.Empty<ActionPage>() : ForFileName(first);
    }

    /// <summary>The pages for a file of this name. Only the extension matters, so the file needn't exist.</summary>
    public static IReadOnlyList<ActionPage> ForFileName(string path)
    {
        var pages = new List<ActionPage>();
        AddPage(pages, "Convert", ConvertActions, path);
        AddPage(pages, "Tools", ToolActions, path);
        return pages;
    }

    private static void AddPage(List<ActionPage> pages, string name, IFileAction[] actions, string path)
    {
        IFileAction[] applicable = actions.Where(action => action.AppliesTo(path)).ToArray();
        if (applicable.Length > 0)
            pages.Add(new ActionPage(name, applicable));
    }

    // Icons hold several sizes on purpose, and PSD layers would be flattened.
    private static bool CanResize(ImageFormat format) => format.Kind is not (ImageKind.Ico or ImageKind.Psd);
}
