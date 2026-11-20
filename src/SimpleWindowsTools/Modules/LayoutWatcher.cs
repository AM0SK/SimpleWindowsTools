using System;
using System.Globalization;
using System.Windows.Threading;
using SimpleWindowsTools.Core;
using SimpleWindowsTools.UI;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// Was Swither-Lng.ahk: when the keyboard layout of the active window changes, show EN / RU / UA
/// in the middle of that window's monitor. Also skips chosen languages (e.g. UA) when switching: as soon
/// as a skipped layout turns up, it asks the window for the next allowed one (WM_INPUTLANGCHANGEREQUEST).
///
/// Instead of checking 10 times a second, it checks when something could have changed it:
/// - right after any key is released (Alt+Shift, Ctrl+Shift, Win+Space) — twice, at 60 and 250 ms,
///   because the new layout lands a moment after the keys;
/// - every 500 ms as a fallback, for switching with the mouse in the taskbar.
/// Switching to another window only resets the baseline (no overlay), same as the script.
/// </summary>
public sealed class LayoutWatcher : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _quick;
    private readonly DispatcherTimer _late;
    private readonly DispatcherTimer _poll;
    private readonly Native.WinEventDelegate _winEventProc; // kept alive for the native callback
    private IntPtr _winEventHook;

    private readonly SkipTracker _skip = new();

    /// <summary>Show the EN/RU/UA pill. The watcher can run without it, just to skip languages.</summary>
    public bool ShowOverlayEnabled { get; set; }

    private IntPtr _lastWindow;
    private IntPtr _lastLayout;
    private bool _enabled;

    public LayoutWatcher(KeyboardHook hook, OverlayWindow overlay, Func<AppSettings> settings)
    {
        _overlay = overlay;
        _settings = settings;
        _quick = OneShot(60);
        _late = OneShot(250);
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _poll.Tick += (_, _) => Check();
        _winEventProc = OnForegroundChanged;

        hook.KeyPassed += e =>
        {
            if (!_enabled || e.IsDown) return;
            _quick.Stop(); _quick.Start();
            _late.Stop(); _late.Start();
        };
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                ResetBaseline(Native.GetForegroundWindow());
                _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
                _poll.Start();
            }
            else
            {
                _poll.Stop();
                _quick.Stop();
                _late.Stop();
                if (_winEventHook != IntPtr.Zero) Native.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
                _overlay.HideNow();
            }
        }
    }

    private DispatcherTimer OneShot(int ms)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); Check(); };
        return t;
    }

    private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_enabled) ResetBaseline(hwnd);
    }

    private void ResetBaseline(IntPtr hwnd)
    {
        _skip.Reset();
        _lastWindow = hwnd;
        _lastLayout = LayoutOf(hwnd);
    }

    private void Check()
    {
        if (!_enabled) return;
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var layout = LayoutOf(hwnd);
        if (hwnd != _lastWindow)
        {
            ResetBaseline(hwnd);
            return;
        }

        var step = _skip.Step(layout, _lastLayout, Environment.TickCount64, IsSkipped, PickTarget);
        if (step.Request != IntPtr.Zero) RequestLayout(hwnd, step.Request);
        _lastLayout = step.Baseline;
        if (step.Show != IntPtr.Zero) ShowOverlay(step.Show, hwnd);
    }

    /// <summary>Skipping was just switched on: leave a skipped layout right away.</summary>
    public void LeaveSkippedLayoutNow()
    {
        var hwnd = Native.GetForegroundWindow();
        var layout = LayoutOf(hwnd);
        if (hwnd == IntPtr.Zero || !IsSkipped(layout)) return;
        var target = PickTarget(layout, IntPtr.Zero);
        if (target == IntPtr.Zero) return;
        RequestLayout(hwnd, target);
        _skip.Begin(target, Environment.TickCount64);
        _lastWindow = hwnd;
        _lastLayout = target;
        ShowOverlay(target, hwnd);
    }

    private void ShowOverlay(IntPtr layout, IntPtr hwnd)
    {
        if (!ShowOverlayEnabled) return;
        var s = _settings();
        _overlay.ShowText(LanguageCode(layout), hwnd, s.OverlayDurationMs, s.OverlayFontSize);
    }

    private bool IsSkipped(IntPtr layout)
    {
        var s = _settings();
        return s.SkipLanguagesEnabled && s.SkippedLanguages.Contains(LangId(layout));
    }

    /// <summary>
    /// Next layout after <paramref name="current"/> in the system order that isn't skipped. If that is the
    /// layout we came from, the user was going backwards (Ctrl+Shift+Shift) — then go back one more.
    /// </summary>
    private IntPtr PickTarget(IntPtr current, IntPtr previous) =>
        PickTarget(InstalledLayouts(), current, previous, IsSkipped);

    internal static IntPtr PickTarget(IntPtr[] list, IntPtr current, IntPtr previous, Func<IntPtr, bool> isSkipped)
    {
        int i = Array.IndexOf(list, current);
        if (i < 0) return IntPtr.Zero;

        IntPtr Step(int dir)
        {
            for (int k = 1; k < list.Length; k++)
            {
                var candidate = list[((i + dir * k) % list.Length + list.Length) % list.Length];
                if (!isSkipped(candidate)) return candidate;
            }
            return IntPtr.Zero;
        }

        var forward = Step(+1);
        if (forward == previous && previous != IntPtr.Zero)
        {
            var back = Step(-1);
            if (back != IntPtr.Zero) return back;
        }
        return forward;
    }

    private static void RequestLayout(IntPtr hwnd, IntPtr layout) =>
        Native.PostMessage(hwnd, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout);

    public static IntPtr[] InstalledLayouts()
    {
        int n = Native.GetKeyboardLayoutList(0, null);
        var list = new IntPtr[n];
        Native.GetKeyboardLayoutList(n, list);
        return list;
    }

    public static int LangId(IntPtr layout) => (int)(layout.ToInt64() & 0xFFFF);

    private static IntPtr LayoutOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        uint thread = Native.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        return Native.GetKeyboardLayout(thread);
    }

    /// <summary>Low word of the layout handle is the language id: 0x0409 → EN, 0x0419 → RU, 0x0422 → UA.</summary>
    public static string LanguageCode(IntPtr layout)
    {
        int langId = LangId(layout);
        try
        {
            var code = CultureInfo.GetCultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant();
            return code == "UK" ? "UA" : code;
        }
        catch (CultureNotFoundException)
        {
            return "??";
        }
    }

    public void Dispose() => Enabled = false;
}

/// <summary>
/// What to do when the active window's layout is checked: which layout to ask the window for, which one to
/// show in the pill, and the new baseline. Pure (time is passed in), so the self-test drives it directly.
///
/// A skipped layout (UA) is never shown while a request to leave it is still pending: some windows apply
/// the request a few hundred ms late, and the checks (60 ms, 250 ms, 500 ms poll) come faster than that.
/// Only if the window still sits on it after <see cref="GiveUpMs"/> do we accept it and show it — that
/// window ignores the request, and the pill should tell the truth.
/// </summary>
internal sealed class SkipTracker
{
    public const long GiveUpMs = 1500;

    public IntPtr Pending { get; private set; }
    private long _since;

    public void Reset() => Pending = IntPtr.Zero;

    public void Begin(IntPtr target, long now)
    {
        Pending = target;
        _since = now;
    }

    public (IntPtr Request, IntPtr Show, IntPtr Baseline) Step(IntPtr layout, IntPtr last, long now,
        Func<IntPtr, bool> isSkipped, Func<IntPtr, IntPtr, IntPtr> pickTarget)
    {
        if (!isSkipped(layout))
        {
            Pending = IntPtr.Zero; // either the skip worked or the user moved on to another allowed layout
            return layout == last ? (IntPtr.Zero, IntPtr.Zero, last) : (IntPtr.Zero, layout, layout);
        }

        if (Pending != IntPtr.Zero)
        {
            if (now - _since < GiveUpMs) return (Pending, IntPtr.Zero, Pending); // still waiting: ask again, show nothing
            Pending = IntPtr.Zero; // the window ignores the request: accept the skipped layout
            return (IntPtr.Zero, layout, layout);
        }

        if (layout == last) return (IntPtr.Zero, IntPtr.Zero, last); // accepted earlier, nothing new

        var target = pickTarget(layout, last);
        if (target == IntPtr.Zero) return (IntPtr.Zero, layout, layout); // nothing allowed to go to
        Begin(target, now);
        return (target, target, target);
    }
}
