using System.Threading.Channels;

namespace Morphdrop;

/// <summary>
/// Appends diagnostics to %LOCALAPPDATA%\Morphdrop\morphdrop.log. Writing happens on a background task,
/// so logging is safe from the mouse-hook thread.
/// </summary>
internal static class Log
{
    private const long MaxSizeBytes = 1_000_000;

    private static readonly Channel<string> Lines =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private static readonly Task Writer;

    static Log()
    {
        string folder = AppPaths.DataFolder;
        FilePath = Path.Combine(folder, "morphdrop.log");
        try
        {
            Directory.CreateDirectory(folder);
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxSizeBytes)
                file.MoveTo(Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort.
        }

        Writer = Task.Run(WriteLinesAsync);
    }

    public static string FilePath { get; }

    /// <summary>Off in the tests, so test runs don't fill the user's real log.</summary>
    public static bool Enabled { get; set; } = true;

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}: {error}");

    /// <summary>Writes out pending lines; call once at exit.</summary>
    public static void Flush()
    {
        Lines.Writer.TryComplete();
        Writer.Wait(TimeSpan.FromSeconds(1));
    }

    private static void Write(string level, string message)
    {
        if (Enabled)
            Lines.Writer.TryWrite($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
    }

    private static async Task WriteLinesAsync()
    {
        while (await Lines.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                using var file = new StreamWriter(FilePath, append: true);
                while (Lines.Reader.TryRead(out string? line))
                    file.Write(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging is best effort.
            }
        }
    }
}
