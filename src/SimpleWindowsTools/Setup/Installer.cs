using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Setup;

/// <summary>
/// Install / uninstall for the "setup" build (SimpleWindowsTools-Setup.exe, self-contained, .NET inside).
/// The setup exe copies itself to Program Files, so the installed app is the same file.
///
/// Program Files and not the user profile on purpose: the scheduled task starts this exe with admin
/// rights without a prompt, so it must live where only admins can replace it.
/// </summary>
public static class Installer
{
    public const string ProductName = "Simple Windows Tools";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SimpleWindowsTools";
    public const string UninstallArgument = "--uninstall";
    public const string SetupArgument = "--setup";
    /// <summary>First start after a fresh install: open the settings and say the app lives in the tray.</summary>
    public const string WelcomeArgument = "--welcome";

    private const string ExeName = "SimpleWindowsTools.exe";

    public static string DefaultInstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    /// <summary>Where it is installed now (from the Apps entry), or null.</summary>
    public static string? RegisteredDir
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string is { Length: > 0 } dir ? dir : null;
        }
    }

    /// <summary>Target folder. The installer window can change it; starts at the current install or the default.</summary>
    public static string InstallDir { get; set; } = RegisteredDir ?? DefaultInstallDir;

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    /// <summary>
    /// A folder picked in the dialog → the folder we install into: always our own "Simple Windows Tools"
    /// subfolder, so uninstall never deletes a folder that holds anything else (think "D:\").
    /// </summary>
    public static string NormalizeChosenFolder(string chosen)
    {
        var full = Path.GetFullPath(chosen.Trim());
        return string.Equals(Path.GetFileName(full.TrimEnd('\\')), ProductName, StringComparison.OrdinalIgnoreCase)
            ? full.TrimEnd('\\')
            : Path.Combine(full, ProductName);
    }

    /// <summary>
    /// Under Program Files (either one) only admins can change the exe. Anywhere else any program could
    /// swap it and get admin rights through our no-prompt task — the window warns about that.
    /// </summary>
    public static bool IsProtectedLocation(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var r = Environment.GetFolderPath(root).TrimEnd('\\') + "\\";
            if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ProductName + ".lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk");

    /// <summary>This exe is the installed copy (then it runs as the app, not as the installer).</summary>
    public static bool IsRunningInstalled
    {
        get
        {
            var self = Path.GetFullPath(Environment.ProcessPath ?? "");
            var dir = RegisteredDir ?? DefaultInstallDir;
            return string.Equals(self, Path.GetFullPath(Path.Combine(dir, ExeName)), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsInstalled => RegisteredDir is { } dir && File.Exists(Path.Combine(dir, ExeName));

    public static string? InstalledVersion
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
            return key?.GetValue("DisplayVersion") as string;
        }
    }

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Copies the app, registers it in "Apps", creates shortcuts. Needs admin. Throws on failure.</summary>
    public static void Install(bool autostart, bool desktopShortcut, Action<string> progress)
    {
        progress(Loc.T("I.P.Stop"));
        StopRunningCopy();

        progress(Loc.T("I.P.Copy"));
        var previousDir = RegisteredDir;
        Directory.CreateDirectory(InstallDir);
        CopyWithRetry(Environment.ProcessPath!, InstalledExe);
        // Moved to another folder on update: remove the old copy.
        if (previousDir is not null && !string.Equals(Path.GetFullPath(previousDir), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase))
            RemoveOurFiles(previousDir);

        progress(Loc.T("I.P.Shortcuts"));
        CreateShortcut(StartMenuShortcut, InstalledExe);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, InstalledExe);
        else TryDelete(DesktopShortcut);

        progress(Loc.T("I.P.Register"));
        using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", ProductName);
            key.SetValue("DisplayVersion", CurrentVersion);
            key.SetValue("Publisher", ProductName);
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArgument}");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }

        progress(Loc.T("I.P.Task"));
        if (!Autostart.EnsureTask(InstalledExe)) throw new InvalidOperationException(Loc.T("I.TaskFailed"));
        if (autostart) Autostart.Enable(InstalledExe);
        else Autostart.Disable();
    }

    /// <summary>Removes everything Install created, plus settings. The folder itself is removed after exit.</summary>
    public static void Uninstall()
    {
        // The uninstaller is the installed exe itself, so its own folder is the one to remove.
        InstallDir = Path.GetDirectoryName(Environment.ProcessPath!) ?? InstallDir;
        StopRunningCopy(exceptSelf: true);
        Autostart.Disable();
        Autostart.DeleteTask();
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        try { Directory.Delete(SettingsStore.Folder, recursive: true); } catch { /* settings are optional */ }

        // A running exe can't delete itself: a hidden cmd waits for this process to exit, then deletes the
        // exe and the folder — the folder only if nothing else is in it (rmdir without /s).
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 3 /nobreak >nul & del /f /q \"{InstalledExe}\" & del /f /q \"{Path.Combine(InstallDir, "SimpleWindowsTools.pdb")}\" & rmdir \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    private static void RemoveOurFiles(string dir)
    {
        TryDelete(Path.Combine(dir, ExeName));
        TryDelete(Path.Combine(dir, "SimpleWindowsTools.pdb"));
        try { Directory.Delete(dir, recursive: false); } catch { /* not empty or in use: leave it */ }
    }

    /// <summary>Starts the installed copy: with the welcome after a first install, quietly to the tray after an update.</summary>
    public static void LaunchInstalled(bool firstInstall) =>
        Process.Start(new ProcessStartInfo(InstalledExe, firstInstall ? WelcomeArgument : Autostart.Argument)
        {
            UseShellExecute = true,
            WorkingDirectory = InstallDir,
        });

    /// <summary>Asks the running app to quit (it may be in Program Files or anywhere), then makes sure.</summary>
    private static void StopRunningCopy(bool exceptSelf = false)
    {
        IpcWindow.AskRunningToExit();
        int self = Environment.ProcessId;
        for (int i = 0; i < 30; i++)
        {
            var others = Process.GetProcessesByName("SimpleWindowsTools").Where(p => p.Id != self).ToList();
            if (others.Count == 0) return;
            Thread.Sleep(100);
            if (i == 29)
                foreach (var p in others)
                    try { p.Kill(); p.WaitForExit(2000); } catch { /* gone already */ }
        }
    }

    private static void CopyWithRetry(string from, string to)
    {
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250); // the old exe may still be closing
            }
        }
    }

    /// <summary>.lnk through the Windows Script Host COM object — no extra libraries needed.</summary>
    private static void CreateShortcut(string path, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell missing");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.IconLocation = target + ",0";
            link.Description = ProductName;
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { /* not critical */ }
    }
}
