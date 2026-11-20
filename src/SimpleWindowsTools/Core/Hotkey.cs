using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SimpleWindowsTools.Core;

/// <summary>
/// A key combination. Stored as text like "Ctrl+Alt+F10" or "Alt+OemMinus" (key names are WPF
/// <see cref="Key"/> names); <see cref="Display"/> is the friendly form shown in the UI ("Alt+-").
/// </summary>
public readonly record struct Hotkey(uint Modifiers, uint Vk)
{
    public bool HasCtrl => (Modifiers & Native.MOD_CONTROL) != 0;
    public bool HasAlt => (Modifiers & Native.MOD_ALT) != 0;
    public bool HasShift => (Modifiers & Native.MOD_SHIFT) != 0;
    public bool HasWin => (Modifiers & Native.MOD_WIN) != 0;

    public Key Key => KeyInterop.KeyFromVirtualKey((int)Vk);

    public static Hotkey From(ModifierKeys mods, Key key)
    {
        uint m = 0;
        if (mods.HasFlag(ModifierKeys.Control)) m |= Native.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= Native.MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= Native.MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= Native.MOD_WIN;
        return new Hotkey(m, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        uint vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default:
                    if (vk != 0) return false;
                    if (!Enum.TryParse<Key>(raw, ignoreCase: true, out var key) || key == Key.None) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        if (vk == 0) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    /// <summary>Canonical text for settings.json.</summary>
    public override string ToString() => Join(Key.ToString());

    /// <summary>What the user sees.</summary>
    public string Display => Join(KeyName(Key));

    private string Join(string keyName)
    {
        var parts = new List<string>(5);
        if (HasCtrl) parts.Add("Ctrl");
        if (HasAlt) parts.Add("Alt");
        if (HasShift) parts.Add("Shift");
        if (HasWin) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        Key.Oem1 => ";",
        Key.Oem7 => "'",
        Key.Oem4 => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        Key.Oem3 => "`",
        Key.Subtract => "Num-",
        Key.Add => "Num+",
        Key.Multiply => "Num*",
        Key.Divide => "Num/",
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Capital => "CapsLock",
        Key.Snapshot => "PrintScreen",
        Key.Next => "PageDown",
        Key.Prior => "PageUp",
        _ => key.ToString(),
    };

    public static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
}
