namespace Morphdrop.Actions;

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
    private readonly TimeSpan _slowAfter;

    /// <param name="slowAfter">How long a job may run before <see cref="Working"/> is raised. Default 0.8 s.</param>
    public ActionRunner(TimeSpan? slowAfter = null) => _slowAfter = slowAfter ?? TimeSpan.FromMilliseconds(800);

    /// <summary>Raised once when a job is still running after a moment, so the user knows something is happening.</summary>
    public event Action<IFileAction, IReadOnlyList<string>>? Working;

    public event Action<ActionResult>? Completed;

    /// <returns>The background job, or null when none of the files applied.</returns>
    public Task? Run(IFileAction action, IReadOnlyList<string> files)
    {
        string[] inputs = files.Where(file => File.Exists(file) && action.AppliesTo(file)).ToArray();
        if (inputs.Length == 0)
            return null;

        // Several images dropped on PDF make one file: run once with all of them.
        if (action is ICombiningAction combining && combining.Combines(inputs))
            return Start(action, inputs, new[] { inputs[0] }, _ => combining.RunAll(inputs));

        return Start(action, inputs, inputs, action.Run);
    }

    /// <summary>Runs work prepared elsewhere (an editor's save) with the same notifications as a dropped action.</summary>
    public Task RunJob(IFileAction action, string input, Func<string> job) =>
        Start(action, new[] { input }, new[] { input }, _ => job());

    private Task Start(IFileAction action, string[] inputs, string[] steps, Func<string, string> run)
    {
        SynchronizationContext? caller = SynchronizationContext.Current;
        void Report(Action raise)
        {
            if (caller is null)
                raise();
            else
                caller.Post(_ => raise(), null);
        }

        var finished = new CancellationTokenSource();
        Task.Delay(_slowAfter, finished.Token).ContinueWith(
            _ => Report(() => Working?.Invoke(action, inputs)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

        return Task.Run(() =>
        {
            var outputs = new List<(string, string)>();
            var skipped = new List<(string, string)>();
            var errors = new List<(string, Exception)>();
            foreach (string input in steps)
            {
                string what = steps.Length < inputs.Length ? $"{inputs.Length} files" : $"'{input}'";
                try
                {
                    string output = run(input);
                    outputs.Add((input, output));
                    Log.Info($"{action.Description}: {what} -> '{output}'");
                }
                catch (NothingToDoException nothing)
                {
                    skipped.Add((input, nothing.Message));
                    Log.Info($"{action.Description}: nothing to do for {what}: {nothing.Message}");
                }
                catch (Exception ex)
                {
                    errors.Add((input, ex));
                    Log.Error($"{action.Description} failed for {what}", ex);
                }
            }

            finished.Cancel();
            var result = new ActionResult(action, outputs, skipped, errors);
            Report(() => Completed?.Invoke(result));
        });
    }
}
