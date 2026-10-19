namespace Morphdrop;

internal static class AppPaths
{
    /// <summary>
    /// %LOCALAPPDATA%\Morphdrop: settings, the log and the downloaded FFmpeg. Before 1.0 the app was called ShiftDrop;
    /// the first start moves that folder here, so nothing is lost and FFmpeg isn't downloaded again. If it can't be
    /// moved (say the old version is still running), the old folder keeps being used.
    /// </summary>
    public static string DataFolder { get; } =
        Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom
            ? custom
            : Prepare(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Points the data folder elsewhere; the tests use it so they never touch the user's real one.</summary>
    public const string OverrideVariable = "MORPHDROP_DATA";

    internal static string Prepare(string localAppData)
    {
        string folder = Path.Combine(localAppData, "Morphdrop");
        string previous = Path.Combine(localAppData, "ShiftDrop");
        if (Directory.Exists(folder) || !Directory.Exists(previous))
            return folder;

        try
        {
            Directory.Move(previous, folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return previous;
        }
    }
}
