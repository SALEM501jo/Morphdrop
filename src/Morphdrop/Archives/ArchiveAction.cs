using Morphdrop.Actions;

namespace Morphdrop.Archives;

/// <summary>The Tools page's ZIP slice (any files or folders, several make one ZIP) and Extract slice (archives).</summary>
internal sealed class ArchiveAction : ICombiningAction
{
    private readonly bool _extract;

    private ArchiveAction(bool extract, string label, string glyph, string description)
    {
        _extract = extract;
        Label = label;
        Glyph = glyph;
        Description = description;
    }

    public static ArchiveAction Zip(string glyph) => new(false, "ZIP", glyph, "Put it in a ZIP");

    public static ArchiveAction Extract(string glyph) => new(true, "Extract", glyph, "Unpack it into a folder");

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => _extract
        ? Archives.IsArchive(path)
        : !Archives.IsArchive(path);

    public string Run(string inputPath) => _extract ? Archives.Extract(inputPath) : Archives.Zip(new[] { inputPath });

    public bool Combines(IReadOnlyList<string> inputs) => !_extract && inputs.Count > 1;

    public string CombinedDescription(int count) => $"Put {count} items in one ZIP";

    public string RunAll(IReadOnlyList<string> inputs) => Archives.Zip(inputs);
}
