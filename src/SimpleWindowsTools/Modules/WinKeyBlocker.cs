using System.Collections.Generic;
using System.Windows.Threading;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// Was disable_btns.ahk: the Win keys do nothing, and neither do Win+Space and Alt+Space.
/// While Win is held, other (non-modifier) keys are swallowed too, so Win+E doesn't type "e".
/// </summary>
public sealed class WinKeyBlocker
{
    private readonly KeyboardHook _hook;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<int> _swallowed = new(); // keys whose key-down we ate; their key-up must be eaten too

    public bool Enabled { get; set; }

    public WinKeyBlocker(KeyboardHook hook, Dispatcher dispatcher)
    {
        _hook = hook;
        _dispatcher = dispatcher;
        hook.Add(Handle);
    }

    private bool Handle(KeyEvent e)
    {
        if (!e.IsDown)
            return _swallowed.Remove(e.VkCode); // release only what we swallowed on the way down

        if (!Enabled) return false;

        bool swallow = e.VkCode is VK.LWin or VK.RWin
            || (_hook.WinDown && !VK.IsModifier(e.VkCode))
            || (e.VkCode == VK.Space && _hook.AltDown);

        if (!swallow) return false;

        _swallowed.Add(e.VkCode);
        if (e.VkCode == VK.Space && _hook.AltDown)
        {
            // Alt went down and will come up with nothing in between → Windows would open the
            // window/menu bar. A dummy key press in the middle prevents that.
            _dispatcher.BeginInvoke(InputSender.MaskKey);
        }
        return true;
    }
}
