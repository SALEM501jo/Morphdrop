namespace ShiftDrop.Actions;

/// <summary>Something a slice of the wheel does to a dropped file.</summary>
internal interface IFileAction
{
    /// <summary>Short text on the slice, e.g. "PNG".</summary>
    string Label { get; }

    /// <summary>Shown in the hub while hovering the slice, e.g. "Convert to PNG".</summary>
    string Description { get; }

    bool AppliesTo(string path);

    /// <summary>Runs on a background thread. Returns the path of the file it created.</summary>
    string Run(string inputPath);
}
