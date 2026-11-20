using System.Collections.Generic;
using System.Windows.Threading;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// Was "!-::Send('—')" in Swither-Lng.ahk: a key combination (Alt + "-" by default) types an em dash.
///
/// Handled in the keyboard hook, not RegisterHotKey: the dash goes out as a Unicode character, and the
/// held modifiers have to be released logically first — otherwise the app would get Alt+— (a menu
/// shortcut). They're released once per hold, and their physical release is then eaten, so holding the
/// modifiers and tapping the key types several dashes.
/// </summary>
public sealed class EmDash
{
    private const string Dash = "—";
    private static readonly int[] ModifierVks =
        { VK.LControl, VK.RControl, VK.LMenu, VK.RMenu, VK.LShift, VK.RShift, VK.LWin, VK.RWin };

    private readonly KeyboardHook _hook;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<int> _releasedByUs = new(); // modifiers we lifted logically while still held
    private bool _keySwallowed;

    public bool Enabled { get; set; }
    public Hotkey Combo { get; set; }

    public EmDash(KeyboardHook hook, Dispatcher dispatcher)
    {
        _hook = hook;
        _dispatcher = dispatcher;
        hook.Add(Handle);
    }

    private bool Handle(KeyEvent e)
    {
        // A modifier we already released for the app: keep it released until the user lets go.
        if (_releasedByUs.Contains(e.VkCode))
        {
            if (!e.IsDown) _releasedByUs.Remove(e.VkCode);
            return true;
        }

        if (e.VkCode != (int)Combo.Vk) return false;

        if (!e.IsDown)
        {
            bool wasOurs = _keySwallowed;
            _keySwallowed = false;
            return wasOurs;
        }

        if (!Enabled || Combo.Vk == 0 || !ModifiersMatch()) return false;

        _keySwallowed = true;
        var toRelease = new List<int>();
        foreach (int vk in ModifierVks)
            if (_hook.IsDown(vk) && _releasedByUs.Add(vk)) toRelease.Add(vk);

        _dispatcher.BeginInvoke(() =>
        {
            if (toRelease.Count > 0)
            {
                InputSender.MaskKey(); // so releasing Alt/Win doesn't open the menu bar / Start
                foreach (int vk in toRelease) InputSender.Key((ushort)vk, up: true);
            }
            InputSender.Text(Dash);
        });
        return true;
    }

    /// <summary>Exactly the combo's modifiers are held — extra ones make it a different shortcut.</summary>
    private bool ModifiersMatch() =>
        _hook.CtrlDown == Combo.HasCtrl
        && _hook.AltDown == Combo.HasAlt
        && _hook.ShiftDown == Combo.HasShift
        && _hook.WinDown == Combo.HasWin;
}
