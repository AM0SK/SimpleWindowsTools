using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// "Characters": any number of rows «character + key combination»; the combination types the character
/// in any layout. Grew out of the em dash ("!-::Send('—')" in Swither-Lng.ahk), which is now just its
/// first row.
///
/// Handled in the keyboard hook, not RegisterHotKey: the text goes out as Unicode, and the held
/// modifiers have to be released logically first — otherwise the app would get Alt+— (a menu
/// shortcut). They're released once per hold, and their physical release is then eaten, so holding the
/// modifiers and tapping the key types the character several times.
/// </summary>
public sealed class CharTyper
{
    private static readonly int[] ModifierVks =
        { VK.LControl, VK.RControl, VK.LMenu, VK.RMenu, VK.LShift, VK.RShift, VK.LWin, VK.RWin };

    private readonly KeyboardHook _hook;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<int> _releasedByUs = new(); // modifiers we lifted logically while still held
    private int _swallowedVk; // key whose down we took: its up is ours too

    public bool Enabled { get; set; }

    /// <summary>The rows with a hotkey: combination → text to type.</summary>
    public IReadOnlyList<(Hotkey Combo, string Text)> Items { get; set; } = Array.Empty<(Hotkey, string)>();

    public CharTyper(KeyboardHook hook, Dispatcher dispatcher)
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

        if (!e.IsDown)
        {
            if (_swallowedVk == 0 || e.VkCode != _swallowedVk) return false;
            _swallowedVk = 0;
            return true;
        }

        if (!Enabled) return false;
        string? text = null;
        foreach (var (combo, t) in Items)
            if (combo.Vk != 0 && e.VkCode == (int)combo.Vk && ModifiersMatch(combo) && !string.IsNullOrEmpty(t))
            {
                text = t;
                break;
            }
        if (text is null) return false;

        _swallowedVk = e.VkCode;
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
            InputSender.Text(text);
        });
        return true;
    }

    /// <summary>Exactly the combo's modifiers are held — extra ones make it a different shortcut.</summary>
    private bool ModifiersMatch(Hotkey combo) =>
        _hook.CtrlDown == combo.HasCtrl
        && _hook.AltDown == combo.HasAlt
        && _hook.ShiftDown == combo.HasShift
        && _hook.WinDown == combo.HasWin;

    // ===== Text helpers for the settings card =====

    public const int MaxLength = 32;

    /// <summary>
    /// Popular characters offered with one click: text + name key in Loc. Order = order of the chips.
    /// </summary>
    public static readonly (string Text, string NameKey)[] Popular =
    {
        ("—", "Ch.EmDash"), ("–", "Ch.EnDash"), ("−", "Ch.Minus"), ("\u00A0", "Ch.Nbsp"), ("…", "Ch.Ellipsis"),
        ("«", "Ch.LGuillemet"), ("»", "Ch.RGuillemet"), ("„", "Ch.LowQuote"), ("“", "Ch.LQuote"), ("”", "Ch.RQuote"),
        ("’", "Ch.Apostrophe"), ("•", "Ch.Bullet"), ("·", "Ch.MiddleDot"), ("×", "Ch.Times"), ("÷", "Ch.Divide"),
        ("±", "Ch.PlusMinus"), ("≈", "Ch.Approx"), ("≠", "Ch.NotEqual"), ("≤", "Ch.LessEq"), ("≥", "Ch.GreaterEq"),
        ("→", "Ch.Right"), ("←", "Ch.Left"), ("↑", "Ch.Up"), ("↓", "Ch.Down"), ("°", "Ch.Degree"),
        ("₴", "Ch.Hryvnia"), ("€", "Ch.Euro"), ("£", "Ch.Pound"), ("©", "Ch.Copyright"), ("®", "Ch.Registered"),
        ("™", "Ch.Trademark"), ("§", "Ch.Section"), ("№", "Ch.Numero"), ("✓", "Ch.Check"), ("∞", "Ch.Infinity"),
        ("½", "Ch.Half"), ("²", "Ch.Squared"), ("³", "Ch.Cubed"),
    };

    /// <summary>Name key of a popular character, or null for the user's own text.</summary>
    public static string? NameKey(string text) => Popular.FirstOrDefault(p => p.Text == text).NameKey;

    private static readonly Regex CodeRx = new(@"^(?:(?<p>U\+|\\u|0x|&#x)(?<h>[0-9A-F]{1,6});?|(?<h>[0-9A-F]{4,6}))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// What the user typed in the "add" field → the text to type. A code ("U+2014", "2014", "—",
    /// "0x2014", "&amp;#x2014;") becomes its character; anything else is taken literally (pasted "—").
    /// Null if empty, too long or not a valid code point.
    /// </summary>
    public static string? ParseInput(string? input)
    {
        if (input is null) return null;
        var text = input.Trim(' ', '\t', '\r', '\n'); // not Trim(): it would eat a pasted no-break space
        if (text.Length == 0) return null;
        var m = CodeRx.Match(text);
        if (m.Success)
        {
            int cp = int.Parse(m.Groups["h"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (cp is < 0x20 or > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF) || (cp >= 0x7F && cp < 0xA0)) return null;
            return char.ConvertFromUtf32(cp);
        }
        return text.Length <= MaxLength ? text : null;
    }

    /// <summary>"U+2014", or "U+00AB U+00BB" for several characters (at most four, then "…").</summary>
    public static string Codes(string text)
    {
        var codes = new List<string>();
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            codes.Add($"U+{char.ConvertToUtf32(text, i):X4}");
        return codes.Count <= 4 ? string.Join(" ", codes) : string.Join(" ", codes.Take(4)) + " …";
    }

    /// <summary>Text as shown on screen: invisible spaces get a visible stand-in.</summary>
    public static string Visible(string text) =>
        text.Replace('\u00A0', '⍽').Replace('\u202F', '⍽').Replace(' ', '␣');
}
