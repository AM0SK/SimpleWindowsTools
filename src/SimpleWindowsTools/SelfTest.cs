#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SimpleWindowsTools.Core;
using SimpleWindowsTools.Modules;
using SimpleWindowsTools.Screenshot;

namespace SimpleWindowsTools;

/// <summary>
/// Debug only: `SimpleWindowsTools.exe --selftest report.txt`. Checks the logic without touching the
/// real keyboard: key events are fed straight into the modules and injected input is captured instead
/// of sent. Settings go to a temp folder. Exit code = number of failed checks.
/// </summary>
internal static class SelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _failed, _passed;
    private static string _section = "";

    public static int Run(string reportPath)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "swt-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        SettingsStore.Folder = tempFolder;
        Loc.Set(UiLanguage.En); // checks below compare English texts

        Safe(HotkeyTests);
        Safe(SettingsTests);
        Safe(EmDashTests);
        Safe(WinBlockerTests);
        Safe(HookHotkeyTests);
        Safe(SuspendTests);
        Safe(LayoutTests);
        Safe(AutostartTests);
        Safe(ControllerTests);
        Safe(ScreenshotTests);
        Safe(InstallerTests);
        Safe(LocalizationTests);
        Safe(ColorTests);
        Safe(UpdaterTests);
        Loc.Set(UiLanguage.En);

        Report.Insert(0, $"Self-test: {_passed} passed, {_failed} failed{Environment.NewLine}{Environment.NewLine}");
        File.WriteAllText(reportPath, Report.ToString());
        try { Directory.Delete(tempFolder, recursive: true); } catch { }
        return _failed;
    }

    // ===== helpers =====

    private static void Section(string name)
    {
        _section = name;
        Report.AppendLine($"[{name}]");
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) _passed++; else _failed++;
        Report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(ok || detail is null ? "" : "  — " + detail)}");
    }

    private static void Safe(Action test)
    {
        try { test(); }
        catch (Exception ex) { Check($"{_section}: crashed", false, ex.ToString()); }
        Report.AppendLine();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static readonly List<Native.INPUT> Sent = new();

    private static void CaptureInput()
    {
        Sent.Clear();
        InputSender.TestSink = inputs => Sent.AddRange(inputs);
    }

    private static string Describe(IEnumerable<Native.INPUT> inputs) => string.Join(" ", inputs.Select(i =>
        (i.U.ki.dwFlags & Native.KEYEVENTF_UNICODE) != 0
            ? $"'{(char)i.U.ki.wScan}'{((i.U.ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0 ? "^" : "v")}"
            : $"{i.U.ki.wVk:X2}{((i.U.ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0 ? "^" : "v")}"));

    private static bool Down(KeyboardHook hook, int vk) => hook.Feed(new KeyEvent(vk, true, false));
    private static bool Up(KeyboardHook hook, int vk) => hook.Feed(new KeyEvent(vk, false, false));

    // ===== tests =====

    private static void HotkeyTests()
    {
        Section("Hotkey parse/format");
        Check("Ctrl+Alt+F10 parses", Hotkey.TryParse("Ctrl+Alt+F10", out var a) && a.HasCtrl && a.HasAlt && !a.HasShift && a.Vk == 0x79);
        Check("round trip text", a.ToString() == "Ctrl+Alt+F10", a.ToString());
        Check("Alt+OemMinus shows as Alt+-", Hotkey.TryParse("Alt+OemMinus", out var m) && m.Display == "Alt+-", m.Display);
        Check("PrintScreen parses", Hotkey.TryParse("PrintScreen", out var p) && p.Vk == 0x2C && p.Modifiers == 0);
        Check("PrintScreen display", p.Display == "PrintScreen", p.Display);
        Check("case and spaces tolerated", Hotkey.TryParse(" ctrl + shift + s ", out var s) && s.HasCtrl && s.HasShift && s.Vk == 0x53);
        Check("Win modifier", Hotkey.TryParse("Win+F1", out var w) && w.HasWin);
        Check("empty rejected", !Hotkey.TryParse("", out _));
        Check("modifiers only rejected", !Hotkey.TryParse("Ctrl+Alt", out _));
        Check("two keys rejected", !Hotkey.TryParse("Ctrl+A+B", out _));
        Check("garbage rejected", !Hotkey.TryParse("Ctrl+Blah", out _));
        Check("digit display", Hotkey.TryParse("Ctrl+D5", out var d) && d.Display == "Ctrl+5", d.Display);
        Check("backtick display", Hotkey.TryParse("Ctrl+Oem3", out var b) && b.Display == "Ctrl+`", b.Display);
        Check("equality", Hotkey.TryParse("Alt+Ctrl+F10", out var a2) && a2 == a);
    }

    private static void SettingsTests()
    {
        Section("Settings");
        var file = Path.Combine(SettingsStore.Folder, "settings.json");

        File.Delete(file);
        var fresh = SettingsStore.Load();
        Check("defaults when no file", fresh.ExitHotkey == "Ctrl+Alt+F12" && fresh.ToggleSkipHotkey == "Ctrl+Alt+F9" && fresh.ScreenshotHotkey == "PrintScreen");

        File.WriteAllText(file, """{ "ExitHotkey": "Ctrl+Alt+F9", "OverlayFontSize": 22 }""");
        var old = SettingsStore.Load();
        Check("v1 exit F9 migrates to F12", old.ExitHotkey == "Ctrl+Alt+F12", old.ExitHotkey);
        Check("other values kept", old.OverlayFontSize == 22);

        File.WriteAllText(file, """{ "Version": 2, "ExitHotkey": "Ctrl+Alt+F9" }""");
        Check("v2 keeps a chosen F9 exit", SettingsStore.Load().ExitHotkey == "Ctrl+Alt+F9");

        File.WriteAllText(file, "{ not json");
        Check("broken file → defaults, no crash", SettingsStore.Load().EmDashHotkey == "Alt+OemMinus");

        var s = new AppSettings { SkippedLanguages = new List<int> { 0x0422 }, ScreenshotColor = "#4C8DFF" };
        SettingsStore.Save(s);
        var back = SettingsStore.Load();
        Check("save/load round trip", back.SkippedLanguages.SequenceEqual(new[] { 0x0422 }) && back.ScreenshotColor == "#4C8DFF");
        Check("'+' readable in json", File.ReadAllText(file).Contains("Ctrl+Alt+F10"));

        File.WriteAllText(file, """{ "Version": 2 }""");
        Check("v2 file (Russian UI before 0.4) → Ukrainian (Russian dropped)", SettingsStore.Load().Language == "uk", SettingsStore.Load().Language);
        File.WriteAllText(file, """{ "Version": 3, "Language": "ru" }""");
        Check("v3 file with Russian → Ukrainian", SettingsStore.Load().Language == "uk", SettingsStore.Load().Language);
        File.WriteAllText(file, """{ "Version": 3, "Language": "en" }""");
        Check("chosen language kept", SettingsStore.Load().Language == "en");
        File.Delete(file);
        Check("new install → language follows Windows", SettingsStore.Load().Language == "");
        var defaults = SettingsStore.Load();
        Check("new install → updates on", defaults.CheckUpdates);
        File.WriteAllText(file, """{ "Version": 3, "Language": "ru" }""");
        var olderFile = SettingsStore.Load();
        Check("settings from 0.4 → updates on", olderFile.CheckUpdates);
        File.WriteAllText(file, """{ "Version": 4, "Theme": "light", "Language": "en" }""");
        Check("old file with a theme still loads (theme ignored)", SettingsStore.Load().Language == "en");
        File.Delete(file);
    }

    private static void EmDashTests()
    {
        Section("Em dash (Alt + -)");
        var hook = new KeyboardHook();
        var dash = new EmDash(hook, Dispatcher.CurrentDispatcher) { Enabled = true };
        Hotkey.TryParse("Alt+OemMinus", out var combo);
        dash.Combo = combo;
        CaptureInput();

        Check("Alt down passes", !Down(hook, VK.LMenu));
        Check("'-' with Alt swallowed", Down(hook, VK.OemMinus));
        Pump();
        var first = Describe(Sent);
        Check("sends mask, Alt up, then —", first == "E8v E8^ A4^ '—'v '—'^", first);
        Check("'-' up swallowed", Up(hook, VK.OemMinus));

        Sent.Clear();
        Check("second '-' while Alt held swallowed", Down(hook, VK.OemMinus));
        Pump();
        Check("second time only the dash", Describe(Sent) == "'—'v '—'^", Describe(Sent));
        Up(hook, VK.OemMinus);
        Check("physical Alt up swallowed (already released)", Up(hook, VK.LMenu));

        Sent.Clear();
        Check("'-' alone passes", !Down(hook, VK.OemMinus) && !Up(hook, VK.OemMinus));
        Down(hook, VK.LControl); Down(hook, VK.RMenu);
        Check("AltGr (Ctrl+Alt) + '-' passes", !Down(hook, VK.OemMinus));
        Up(hook, VK.OemMinus); Up(hook, VK.RMenu); Up(hook, VK.LControl);
        Down(hook, VK.LMenu); Down(hook, VK.LShift);
        Check("Alt+Shift+'-' passes", !Down(hook, VK.OemMinus));
        Up(hook, VK.OemMinus); Up(hook, VK.LShift); Up(hook, VK.LMenu);

        dash.Enabled = false;
        Down(hook, VK.LMenu);
        Check("disabled → passes", !Down(hook, VK.OemMinus));
        Up(hook, VK.OemMinus); Up(hook, VK.LMenu);
        Pump();
        Check("nothing sent while disabled / not matching", Sent.Count == 0, Describe(Sent));

        // Re-assigned combo
        dash.Enabled = true;
        Hotkey.TryParse("Ctrl+Shift+D", out var custom);
        dash.Combo = custom;
        Sent.Clear();
        Down(hook, VK.LControl); Down(hook, VK.LShift);
        Check("custom combo Ctrl+Shift+D swallowed", Down(hook, 0x44));
        Pump();
        Check("custom combo releases Ctrl and Shift", Describe(Sent) == "E8v E8^ A2^ A0^ '—'v '—'^", Describe(Sent));
        Up(hook, 0x44);
        Check("Ctrl up swallowed", Up(hook, VK.LControl));
        Check("Shift up swallowed", Up(hook, VK.LShift));
        InputSender.TestSink = null;
    }

    private static void WinBlockerTests()
    {
        Section("Win blocker");
        var hook = new KeyboardHook();
        var win = new WinKeyBlocker(hook, Dispatcher.CurrentDispatcher) { Enabled = true };
        CaptureInput();

        Check("LWin down swallowed", Down(hook, VK.LWin));
        Check("E while Win held swallowed", Down(hook, 0x45));
        Check("E up swallowed", Up(hook, 0x45));
        Check("Space while Win held swallowed", Down(hook, VK.Space) && Up(hook, VK.Space));
        Check("Shift while Win held passes (modifier)", !Down(hook, VK.LShift) && !Up(hook, VK.LShift));
        Check("LWin up swallowed", Up(hook, VK.LWin));
        Check("RWin swallowed", Down(hook, VK.RWin) && Up(hook, VK.RWin));

        Check("Alt down passes", !Down(hook, VK.LMenu));
        Check("Alt+Space swallowed", Down(hook, VK.Space));
        Pump();
        Check("Alt+Space sends mask key", Describe(Sent) == "E8v E8^", Describe(Sent));
        Check("Space up swallowed", Up(hook, VK.Space));
        Check("Alt up passes", !Up(hook, VK.LMenu));

        Check("plain Space passes", !Down(hook, VK.Space) && !Up(hook, VK.Space));
        Check("E typed before Win: its up passes", !Down(hook, 0x45) & Down(hook, VK.LWin) & !Up(hook, 0x45));
        Up(hook, VK.LWin);

        Down(hook, VK.LWin);
        win.Enabled = false;
        Check("Win pressed while on, released after off: up still swallowed", Up(hook, VK.LWin));
        Check("off → Win passes", !Down(hook, VK.LWin) && !Up(hook, VK.LWin));
        InputSender.TestSink = null;
    }

    private static void HookHotkeyTests()
    {
        Section("Hook hotkey (PrintScreen)");
        var hook = new KeyboardHook();
        int fired = 0;
        var key = new HookHotkey(hook, Dispatcher.CurrentDispatcher, () => fired++);
        Hotkey.TryParse("PrintScreen", out var combo);
        key.Combo = combo;

        Check("PrintScreen down swallowed", Down(hook, 0x2C));
        Check("auto-repeat swallowed", Down(hook, 0x2C));
        Pump();
        Check("fires once per press", fired == 1, $"fired {fired}");
        Check("PrintScreen up swallowed", Up(hook, 0x2C));
        Down(hook, VK.LShift);
        Check("Shift+PrintScreen passes", !Down(hook, 0x2C) && !Up(hook, 0x2C));
        Up(hook, VK.LShift);
        key.Enabled = false;
        Check("disabled → passes (Windows gets it back)", !Down(hook, 0x2C) && !Up(hook, 0x2C));
        Pump();
        Check("no extra fires", fired == 1, $"fired {fired}");
    }

    private static void SuspendTests()
    {
        Section("Hook suspended while recording a hotkey");
        var hook = new KeyboardHook();
        var win = new WinKeyBlocker(hook, Dispatcher.CurrentDispatcher) { Enabled = true };
        Check("Win swallowed normally", Down(hook, VK.LWin));
        hook.Suspended = true;
        Check("release of a key swallowed before suspend still swallowed", Up(hook, VK.LWin));
        Check("new Win press passes while suspended", !Down(hook, VK.LWin) && !Up(hook, VK.LWin));
        hook.Suspended = false;
        Check("back to blocking after", Down(hook, VK.LWin) && Up(hook, VK.LWin));
    }

    private static void LayoutTests()
    {
        Section("Layouts and skipping");
        Check("0x0409 → EN", LayoutWatcher.LanguageCode(new IntPtr(0x04090409)) == "EN");
        Check("0x0419 → RU", LayoutWatcher.LanguageCode(new IntPtr(0x04190419)) == "RU");
        Check("0x0422 → UA", LayoutWatcher.LanguageCode(new IntPtr(0x04220422)) == "UA");
        Check("unknown id → ??", LayoutWatcher.LanguageCode(new IntPtr(0x7777)) == "??", LayoutWatcher.LanguageCode(new IntPtr(0x7777)));

        IntPtr en = new(0x04090409), ru = new(0x04190419), ua = new(0x04220422), de = new(0x04070407);
        var list = new[] { en, ru, ua };
        bool SkipUa(IntPtr l) => l == ua;
        Check("forward RU → UA lands on EN", LayoutWatcher.PickTarget(list, ua, ru, SkipUa) == en);
        Check("backward EN → UA lands on RU", LayoutWatcher.PickTarget(list, ua, en, SkipUa) == ru);
        Check("no previous → next forward", LayoutWatcher.PickTarget(list, ua, IntPtr.Zero, SkipUa) == en);
        Check("everything skipped → nothing", LayoutWatcher.PickTarget(list, ua, ru, _ => true) == IntPtr.Zero);
        Check("not installed → nothing", LayoutWatcher.PickTarget(list, de, ru, SkipUa) == IntPtr.Zero);
        var four = new[] { en, ru, ua, de };
        Check("two skipped in a row are passed over", LayoutWatcher.PickTarget(four, ua, ru, l => l == ua || l == de) == en);
        Check("installed layouts readable", LayoutWatcher.InstalledLayouts().Length > 0);

        // Skip timing: UA must not reach the pill while the request to leave it is pending.
        IntPtr Pick(IntPtr cur, IntPtr prev) => LayoutWatcher.PickTarget(list, cur, prev, SkipUa);
        var t = new SkipTracker();
        var st = t.Step(ua, ru, 0, SkipUa, Pick);
        Check("RU → UA: asks for EN and shows EN", st.Request == en && st.Show == en && st.Baseline == en);
        st = t.Step(ua, st.Baseline, 60, SkipUa, Pick);
        Check("slow window still on UA at 60 ms: asks again, shows nothing", st.Request == en && st.Show == IntPtr.Zero);
        st = t.Step(ua, st.Baseline, 250, SkipUa, Pick);
        Check("still on UA at 250 ms: shows nothing", st.Show == IntPtr.Zero && st.Baseline == en);
        st = t.Step(ua, st.Baseline, 600, SkipUa, Pick);
        Check("still on UA at 600 ms: shows nothing", st.Show == IntPtr.Zero);
        st = t.Step(en, st.Baseline, 900, SkipUa, Pick);
        Check("request lands late: no second pill", st.Show == IntPtr.Zero && st.Request == IntPtr.Zero && t.Pending == IntPtr.Zero);

        var t2 = new SkipTracker();
        st = t2.Step(ua, ru, 0, SkipUa, Pick);
        st = t2.Step(ua, st.Baseline, 1000, SkipUa, Pick);
        st = t2.Step(ua, st.Baseline, SkipTracker.GiveUpMs, SkipUa, Pick);
        Check("window ignores the request: UA shown after the give-up time", st.Show == ua && st.Baseline == ua && st.Request == IntPtr.Zero);
        st = t2.Step(ua, st.Baseline, SkipTracker.GiveUpMs + 500, SkipUa, Pick);
        Check("accepted UA isn't shown again on every poll", st.Show == IntPtr.Zero && st.Request == IntPtr.Zero);
        st = t2.Step(ru, st.Baseline, 5000, SkipUa, Pick);
        Check("then RU is shown normally", st.Show == ru);
        st = t2.Step(ua, st.Baseline, 6000, SkipUa, Pick);
        Check("next switch to UA tries to skip again", st.Request == en && st.Show == en);

        var t3 = new SkipTracker();
        st = t3.Step(ua, en, 0, SkipUa, Pick);
        st = t3.Step(ru, st.Baseline, 100, SkipUa, Pick);
        Check("EN → UA goes back to RU; RU lands: no extra pill", t3.Pending == IntPtr.Zero && st.Show == IntPtr.Zero);
        st = new SkipTracker().Step(ru, en, 0, SkipUa, Pick);
        Check("allowed change is shown as is", st.Show == ru && st.Request == IntPtr.Zero);
    }

    private static void AutostartTests()
    {
        Section("Autostart");
        Check("no StartupApproved value = on", Autostart.IsApproved(null));
        Check("02… = on", Autostart.IsApproved(new byte[] { 2, 0, 0, 0 }));
        Check("03… = off (Task Manager disabled)", !Autostart.IsApproved(new byte[] { 3, 0, 0, 0 }));
        Check("06… = on", Autostart.IsApproved(new byte[] { 6, 0 }));
        Check("07… = off", !Autostart.IsApproved(new byte[] { 7, 0 }));
        Check("task query does not crash", Autostart.TaskExists() || !Autostart.TaskExists());
    }

    private static void LauncherTests(AppController app)
    {
        var calc = Path.Combine(Environment.SystemDirectory, "calc.exe");
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Check("launcher name of calc.exe comes from the program", AppLauncher.DisplayName(calc).Length > 0 && !AppLauncher.DisplayName(calc).Equals("calc", StringComparison.OrdinalIgnoreCase),
            AppLauncher.DisplayName(calc));
        Check("launcher icon of calc.exe", AppLauncher.Icon(calc) is not null);
        Check("launcher name of a folder", AppLauncher.DisplayName(Environment.SystemDirectory).Equals("System32", StringComparison.OrdinalIgnoreCase), AppLauncher.DisplayName(Environment.SystemDirectory));

        var present = AppLauncher.Standard.ToList();
        Check("built-in apps: Calculator and Notepad are offered", present.Any(s => s.Path == calc) && present.Any(s => s.Path == notepad), string.Join(", ", present.Select(s => Path.GetFileName(s.Path))));
        Check("built-in apps: every name has a string", AppLauncher.StandardCandidates.All(s => Strings.Table.ContainsKey(s.NameKey)));
        Check("built-in apps: only files that exist are offered", present.All(s => File.Exists(s.Path)));
        Check("built-in apps: PowerShell path resolves", present.Any(s => s.Path.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", StringComparison.OrdinalIgnoreCase)));

        var a = app.AddLauncher(calc);
        var b = app.AddLauncher(notepad);
        Check("new launcher has no hotkey yet", app.GetLauncherHotkey(a.Id) is null && app.Launchers.Count == 2);

        Hotkey.TryParse("Ctrl+Alt+Shift+F7", out var combo);
        Check("launcher hotkey accepted", app.TrySetLauncherHotkey(a.Id, combo) is null && app.GetLauncherHotkey(a.Id) == combo);
        var same = app.TrySetLauncherHotkey(b.Id, combo);
        Check("two launchers can't share a hotkey", same is not null && same.Contains(AppLauncher.DisplayName(calc)), same);
        Hotkey.TryParse("Ctrl+Alt+F10", out var overlay);
        var feature = app.TrySetLauncherHotkey(b.Id, overlay);
        Check("launcher can't take a feature's hotkey", feature is not null && feature.Contains("layout pill"), feature);
        var reverse = app.TrySetHotkey(HotkeyId.Taskbar, combo);
        Check("feature can't take a launcher's hotkey", reverse is not null && reverse.Contains(AppLauncher.DisplayName(calc)), reverse);

        var saved = SettingsStore.Load();
        Check("launchers saved to settings", saved.Launchers.Count == 2 && saved.Launchers[0].Path == calc && saved.Launchers[0].Hotkey == combo.ToString());

        app.RemoveLauncher(a.Id);
        Check("removed launcher frees its hotkey", app.TrySetLauncherHotkey(b.Id, combo) is null, app.TrySetLauncherHotkey(b.Id, combo));
        app.RemoveLauncher(b.Id);
        Check("all launchers removed", app.Launchers.Count == 0 && SettingsStore.Load().Launchers.Count == 0);

        string? missingError = null;
        try { AppLauncher.Start(Path.Combine(Path.GetTempPath(), "no-such-app-" + Guid.NewGuid().ToString("N") + ".exe")); }
        catch (FileNotFoundException) { missingError = "ok"; }
        catch (Exception ex) { missingError = ex.GetType().Name; }
        Check("missing file reported, not started", missingError == "ok", missingError);

        // The real route the elevated app uses: ask Explorer to run it (hidden cmd writes a marker file).
        var marker = Path.Combine(Path.GetTempPath(), "swt-launch-" + Guid.NewGuid().ToString("N") + ".txt");
        string? shellError = null;
        AppLauncher.ForceShellRoute = true;
        try { AppLauncher.Start(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c echo ok>\"{marker}\"", show: 0); }
        catch (Exception ex) { shellError = ex.Message; }
        finally { AppLauncher.ForceShellRoute = false; }
        for (int i = 0; i < 50 && !File.Exists(marker); i++) System.Threading.Thread.Sleep(100);
        Check("launch through Explorer works", shellError is null && File.Exists(marker), shellError ?? "no marker file");
        try { File.Delete(marker); } catch { }
    }

    private static void ControllerTests()
    {
        Section("Controller: hotkey changes and modules");
        File.Delete(Path.Combine(SettingsStore.Folder, "settings.json"));
        using var app = new AppController();

        // Taskbar auto-hide: its own hotkey, default Ctrl+Alt+F8, different from every other default
        Check("taskbar hotkey default Ctrl+Alt+F8", app.GetHotkey(HotkeyId.Taskbar).Display == "Ctrl+Alt+F8", app.GetHotkey(HotkeyId.Taskbar).Display);
        var defaults = Enum.GetValues<HotkeyId>().Select(id => app.GetDefaultHotkey(id).Display).ToList();
        Check("all default hotkeys differ", defaults.Distinct().Count() == defaults.Count, string.Join(", ", defaults));
        Hotkey.TryParse("Ctrl+Alt+F11", out var winKey);
        var taskDup = app.TrySetHotkey(HotkeyId.Taskbar, winKey);
        Check("taskbar hotkey can't take the Win blocker's", taskDup is not null, taskDup);
        string? readError = null;
        try { _ = Modules.TaskbarAutoHide.IsOn; } catch (Exception ex) { readError = ex.Message; }
        Check("taskbar state reads from Windows without throwing", readError is null, readError);

        LauncherTests(app);

        Hotkey.TryParse("Ctrl+Alt+F10", out var overlayKey);
        var dup = app.TrySetHotkey(HotkeyId.EmDash, overlayKey);
        Check("duplicate of another function refused", dup is not null && dup.Contains("layout pill"), dup);
        Check("em dash unchanged after refusal", app.GetHotkey(HotkeyId.EmDash).Display == "Alt+-");

        Hotkey.TryParse("Ctrl+Shift+Oem3", out var newDash);
        Check("new em dash combo accepted", app.TrySetHotkey(HotkeyId.EmDash, newDash) is null);
        Check("saved to settings", SettingsStore.Load().EmDashHotkey == "Ctrl+Shift+Oem3", SettingsStore.Load().EmDashHotkey);
        Check("reset to default", app.TrySetHotkey(HotkeyId.EmDash, app.GetDefaultHotkey(HotkeyId.EmDash)) is null
                                  && app.GetHotkey(HotkeyId.EmDash).Display == "Alt+-");

        Hotkey.TryParse("Ctrl+Alt+Shift+F7", out var free);
        var r = app.TrySetHotkey(HotkeyId.ToggleSkip, free);
        Check("registered hotkey can be moved to a free combo", r is null && !app.IsHotkeyTaken(HotkeyId.ToggleSkip), r);

        app.BeginHotkeyCapture();
        app.EndHotkeyCapture();
        Check("capture begin/end keeps the new combo", app.GetHotkey(HotkeyId.ToggleSkip) == free);

        app.SetLanguageSkipped(0x0422, true);
        Check("skip UA saved", SettingsStore.Load().SkippedLanguages.Contains(0x0422));
        app.SetSkipLanguages(false);
        Check("skip toggle off saved", !SettingsStore.Load().SkipLanguagesEnabled);
        app.SetLanguageSkipped(0x0422, false);
        Check("unskip UA", !SettingsStore.Load().SkippedLanguages.Contains(0x0422));
        app.SetWinBlock(false); app.SetEmDash(false); app.SetLayoutOverlay(false); app.SetScreenshot(false);
        var s = SettingsStore.Load();
        Check("module switches saved", !s.WinBlockEnabled && !s.EmDashEnabled && !s.LayoutOverlayEnabled && !s.ScreenshotEnabled);
    }

    private static void InstallerTests()
    {
        Section("Installer folder");
        Check("drive root → own subfolder", Setup.Installer.NormalizeChosenFolder(@"D:\") == @"D:\Simple Windows Tools",
            Setup.Installer.NormalizeChosenFolder(@"D:\"));
        Check("any folder → own subfolder", Setup.Installer.NormalizeChosenFolder(@"D:\Apps") == @"D:\Apps\Simple Windows Tools");
        Check("already our folder stays", Setup.Installer.NormalizeChosenFolder(@"D:\Apps\Simple Windows Tools\") == @"D:\Apps\Simple Windows Tools");
        Check("Program Files is protected", Setup.Installer.IsProtectedLocation(Setup.Installer.DefaultInstallDir));
        Check("folder outside Program Files is not protected", !Setup.Installer.IsProtectedLocation(@"D:\Apps\Simple Windows Tools"));
        Check("look-alike 'Program Files Evil' is not protected",
            !Setup.Installer.IsProtectedLocation(Setup.Installer.DefaultInstallDir.Replace("Program Files", "Program Files Evil")));
    }

    private static void LocalizationTests()
    {
        Section("Localization");
        bool complete = true, sameArgs = true;
        string? bad = null;
        foreach (var (key, v) in Strings.Table)
        {
            if (v.Length != 2 || v.Any(string.IsNullOrWhiteSpace)) { complete = false; bad ??= key; }
            else if (!Strings.Placeholders(v[1]).SequenceEqual(Strings.Placeholders(v[0]))) { sameArgs = false; bad ??= key; }
        }
        Check("every string has uk and en", complete, bad);
        Check("translations use the same {0} placeholders", sameArgs, bad);
        Check("no Cyrillic in the en column by mistake",
            Strings.Table.All(kv => kv.Value[1].All(c => c < 0x0400 || c > 0x04FF)),
            Strings.Table.FirstOrDefault(kv => kv.Value[1].Any(c => c >= 0x0400 && c <= 0x04FF)).Key);
        Check("no Russian in the uk column (ы/э/ъ/ё not in Ukrainian)",
            Strings.Table.All(kv => !kv.Value[0].Any(c => "ыэъёЫЭЪЁ".Contains(c))),
            Strings.Table.FirstOrDefault(kv => kv.Value[0].Any(c => "ыэъёЫЭЪЁ".Contains(c))).Key);

        // Every {DynamicResource X} in the XAML sources has a string (resource keys with a dot are ours).
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "SimpleWindowsTools.csproj"))) dir = Path.GetDirectoryName(dir);
        if (dir is not null)
        {
            var missing = Directory.GetFiles(dir, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\{DynamicResource ([\w.]+)\}").Select(m => m.Groups[1].Value))
                .Where(k => k.Contains('.') && !Strings.Table.ContainsKey(k)).Distinct().ToList();
            Check("all XAML string keys exist", missing.Count == 0, string.Join(", ", missing));
        }
        var codeKeys = UI.SettingsWindow.ShotCommands.SelectMany(g => g.Rows.SelectMany(r => new[] { r.Keys, r.Text }).Append(g.Group))
            .Where(k => k.StartsWith("H.") && !Strings.Table.ContainsKey(k)).ToList();
        Check("all screenshot-help keys exist", codeKeys.Count == 0, string.Join(", ", codeKeys));

        Check("code uk/en, old ru → uk", Loc.FromCode("ru") == UiLanguage.Uk && Loc.FromCode("uk") == UiLanguage.Uk && Loc.FromCode("ua") == UiLanguage.Uk && Loc.FromCode("en") == UiLanguage.En);
        var ukUi = System.Globalization.CultureInfo.GetCultureInfo("uk-UA");
        var ruUi = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        var deUi = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        Check("empty → Windows language", Loc.FromCode("", ukUi) == UiLanguage.Uk && Loc.FromCode("", ruUi) == UiLanguage.Uk && Loc.FromCode(null, deUi) == UiLanguage.En);
        Check("Code round trip", Loc.All.All(l => Loc.FromCode(Loc.Code(l)) == l));

        Loc.Set(UiLanguage.Uk);
        Check("uk text", Loc.T("T.Settings") == "Налаштування" && (string)Application.Current.Resources["T.Settings"] == "Налаштування");
        Check("uk format", Loc.F("I.Update", "0.3.1", "0.4.0") == "Оновлення 0.3.1 → 0.4.0");
        Loc.Set(UiLanguage.En);
        Check("en text through resources", (string)Application.Current.Resources["S.Overlay.Title"] == "Layout pill");
        Check("en seconds", Loc.F("S.Overlay.Seconds", 1.0) == "1.0 s", Loc.F("S.Overlay.Seconds", 1.0));
        Loc.Set(UiLanguage.Uk);
        Check("uk seconds use a comma", Loc.F("S.Overlay.Seconds", 1.0) == "1,0 с", Loc.F("S.Overlay.Seconds", 1.0));
        Loc.Set(UiLanguage.En);
        Check("unknown key → the key itself", Loc.T("Shift") == "Shift");

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        Check("changelog has this version on top", Changelog.Releases[0].Version == version, $"{Changelog.Releases[0].Version} vs {version}");
        Check("changelog newest first", Changelog.Releases.Zip(Changelog.Releases.Skip(1)).All(p => Version.Parse(p.First.Version) > Version.Parse(p.Second.Version)));
        Check("changelog items in two languages", Changelog.Releases.All(r => r.Items.Length > 0 && r.Items.All(i => i.Length == 2 && i.All(t => !string.IsNullOrWhiteSpace(t)))));
    }

    private static void ColorTests()
    {
        Section("Colors");
        Check("dark colors loaded", ((SolidColorBrush)Application.Current.Resources["Surface0"]).Color == Color.FromRgb(0x0E, 0x0D, 0x12));
        Check("one color dictionary", Application.Current.Resources.MergedDictionaries.Count(d => d.Source?.OriginalString.EndsWith("Dark.xaml") == true) == 1);
    }

    private static void UpdaterTests()
    {
        Section("Updates");

        // Download: full file passes, a cut-off one and a stalled connection fail (run off the UI thread)
        static Exception? Copy(Stream src, long expected, out long got)
        {
            got = 0;
            try { got = Task.Run(() => Updater.CopyAsync(src, Stream.Null, expected, TimeSpan.FromMilliseconds(300))).GetAwaiter().GetResult(); return null; }
            catch (Exception ex) { return ex; }
        }
        var full = Copy(new MemoryStream(new byte[200_000]), 200_000, out var gotFull);
        Check("download: complete file accepted", full is null && gotFull == 200_000, full?.Message);
        var cut = Copy(new MemoryStream(new byte[120_000]), 200_000, out _);
        Check("download: cut-off file refused", cut is InvalidDataException, cut?.GetType().Name);
        var unknown = Copy(new MemoryStream(new byte[5_000]), 0, out var gotUnknown);
        Check("download: no size from server → no size check", unknown is null && gotUnknown == 5_000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stalled = Copy(new StallingStream(50_000), 200_000, out _);
        Check("download: stalled connection times out", stalled is TimeoutException && sw.ElapsedMilliseconds < 5000,
            $"{stalled?.GetType().Name} after {sw.ElapsedMilliseconds} ms");
        Check("tag v0.5.0", Updater.ParseTag("v0.5.0") == new Version(0, 5, 0));
        Check("tag 1.2 → 1.2.0", Updater.ParseTag("1.2") == new Version(1, 2, 0));
        Check("bad tag", Updater.ParseTag("latest") is null && Updater.ParseTag(null) is null);

        string Release(string tag, string asset = "SimpleWindowsTools-Setup.exe", bool pre = false,
            string url = "https://github.com/AM0SK/SimpleWindowsTools/releases/download/v0.6.0/SimpleWindowsTools-Setup.exe") =>
            $$"""
            { "tag_name": "{{tag}}", "draft": false, "prerelease": {{(pre ? "true" : "false")}}, "html_url": "https://github.com/AM0SK/SimpleWindowsTools/releases/tag/{{tag}}",
              "assets": [ { "name": "notes.txt", "browser_download_url": "https://github.com/AM0SK/SimpleWindowsTools/releases/download/x/notes.txt", "size": 10 },
                          { "name": "{{asset}}", "browser_download_url": "{{url}}", "size": 75000000 } ] }
            """;
        var current = new Version(0, 5, 0);
        var found = Updater.ParseLatest(Release("v0.6.0"), current);
        Check("newer release found", found is not null && found.Version == new Version(0, 6, 0) && found.Size == 75000000);
        Check("download url is the setup asset", found?.DownloadUrl.EndsWith("/SimpleWindowsTools-Setup.exe") == true);
        Check("same version → nothing", Updater.ParseLatest(Release("v0.5.0"), current) is null);
        Check("older version → nothing", Updater.ParseLatest(Release("v0.4.0"), current) is null);
        Check("pre-release ignored", Updater.ParseLatest(Release("v0.6.0", pre: true), current) is null);
        Check("no setup asset → nothing", Updater.ParseLatest(Release("v0.6.0", asset: "other.zip"), current) is null);
        Check("asset outside this repo refused", Updater.ParseLatest(Release("v0.6.0", url: "https://example.com/SimpleWindowsTools-Setup.exe"), current) is null);
        Check("current version from the assembly", Updater.CurrentVersion.ToString() == System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));

        var tmp = Path.Combine(SettingsStore.Folder, "fake.exe");
        File.WriteAllText(tmp, "<html>not found</html>");
        Check("HTML page is not an exe", !Updater.LooksLikeExe(tmp));
        var bytes = new byte[2 * 1024 * 1024]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        File.WriteAllBytes(tmp, bytes);
        Check("MZ file of 2 MB passes", Updater.LooksLikeExe(tmp));
        File.Delete(tmp);
    }

    private static void ScreenshotTests()
    {
        Section("Screenshot editor");
        Check("Shift snaps mostly-horizontal line", ScreenshotWindow.Straighten(new Point(0, 0), new Point(100, 12)) == new Point(100, 0));
        Check("Shift snaps mostly-vertical line", ScreenshotWindow.Straighten(new Point(0, 0), new Point(-9, 80)) == new Point(0, 80));

        // Tool order: saved names → full order, keys follow it
        Check("tool order: empty → default", ToolOrder.Normalize(null).SequenceEqual(ToolOrder.Default));
        var saved = ToolOrder.Normalize(new[] { "Blur", "arrow", "Nope", "Blur" });
        Check("tool order: saved first, junk/repeats dropped, missing appended",
            saved.SequenceEqual(new[] { Tool.Blur, Tool.Arrow, Tool.Rectangle, Tool.Line, Tool.Text, Tool.Pencil }), string.Join(",", saved));
        Check("tool order: key 1 = first, 6 = sixth, 7 = none",
            ToolOrder.ForKey(saved, 1) == Tool.Blur && ToolOrder.ForKey(saved, 6) == Tool.Pencil && ToolOrder.ForKey(saved, 7) is null);
        var moved = ToolOrder.Move(ToolOrder.Default, Tool.Pencil, 0);
        Check("tool order: move pencil to the front", moved[0] == Tool.Pencil && moved.Length == 6 && ToolOrder.ForKey(moved, 2) == Tool.Arrow);
        Check("tool order: names round trip", ToolOrder.Normalize(ToolOrder.ToNames(moved)).SequenceEqual(moved));
        Check("new settings → default tool order", new AppSettings().ScreenshotTools.Count == 0);

        // Pencil: a zigzag hand gets smoothed — no sharp corners left
        var pencil = new PencilAnnotation(new Point(0, 0), Colors.Red, 4);
        var raw = new List<Point> { new(0, 0) };
        for (int i = 1; i <= 40; i++)
        {
            var q = new Point(i * 6, (i % 2 == 0 ? 1 : -1) * 4); // jitter ±4 px every step
            raw.Add(q);
            pencil.AddPoint(q);
        }
        pencil.Finish(raw[^1]);
        static double SharpestTurn(IReadOnlyList<Point> pts)
        {
            double worst = 0;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var u = pts[i] - pts[i - 1];
                var v = pts[i + 1] - pts[i];
                if (u.Length < 0.01 || v.Length < 0.01) continue;
                worst = Math.Max(worst, Math.Abs(Vector.AngleBetween(u, v)));
            }
            return worst;
        }
        double rawTurn = SharpestTurn(raw), smoothTurn = SharpestTurn(pencil.Points);
        Check("pencil smooths a zigzag (sharpest turn much smaller)", smoothTurn < rawTurn / 3, $"{rawTurn:0}° → {smoothTurn:0}°");
        Check("pencil keeps its start and ends near the cursor", pencil.Points[0] == new Point(0, 0) && (pencil.Points[^1] - raw[^1]).Length < 15,
            $"{pencil.Points[^1]}");
        var dense = new PencilAnnotation(new Point(0, 0), Colors.Red, 4);
        for (int i = 1; i <= 100; i++) dense.AddPoint(new Point(i * 0.5, 0)); // 50 px in 0.5 px steps
        Check("pencil: tiny moves are thinned out", dense.Points.Count <= 25, dense.Points.Count.ToString());
        Check("pencil hit near the line, miss far away", pencil.Hit(new Point(120, 1)) && !pencil.Hit(new Point(120, 60)));
        pencil.Offset(new Vector(10, 20));
        Check("pencil moves with Shift+drag", pencil.Points[0] == new Point(10, 20));
        Check("pencil click without a drag is dropped", new PencilAnnotation(new Point(5, 5), Colors.Red, 4).IsEmpty);
        Check("pencil spline is one smooth figure", PencilAnnotation.Spline(pencil.Points).Figures.Count == 1);

        var line = new LineAnnotation(false, new Point(10, 10), Colors.Red, 4) { B = new Point(110, 10) };
        line.Update();
        Check("line hit on it", line.Hit(new Point(60, 13)));
        Check("line miss away", !line.Hit(new Point(60, 40)));
        line.Size = 30;
        Check("thicker line hits further", line.Hit(new Point(60, 28)));
        var dot = new LineAnnotation(true, new Point(5, 5), Colors.Red, 4);
        dot.Update();
        Check("zero-length arrow is empty and doesn't crash", dot.IsEmpty);
        var arrow = new LineAnnotation(true, new Point(0, 0), Colors.Red, 4) { B = new Point(200, 100) };
        arrow.Update();
        arrow.Offset(new Vector(10, 5));
        Check("arrow moves", arrow.A == new Point(10, 5) && arrow.B == new Point(210, 105));

        var rect = new RectAnnotation(new Point(100, 100), Colors.Blue, 3) { B = new Point(50, 40) };
        rect.Update();
        Check("rect normalizes reversed drag", rect.Bounds == new Rect(50, 40, 50, 60));
        Check("rect hit inside", rect.Hit(new Point(70, 70)));
        Check("tiny rect is empty", new RectAnnotation(new Point(1, 1), Colors.Blue, 3).IsEmpty);

        // Blur: on black/white stripes the blurred area must be much flatter than the original.
        const int sw = 200, sh = 100;
        var stripes = new byte[sw * sh * 4];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                byte v = (byte)((x / 4) % 2 == 0 ? 0 : 255);
                int i = (y * sw + x) * 4;
                stripes[i] = stripes[i + 1] = stripes[i + 2] = v;
                stripes[i + 3] = 255;
            }
        var stripeBmp = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, stripes, sw * 4);
        stripeBmp.Freeze();
        var blur = new BlurAnnotation(new Point(50, 20), stripeBmp, 1.0, 12) { B = new Point(150, 80) };
        blur.Update();
        var holder = new System.Windows.Controls.Canvas { Width = sw, Height = sh };
        holder.Children.Add(blur.Visual);
        holder.Measure(new Size(sw, sh));
        holder.Arrange(new Rect(0, 0, sw, sh));
        holder.UpdateLayout();
        var shotOfBlur = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
        shotOfBlur.Render(holder);
        var row = new byte[sw * 4];
        shotOfBlur.CopyPixels(new Int32Rect(0, 50, sw, 1), row, sw * 4, 0);
        double Spread(int from, int to)
        {
            int min = 255, max = 0;
            for (int x = from; x < to; x++) { min = Math.Min(min, row[x * 4]); max = Math.Max(max, row[x * 4]); }
            return max - min;
        }
        double inside = Spread(70, 130);
        Check("blur flattens the stripes under it", inside < 80, $"spread inside {inside}");
        Check("blur area keeps solid edges (no see-through)", row[51 * 4 + 3] == 255 && row[148 * 4 + 3] == 255);
        blur.Size = 30;
        blur.Offset(new Vector(10, 0));
        Check("blur strength and move apply", blur.Size == 30 && blur.A == new Point(60, 20));
        Check("tiny blur is empty", new BlurAnnotation(new Point(1, 1), stripeBmp, 1.0, 12).IsEmpty);
        var edge = new BlurAnnotation(new Point(-50, -50), stripeBmp, 1.0, 12) { B = new Point(400, 400) };
        edge.Update();
        Check("blur bigger than the picture doesn't crash", edge.Bounds.Width == 450);

        var text = new TextAnnotation(new Rect(0, 0, 10, 10), Colors.White, 20);
        Check("text box gets a usable minimum width", text.BoxWidth >= 120);
        Check("empty text is dropped", text.IsEmpty);
        text.Box.Text = "hi";
        Check("typed text is kept", !text.IsEmpty);

        // Whole editor: sample scene rendered, crop size must match the selection in real pixels.
        var bmp = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgra32, null, new byte[1280 * 720 * 4], 1280 * 4);
        bmp.Freeze();
        var fake = new CapturedScreen(bmp, new Native.RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 });
        var settings = new AppSettings();
        var editor = new ScreenshotWindow(fake, settings, _ => { });
        var root = editor.DebugScene();
        root.Measure(new Size(1280, 720));
        root.Arrange(new Rect(0, 0, 1280, 720));
        root.UpdateLayout();
        double scale = VisualTreeHelper.GetDpi(editor).DpiScaleX;
        var output = editor.RenderArea();
        int expectW = (int)Math.Round(Math.Round(760 * scale) / scale * scale), expectH = (int)Math.Round(Math.Round(420 * scale) / scale * scale);
        Check("output size = selection in pixels", output.PixelWidth == expectW && output.PixelHeight == expectH,
            $"{output.PixelWidth}×{output.PixelHeight}, expected {expectW}×{expectH}");

        // Something was drawn: the output isn't all transparent/black.
        var pixels = new byte[output.PixelWidth * output.PixelHeight * 4];
        output.CopyPixels(pixels, output.PixelWidth * 4, 0);
        bool hasColor = false;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 2] > 200 && pixels[i + 1] < 120) { hasColor = true; break; } // red arrow
        Check("drawn arrow is in the output", hasColor);
        editor.Close();
    }
}
/// <summary>Gives some bytes, then hangs like a frozen connection (until cancelled).</summary>
internal sealed class StallingStream : Stream
{
    private int _left;
    public StallingStream(int bytes) => _left = bytes;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken ct = default)
    {
        if (_left > 0)
        {
            int n = Math.Min(_left, buffer.Length);
            _left -= n;
            return n;
        }
        await Task.Delay(System.Threading.Timeout.Infinite, ct);
        return 0;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
#endif
