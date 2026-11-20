using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.UI;

/// <summary>
/// Click-through pill in the middle of a monitor (layout code, "Win: off" and similar).
/// Created once and reused; never takes focus, so typing continues uninterrupted.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private IntPtr _hwnd;

    public OverlayWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); FadeOut(); };

        // Create the native window now, invisible, so the first popup has no startup lag.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Shows text centered on the monitor that holds <paramref name="target"/>.</summary>
    public void ShowText(string text, IntPtr target, int durationMs, int fontSize)
    {
        Label.Text = text;
        Label.FontSize = fontSize;

        if (!IsVisible)
        {
            Pill.Opacity = 0;
            Show();
        }
        UpdateLayout();
        CenterOn(target);
        // Moving to a monitor with another scale makes WPF resize the window; center again with the new size.
        UpdateLayout();
        CenterOn(target);

        FadeIn();
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, durationMs));
        _hideTimer.Start();
    }

    // Full pill: radius = half the height (a huge CornerRadius in WPF gives an ellipse instead).
    private void Pill_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var r = new CornerRadius(e.NewSize.Height / 2);
        Pill.CornerRadius = r;
        Glow.CornerRadius = r;
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        Pill.BeginAnimation(OpacityProperty, null);
        Pill.Opacity = 0;
        Hide();
    }

    private void CenterOn(IntPtr target)
    {
        var monitor = Native.MonitorFromWindow(target != IntPtr.Zero ? target : Native.GetForegroundWindow(),
            Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return;

        Native.GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var m = info.rcMonitor;
        int x = m.Left + (m.Right - m.Left - w) / 2;
        int y = m.Top + (m.Bottom - m.Top - h) / 2;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void FadeIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        bool fromHidden = Pill.Opacity < 0.5;
        Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
        // Small pop only when appearing from hidden; a repeat switch just swaps the text.
        if (fromHidden)
        {
            PillScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            PillScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }
    }

    private void FadeOut()
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        anim.Completed += (_, _) =>
        {
            // A new ShowText may have started during the fade.
            if (!_hideTimer.IsEnabled) Hide();
        };
        Pill.BeginAnimation(OpacityProperty, anim);
    }
}
