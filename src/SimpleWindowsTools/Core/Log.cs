using System;
using System.IO;

namespace SimpleWindowsTools.Core;

/// <summary>Tiny file log in %APPDATA%\SimpleWindowsTools\log.txt — errors only, trimmed at 1 MB.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(SettingsStore.Folder, "log.txt");

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message + (ex is null ? "" : $": {ex}"));

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 1_000_000) file.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
