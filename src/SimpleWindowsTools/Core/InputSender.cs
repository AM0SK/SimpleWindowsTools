using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SimpleWindowsTools.Core;

/// <summary>Synthesizes key presses. Everything sent here is tagged so our own hook ignores it.</summary>
public static class InputSender
{
    /// <summary>dwExtraInfo tag for our injected keys ("SWT").</summary>
    public static readonly IntPtr Marker = new(0x53575400);

    /// <summary>
    /// Unassigned virtual key. Pressing it between Alt-down and Alt-up stops Windows from treating a
    /// lone Alt release as "open the menu bar" (same trick AutoHotkey uses).
    /// </summary>
    public const ushort VK_MASK = 0xE8;

    public static void Key(ushort vk, bool up) => Send(new List<Native.INPUT> { KeyInput(vk, up) });

    public static void MaskKey() => Send(new List<Native.INPUT> { KeyInput(VK_MASK, false), KeyInput(VK_MASK, true) });

    public static void Text(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (char c in text)
        {
            inputs.Add(UnicodeInput(c, false));
            inputs.Add(UnicodeInput(c, true));
        }
        Send(inputs);
    }

    internal static Native.INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0, dwExtraInfo = Marker },
        },
    };

    private static Native.INPUT UnicodeInput(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Marker,
            },
        },
    };

    /// <summary>Self-test only: when set, input is handed here instead of to Windows.</summary>
#pragma warning disable CS0649 // set only by the Debug self-test
    internal static Action<List<Native.INPUT>>? TestSink;
#pragma warning restore CS0649

    internal static void Send(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        if (TestSink is not null)
        {
            TestSink(inputs);
            return;
        }
        Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Native.INPUT>());
    }
}
