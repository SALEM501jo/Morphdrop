using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShiftDrop.Media;

/// <summary>What FFmpeg reports about a file's first video and audio streams.</summary>
internal sealed record MediaInfo(TimeSpan? Duration, int Width, int Height, bool HasVideo, bool HasAudio)
{
    public int ShortSide => Math.Min(Width, Height);

    public int LongSide => Math.Max(Width, Height);
}

/// <summary>Runs ffmpeg.exe without a window, at below-normal priority, and reports its progress.</summary>
internal static partial class FfmpegRunner
{
    /// <summary>Raised from the worker thread with the job's description and how far it is (0–1).</summary>
    public static event Action<string, double>? ProgressChanged;

    public static string Executable =>
        Ffmpeg.Find() ?? throw new InvalidOperationException("FFmpeg isn't installed yet.");

    /// <summary>Reads duration, size and streams (ffmpeg -i lists them and exits; no ffprobe needed).</summary>
    public static MediaInfo Probe(string input)
    {
        (_, string log) = Execute(new[] { "-hide_banner", "-i", input }, onProgressLine: null);
        return ParseInfo(log);
    }

    internal static MediaInfo ParseInfo(string log)
    {
        TimeSpan? duration = DurationPattern().Match(log) is { Success: true } d
            && TimeSpan.TryParse(d.Groups[1].Value, CultureInfo.InvariantCulture, out TimeSpan parsed) ? parsed : null;

        Match video = VideoPattern().Match(log);
        int width = video.Success ? int.Parse(video.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        int height = video.Success ? int.Parse(video.Groups[2].Value, CultureInfo.InvariantCulture) : 0;

        // Phone videos are often stored sideways with a rotation tag; FFmpeg turns them upright when encoding.
        if (RotationPattern().Match(log) is { Success: true } rotation
            && Math.Abs(double.Parse(rotation.Groups[1].Value, CultureInfo.InvariantCulture)) % 180 == 90)
        {
            (width, height) = (height, width);
        }

        return new MediaInfo(duration, width, height, video.Success, log.Contains(": Audio:", StringComparison.Ordinal));
    }

    /// <summary>Encodes; throws with FFmpeg's own last error line when it fails.</summary>
    public static void Run(IReadOnlyList<string> arguments, TimeSpan? duration, string description)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats" };
        args.AddRange(arguments);

        ProgressChanged?.Invoke(description, 0);
        (int exitCode, string log) = Execute(args, line =>
        {
            if (duration is { TotalMilliseconds: > 0 } total && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan("out_time_us=".Length), out long microseconds))
            {
                ProgressChanged?.Invoke(description, Math.Clamp(microseconds / 1000.0 / total.TotalMilliseconds, 0, 1));
            }
        });
        ProgressChanged?.Invoke(description, 1);

        if (exitCode != 0)
        {
            string reason = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? $"exit code {exitCode}";
            throw new InvalidOperationException($"FFmpeg couldn't do it: {reason}");
        }
    }

    private static (int ExitCode, string Log) Execute(IEnumerable<string> arguments, Action<string>? onProgressLine)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg didn't start.");
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal; // keep the PC responsive while encoding
        }
        catch (InvalidOperationException)
        {
            // It already exited (a quick probe).
        }

        Task<string> errors = process.StandardError.ReadToEndAsync();
        while (process.StandardOutput.ReadLine() is { } line)
            onProgressLine?.Invoke(line);
        process.WaitForExit();
        return (process.ExitCode, errors.GetAwaiter().GetResult());
    }

    [GeneratedRegex(@"Duration: (\d+:\d\d:\d\d(?:\.\d+)?)")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"Stream #\d+:\d+.*?: Video: .*?, (\d{2,5})x(\d{2,5})")]
    private static partial Regex VideoPattern();

    [GeneratedRegex(@"rotation of (-?\d+(?:\.\d+)?) degrees")]
    private static partial Regex RotationPattern();
}
