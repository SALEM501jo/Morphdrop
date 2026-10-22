namespace Morphdrop.Cutout;

/// <summary>
/// The AI model that finds the subject of a photo: IS-Net, "general use" weights (DIS, Apache-2.0 license), as exported
/// to ONNX by the rembg project. It takes about 2 s on an ordinary laptop CPU. It isn't shipped with the app: it is downloaded once, when the user asks for it, and checked
/// against a pinned SHA-256 before it is ever loaded. It runs on the user's PC; no picture leaves it.
/// </summary>
internal static class CutoutModel
{
    public const string Name = "IS-Net";
    public const string FileName = "isnet-general-use.onnx";
    public const long DownloadSize = 178_648_008;
    public const string DownloadSha256 = "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a";
    public static readonly Uri DownloadUrl = new($"https://github.com/danielgatis/rembg/releases/download/v0.0.0/{FileName}");

    /// <summary>Points at another copy of the model (tests, development).</summary>
    public const string OverrideVariable = "MORPHDROP_CUTOUT_MODEL";

    public static string InstallFolder { get; } = Path.Combine(AppPaths.DataFolder, "models", "isnet-general");

    public static string InstalledPath => Path.Combine(InstallFolder, FileName);

    public static bool IsAvailable => Find() is not null;

    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom && File.Exists(custom))
            return custom;
        return File.Exists(InstalledPath) ? InstalledPath : null;
    }

    public static Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        VerifiedDownload.DownloadAsync(DownloadUrl, InstalledPath, DownloadSize, DownloadSha256, progress, cancellationToken);
}
