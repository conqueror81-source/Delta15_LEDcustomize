using System.ComponentModel;

namespace MsiKbLight;

public enum LightMode : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Flashing = 3,
    DoubleFlashing = 4,
    ColorShift = 5,
    Rainbow = 6,
}

internal sealed class DeviceNotFoundException()
    : Exception("MysticLight MS-1562 controller (VID 1462, PID 1562-1564) not found.");

/// <summary>
/// Protocol of the "MysticLight MS-1562" keyboard lighting MCU (VID 0x1462, PID 0x1562-0x1564),
/// as used by MSI Center (LEDKeeper2 / MSI_1562Led).
///
/// Report 0x02 (64 bytes, set/get):
///   [0]=0x02 [1]=0x00 [2]=mode [3]=speed(0-2) [4]=brightness(0-10) [5]=color count(1-7)
///   [6..26]=up to 7 RGB triplets
/// Reading the current state: SetFeature report 0x01 {0x01,0x91,0x02,0xCC...}, then GetFeature report 0x02.
/// </summary>
internal sealed class KeyboardLight : IDisposable
{
    public const ushort VendorId = 0x1462;
    public static readonly ushort[] ProductIds = [0x1562, 0x1563, 0x1564];

    public const int MaxColors = 7;
    public const int MaxBrightness = 10;
    public const int MaxSpeed = 2;

    internal const int ReportLength = 64;
    private const byte LedReportId = 0x02;
    private const byte CommandReportId = 0x01;
    private const byte CmdQuery = 0x91;
    private const byte QueryLedSetting = 0x02;
    private const byte Filler = 0xCC;

    private readonly HidDevice _device;

    private KeyboardLight(HidDevice device) => _device = device;

    public ushort ProductId => _device.ProductId;
    public string DevicePath => _device.Path;

    public static KeyboardLight? Open()
    {
        var dev = HidDevice.Open(VendorId, ProductIds);
        return dev is null ? null : new KeyboardLight(dev);
    }

    /// <summary>Retries until the device shows up (e.g. right after boot or resume).</summary>
    public static KeyboardLight? OpenWithWait(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var light = Open();
            if (light is not null || DateTime.UtcNow >= deadline)
                return light;
            Thread.Sleep(1000);
        }
    }

    /// <summary>
    /// Opens the controller and writes the settings, retrying until the timeout: right after boot or resume
    /// the device can take a while to enumerate and may reject the first reports.
    /// </summary>
    public static void WriteWithRetry(LightSettings settings, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var light = Open() ?? throw new DeviceNotFoundException();
                light.Write(settings);
                return;
            }
            catch (Exception ex) when ((ex is DeviceNotFoundException or Win32Exception or IOException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(1000);
            }
        }
    }

    public LightSettings Read()
    {
        var cmd = Enumerable.Repeat(Filler, ReportLength).ToArray();
        cmd[0] = CommandReportId;
        cmd[1] = CmdQuery;
        cmd[2] = QueryLedSetting;
        _device.SetFeature(cmd);
        return DecodeReport(_device.GetFeature(LedReportId));
    }

    public void Write(LightSettings settings) => _device.SetFeature(EncodeReport(settings));

    internal static byte[] EncodeReport(LightSettings settings)
    {
        var colors = settings.ParseColors();
        var data = new byte[ReportLength];
        data[0] = LedReportId;
        data[1] = 0x00;
        data[2] = (byte)settings.Mode;
        data[3] = (byte)Math.Clamp(settings.Speed, 0, MaxSpeed);
        data[4] = (byte)Math.Clamp(settings.Brightness, 0, MaxBrightness);
        data[5] = (byte)colors.Count;
        for (int i = 0; i < colors.Count; i++)
        {
            data[6 + i * 3] = colors[i].R;
            data[7 + i * 3] = colors[i].G;
            data[8 + i * 3] = colors[i].B;
        }
        return data;
    }

    internal static LightSettings DecodeReport(byte[] data)
    {
        var count = Math.Clamp((int)data[5], 1, MaxColors);
        var colors = new List<string>();
        for (int i = 0; i < count; i++)
            colors.Add($"{data[6 + i * 3]:X2}{data[7 + i * 3]:X2}{data[8 + i * 3]:X2}");

        return new LightSettings
        {
            Mode = Enum.IsDefined(typeof(LightMode), data[2]) ? (LightMode)data[2] : LightMode.Off,
            Speed = Math.Clamp((int)data[3], 0, MaxSpeed),
            Brightness = Math.Clamp((int)data[4], 0, MaxBrightness),
            Colors = colors,
        };
    }

    public void Dispose() => _device.Dispose();
}
