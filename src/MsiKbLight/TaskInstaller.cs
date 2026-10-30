using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace MsiKbLight;

internal enum AutoApplyState
{
    Disabled,
    Enabled,
    /// <summary>The task exists but points at a dotnet.exe or msikblight.dll that is gone.</summary>
    NeedsRepair,
    /// <summary>The installed copy is older than the one running.</summary>
    Outdated,
}

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
    public static string DotnetHost { get; } = FindDotnetHost();

    private static string AssemblyPath(string directory) => Path.Combine(directory, "msikblight.dll");

    public static bool IsRunningFromInstallDirectory => SamePath(AppContext.BaseDirectory, InstallDirectory);

    private static string ShortcutDirectory => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

    private static IEnumerable<string> AllShortcutPaths =>
        Strings.AllAppTitles.Select(title => Path.Combine(ShortcutDirectory, title + ".lnk"));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string FindDotnetHost()
    {
        // The host running this process is known to work with this build.
        var current = Environment.ProcessPath;
        if (current is not null && Path.GetFileName(current).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return current;

        // Location recorded by the .NET installer (documented to live in the 32-bit registry view).
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            using var key = hklm.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}");
            if (key?.GetValue("InstallLocation") is string dir && File.Exists(Path.Combine(dir, "dotnet.exe")))
                return Path.Combine(dir, "dotnet.exe");
        }
        catch (Exception)
        {
            // Fall through to the default location.
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    }

    public static bool IsRegistered() => GetState() != AutoApplyState.Disabled;

    /// <summary>Checks that the task exists and that the files it starts are still there.</summary>
    public static AutoApplyState GetState()
    {
        string? xml;
        try
        {
            xml = ReadTaskXml();
        }
        catch (Exception)
        {
            // Task Scheduler COM unavailable: fall back to a plain existence check.
            try
            {
                RunSchtasks($"/Query /TN \"{TaskName}\"");
                return AutoApplyState.Enabled;
            }
            catch (InvalidOperationException)
            {
                return AutoApplyState.Disabled;
            }
        }
        if (xml is null)
            return AutoApplyState.Disabled;
        if (xml.Length == 0)
            return AutoApplyState.Enabled; // Exists, but this user may not read its definition.

        try
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var exec = XDocument.Parse(xml).Descendants(ns + "Exec").First();
            var command = exec.Element(ns + "Command")?.Value.Trim('"') ?? "";
            var dll = (exec.Element(ns + "Arguments")?.Value ?? "")
                .Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (!File.Exists(command) || dll is null || !File.Exists(dll))
                return AutoApplyState.NeedsRepair;

            var running = typeof(TaskInstaller).Assembly.GetName().Version;
            if (!IsRunningFromInstallDirectory && AssemblyName.GetAssemblyName(dll).Version < running)
                return AutoApplyState.Outdated;
            return AutoApplyState.Enabled;
        }
        catch (Exception)
        {
            return AutoApplyState.NeedsRepair;
        }
    }

    /// <summary>The task definition, null when there is no such task, "" when it exists but cannot be read.</summary>
    private static string? ReadTaskXml()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try
        {
            dynamic task = folder.GetTask(TaskName);
            return (string)task.Xml;
        }
        // COM interop maps these HRESULTs to different exception types, so match on the code.
        catch (Exception ex) when (HResultOf(ex) is unchecked((int)0x80070002) or unchecked((int)0x80070003))
        {
            return null; // File / path not found.
        }
        catch (Exception ex) when (HResultOf(ex) == unchecked((int)0x80070005))
        {
            return ""; // Access denied.
        }
    }

    private static int HResultOf(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner.HResult : ex.HResult;

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
        lnk.Description = Strings.Current.ShortcutDescription;
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
        if (!SamePath(sourceDir, InstallDirectory))
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

        var xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(assemblyPath), Encoding.Unicode);
        try
        {
            RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        finally
        {
            File.Delete(xmlPath);
        }
        Console.WriteLine($"Scheduled task '{TaskName}' registered (boot / logon / resume) using {DotnetHost}.");

        DeleteShortcuts();
        var shortcut = Path.Combine(ShortcutDirectory, Strings.Current.AppTitle + ".lnk");
        CreateShortcut(shortcut, assemblyPath);
        Console.WriteLine($"Start menu shortcut created: {shortcut}");
        AppLog.Info($"Installed to {InstallDirectory} (version {typeof(TaskInstaller).Assembly.GetName().Version}).");
    }

    public static void Uninstall()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F", ignoreFailure: true);
        Console.WriteLine($"Scheduled task '{TaskName}' removed.");
        DeleteShortcuts();
        if (Directory.Exists(InstallDirectory))
            Console.WriteLine($"Program files remain in {InstallDirectory} (use 'release' to remove everything).");
        AppLog.Info("Auto-apply task removed.");
    }

    private static void DeleteShortcuts()
    {
        foreach (var path in AllShortcutPaths.Where(File.Exists))
            File.Delete(path);
    }

    /// <summary>Whether <see cref="Release"/> has machine-wide things to remove and so needs administrator rights.</summary>
    public static bool ReleaseRequiresElevation() =>
        IsRegistered() || Directory.Exists(InstallDirectory) || AllShortcutPaths.Any(File.Exists);

    /// <summary>
    /// Hands the keyboard back: restores the lighting recorded before first use, then removes the task, the shortcut,
    /// the saved settings and the installed files. Returns true when some files can only go after a restart.
    /// </summary>
    public static bool Release()
    {
        AppLog.Info("Releasing lighting control.");
        var original = LightSettings.LoadOriginal();
        if (original is null)
        {
            Console.WriteLine("No record of the original lighting; leaving the keyboard as it is.");
        }
        else
        {
            try
            {
                KeyboardLight.WriteWithRetry(original, TimeSpan.FromSeconds(3));
                Console.WriteLine($"Restored original lighting: {original}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not restore the original lighting: {ex.Message}");
            }
        }

        if (IsRegistered())
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F", ignoreFailure: true);
            Console.WriteLine($"Scheduled task '{TaskName}' removed.");
        }
        DeleteShortcuts();

        // Nothing may log after this point, or the directory would be recreated.
        DeleteDirectory(LightSettings.ConfigDirectory, out _);
        Console.WriteLine($"Removed {LightSettings.ConfigDirectory}");

        var rebootPending = false;
        if (Directory.Exists(InstallDirectory))
        {
            DeleteDirectory(InstallDirectory, out rebootPending);
            if (rebootPending)
            {
                ScheduleDeleteAfterExit(InstallDirectory);
                Console.WriteLine($"{InstallDirectory} is in use; it will be removed once the app closes or at the next restart.");
            }
            else
            {
                Console.WriteLine($"Removed {InstallDirectory}");
            }
        }
        return rebootPending;
    }

    /// <summary>Deletes a directory tree. Files in use (e.g. the running msikblight.dll) are queued for deletion at restart.</summary>
    private static void DeleteDirectory(string directory, out bool rebootPending)
    {
        rebootPending = false;
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                rebootPending |= MoveFileEx(file, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }
        // Deepest first, the root last.
        foreach (var dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length).Append(directory))
        {
            try
            {
                Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                rebootPending |= MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }
    }

    /// <summary>
    /// The settings window may still hold msikblight.dll open. A hidden cmd keeps retrying for a minute, so the
    /// directory goes as soon as the window closes instead of waiting for a restart.
    /// </summary>
    private static void ScheduleDeleteAfterExit(string directory)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/d /c for /l %i in (1,1,60) do @(rd /s /q \"{directory}\" 2>nul & if not exist \"{directory}\\\" exit /b 0" +
            " & ping -n 2 127.0.0.1 >nul)")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Exception)
        {
            // The restart-time deletion is still queued.
        }
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

    private static string RunSchtasks(string args, bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException($"schtasks failed ({p.ExitCode}): {stderr}{stdout}".Trim());
        return stdout;
    }

    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
