namespace Morphdrop.Actions;

/// <summary>A slice that runs a function on each dropped file (in the background) and returns what it saved.</summary>
internal sealed class FileAction : IFileAction
{
    private readonly Func<string, bool> _appliesTo;
    private readonly Func<string, string> _run;

    public FileAction(string label, string? glyph, string description, Func<string, bool> appliesTo, Func<string, string> run)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _appliesTo = appliesTo;
        _run = run;
    }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => _appliesTo(path);

    public string Run(string inputPath) => _run(inputPath);
}
