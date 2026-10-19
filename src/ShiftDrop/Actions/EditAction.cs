using ShiftDrop.Imaging;

namespace ShiftDrop.Actions;

internal enum EditKind { Crop, Background, Redact }

/// <summary>A slice on the Edit page: dropping a file on it opens an editor window instead of running at once.</summary>
internal sealed class EditAction : IFileAction
{
    public EditAction(EditKind kind, string label, string glyph, string description)
    {
        Kind = kind;
        Label = label;
        Glyph = glyph;
        Description = description;
    }

    public EditKind Kind { get; }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    // Icons hold several pictures of different sizes; editing one of them makes little sense.
    public bool AppliesTo(string path) => ImageFormat.FromPath(path) is { CanRead: true } format && format.Kind != ImageKind.Ico;

    public string Run(string inputPath) =>
        throw new InvalidOperationException("Edit actions open an editor window; they don't run on their own.");
}
