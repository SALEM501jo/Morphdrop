using ShiftDrop.Imaging;

namespace ShiftDrop.Actions;

internal sealed class ConvertImageAction : IFileAction
{
    private readonly ImageFormat _target;

    public ConvertImageAction(ImageFormat target) => _target = target;

    public string Label => _target.Name;

    public string Description => $"Convert to {_target.Name}";

    public bool AppliesTo(string path) => ImageFormat.CanRead(path) && ImageFormat.FromPath(path) != _target;

    public string Run(string inputPath) => ImageConverter.Convert(inputPath, _target);
}
