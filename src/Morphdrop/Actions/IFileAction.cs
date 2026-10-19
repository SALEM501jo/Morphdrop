namespace Morphdrop.Actions;

/// <summary>Something a slice of the wheel does to a dropped file.</summary>
internal interface IFileAction
{
    /// <summary>Short text on the slice, e.g. "PNG".</summary>
    string Label { get; }

    /// <summary>A Segoe Fluent Icons glyph shown above the label, or null for a text-only slice.</summary>
    string? Glyph { get; }

    /// <summary>Shown in the hub while hovering the slice, e.g. "Convert to PNG".</summary>
    string Description { get; }

    bool AppliesTo(string path);

    /// <summary>
    /// Runs on a background thread and returns the path of the file it created. Throws
    /// <see cref="NothingToDoException"/> when the file needs no change.
    /// </summary>
    string Run(string inputPath);
}
