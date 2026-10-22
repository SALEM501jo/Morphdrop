using Microsoft.Win32;

namespace Morphdrop.Shell;

/// <summary>
/// "Morphdrop…" in File Explorer's right-click menu, for files and folders, for this Windows user only (no admin
/// rights). It runs <c>Morphdrop.exe --menu "file"</c>, which hands the file to the running Morphdrop and opens the
/// wheel at the cursor. On Windows 11 it sits under "Show more options" (Shift+F10), with other classic entries.
/// </summary>
internal sealed class ExplorerMenu
{
    public const string Argument = "--menu";
    private const string VerbName = "Morphdrop";
    private static readonly string[] Targets = { @"*\shell", @"Directory\shell" };

    private readonly RegistryKey _root;
    private readonly string _classesPath;

    /// <param name="root">The registry root; HKEY_CURRENT_USER by default (tests use a scratch key).</param>
    public ExplorerMenu(RegistryKey? root = null, string classesPath = @"Software\Classes")
    {
        _root = root ?? Registry.CurrentUser;
        _classesPath = classesPath;
    }

    public bool IsInstalledFor(string exePath)
    {
        using RegistryKey? command = _root.OpenSubKey($@"{_classesPath}\{Targets[0]}\{VerbName}\command");
        return command?.GetValue(null) is string line && line.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled, string exePath)
    {
        foreach (string target in Targets)
        {
            string verb = $@"{_classesPath}\{target}\{VerbName}";
            if (!enabled)
            {
                _root.DeleteSubKeyTree(verb, throwOnMissingSubKey: false);
                continue;
            }

            using RegistryKey key = _root.CreateSubKey(verb);
            key.SetValue("MUIVerb", "Morphdrop…");
            key.SetValue("Icon", $"\"{exePath}\",0");
            using RegistryKey command = key.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" {Argument} \"%1\"");
        }
    }
}
