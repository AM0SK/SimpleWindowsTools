using System;
using System.IO;
using System.Windows.Interop;

namespace SimpleWindowsTools.Core;

/// <summary>
/// How a new launch finds the running copy and asks it to open the settings window.
/// The running copy is elevated and the new one usually isn't, so named events/mutexes don't work
/// across them (integrity levels). A hidden top-level window with a known title does: FindWindow sees
/// it from any process, and the elevated copy explicitly lets this one message through UIPI.
/// (Not a message-only window: those aren't found by FindWindowEx in every environment.)
/// </summary>
public sealed class IpcWindow : IDisposable
{
    private const string Title = "SimpleWindowsTools.Ipc";
    private static readonly uint ShowMessage = Native.RegisterWindowMessage("SimpleWindowsTools.ShowSettings");
    private static readonly uint ExitMessage = Native.RegisterWindowMessage("SimpleWindowsTools.Exit");

    /// <summary>Written by a non-elevated launch before it starts the task; the elevated copy reads it.</summary>
    private static string ShowFlagPath => Path.Combine(SettingsStore.Folder, "show-settings.flag");

    private readonly HwndSource _window;

    public event Action? ShowRequested;

    /// <summary>The installer / uninstaller asks the running copy to quit before replacing it.</summary>
    public event Action? ExitRequested;

    public IpcWindow()
    {
        // WS_POPUP, never shown; WS_EX_TOOLWINDOW keeps it out of Alt+Tab even if something shows it.
        _window = new HwndSource(new HwndSourceParameters(Title)
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x80,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
        Native.ChangeWindowMessageFilterEx(_window.Handle, ShowMessage, Native.MSGFLT_ALLOW, IntPtr.Zero);
        Native.ChangeWindowMessageFilterEx(_window.Handle, ExitMessage, Native.MSGFLT_ALLOW, IntPtr.Zero);
    }

    /// <summary>If a copy is already running, tells it (optionally) to show settings and returns true.</summary>
    public static bool NotifyRunning(bool showSettings)
    {
        var hwnd = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, Title);
        if (hwnd == IntPtr.Zero) return false;
        Log.Info($"Already running — {(showSettings ? "asking it to show settings" : "nothing to do")}, this launch exits");
        if (showSettings)
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY); // let it bring its window to the front
            Native.PostMessage(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }
        return true;
    }

    /// <summary>Asks a running copy to quit. True if one was found.</summary>
    public static bool AskRunningToExit()
    {
        var hwnd = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, Title);
        if (hwnd == IntPtr.Zero) return false;
        Native.PostMessage(hwnd, ExitMessage, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    public static void LeaveShowFlag()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(ShowFlagPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Log.Error("Could not write the show-settings flag", ex);
        }
    }

    /// <summary>True if a launch in the last 30 s asked for the window. Clears the flag.</summary>
    public static bool TakeShowFlag()
    {
        try
        {
            if (!File.Exists(ShowFlagPath)) return false;
            bool fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(ShowFlagPath) < TimeSpan.FromSeconds(30);
            File.Delete(ShowFlagPath);
            return fresh;
        }
        catch
        {
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == ShowMessage)
        {
            handled = true;
            Log.Info("Another launch asked for the settings window");
            ShowRequested?.Invoke();
        }
        else if ((uint)msg == ExitMessage)
        {
            handled = true;
            Log.Info("Asked to exit (installer / uninstaller)");
            ExitRequested?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _window.Dispose();
}
