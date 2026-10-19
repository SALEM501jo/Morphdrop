using Microsoft.Win32;

namespace Morphdrop.Tray;

/// <summary>
/// "Start with Windows": a value under the current user's Run key, so no admin rights are needed. It only counts
/// as enabled when it points at this copy of Morphdrop; enabling a moved copy rewrites the path.
/// </summary>
internal sealed class AutoStart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Morphdrop";

    private readonly string _keyPath;
    private readonly string _command;

    public AutoStart(string executablePath, string keyPath = RunKey)
    {
        _keyPath = keyPath;
        _command = $"\"{executablePath}\"";
    }

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(ValueName) is string command && string.Equals(command, _command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath);
        if (enabled)
            key.SetValue(ValueName, _command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
