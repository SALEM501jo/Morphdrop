namespace Morphdrop.Media;

/// <summary>
/// Where Morphdrop's FFmpeg comes from. It isn't shipped with the app: on first use, the "essentials" build by
/// gyan.dev is downloaded from its GitHub mirror and checked against a pinned SHA-256 before it is ever run.
/// </summary>
internal static class Ffmpeg
{
    public const string Version = "9.0.2";
    public const long DownloadSize = 114_768_076;
    public const string DownloadSha256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba";
    public static readonly Uri DownloadUrl = new($"https://github.com/GyanD/codexffmpeg/releases/download/{Version}/ffmpeg-{Version}-essentials_build.zip");

    /// <summary>Points at another ffmpeg.exe (tests, development).</summary>
    public const string OverrideVariable = "MORPHDROP_FFMPEG";

    public static string InstallFolder { get; } = Path.Combine(
        AppPaths.DataFolder, "ffmpeg", Version);

    public static string InstalledPath => Path.Combine(InstallFolder, "ffmpeg.exe");

    public static bool IsAvailable => Find() is not null;

    /// <summary>The override variable, then Morphdrop's own copy, then an ffmpeg.exe on PATH.</summary>
    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom && File.Exists(custom))
            return custom;
        if (File.Exists(InstalledPath))
            return InstalledPath;

        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry; skip it.
            }
        }

        return null;
    }
}
