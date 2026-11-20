using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace SimpleWindowsTools.Core;

/// <summary>
/// System-wide hotkeys through RegisterHotKey, delivered to a hidden message window on the UI thread.
/// Registration fails if another program already owns the combination — callers get false and can show it.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _window;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly Dictionary<string, int> _idsByName = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        // Message-only window (parent HWND_MESSAGE = -3): receives WM_HOTKEY, never shown.
        var p = new HwndSourceParameters("SimpleWindowsTools.Hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
    }

    /// <summary>Registers (or re-registers) a named hotkey. Returns false if the text is invalid or the combo is taken.</summary>
    public bool Set(string name, string? text, Action action)
    {
        Remove(name);
        if (!Hotkey.TryParse(text, out var hotkey)) return false;

        int id = _nextId++;
        if (!Native.RegisterHotKey(_window.Handle, id, hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.Vk))
        {
            Log.Info($"Hotkey {text} for {name} is taken by another program");
            return false;
        }
        _actions[id] = action;
        _idsByName[name] = id;
        return true;
    }

    public void Remove(string name)
    {
        if (_idsByName.Remove(name, out int id))
        {
            Native.UnregisterHotKey(_window.Handle, id);
            _actions.Remove(id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        _window.Dispose();
    }
}
