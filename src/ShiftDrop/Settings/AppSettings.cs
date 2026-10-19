using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftDrop.Settings;

internal enum TriggerKey { Shift, Ctrl, Alt }

internal enum ThemeChoice { System, Light, Dark }

internal enum QualityPreset { High, Balanced, Smaller }

internal enum NotificationLevel { All, ErrorsOnly }

/// <summary>Everything the Settings window changes. Missing or unknown values in the file fall back to these defaults.</summary>
internal sealed record AppSettings
{
    public TriggerKey TriggerKey { get; init; } = TriggerKey.Shift;

    public bool ConvertPage { get; init; } = true;

    public bool ToolsPage { get; init; } = true;

    public bool EditPage { get; init; } = true;

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    public QualityPreset Quality { get; init; } = QualityPreset.High;

    /// <summary>Where results go; null means next to the original file.</summary>
    public string? OutputFolder { get; init; }

    public NotificationLevel Notifications { get; init; } = NotificationLevel.All;

    public IReadOnlySet<string> EnabledPages => new HashSet<string>(
        new[] { ConvertPage ? "Convert" : null, ToolsPage ? "Tools" : null, EditPage ? "Edit" : null }.OfType<string>());

    /// <summary>Repairs combinations the window can't produce but a hand-edited file could.</summary>
    public AppSettings Normalized() =>
        ConvertPage || ToolsPage || EditPage ? this : this with { ConvertPage = true, ToolsPage = true, EditPage = true };
}

/// <summary>Reads and writes %LOCALAPPDATA%\ShiftDrop\settings.json. A damaged file never stops ShiftDrop: defaults are used.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShiftDrop", "settings.json");

    public event Action<AppSettings>? Changed;

    public AppSettings Current { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated = change(Current).Normalized();
        if (updated == Current)
            return;

        Current = updated;
        Save();
        Changed?.Invoke(updated);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error($"Settings file '{_path}' couldn't be read; using defaults", ex);
            return new AppSettings();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string partial = _path + ".part";
            File.WriteAllText(partial, JsonSerializer.Serialize(Current, Json));
            File.Move(partial, _path, overwrite: true); // never leave a half-written file
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Settings couldn't be saved to '{_path}'", ex);
        }
    }
}
