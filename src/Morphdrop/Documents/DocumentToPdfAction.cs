using Morphdrop.Actions;

namespace Morphdrop.Documents;

/// <summary>The PDF slice for Word, Excel, PowerPoint and OpenDocument files.</summary>
internal sealed class DocumentToPdfAction : IFileAction
{
    public string Label => "PDF";

    public string? Glyph => null;

    public string Description => "Save as PDF";

    public bool AppliesTo(string path) => DocumentConverter.CanConvert(path);

    public string Run(string inputPath) => DocumentConverter.ToPdf(inputPath);
}
