using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SimpleWindowsTools.Core;

public readonly record struct KeyEvent(int VkCode, bool IsDown, bool IsInjected);

/// <summary>
/// One shared low-level keyboard hook for all modules. Each handler sees every key event and returns
/// true to swallow it. Keys we inject ourselves (marked with <see cref="InputSender.Marker"/>) skip
/// the handlers, so modules never react to their own output.
///
/// The hook runs on the UI thread and must return fast (Windows drops a hook that takes over ~1 s):
/// handlers only decide; anything slow is posted to the dispatcher.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public delegate bool Handler(KeyEvent e);

    private readonly List<Handler> _handlers = new();
    private readonly bool[] _down = new bool[256];
    private readonly Native.LowLevelKeyboardProc _proc; // kept alive: the GC must not collect the delegate
    private IntPtr _hook;

    /// <summary>Raised after handlers, for every non-swallowed event (layout detection listens here).</summary>
    public event Action<KeyEvent>? KeyPassed;

    public KeyboardHook()
    {
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    public void Add(Handler handler) => _handlers.Add(handler);

    /// <summary>While true, modules don't act on key presses (the settings window is recording a hotkey).</summary>
    public bool Suspended { get; set; }

    /// <summary>
    /// Is the key physically held? Tracked from hook events, so it stays right even when a module
    /// swallows the key or releases it logically (GetAsyncKeyState would report the logical state).
    /// </summary>
    public bool IsDown(int vk) => vk is >= 0 and < 256 && _down[vk];

    public bool AltDown => IsDown(VK.LMenu) || IsDown(VK.RMenu);
    public bool CtrlDown => IsDown(VK.LControl) || IsDown(VK.RControl);
    public bool ShiftDown => IsDown(VK.LShift) || IsDown(VK.RShift);
    public bool WinDown => IsDown(VK.LWin) || IsDown(VK.RWin);

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != InputSender.Marker)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var e = new KeyEvent((int)data.vkCode, isDown, (data.flags & Native.LLKHF_INJECTED) != 0);
                if (Feed(e)) return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Runs one key event through the modules. True = swallow it. Also used by the self-test.</summary>
    internal bool Feed(KeyEvent e)
    {
        if (e.VkCode is >= 0 and < 256) _down[e.VkCode] = e.IsDown;

        // Suspended (recording a hotkey in settings): new presses go straight through. Releases
        // still reach the handlers so they can eat the key-ups of keys they swallowed earlier.
        IReadOnlyList<Handler> handlers = Suspended && e.IsDown ? Array.Empty<Handler>() : _handlers;
        foreach (var handler in handlers)
        {
            try
            {
                if (handler(e)) return true;
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard handler failed", ex);
            }
        }

        KeyPassed?.Invoke(e);
        return false;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
