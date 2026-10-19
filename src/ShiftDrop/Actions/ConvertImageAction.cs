using ShiftDrop.Imaging;

namespace ShiftDrop.Actions;

/// <summary>Converts images to a format; several images dropped on PDF become one PDF, a page each.</summary>
internal sealed class ConvertImageAction : ICombiningAction
{
    private readonly ImageFormat _target;

    public ConvertImageAction(ImageFormat target) => _target = target;

    public string Label => _target.Name;

    public string? Glyph => null;

    public string Description => $"Convert to {_target.Name}";

    public bool AppliesTo(string path) => ImageFormat.FromPath(path) is { CanRead: true } source && source != _target;

    public string Run(string inputPath) => ImageConverter.Convert(inputPath, _target);

    public bool Combines(IReadOnlyList<string> inputs) => _target.Kind == ImageKind.Pdf && inputs.Count > 1;

    public string CombinedDescription(int count) => _target.Kind == ImageKind.Pdf ? $"Combine into one {count}-page PDF" : Description;

    public string RunAll(IReadOnlyList<string> inputs) => ImageConverter.CombineToPdf(inputs);
}
