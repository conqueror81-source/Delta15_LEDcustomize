using System.Runtime.InteropServices;
using System.Text;

namespace MsiKbLight;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }

        // This is a GUI-subsystem exe, so CLI output has to go to the parent console or a log file.
        var logIndex = Array.FindIndex(args, a => a.Equals("--log", StringComparison.OrdinalIgnoreCase));
        StreamWriter? log = null;
        if (logIndex >= 0 && logIndex + 1 < args.Length)
        {
            log = new StreamWriter(args[logIndex + 1], append: false, Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            args = args.Where((_, i) => i != logIndex && i != logIndex + 1).ToArray();
        }
        else
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
        }

        using (log)
            return args.Length == 0 ? 0 : Cli.Run(args);
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
