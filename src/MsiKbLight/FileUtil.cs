using System.Text;

namespace MsiKbLight;

internal static class FileUtil
{
    /// <summary>
    /// Writes through a temporary file and a rename, so a crash or power loss leaves either the old or the new
    /// file, never a half-written one. With <paramref name="backupPath"/>, the previous file is kept there.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string contents, string? backupPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (backupPath is not null && File.Exists(path))
            File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// Small rolling log in %ProgramData%\MsiKbLight\logs, mainly for the unattended scheduled task.
/// Logging never throws: a full disk or a permission problem must not stop the lighting from being applied.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;

    public static string LogPath => Path.Combine(LightSettings.ConfigDirectory, "logs", "msikblight.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxBytes)
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] ({Environment.UserName}) {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }
}
