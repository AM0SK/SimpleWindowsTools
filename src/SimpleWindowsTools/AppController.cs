using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using SimpleWindowsTools.Core;
using SimpleWindowsTools.Modules;
using SimpleWindowsTools.UI;

namespace SimpleWindowsTools;

/// <summary>
/// Owns the modules, settings and hotkeys; the tray menu and the settings window both go through it,
/// so a change made in one place shows up everywhere. New features (screenshots, …) plug in here.
/// </summary>
public enum HotkeyId { ToggleOverlay, ToggleSkip, ToggleWinBlock, EmDash, Screenshot, Exit, Taskbar }

public sealed class AppController : IDisposable
{
    private readonly KeyboardHook _hook = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly OverlayWindow _overlay = new();
    private readonly LayoutWatcher _layout;
    private readonly WinKeyBlocker _winBlocker;
    private readonly EmDash _emDash;
    private readonly HookHotkey _screenshotKey;
    private Screenshot.ScreenshotWindow? _screenshot;

    public AppSettings Settings { get; }

    /// <summary>Raised after any setting or module state changes (tray menu and settings window refresh).</summary>
    public event Action? Changed;

    private readonly Dictionary<HotkeyId, HotkeyDef> _defs;
    private readonly HashSet<HotkeyId> _failed = new();

    public event Action? ExitRequested;

    public AppController()
    {
        Settings = SettingsStore.Load();
        var dispatcher = Dispatcher.CurrentDispatcher;

        _winBlocker = new WinKeyBlocker(_hook, dispatcher);
        _emDash = new EmDash(_hook, dispatcher);
        _screenshotKey = new HookHotkey(_hook, dispatcher, TakeScreenshot);
        _layout = new LayoutWatcher(_hook, _overlay, () => Settings);

        _defs = new HotkeyDef[]
        {
            new(HotkeyId.ToggleOverlay, "C.Title.Overlay", HotkeyDefaults.ToggleOverlay,
                s => s.ToggleOverlayHotkey, (s, v) => s.ToggleOverlayHotkey = v,
                () => SetLayoutOverlay(!Settings.LayoutOverlayEnabled, notify: true)),
            new(HotkeyId.ToggleSkip, "C.Title.Skip", HotkeyDefaults.ToggleSkip,
                s => s.ToggleSkipHotkey, (s, v) => s.ToggleSkipHotkey = v,
                () => SetSkipLanguages(!Settings.SkipLanguagesEnabled, notify: true)),
            new(HotkeyId.ToggleWinBlock, "C.Title.Win", HotkeyDefaults.ToggleWinBlock,
                s => s.ToggleWinBlockHotkey, (s, v) => s.ToggleWinBlockHotkey = v,
                () => SetWinBlock(!Settings.WinBlockEnabled, notify: true)),
            new(HotkeyId.EmDash, "C.Title.Dash", HotkeyDefaults.EmDash,
                s => s.EmDashHotkey, (s, v) => s.EmDashHotkey = v,
                null), // handled in the keyboard hook by EmDash
            new(HotkeyId.Screenshot, "C.Title.Shot", HotkeyDefaults.Screenshot,
                s => s.ScreenshotHotkey, (s, v) => s.ScreenshotHotkey = v,
                null), // handled in the keyboard hook: PrintScreen must be taken from the Snipping Tool
            new(HotkeyId.Taskbar, "C.Title.Taskbar", HotkeyDefaults.Taskbar,
                s => s.TaskbarHotkey, (s, v) => s.TaskbarHotkey = v,
                () => SetTaskbarAutoHide(!TaskbarAutoHide.IsOn, notify: true)),
            new(HotkeyId.Exit, "C.Title.Exit", HotkeyDefaults.Exit,
                s => s.ExitHotkey, (s, v) => s.ExitHotkey = v,
                RequestExit),
        }.ToDictionary(d => d.Id);

        _hook.Start();
        Apply();
        RegisterHotkeys();
    }

    public void SetLayoutOverlay(bool on, bool notify = false)
    {
        Settings.LayoutOverlayEnabled = on;
        Commit();
        if (notify) Toast(Loc.T(on ? "C.LayoutOn" : "C.LayoutOff"));
    }

    public void SetSkipLanguages(bool on, bool notify = false)
    {
        Settings.SkipLanguagesEnabled = on;
        Commit();
        if (on) _layout.LeaveSkippedLayoutNow();
        if (!notify) return;

        var codes = SkippedLanguageCodes();
        if (codes.Length == 0) Toast(Loc.T("C.NoSkipLangs"));
        else Toast(Loc.F(on ? "C.Skipped" : "C.InSwitch", codes));
    }

    public void SetLanguageSkipped(int langId, bool skipped)
    {
        Settings.SkippedLanguages.Remove(langId);
        if (skipped) Settings.SkippedLanguages.Add(langId);
        Commit();
        if (skipped) _layout.LeaveSkippedLayoutNow();
    }

    private string SkippedLanguageCodes() => string.Join(", ",
        Settings.SkippedLanguages.Select(id => LayoutWatcher.LanguageCode(new IntPtr(id))).Distinct());

    public void SetWinBlock(bool on, bool notify = false)
    {
        Settings.WinBlockEnabled = on;
        Commit();
        if (notify) Toast(Loc.T(on ? "C.WinBlocked" : "C.WinWorks"));
    }

    /// <summary>Windows' taskbar auto-hide. Not a setting of ours: Windows keeps it, we only flip it.</summary>
    public void SetTaskbarAutoHide(bool on, bool notify = false)
    {
        bool ok = TaskbarAutoHide.Set(on);
        if (!ok) Log.Info($"Taskbar auto-hide -> {on} was not applied");
        Changed?.Invoke();
        if (notify || !ok) Toast(Loc.T(!ok ? "C.TaskbarFailed" : on ? "C.TaskbarHidden" : "C.TaskbarShown"));
    }

    public void SetEmDash(bool on)
    {
        Settings.EmDashEnabled = on;
        Commit();
    }

    /// <summary>Switches the interface language right away (open windows follow through resources).</summary>
    public void SetLanguage(UiLanguage lang)
    {
        Settings.Language = Loc.Code(lang);
        Loc.Set(lang);
        Commit();
    }

    // ===== Updates (GitHub Releases, see Core/Updater.cs) =====

    public UpdateInfo? AvailableUpdate { get; private set; }
    public DateTime? LastUpdateCheck { get; private set; }
    public bool CheckingUpdates { get; private set; }
    public string? UpdateError { get; private set; }
    /// <summary>0..1 while the setup exe downloads, null otherwise.</summary>
    public double? UpdateDownload { get; private set; }

    /// <summary>Raised when anything about updates changes (settings window shows it).</summary>
    public event Action? UpdateStateChanged;
    /// <summary>A new version was found that hasn't been announced yet (tray shows a notification).</summary>
    public event Action<UpdateInfo>? UpdateAnnounced;

    private DispatcherTimer? _updateTimer;

    // ===== Memory =====
    // WPF + .NET keep ~100 MB of pages around after a window has been shown. The app idles in the tray
    // for hours, so hand untouched pages back to Windows: shortly after a window closes, and every few
    // minutes. Pages come back on demand (from RAM, the standby list — cheap).
    private static readonly TimeSpan TrimInterval = TimeSpan.FromMinutes(3);
    private DispatcherTimer? _trimTimer;
    private DispatcherTimer? _trimSoon;

    public void StartMemoryTrim()
    {
        _trimTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TrimInterval };
        _trimTimer.Tick += (_, _) => { if (_screenshot is null) Native.EmptyWorkingSetSelf(); };
        _trimTimer.Start();
        TrimSoon();
    }

    /// <summary>A window just closed: collect its garbage and trim in a few seconds (once it's really gone).</summary>
    public void TrimSoon()
    {
        if (_trimSoon is null)
        {
            _trimSoon = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
            _trimSoon.Tick += (_, _) =>
            {
                _trimSoon.Stop();
                if (_screenshot is not null) return;
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Native.EmptyWorkingSetSelf();
            };
        }
        _trimSoon.Stop();
        _trimSoon.Start();
    }
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>First check shortly after start (not at logon itself — the network may not be up), then every 6 hours.</summary>
    public void StartUpdateChecks()
    {
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = CheckInterval;
            if (Settings.CheckUpdates) await CheckForUpdates(manual: false);
        };
        _updateTimer.Start();
    }

    public void SetCheckUpdates(bool on)
    {
        Settings.CheckUpdates = on;
        Commit();
        if (!on) { AvailableUpdate = null; UpdateError = null; UpdateStateChanged?.Invoke(); }
    }

    public async Task CheckForUpdates(bool manual)
    {
        if (CheckingUpdates) return;
        CheckingUpdates = true;
        UpdateError = null;
        UpdateStateChanged?.Invoke();
        try
        {
            AvailableUpdate = await Updater.CheckAsync();
            LastUpdateCheck = DateTime.Now;
            var found = AvailableUpdate;
            if (found is not null && (manual || Settings.UpdateNotifiedVersion != found.Version.ToString()))
            {
                Settings.UpdateNotifiedVersion = found.Version.ToString();
                SettingsStore.Save(Settings);
                if (!manual) UpdateAnnounced?.Invoke(found);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            UpdateError = ex is System.Net.Http.HttpRequestException or TaskCanceledException ? "GitHub" : ex.Message;
        }
        finally
        {
            CheckingUpdates = false;
            UpdateStateChanged?.Invoke();
        }
    }

    /// <summary>Downloads the new setup and starts it; the setup closes this copy and replaces the exe.</summary>
    public async Task<string?> InstallUpdate()
    {
        var update = AvailableUpdate;
        if (update is null || UpdateDownload is not null) return null;
        try
        {
            UpdateDownload = 0;
            UpdateStateChanged?.Invoke();
            // The offer may be hours old: if an even newer release came out meanwhile, take that one.
            try
            {
                if (await Updater.CheckAsync() is { } fresh && fresh.Version > update.Version)
                {
                    update = AvailableUpdate = fresh;
                    UpdateStateChanged?.Invoke();
                }
            }
            catch (Exception ex) { Log.Info($"Re-check before download failed, using {update.Version}: {ex.Message}"); }
            var progress = new Progress<double>(p => { UpdateDownload = p; UpdateStateChanged?.Invoke(); });
            var file = await Updater.DownloadAsync(update, progress);
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Update download failed", ex);
            return ex.Message;
        }
        finally
        {
            UpdateDownload = null;
            UpdateStateChanged?.Invoke();
        }
    }

    /// <summary>New toolbar order of the screenshot editor tools (keys 1–9 follow it).</summary>
    public void SetScreenshotTools(IEnumerable<Screenshot.Tool> order)
    {
        Settings.ScreenshotTools = Screenshot.ToolOrder.ToNames(order);
        Commit();
    }

    public void SetScreenshot(bool on)
    {
        Settings.ScreenshotEnabled = on;
        Commit();
    }

    /// <summary>Opens the area-selection editor on the monitor under the cursor.</summary>
    public void TakeScreenshot()
    {
        if (_screenshot is not null)
        {
            _screenshot.Activate();
            return;
        }
        try
        {
            _overlay.HideNow(); // don't capture our own pill
            var shot = Screenshot.ScreenCapture.CaptureMonitorUnderCursor();
            _screenshot = new Screenshot.ScreenshotWindow(shot, Settings, Toast);
            _screenshot.Closed += (_, _) =>
            {
                _screenshot = null;
                SettingsStore.Save(Settings); // color / thickness / folder picked in the editor
                // A shot holds full-screen bitmaps (capture + render, ~15 MB each at 1440p). Give the
                // memory back soon instead of whenever the GC gets to it — the app idles for hours.
                TrimSoon();
            };
            _screenshot.Show();
        }
        catch (Exception ex)
        {
            _screenshot = null;
            Log.Error("Screenshot failed", ex);
            Toast(Loc.T("C.ShotFailed"));
        }
    }

    public void SetOverlayLook(int durationMs, int fontSize)
    {
        Settings.OverlayDurationMs = durationMs;
        Settings.OverlayFontSize = fontSize;
        Commit();
    }

    public void PreviewOverlay()
    {
        var layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero));
        _overlay.ShowText(LayoutWatcher.LanguageCode(layout), Native.GetForegroundWindow(), Settings.OverlayDurationMs, Settings.OverlayFontSize);
    }

    public void RequestExit() => ExitRequested?.Invoke();

    /// <summary>AutoHotkey still running would fight over the same keys.</summary>
    public static bool IsAutoHotkeyRunning() =>
        Process.GetProcesses().Any(p => p.ProcessName.StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase));

    public void Toast(string text) =>
        _overlay.ShowText(text, Native.GetForegroundWindow(), 1200, Math.Max(13, Settings.OverlayFontSize - 4));

    private void Commit()
    {
        Apply();
        SettingsStore.Save(Settings);
        Changed?.Invoke();
    }

    private void Apply()
    {
        _layout.ShowOverlayEnabled = Settings.LayoutOverlayEnabled;
        // The watcher also does the skipping, so it runs if either feature needs it.
        _layout.Enabled = Settings.LayoutOverlayEnabled
            || (Settings.SkipLanguagesEnabled && Settings.SkippedLanguages.Count > 0);
        _winBlocker.Enabled = Settings.WinBlockEnabled;
        _emDash.Enabled = Settings.EmDashEnabled;
        _screenshotKey.Enabled = Settings.ScreenshotEnabled;
    }

    // ===== Hotkeys =====

    public Hotkey GetHotkey(HotkeyId id) => Parse(_defs[id], _defs[id].Get(Settings));

    public Hotkey GetDefaultHotkey(HotkeyId id) => Parse(_defs[id], _defs[id].Default);

    /// <summary>Registered hotkey that another program already holds.</summary>
    public bool IsHotkeyTaken(HotkeyId id) => _failed.Contains(id);

    public bool AnyHotkeyTaken => _failed.Count > 0;

    /// <summary>Applies a new combination. Returns an error to show, or null on success.</summary>
    public string? TrySetHotkey(HotkeyId id, Hotkey hotkey)
    {
        var def = _defs[id];
        foreach (var other in _defs.Values)
            if (other.Id != id && GetHotkey(other.Id) == hotkey)
                return Loc.F("C.InUse", Loc.T(other.TitleKey));

        if (def.Action is not null && !_capturing)
        {
            if (!_hotkeys.Set(def.Id.ToString(), hotkey.ToString(), def.Action))
            {
                RegisterOne(def); // put the old one back
                return Loc.T("C.TakenByOther");
            }
            _failed.Remove(id);
        }

        def.Set(Settings, hotkey.ToString());
        _emDash.Combo = GetHotkey(HotkeyId.EmDash);
        _screenshotKey.Combo = GetHotkey(HotkeyId.Screenshot);
        SettingsStore.Save(Settings);
        Changed?.Invoke();
        return null;
    }

    private bool _capturing;

    /// <summary>
    /// The settings window is recording a combination: release our hotkeys and pause the modules,
    /// so the keys reach the window instead of triggering us.
    /// </summary>
    public void BeginHotkeyCapture()
    {
        _capturing = true;
        _hook.Suspended = true;
        foreach (var def in _defs.Values) _hotkeys.Remove(def.Id.ToString());
    }

    public void EndHotkeyCapture()
    {
        _capturing = false;
        _hook.Suspended = false;
        RegisterHotkeys();
        Changed?.Invoke();
    }

    private void RegisterHotkeys()
    {
        _failed.Clear();
        foreach (var def in _defs.Values) RegisterOne(def);
        _emDash.Combo = GetHotkey(HotkeyId.EmDash);
        _screenshotKey.Combo = GetHotkey(HotkeyId.Screenshot);
    }

    private void RegisterOne(HotkeyDef def)
    {
        if (def.Action is null || _capturing) return;
        if (!_hotkeys.Set(def.Id.ToString(), GetHotkey(def.Id).ToString(), def.Action)) _failed.Add(def.Id);
        else _failed.Remove(def.Id);
    }

    private static Hotkey Parse(HotkeyDef def, string text) =>
        Hotkey.TryParse(text, out var hk) ? hk : Hotkey.TryParse(def.Default, out var d) ? d : default;

    private sealed record HotkeyDef(HotkeyId Id, string TitleKey, string Default,
        Func<AppSettings, string> Get, Action<AppSettings, string> Set, Action? Action);


#if DEBUG
    /// <summary>Docs screenshots: don't show "taken by another app" (the installed copy holds the hotkeys).</summary>
    internal void ForgetHotkeyConflicts() { _failed.Clear(); Changed?.Invoke(); }

    /// <summary>Renders: pretend a release is on offer, to see the banner.</summary>
    internal void FakeUpdateOffer(UpdateInfo? update) { AvailableUpdate = update; LastUpdateCheck = DateTime.Now; UpdateStateChanged?.Invoke(); }
#endif

    public void Dispose()
    {
        _updateTimer?.Stop();
        _trimTimer?.Stop();
        _trimSoon?.Stop();
        _layout.Dispose();
        _hook.Dispose();
        _hotkeys.Dispose();
        _overlay.Close();
    }
}
