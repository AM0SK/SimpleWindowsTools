using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Screenshot;

/// <summary>A frozen picture of one monitor plus where that monitor is, in physical pixels.</summary>
internal sealed record CapturedScreen(BitmapSource Image, Native.RECT Bounds)
{
    public int Width => Bounds.Right - Bounds.Left;
    public int Height => Bounds.Bottom - Bounds.Top;
}

internal static class ScreenCapture
{
    /// <summary>The app is per-monitor DPI aware, so these are real pixels on every monitor.</summary>
    internal static CapturedScreen CaptureMonitorUnderCursor()
    {
        Native.GetCursorPos(out var cursor);
        var monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        var r = info.rcMonitor;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;

        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);

        IntPtr hbmp = bmp.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return new CapturedScreen(source, r);
        }
        finally
        {
            Native.DeleteObject(hbmp);
        }
    }
}
