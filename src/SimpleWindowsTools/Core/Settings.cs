using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleWindowsTools.Core;

/// <summary>Everything the user can change. Saved as JSON; missing fields fall back to these defaults.</summary>
public sealed class AppSettings
{
    // Layout overlay (was Swither-Lng.ahk)
    public bool LayoutOverlayEnabled { get; set; } = true;
    public int OverlayDurationMs { get; set; } = 1000;
    public int OverlayFontSize { get; set; } = 18;

    // Skip languages when switching layouts (e.g. only EN/RU, UA on demand by hotkey)
    public bool SkipLanguagesEnabled { get; set; } = true;
    /// <summary>Language ids (low word of the layout handle): 0x0422 = Ukrainian.</summary>
    public List<int> SkippedLanguages { get; set; } = new();

    // Win key blocker (was disable_btns.ahk)
    public bool WinBlockEnabled { get; set; } = true;

    /// <summary>"Characters": rows of text + hotkey typed in any layout (the em dash is the first by default).</summary>
    public bool CharsEnabled { get; set; } = true;
    public List<CharEntry> Chars { get; set; } = new();

    // Before v5 the em dash was a feature of its own. Read once by Migrate(), then dropped from the file.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? EmDashEnabled { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EmDashHotkey { get; set; }

    // Hotkeys, "Ctrl+Alt+F10" style (see Hotkey). Defaults live in HotkeyDefaults.
    public string ToggleOverlayHotkey { get; set; } = HotkeyDefaults.ToggleOverlay;
    public string ToggleWinBlockHotkey { get; set; } = HotkeyDefaults.ToggleWinBlock;
    public string ToggleSkipHotkey { get; set; } = HotkeyDefaults.ToggleSkip;
    public string ScreenshotHotkey { get; set; } = HotkeyDefaults.Screenshot;
    public string ExitHotkey { get; set; } = HotkeyDefaults.Exit;
    /// <summary>Flips Windows' "automatically hide the taskbar" (the state itself lives in Windows).</summary>
    public string TaskbarHotkey { get; set; } = HotkeyDefaults.Taskbar;

    /// <summary>"Launch apps": any number of program / file / folder + hotkey rows.</summary>
    public List<LaunchEntry> Launchers { get; set; } = new();

    // Screenshot editor: last used color, line thickness and text size are kept between shots
    public bool ScreenshotEnabled { get; set; } = true;
    public string ScreenshotColor { get; set; } = "#F2555A";
    public double ScreenshotThickness { get; set; } = 4;
    public double ScreenshotFontSize { get; set; } = 20;
    public double ScreenshotBlur { get; set; } = 12;
    public string? ScreenshotFolder { get; set; }
    /// <summary>Toolbar order of the editor tools by name (keys 1–9 follow it); empty = default.</summary>
    public List<string> ScreenshotTools { get; set; } = new();

    /// <summary>Interface language: "uk" / "en"; empty = follow the Windows display language.</summary>
    public string Language { get; set; } = "";

    /// <summary>Look for new releases on GitHub and notify. On by default, also for existing installs.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Last version the tray notification was shown for, so it pops up once per version.</summary>
    public string? UpdateNotifiedVersion { get; set; }

    /// <summary>Bumped when a default changes in a way old settings files must follow.</summary>
    public int Version { get; set; }
}

/// <summary>One launcher row: what to open and its hotkey ("" until the user sets one).</summary>
public sealed class LaunchEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Path { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

/// <summary>One "characters" row: the text to type and its hotkey ("" until the user sets one).</summary>
public sealed class CharEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

/// <summary>The "reset" button in the settings window goes back to these.</summary>
public static class HotkeyDefaults
{
    public const string ToggleOverlay = "Ctrl+Alt+F10";
    public const string ToggleWinBlock = "Ctrl+Alt+F11";
    public const string EmDash = "Alt+OemMinus";
    public const string ToggleSkip = "Ctrl+Alt+F9";
    public const string Screenshot = "PrintScreen";
    public const string Exit = "Ctrl+Alt+F12";
    public const string Taskbar = "Ctrl+Alt+F8";
}

public static class SettingsStore
{
    public static string Folder { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleWindowsTools");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private const int CurrentVersion = 5;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "+" readable
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings());
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be read, using defaults", ex);
        }
        return Fresh();
    }

    /// <summary>A new install: defaults plus the em dash row.</summary>
    private static AppSettings Fresh()
    {
        var s = new AppSettings { Version = CurrentVersion };
        s.Chars.Add(new CharEntry { Text = "—", Hotkey = HotkeyDefaults.EmDash });
        return s;
    }

    private static AppSettings Migrate(AppSettings s)
    {
        // v2: "exit" moved from Ctrl+Alt+F9 to Ctrl+Alt+F12.
        if (s.Version < 2 && s.ExitHotkey == "Ctrl+Alt+F9") s.ExitHotkey = HotkeyDefaults.Exit;
        // v3: interface languages. Everyone before that had the Russian UI — keep it.
        if (s.Version < 3 && string.IsNullOrWhiteSpace(s.Language)) s.Language = "ru";
        // v4: the Russian UI was dropped — Russian users get Ukrainian.
        if (s.Version < 4 && s.Language == "ru") s.Language = "uk";
        // v5: the em dash became the first row of "characters", with its hotkey and on/off state.
        if (s.Version < 5)
        {
            if (s.Chars.Count == 0)
                s.Chars.Add(new CharEntry { Text = "—", Hotkey = string.IsNullOrWhiteSpace(s.EmDashHotkey) ? HotkeyDefaults.EmDash : s.EmDashHotkey });
            s.CharsEnabled = s.EmDashEnabled ?? true;
        }
        s.EmDashEnabled = null;
        s.EmDashHotkey = null;
        s.Version = CurrentVersion;
        return s;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Write to a temp file first so a crash mid-write never leaves a broken settings.json.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }
}
