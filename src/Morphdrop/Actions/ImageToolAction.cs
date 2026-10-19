using Morphdrop.Imaging;

namespace Morphdrop.Actions;

internal sealed class ImageToolAction : IFileAction
{
    private readonly Func<ImageFormat, bool> _supports;
    private readonly Func<string, string> _run;

    public ImageToolAction(string label, string glyph, string description, Func<ImageFormat, bool> supports, Func<string, string> run)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _supports = supports;
        _run = run;
    }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => ImageFormat.FromPath(path) is { CanRead: true } format && _supports(format);

    public string Run(string inputPath) => _run(inputPath);
}
