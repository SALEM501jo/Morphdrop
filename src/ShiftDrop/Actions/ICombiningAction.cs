namespace ShiftDrop.Actions;

/// <summary>An action that, given several files at once, makes one output from all of them (e.g. one PDF).</summary>
internal interface ICombiningAction : IFileAction
{
    bool Combines(IReadOnlyList<string> inputs);

    /// <summary>Shown in the hub instead of <see cref="IFileAction.Description"/> when several files are dragged.</summary>
    string CombinedDescription(int count);

    /// <summary>Runs on a background thread and returns the path of the single file it created.</summary>
    string RunAll(IReadOnlyList<string> inputs);
}
