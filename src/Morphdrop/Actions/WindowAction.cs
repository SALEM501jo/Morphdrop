namespace Morphdrop.Actions;

/// <summary>
/// A slice that opens a window with all the dropped files (the PDF builder, the page organizer), instead of running
/// in the background. The window then hands its work to the <see cref="ActionRunner"/>, so it is reported as usual.
/// </summary>
internal sealed class WindowAction : ICombiningAction
{
    private readonly Func<string, bool> _appliesTo;
    private readonly Func<int, string>? _combinedDescription;
    private readonly Action<WindowAction, IReadOnlyList<string>, ActionRunner> _open;

    public WindowAction(string label, string glyph, string description, Func<string, bool> appliesTo,
        Func<int, string>? combinedDescription, Action<WindowAction, IReadOnlyList<string>, ActionRunner> open)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _appliesTo = appliesTo;
        _combinedDescription = combinedDescription;
        _open = open;
    }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => _appliesTo(path);

    public bool Combines(IReadOnlyList<string> inputs) => _combinedDescription is not null && inputs.Count > 1;

    public string CombinedDescription(int count) => _combinedDescription?.Invoke(count) ?? Description;

    /// <summary>Call on the UI thread, after the drop has returned.</summary>
    public void Open(IReadOnlyList<string> files, ActionRunner runner)
    {
        string[] inputs = files.Where(file => File.Exists(file) && AppliesTo(file)).ToArray();
        if (inputs.Length > 0)
            _open(this, inputs, runner);
    }

    public string Run(string inputPath) => throw new InvalidOperationException("This slice opens a window; it doesn't run on its own.");

    public string RunAll(IReadOnlyList<string> inputs) => Run(inputs[0]);
}
