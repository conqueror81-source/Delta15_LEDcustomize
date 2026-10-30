using System.Text.RegularExpressions;
using Xunit;

namespace MsiKbLight.Tests;

public class ProtocolTests
{
    [Fact]
    public void EncodeReport_MatchesControllerLayout()
    {
        var settings = new LightSettings
        {
            Mode = LightMode.ColorShift, Speed = 2, Brightness = 6, Colors = ["FF0000", "00ff00", "#0000FF"],
        };

        var report = KeyboardLight.EncodeReport(settings);

        Assert.Equal(KeyboardLight.ReportLength, report.Length);
        Assert.Equal(new byte[] { 0x02, 0x00, 5, 2, 6, 3, 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF }, report[..15]);
        Assert.All(report[15..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void DecodeReport_RoundTripsEncode()
    {
        var settings = new LightSettings { Mode = LightMode.Breathing, Speed = 0, Brightness = 8, Colors = ["12AB34"] };

        var decoded = KeyboardLight.DecodeReport(KeyboardLight.EncodeReport(settings));

        Assert.Equal(settings.ToString(), decoded.ToString());
    }

    [Fact]
    public void DecodeReport_ClampsUnexpectedValues()
    {
        var report = new byte[KeyboardLight.ReportLength];
        report[2] = 99; // unknown mode
        report[3] = 7;
        report[4] = 200;
        report[5] = 0;

        var decoded = KeyboardLight.DecodeReport(report);

        Assert.Equal(LightMode.Off, decoded.Mode);
        Assert.Equal(KeyboardLight.MaxSpeed, decoded.Speed);
        Assert.Equal(KeyboardLight.MaxBrightness, decoded.Brightness);
        Assert.Single(decoded.Colors);
        decoded.Validate();
    }
}

public class SettingsFileTests
{
    [Fact]
    public void Parse_ReadsVersion1ConfigWithoutSchemaVersion()
    {
        // Format written by msikblight 1.x.
        var settings = LightSettings.Parse("""
            {
              "Mode": "ColorShift",
              "Speed": 1,
              "Brightness": 8,
              "Colors": [ "FF0000", "00FF00" ]
            }
            """);

        Assert.Equal(LightMode.ColorShift, settings.Mode);
        Assert.Equal(["FF0000", "00FF00"], settings.Colors);
        Assert.Equal(LightSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void Parse_ToleratesHandEditsAndUnknownFields()
    {
        var settings = LightSettings.Parse("""
            {
              // edited by hand
              "schemaVersion": 7,
              "mode": "rainbow",
              "someFutureOption": { "x": 1 },
            }
            """);

        Assert.Equal(LightMode.Rainbow, settings.Mode);
        Assert.Equal(1, settings.Speed);
    }

    [Theory]
    [InlineData("""{ "Colors": [ "XYZ" ] }""")]
    [InlineData("""{ "Brightness": 11 }""")]
    [InlineData("""{ "Colors": [] }""")]
    [InlineData("null")]
    public void Parse_RejectsInvalidSettings(string json)
    {
        Assert.ThrowsAny<Exception>(() => LightSettings.Parse(json));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var settings = new LightSettings { Mode = LightMode.Flashing, Speed = 2, Brightness = 3, Colors = ["ABCDEF"] };

        Assert.Equal(settings.ToString(), LightSettings.Parse(settings.Serialize()).ToString());
    }

    [Fact]
    public void WriteAllTextAtomic_KeepsPreviousVersionAsBackup()
    {
        var dir = Directory.CreateTempSubdirectory("msikblight-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "config.json");
            var backup = path + ".bak";

            FileUtil.WriteAllTextAtomic(path, "first", backup);
            FileUtil.WriteAllTextAtomic(path, "second", backup);

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Equal("first", File.ReadAllText(backup));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

public class StringsTests
{
    private static readonly System.Reflection.PropertyInfo[] TextProperties =
        typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)).ToArray();

    [Fact]
    public void EveryLanguageDefinesEveryString()
    {
        foreach (var strings in new[] { Strings.Japanese, Strings.English })
            foreach (var p in TextProperties)
                Assert.False(string.IsNullOrWhiteSpace((string?)p.GetValue(strings)), p.Name);
    }

    [Fact]
    public void FormatPlaceholdersMatchAcrossLanguages()
    {
        static string Placeholders(string s) =>
            string.Join(",", Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Order());

        foreach (var p in TextProperties)
            Assert.Equal(Placeholders((string)p.GetValue(Strings.Japanese)!), Placeholders((string)p.GetValue(Strings.English)!));
    }

    [Fact]
    public void EveryModeHasAName()
    {
        foreach (var mode in Enum.GetValues<LightMode>())
            Assert.NotEqual(mode.ToString(), Strings.Japanese.ModeName(mode));
    }
}
