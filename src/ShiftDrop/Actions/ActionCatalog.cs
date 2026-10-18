using ShiftDrop.Imaging;

namespace ShiftDrop.Actions;

internal static class ActionCatalog
{
    private static readonly IFileAction[] Actions =
        ImageFormat.Writable.Select(format => (IFileAction)new ConvertImageAction(format)).ToArray();

    /// <summary>The wheel's slices for a drop, picked from the first file. Folders and unknown types get none.</summary>
    public static IReadOnlyList<IFileAction> For(IReadOnlyList<string> paths)
    {
        string? first = paths.FirstOrDefault(File.Exists);
        return first is null ? Array.Empty<IFileAction>() : Actions.Where(action => action.AppliesTo(first)).ToArray();
    }
}
