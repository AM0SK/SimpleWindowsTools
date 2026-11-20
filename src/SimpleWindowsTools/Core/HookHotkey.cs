using System;
using System.Windows.Threading;

namespace SimpleWindowsTools.Core;

/// <summary>
/// A hotkey matched in the keyboard hook instead of RegisterHotKey. Used when the key must be taken
/// away from Windows itself — e.g. PrintScreen, which Windows 11 otherwise hands to the Snipping Tool.
/// The key-down and its key-up are swallowed; the action runs on the dispatcher, outside the hook.
/// </summary>
public sealed class HookHotkey
{
    private readonly KeyboardHook _hook;
    private readonly Dispatcher _dispatcher;
    private readonly Action _action;
    private bool _swallowedDown;

    public bool Enabled { get; set; } = true;
    public Hotkey Combo { get; set; }

    public HookHotkey(KeyboardHook hook, Dispatcher dispatcher, Action action)
    {
        _hook = hook;
        _dispatcher = dispatcher;
        _action = action;
        hook.Add(Handle);
    }

    private bool Handle(KeyEvent e)
    {
        if (Combo.Vk == 0 || e.VkCode != (int)Combo.Vk) return false;

        if (!e.IsDown)
        {
            bool wasOurs = _swallowedDown;
            _swallowedDown = false;
            return wasOurs;
        }

        bool matches = Enabled
            && _hook.CtrlDown == Combo.HasCtrl
            && _hook.AltDown == Combo.HasAlt
            && _hook.ShiftDown == Combo.HasShift
            && _hook.WinDown == Combo.HasWin;
        if (!matches) return false;

        if (!_swallowedDown) _dispatcher.BeginInvoke(_action); // auto-repeat while held: fire once
        _swallowedDown = true;
        return true;
    }
}
