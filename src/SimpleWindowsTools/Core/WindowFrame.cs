using System;
using System.Windows;
using System.Windows.Interop;

namespace SimpleWindowsTools.Core;

/// <summary>
/// Native frame for our borderless (WindowChrome) windows, on Windows 11 and 10 alike.
/// Windows 11: dark frame + system rounded corners and shadow.
/// Windows 10: no rounded corners and no shadow around a borderless window, so it gets a 1 px
/// design-system border instead, otherwise the dark window melts into a dark desktop. The dark-mode
/// attribute is 20 on Windows 10 20H1+ and 11, 19 on older Windows 10 builds.
/// </summary>
public static class WindowFrame
{
    /// <summary>Dark native frame (the app has only the dark theme).</summary>
    private static void SetDarkFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = 1;
        if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
            Native.DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

    public static void Apply(Window window)
    {
        if (!IsWindows11)
        {
            window.SetResourceReference(Window.BorderBrushProperty, "Border");
            window.BorderThickness = new Thickness(1);
        }

        window.SourceInitialized += (_, _) =>
        {
            SetDarkFrame(window);
            var hwnd = new WindowInteropHelper(window).Handle;

            if (IsWindows11)
            {
                int round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
        };
    }
}
