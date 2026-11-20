namespace SimpleWindowsTools.Core;

/// <summary>Virtual-key codes as the low-level hook reports them (left/right modifiers are separate).</summary>
public static class VK
{
    public const int Space = 0x20;
    public const int LWin = 0x5B, RWin = 0x5C;
    public const int LShift = 0xA0, RShift = 0xA1;
    public const int LControl = 0xA2, RControl = 0xA3;
    public const int LMenu = 0xA4, RMenu = 0xA5; // Alt
    public const int OemMinus = 0xBD;            // the "-" key in the main row

    public static bool IsModifier(int vk) =>
        vk is LWin or RWin or LShift or RShift or LControl or RControl or LMenu or RMenu or 0x10 or 0x11 or 0x12;
}
