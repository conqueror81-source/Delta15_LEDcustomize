using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsiKbLight;

internal sealed class LightSettings
{
    /// <summary>Bumped only on an incompatible change. Unknown fields are ignored, missing ones get defaults.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LightMode Mode { get; set; } = LightMode.Static;

    /// <summary>0 = slow, 1 = medium, 2 = fast.</summary>
    public int Speed { get; set; } = 1;

    /// <summary>0-10.</summary>
    public int Brightness { get; set; } = KeyboardLight.MaxBrightness;

    /// <summary>"RRGGBB" strings. ColorShift uses up to 7, other modes use the first one.</summary>
    public List<string> Colors { get; set; } = ["FFFFFF"];

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MsiKbLight");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    /// <summary>The previous config.json, used if the current one turns out to be unreadable.</summary>
    public static string BackupPath => ConfigPath + ".bak";

    /// <summary>The controller state from before this tool first changed it (restored by "release").</summary>
    public static string OriginalPath => Path.Combine(ConfigDirectory, "original.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Tolerate hand-edited files.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
    };

    public static LightSettings? Load()
    {
        if (!File.Exists(ConfigPath))
            return null;
        try
        {
            return Parse(File.ReadAllText(ConfigPath));
        }
        catch (Exception ex) when ((ex is JsonException or ArgumentException or InvalidDataException) && File.Exists(BackupPath))
        {
            AppLog.Warn($"{ConfigPath} is unreadable ({ex.Message}); using {BackupPath}.");
            return Parse(File.ReadAllText(BackupPath));
        }
    }

    public void Save() => FileUtil.WriteAllTextAtomic(ConfigPath, Serialize(), BackupPath);

    public static LightSettings? LoadOriginal()
    {
        try
        {
            return File.Exists(OriginalPath) ? Parse(File.ReadAllText(OriginalPath)) : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"{OriginalPath} is unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Records the controller state before this tool changes it for the first time, so "release" can put it back.
    /// Does nothing once a record or a saved setting exists.
    /// </summary>
    public static void CaptureOriginal(KeyboardLight light)
    {
        if (File.Exists(OriginalPath) || File.Exists(ConfigPath))
            return;
        try
        {
            var original = light.Read();
            original.Validate();
            FileUtil.WriteAllTextAtomic(OriginalPath, original.Serialize());
            AppLog.Info($"Recorded original lighting: {original}");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not record the original lighting: {ex.Message}");
        }
    }

    internal static LightSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<LightSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("The settings file is empty.");
        settings.Validate();
        return settings;
    }

    internal string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentException($"Unknown mode: {Mode}");
        if (Speed is < 0 or > KeyboardLight.MaxSpeed)
            throw new ArgumentException($"Speed must be 0-{KeyboardLight.MaxSpeed}.");
        if (Brightness is < 0 or > KeyboardLight.MaxBrightness)
            throw new ArgumentException($"Brightness must be 0-{KeyboardLight.MaxBrightness}.");
        ParseColors();
    }

    public List<(byte R, byte G, byte B)> ParseColors()
    {
        if (Colors is null || Colors.Count == 0)
            throw new ArgumentException("At least one color is required.");
        if (Colors.Count > KeyboardLight.MaxColors)
            throw new ArgumentException($"Up to {KeyboardLight.MaxColors} colors are supported.");

        var result = new List<(byte, byte, byte)>();
        foreach (var raw in Colors)
        {
            var hex = (raw ?? "").Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new ArgumentException($"Invalid color '{raw}'. Use RRGGBB (e.g. FF8000).");
            result.Add(((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
        return result;
    }

    public override string ToString() =>
        $"mode={Mode.ToString().ToLowerInvariant()} speed={Speed} brightness={Brightness} colors={string.Join(",", Colors)}";
}
