using System;
using System.Runtime.InteropServices;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// "Automatically hide the taskbar" (Settings → Personalization → Taskbar) switched by a hotkey.
/// Uses the documented app bar API (SHAppBarMessage ABM_GETSTATE / ABM_SETSTATE), which works the same on
/// Windows 10 and 11, applies right away without restarting Explorer, and covers the taskbars on all monitors.
/// The state lives in Windows, not in our settings: whatever the user set elsewhere, the hotkey flips it.
/// </summary>
public static class TaskbarAutoHide
{
    private const uint ABM_GETSTATE = 0x4;
    private const uint ABM_SETSTATE = 0xA;
    private const int ABS_AUTOHIDE = 0x1;
    private const int ABS_ALWAYSONTOP = 0x2; // always set on Windows 7+; the "off" state

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Native.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    private static APPBARDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null),
    };

    /// <summary>Is the taskbar set to hide automatically right now.</summary>
    public static bool IsOn
    {
        get
        {
            var data = Data();
            return ((long)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
        }
    }

    /// <summary>Turns auto-hide on or off. Returns false if Windows didn't take it (no taskbar, Explorer not running).</summary>
    public static bool Set(bool on)
    {
        var data = Data();
        if (data.hWnd == IntPtr.Zero) return false;
        data.lParam = (IntPtr)(on ? ABS_AUTOHIDE | ABS_ALWAYSONTOP : ABS_ALWAYSONTOP);
        SHAppBarMessage(ABM_SETSTATE, ref data);
        return IsOn == on;
    }
}
