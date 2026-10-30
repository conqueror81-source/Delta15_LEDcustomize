using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsiKbLight;

internal sealed class LightSettings
{
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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LightSettings? Load()
    {
        if (!File.Exists(ConfigPath))
            return null;
        var settings = JsonSerializer.Deserialize<LightSettings>(File.ReadAllText(ConfigPath), JsonOptions)
            ?? throw new InvalidDataException($"{ConfigPath} is empty.");
        settings.Validate();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }

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
        if (Colors.Count == 0)
            throw new ArgumentException("At least one color is required.");
        if (Colors.Count > KeyboardLight.MaxColors)
            throw new ArgumentException($"Up to {KeyboardLight.MaxColors} colors are supported.");

        var result = new List<(byte, byte, byte)>();
        foreach (var raw in Colors)
        {
            var hex = raw.Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new ArgumentException($"Invalid color '{raw}'. Use RRGGBB (e.g. FF8000).");
            result.Add(((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
        return result;
    }

    public override string ToString() =>
        $"mode={Mode.ToString().ToLowerInvariant()} speed={Speed} brightness={Brightness} colors={string.Join(",", Colors)}";
}
