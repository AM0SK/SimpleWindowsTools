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
    private HotkeyId _id;
    private bool _recording;
    private string? _error;

    public HotkeyEditor()
    {
        InitializeComponent();
    }

    public void Init(AppController app, HotkeyId id)
    {
        _app = app;
        _id = id;
        Refresh();
    }

    public void Refresh()
    {
        if (_app is null || _recording) return;
        var current = _app.GetHotkey(_id);
        var def = _app.GetDefaultHotkey(_id);

        ChipText.Text = current.Display;
        ChipText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Ink");
        EditIcon.Visibility = Visibility.Visible;

        bool isDefault = current == def;
        ResetButton.IsEnabled = !isDefault;
        ResetButton.Opacity = isDefault ? 0.35 : 1;
        ResetButton.ToolTip = isDefault ? Loc.T("HK.IsDefault") : Loc.F("HK.Reset", def.Display);

        string? message = _error ?? (_app.IsHotkeyTaken(_id) ? Loc.T("HK.Taken") : null);
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
        if (result is { } hk) _error = _app.TrySetHotkey(_id, hk);
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_app is null) return;
        _error = _app.TrySetHotkey(_id, _app.GetDefaultHotkey(_id));
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
