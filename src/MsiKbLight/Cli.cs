namespace MsiKbLight;

internal static class Cli
{
    private const string Usage = """
        msikblight - MSI MS-1562 keyboard lighting control (no driver required)

        Usage:
          msikblight                           Open the settings window (GUI)
          msikblight get                       Show the current lighting read from the controller
          msikblight set <mode> [options]      Apply lighting and save it as the persistent setting
          msikblight apply [--wait <sec>]      Apply the saved setting (used by the scheduled task)
          msikblight install                   Install to Program Files and register the auto-apply task (admin)
          msikblight uninstall                 Remove the scheduled task (admin)

        Modes:
          off | static | breathing | flashing | doubleflashing | colorshift | rainbow

        Options for set:
          -c, --color <RRGGBB[,RRGGBB...]>     Color(s). colorshift accepts up to 7.
          -b, --brightness <0-10>              Default 10 (the controller caps it at 8)
          -s, --speed <0-2>                    0=slow 1=medium 2=fast (default 1)
              --no-save                        Apply only, do not save

        Global option:
              --log <path>                     Write output to a file instead of the console

        Examples:
          msikblight set static -c FF0000
          msikblight set breathing -c 00A0FF -s 0 -b 6
          msikblight set colorshift -c FF0000,FFFF00,00FF00,00FFFF,0000FF,FF00FF
        """;

    public static int Run(string[] args)
    {
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "get" => Get(),
                "set" => Set(args[1..]),
                "apply" => Apply(args[1..]),
                "install" => Install(),
                "uninstall" => Uninstall(),
                "help" or "-h" or "--help" or "/?" => Help(),
                _ => Fail($"Unknown command: {args[0]}\n\n{Usage}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidDataException)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int Help()
    {
        Console.WriteLine(Usage);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static KeyboardLight OpenOrThrow(TimeSpan wait)
    {
        return KeyboardLight.OpenWithWait(wait)
            ?? throw new InvalidOperationException("MysticLight MS-1562 controller (VID 1462, PID 1562-1564) not found.");
    }

    private static int Get()
    {
        using var light = OpenOrThrow(TimeSpan.Zero);
        Console.WriteLine($"device : PID 0x{light.ProductId:X4}");
        Console.WriteLine($"current: {light.Read()}");
        var saved = LightSettings.Load();
        Console.WriteLine($"saved  : {(saved is null ? "(none)" : saved.ToString())}");
        return 0;
    }

    private static int Set(string[] args)
    {
        if (args.Length == 0)
            return Fail("set: mode is required.\n\n" + Usage);

        var settings = new LightSettings { Mode = ParseMode(args[0]) };
        bool save = true;
        for (int i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} requires a value.");
            switch (args[i].ToLowerInvariant())
            {
                case "-c" or "--color" or "--colors":
                    settings.Colors = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "-b" or "--brightness":
                    settings.Brightness = int.Parse(Next());
                    break;
                case "-s" or "--speed":
                    settings.Speed = int.Parse(Next());
                    break;
                case "--no-save":
                    save = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        if (settings.Mode != LightMode.ColorShift && settings.Colors.Count > 1)
            settings.Colors = settings.Colors.Take(1).ToList();
        settings.Validate();

        using (var light = OpenOrThrow(TimeSpan.Zero))
            light.Write(settings);
        Console.WriteLine($"applied: {settings}");

        if (save)
        {
            settings.Save();
            Console.WriteLine($"saved  : {LightSettings.ConfigPath}");
        }
        return 0;
    }

    private static int Apply(string[] args)
    {
        int waitSec = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--wait" && i + 1 < args.Length)
                waitSec = int.Parse(args[++i]);
            else
                throw new ArgumentException($"Unknown option: {args[i]}");
        }

        var settings = LightSettings.Load();
        if (settings is null)
            return Fail($"No saved setting ({LightSettings.ConfigPath}). Run 'msikblight set ...' first.");

        using var light = OpenOrThrow(TimeSpan.FromSeconds(waitSec));
        light.Write(settings);
        Console.WriteLine($"applied: {settings}");
        return 0;
    }

    private static int Install()
    {
        if (!TaskInstaller.IsElevated())
            return Fail("install requires an elevated (administrator) prompt.");
        TaskInstaller.Install();
        return 0;
    }

    private static int Uninstall()
    {
        if (!TaskInstaller.IsElevated())
            return Fail("uninstall requires an elevated (administrator) prompt.");
        TaskInstaller.Uninstall();
        return 0;
    }

    private static LightMode ParseMode(string s) => s.ToLowerInvariant() switch
    {
        "off" or "disable" => LightMode.Off,
        "static" => LightMode.Static,
        "breathing" => LightMode.Breathing,
        "flashing" => LightMode.Flashing,
        "doubleflashing" => LightMode.DoubleFlashing,
        "colorshift" => LightMode.ColorShift,
        "rainbow" => LightMode.Rainbow,
        _ => throw new ArgumentException($"Unknown mode: {s}"),
    };
}
