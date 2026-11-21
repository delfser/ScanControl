using System.IO;

namespace ScanControl.App;

internal static class StartupDiagnostics
{
    private static readonly object Gate = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScanControl", "Diagnose");
    public static string LogPath => Path.Combine(DirectoryPath, "startup.log");
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000) File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch { /* A diagnostic write must never prevent startup. */ }
    }
}
