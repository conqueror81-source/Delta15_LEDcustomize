using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace MsiKbLight;

/// <summary>
/// Installs the tool to Program Files and registers a SYSTEM scheduled task that re-applies
/// the saved lighting at boot, at logon and on resume from sleep / hibernate / modern standby.
/// </summary>
internal static class TaskInstaller
{
    public const string TaskName = "MsiKbLight";

    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MsiKbLight");

    /// <summary>The Microsoft-signed host every entry point goes through (Smart App Control blocks an unsigned apphost).</summary>
    public static string DotnetHost =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    private static string AssemblyPath(string directory) => Path.Combine(directory, "msikblight.dll");

    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "MSI キーボード ライティング.lnk");

    public static bool IsRegistered()
    {
        try
        {
            RunSchtasks($"/Query /TN \"{TaskName}\"");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs this tool elevated (UAC prompt) with the given command. Returns the exit code and the captured output,
    /// or null when the user cancelled the UAC prompt.
    /// </summary>
    public static (int ExitCode, string Output)? RunElevated(string command)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"msikblight-{Guid.NewGuid():N}.log");
        var psi = new ProcessStartInfo(DotnetHost,
            $"\"{AssemblyPath(AppContext.BaseDirectory)}\" {command} --log \"{logPath}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            var output = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            return (p.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return null;
        }
        finally
        {
            if (File.Exists(logPath))
                File.Delete(logPath);
        }
    }

    // dotnet.exe is a console app; "conhost --headless" gives it an invisible console so only the window shows.
    private static void CreateShortcut(string lnkPath, string assemblyPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        lnk.Arguments = $"--headless \"{DotnetHost}\" \"{assemblyPath}\"";
        lnk.IconLocation = Path.Combine(Environment.SystemDirectory, "main.cpl") + ",1";
        lnk.WorkingDirectory = Path.GetDirectoryName(assemblyPath);
        lnk.Description = "MSI キーボード ライティングの設定";
        lnk.Save();
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void Install()
    {
        var sourceDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(InstallDirectory);
        if (!string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'), Path.GetFullPath(InstallDirectory).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(InstallDirectory, Path.GetFileName(file)), overwrite: true);
        }
        var assemblyPath = AssemblyPath(InstallDirectory);
        Console.WriteLine($"Installed to {InstallDirectory}");

        // Let non-elevated "set" calls update the config that the SYSTEM task reads.
        Directory.CreateDirectory(LightSettings.ConfigDirectory);
        var dir = new DirectoryInfo(LightSettings.ConfigDirectory);
        var acl = dir.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(acl);

        var xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(assemblyPath), Encoding.Unicode);
        try
        {
            RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        finally
        {
            File.Delete(xmlPath);
        }
        Console.WriteLine($"Scheduled task '{TaskName}' registered (boot / logon / resume).");

        CreateShortcut(ShortcutPath, assemblyPath);
        Console.WriteLine($"Start menu shortcut created: {ShortcutPath}");
    }

    public static void Uninstall()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F", ignoreFailure: true);
        Console.WriteLine($"Scheduled task '{TaskName}' removed.");
        if (File.Exists(ShortcutPath))
            File.Delete(ShortcutPath);
        if (Directory.Exists(InstallDirectory))
            Console.WriteLine($"Program files remain in {InstallDirectory} (delete manually if no longer needed).");
    }

    private static string BuildTaskXml(string assemblyPath)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        static string EventQuery(string provider, int eventId) =>
            "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">" +
            $"*[System[Provider[@Name='{provider}'] and EventID={eventId}]]" +
            "</Select></Query></QueryList>";

        var doc = new XDocument(
            new XElement(ns + "Task", new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Re-applies MSI MS-1562 keyboard lighting (msikblight).")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "BootTrigger", new XElement(ns + "Enabled", "true")),
                    new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", "true")),
                    // Resume from S3 sleep / S4 hibernate.
                    new XElement(ns + "EventTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Subscription", EventQuery("Microsoft-Windows-Power-Troubleshooter", 1)),
                        new XElement(ns + "Delay", "PT3S")),
                    // Exit from modern standby.
                    new XElement(ns + "EventTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Subscription", EventQuery("Microsoft-Windows-Kernel-Power", 507)),
                        new XElement(ns + "Delay", "PT3S"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", "S-1-5-18"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "Queue"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "ExecutionTimeLimit", "PT5M"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", DotnetHost),
                        new XElement(ns + "Arguments", $"\"{assemblyPath}\" apply --wait 60")))));

        return doc.Declaration + doc.ToString();
    }

    private static void RunSchtasks(string args, bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException($"schtasks failed ({p.ExitCode}): {stderr}{stdout}".Trim());
    }
}
