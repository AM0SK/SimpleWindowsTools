using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.UI;

/// <summary>
/// Shows one hotkey and lets the user re-record it. While recording, the app's own hotkeys and key
/// modules are paused (AppController.BeginHotkeyCapture), so the combination reaches this control.
/// </summary>
public partial class HotkeyEditor : UserControl
{
    private AppController? _app;
    private Func<Hotkey?> _get = () => null;
    private Func<Hotkey?> _default = () => null; // null: no default (launcher rows) → no reset button
    private Func<bool> _taken = () => false;
    private Func<Hotkey, string?> _set = _ => null;
    private bool _recording;
    private string? _error;

    public HotkeyEditor()
    {
        InitializeComponent();
    }

    /// <summary>One of the app's features (has a default to reset to).</summary>
    public void Init(AppController app, HotkeyId id)
    {
        _app = app;
        _get = () => app.GetHotkey(id);
        _default = () => app.GetDefaultHotkey(id);
        _taken = () => app.IsHotkeyTaken(id);
        _set = hk => app.TrySetHotkey(id, hk);
        Refresh();
    }

    /// <summary>A "launch apps" row: may have no hotkey yet, no default.</summary>
    public void InitLauncher(AppController app, string launcherId)
    {
        _app = app;
        _get = () => app.GetLauncherHotkey(launcherId);
        _default = () => null;
        _taken = () => app.IsLauncherTaken(launcherId);
        _set = hk => app.TrySetLauncherHotkey(launcherId, hk);
        Refresh();
    }

    /// <summary>Starts recording right away (a launcher row that was just added).</summary>
    public void StartRecording() => Chip_Click(this, new RoutedEventArgs());

    public void Refresh()
    {
        if (_app is null || _recording) return;
        var current = _get();
        var def = _default();

        ChipText.Text = current?.Display ?? Loc.T("HK.NotSet");
        ChipText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, current is null ? "InkMuted" : "Ink");
        EditIcon.Visibility = Visibility.Visible;

        ResetButton.Visibility = def is null ? Visibility.Collapsed : Visibility.Visible;
        if (def is { } d)
        {
            bool isDefault = current == d;
            ResetButton.IsEnabled = !isDefault;
            ResetButton.Opacity = isDefault ? 0.35 : 1;
            ResetButton.ToolTip = isDefault ? Loc.T("HK.IsDefault") : Loc.F("HK.Reset", d.Display);
        }

        string? message = _error ?? (_taken() ? Loc.T("HK.Taken") : null);
        Chip.Tag = message is null ? null : "error";
        Message.Text = message ?? "";
        Message.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Critical");
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        Chip.ToolTip = Loc.T("HK.Click");
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (_app is null || _recording) return;
        _recording = true;
        _error = null;
        _app.BeginHotkeyCapture();
        Chip.Tag = "recording";
        Message.Visibility = Visibility.Collapsed;
        EditIcon.Visibility = Visibility.Collapsed;
        ShowPrompt(ModifierKeys.None);
        Chip.Focus();
    }

    private void Chip_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true; // nothing reaches the window while recording (Alt+F4 included)

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            Stop(null);
            return;
        }
        if (Hotkey.IsModifierKey(key) || key is Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
        {
            ShowPrompt(mods);
            return;
        }

        // Ctrl, Alt or Win required (Shift alone would eat normal typing), except for F1–F24 and PrintScreen.
        bool hasRealModifier = (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
        if (!hasRealModifier && !Hotkey.IsFunctionKey(key) && key != Key.Snapshot)
        {
            ShowPrompt(mods, Loc.T("HK.AddMod"));
            return;
        }

        Stop(Hotkey.From(mods, key));
    }

    private void Chip_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        // PrintScreen never sends a key-down to windows, only the key-up.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot)
        {
            Stop(Hotkey.From(Keyboard.Modifiers, key));
            return;
        }
        ShowPrompt(Keyboard.Modifiers);
    }

    private void Chip_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recording) Stop(null);
    }

    private void Stop(Hotkey? result)
    {
        if (_app is null) return;
        _recording = false;
        _app.EndHotkeyCapture(); // hotkeys back first, so TrySetHotkey can tell if the new one is taken
        if (result is { } hk) _error = _set(hk);
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_app is null || _default() is not { } def) return;
        _error = _set(def);
        Refresh();
    }

    private void ShowPrompt(ModifierKeys mods, string? hint = null)
    {
        string held = "";
        if (mods.HasFlag(ModifierKeys.Control)) held += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) held += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) held += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) held += "Win+";

        ChipText.Text = held.Length > 0 ? held + "…" : Loc.T("HK.Press");
        ChipText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentSolid");
        Message.Text = hint ?? Loc.T("HK.EscCancel");
        Message.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, hint is null ? "InkMuted" : "Warning");
        Message.Visibility = Visibility.Visible;
    }
}
