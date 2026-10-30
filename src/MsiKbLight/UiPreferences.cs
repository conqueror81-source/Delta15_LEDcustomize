using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsiKbLight;

/// <summary>Per-user window preferences (%AppData%\MsiKbLight\ui.json), separate from the machine-wide lighting config.</summary>
internal sealed class UiPreferences
{
    public int SchemaVersion { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiLanguage Language { get; set; } = UiLanguage.Auto;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MsiKbLight", "ui.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Never fails: a missing or broken file just means defaults.</summary>
    public static UiPreferences Load()
    {
        try
        {
            var prefs = File.Exists(FilePath) ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FilePath), JsonOptions) : null;
            if (prefs is not null && Enum.IsDefined(prefs.Theme) && Enum.IsDefined(prefs.Language))
                return prefs;
        }
        catch (Exception)
        {
            // Fall back to defaults.
        }
        return new UiPreferences();
    }

    public void Save()
    {
        try
        {
            FileUtil.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Preferences are a convenience; not worth an error dialog.
        }
    }
}
