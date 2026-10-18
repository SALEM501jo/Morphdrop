namespace ShiftDrop.Actions;

internal sealed record ActionResult(
    IFileAction Action,
    IReadOnlyList<(string Input, string Output)> Outputs,
    IReadOnlyList<(string Input, string Reason)> Skipped,
    IReadOnlyList<(string Input, Exception Error)> Errors);

/// <summary>
/// Runs an action on the dropped files in the background (a drop handler must return quickly, or the
/// drag source freezes) and reports back on the thread that started it.
/// </summary>
internal sealed class ActionRunner
{
    public event Action<ActionResult>? Completed;

    public void Run(IFileAction action, IReadOnlyList<string> files)
    {
        string[] inputs = files.Where(file => File.Exists(file) && action.AppliesTo(file)).ToArray();
        if (inputs.Length == 0)
            return;

        SynchronizationContext? caller = SynchronizationContext.Current;
        Task.Run(() =>
        {
            var outputs = new List<(string, string)>();
            var skipped = new List<(string, string)>();
            var errors = new List<(string, Exception)>();
            foreach (string input in inputs)
            {
                try
                {
                    string output = action.Run(input);
                    outputs.Add((input, output));
                    Log.Info($"{action.Description}: '{input}' -> '{output}'");
                }
                catch (NothingToDoException nothing)
                {
                    skipped.Add((input, nothing.Message));
                    Log.Info($"{action.Description}: nothing to do for '{input}': {nothing.Message}");
                }
                catch (Exception ex)
                {
                    errors.Add((input, ex));
                    Log.Error($"{action.Description} failed for '{input}'", ex);
                }
            }

            var result = new ActionResult(action, outputs, skipped, errors);
            if (caller is null)
                Completed?.Invoke(result);
            else
                caller.Post(_ => Completed?.Invoke(result), null);
        });
    }
}
