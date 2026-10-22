using Morphdrop.Actions;

namespace Morphdrop.Pdf;

/// <summary>A slice on the Tools page for PDFs that runs in the background (split, page numbers, compress).</summary>
internal sealed class PdfToolAction : IFileAction
{
    private readonly Func<string, string> _run;

    public PdfToolAction(string label, string glyph, string description, Func<string, string> run)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _run = run;
    }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => IsPdf(path);

    public string Run(string inputPath) => _run(inputPath);

    public static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
}
