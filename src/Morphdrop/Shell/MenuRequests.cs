using System.IO.Pipes;
using System.Windows.Threading;

namespace Morphdrop.Shell;

/// <summary>
/// Carries files from the right-click menu to the running Morphdrop. Explorer starts one short-lived process per
/// selected file; each sends its path down a named pipe that only this Windows user can open, and the running
/// Morphdrop gathers the paths that arrive close together into one request.
/// </summary>
internal sealed class MenuRequests : IDisposable
{
    private static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(400);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _pending = new();
    private readonly DispatcherTimer _timer;
    private readonly string _pipeName;

    /// <param name="pipeName">Another pipe than the real one (tests, so they never reach a running Morphdrop).</param>
    public MenuRequests(Dispatcher dispatcher, string? pipeName = null)
    {
        _pipeName = pipeName ?? DefaultPipeName;
        _timer = new DispatcherTimer(Gather, DispatcherPriority.Normal, (_, _) => Flush(), dispatcher) { IsEnabled = false };
    }

    /// <summary>Raised on the UI thread with the files of one right-click (one or many).</summary>
    public event Action<string[]>? Requested;

    private static string DefaultPipeName => $"Morphdrop.Menu.{Environment.UserName}";

    public void Start() => Task.Run(ListenAsync);

    /// <summary>From the short-lived process: hands the paths to the running Morphdrop. False when none is running.</summary>
    public static bool TrySend(IReadOnlyList<string> paths, string? pipeName = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? DefaultPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(1500);
            using var writer = new StreamWriter(pipe);
            foreach (string path in paths)
                writer.WriteLine(path);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Adds paths as if they had come down the pipe (the first instance's own right-click).</summary>
    public void Receive(IEnumerable<string> paths)
    {
        _pending.AddRange(paths.Where(path => File.Exists(path) || Directory.Exists(path)));
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _timer.Stop();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe);
                var paths = new List<string>();
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.Length > 0 && paths.Count < 1000)
                        paths.Add(line);
                }

                _timer.Dispatcher.Invoke(() => Receive(paths));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("The right-click menu's pipe failed", ex);
                await Task.Delay(1000);
            }
        }
    }

    private void Flush()
    {
        _timer.Stop();
        if (_pending.Count == 0)
            return;

        string[] paths = _pending.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _pending.Clear();
        Requested?.Invoke(paths);
    }
}
