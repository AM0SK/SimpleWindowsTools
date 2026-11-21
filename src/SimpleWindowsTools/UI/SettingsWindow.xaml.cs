using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using SimpleWindowsTools.Modules;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.UI;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private bool _loading;

    public SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();

        WindowFrame.Apply(this); // dark frame; rounded corners on 11, a 1 px border on 10

        AhkWarning.Visibility = AppController.IsAutoHotkeyRunning() ? Visibility.Visible : Visibility.Collapsed;
        OverlayHotkey.Init(_app, HotkeyId.ToggleOverlay);
        SkipHotkey.Init(_app, HotkeyId.ToggleSkip);
        BuildLanguageChips();
        WinHotkey.Init(_app, HotkeyId.ToggleWinBlock);
        TaskbarHotkey.Init(_app, HotkeyId.Taskbar);
        DashHotkey.Init(_app, HotkeyId.EmDash);
        ShotHotkey.Init(_app, HotkeyId.Screenshot);
        ExitHotkey.Init(_app, HotkeyId.Exit);

        BuildUiLanguageChips();
        ApplyLanguage(); // also does Refresh()
        _app.Changed += Refresh;
        Loc.Changed += ApplyLanguage;
        _app.UpdateStateChanged += RefreshUpdates;
        Closed += (_, _) => { _app.Changed -= Refresh; Loc.Changed -= ApplyLanguage; _app.UpdateStateChanged -= RefreshUpdates; };
        HistoryScroll.PreviewMouseWheel += PassWheelAtEdges;

        LoadAutostartState();
        Activated += (_, _) =>
        {
            LoadAutostartState(); // may have been switched off in Task Manager meanwhile
            TaskbarSwitch.IsChecked = Modules.TaskbarAutoHide.IsOn; // or auto-hide in Windows settings
            BuildShotTools();     // may have been reordered in the editor meanwhile
        };
        UI.DragReorder.Attach(ShotTools, tags =>
        {
            _app.SetScreenshotTools(tags.Cast<Screenshot.Tool>());
            BuildShotTools();
        });
    }

    private void Refresh()
    {
        _loading = true;
        var s = _app.Settings;
        OverlaySwitch.IsChecked = s.LayoutOverlayEnabled;
        OverlayOptions.IsEnabled = s.LayoutOverlayEnabled;
        OverlayOptions.Opacity = s.LayoutOverlayEnabled ? 1 : 0.45;
        WinSwitch.IsChecked = s.WinBlockEnabled;
        TaskbarSwitch.IsChecked = Modules.TaskbarAutoHide.IsOn; // read from Windows: may be changed in its settings
        DashSwitch.IsChecked = s.EmDashEnabled;
        DurationSlider.Value = s.OverlayDurationMs;
        FontSlider.Value = s.OverlayFontSize;
        DurationText.Text = Loc.F("S.Overlay.Seconds", s.OverlayDurationMs / 1000.0);
        FontText.Text = $"{s.OverlayFontSize} px";
        OverlayHotkey.Refresh();
        SkipHotkey.Refresh();
        SkipSwitch.IsChecked = s.SkipLanguagesEnabled;
        foreach (var chip in SkipLanguages.Children.OfType<ToggleButton>())
            chip.IsChecked = s.SkippedLanguages.Contains((int)chip.Tag);
        WinHotkey.Refresh();
        TaskbarHotkey.Refresh();
        DashHotkey.Refresh();
        ShotHotkey.Refresh();
        ShotSwitch.IsChecked = s.ScreenshotEnabled;
        ExitHotkey.Refresh();
        foreach (var chip in UiLanguages.Children.OfType<ToggleButton>())
            chip.IsChecked = (UiLanguage)chip.Tag == Loc.Current;
        UpdatesSwitch.IsChecked = s.CheckUpdates;
        _loading = false;
        RefreshUpdates();
    }

    // ===== Tabs =====

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (FeaturesPage is null || AppPage is null) return; // during InitializeComponent
        bool app = TabApp.IsChecked == true;
        FeaturesPage.Visibility = app ? Visibility.Collapsed : Visibility.Visible;
        AppPage.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
        Scroll.ScrollToTop();
    }

    public void ShowAppTab()
    {
        TabApp.IsChecked = true;
        // Opened from the update toast / tray item: the updates card is at the bottom — scroll to it.
        if (_app.AvailableUpdate is not null)
            Dispatcher.BeginInvoke(() => UpdateBanner.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ===== Updates =====

    private void RefreshUpdates()
    {
        var update = _app.AvailableUpdate;
        UpdateBanner.Visibility = update is not null && _app.Settings.CheckUpdates || update is not null && _app.UpdateDownload is not null
            ? Visibility.Visible : Visibility.Collapsed;
        if (update is not null)
        {
            UpdateBannerTitle.Text = Loc.F("S.Updates.Available", update.Version);
            InstallUpdateButton.Content = _app.UpdateDownload is double p
                ? Loc.F("S.Updates.Downloading", p * 100)
                : Loc.T("S.Updates.Install");
            InstallUpdateButton.IsEnabled = _app.UpdateDownload is null;
        }

        CheckUpdatesButton.IsEnabled = !_app.CheckingUpdates;
        UpdateStatus.Text = _app.CheckingUpdates ? Loc.T("S.Updates.Checking")
            : _app.UpdateError is string err ? Loc.F("S.Updates.Failed", err)
            : _app.LastUpdateCheck is DateTime t ? (update is null ? Loc.F("S.Updates.UpToDate", FormatCheckTime(t)) : "")
            : Loc.T("S.Updates.Never");
        UpdateStatus.SetResourceReference(TextBlock.ForegroundProperty, _app.UpdateError is null ? "InkMuted" : "Warning");
        UpdateStatus.Visibility = string.IsNullOrEmpty(UpdateStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatCheckTime(DateTime t) =>
        t.Date == DateTime.Today ? t.ToString("HH:mm", Loc.Culture) : t.ToString("d MMM, HH:mm", Loc.Culture);

    private void UpdatesSwitch_Click(object sender, RoutedEventArgs e) => _app.SetCheckUpdates(UpdatesSwitch.IsChecked == true);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await _app.CheckForUpdates(manual: true);

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        var error = await _app.InstallUpdate();
        if (error is not null)
            MessageBox.Show(this, Loc.F("S.Updates.DownloadFailed", error), "Simple Windows Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UpdatePage_Click(object sender, RoutedEventArgs e)
    {
        var url = _app.AvailableUpdate?.PageUrl ?? Updater.ReleasesUrl;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Opening the release page failed", ex); }
    }

    /// <summary>The history list scrolls inside the page; at its top/bottom the wheel goes on to the page.</summary>
    private void PassWheelAtEdges(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        bool atTop = sv.VerticalOffset <= 0, atBottom = sv.VerticalOffset >= sv.ScrollableHeight;
        if (sv.ScrollableHeight > 0 && !(e.Delta > 0 && atTop) && !(e.Delta < 0 && atBottom)) return;
        e.Handled = true;
        Scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
    }

    // schtasks takes ~100 ms; keep it off the UI thread.
    private async void LoadAutostartState()
    {
        AutostartSwitch.IsEnabled = false;
        bool on = await Task.Run(Autostart.IsEnabled);
        AutostartSwitch.IsChecked = on;
        AutostartSwitch.IsEnabled = true;
    }

    /// <summary>Screenshot controls for the accordion: group → (keys, what they do). All are string keys.</summary>
    internal static readonly (string Group, (string Keys, string Text)[] Rows)[] ShotCommands =
    {
        ("H.Area", new[]
        {
            ("H.Drag", "H.SelectArea"),
            ("H.Corner", "H.Resize"),
            ("H.Side", "H.Move"),
            ("H.Outside", "H.Reselect"),
        }),
        ("H.Drawing", new[]
        {
            ("H.Toolbar", "H.ToolbarText"),
            ("1 … 6", "H.PickTool"),
            ("H.Pencil", "H.PencilText"),
            ("H.Blur", "H.BlurText"),
            ("H.Drag", "H.Draw"),
            ("Shift", "H.ShiftText"),
            ("H.Text", "H.TextText"),
            ("H.ClickOut", "H.FinishText"),
            ("H.ClickText", "H.EditText"),
        }),
        ("H.Drawn", new[]
        {
            ("H.ShiftDrag", "H.MoveElement"),
            ("H.Wheel", "H.WheelText"),
            ("H.Color", "H.ColorText"),
            ("Ctrl+Z", "H.Undo"),
        }),
        ("H.Result", new[]
        {
            ("Enter / Ctrl+C", "H.Copy"),
            ("Ctrl+S", "H.Save"),
            ("Esc", "H.Close"),
        }),
    };

    /// <summary>Texts set from code; re-run when the language changes (XAML texts follow by themselves).</summary>
    private void ApplyLanguage()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = Loc.F("S.Version", version?.ToString(3), SettingsStore.Folder);
        BuildShotHelp();
        BuildShotTools();
        BuildHistory();
        Refresh();
    }

    private void BuildUiLanguageChips()
    {
        foreach (var lang in Loc.All)
        {
            var chip = new ToggleButton
            {
                Content = Loc.NativeName(lang),
                Tag = lang,
                Style = (Style)FindResource("ChoiceChip"),
            };
            chip.Click += (_, _) =>
            {
                if (lang == Loc.Current) { chip.IsChecked = true; return; } // it's a choice, not a toggle
                _app.SetLanguage(lang);
            };
            UiLanguages.Children.Add(chip);
        }
    }

    /// <summary>Update history, newest first. Lives in a fixed-height box that scrolls (HistoryScroll).</summary>
    private void BuildHistory()
    {
        History.Children.Clear();
        var releases = Changelog.Releases;
        for (int i = 0; i < releases.Length; i++)
        {
            var r = releases[i];
            var title = new TextBlock
            {
                Text = $"{r.Version}  ·  {r.Date.ToString("d MMMM yyyy", Loc.Culture)}",
                Style = (Style)FindResource("Text"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, i == 0 ? 0 : 14, 0, 6),
            };
            if (i == 0) title.SetResourceReference(TextBlock.ForegroundProperty, "AccentSolid");
            History.Children.Add(title);
            foreach (var item in r.Items)
                History.Children.Add(new TextBlock
                {
                    Text = "•  " + Loc.Pick(item),
                    Style = (Style)FindResource(i == 0 ? "Text" : "Muted"),
                    Margin = new Thickness(0, 0, 0, 4),
                });
        }
    }

    private void BuildShotHelp()
    {
        ShotHelp.Children.Clear();
        bool first = true;
        foreach (var (group, rows) in ShotCommands)
        {
            ShotHelp.Children.Add(new TextBlock
            {
                Text = Loc.T(group).ToUpper(Loc.Culture),
                Style = (Style)FindResource("SectionLabel"),
                Margin = new Thickness(0, first ? 6 : 14, 0, 6),
            });
            first = false;

            foreach (var (keys, text) in rows)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var chip = new Border
                {
                    Style = (Style)FindResource("KeyChip"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = Loc.T(keys), Style = (Style)FindResource("KeyChipText") },
                };
                var label = new TextBlock
                {
                    Text = Loc.T(text),
                    Style = (Style)FindResource("Muted"),
                    Margin = new Thickness(10, 2, 0, 0),
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(chip);
                row.Children.Add(label);
                ShotHelp.Children.Add(row);
            }
        }
    }

    /// <summary>Editor tools in toolbar order: number (its key) + icon. Drag to reorder.</summary>
    private void BuildShotTools()
    {
        ShotTools.Children.Clear();
        int n = 1;
        foreach (var tool in Screenshot.ToolOrder.Normalize(_app.Settings.ScreenshotTools))
        {
            var (icon, thickness, hint) = Screenshot.ToolOrder.Look(tool);
            var number = new TextBlock { Text = n.ToString(), Style = (Style)FindResource("KeyChipText"), VerticalAlignment = VerticalAlignment.Center };
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
            var chip = new Border
            {
                Style = (Style)FindResource("KeyChip"),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.SizeWE,
                Tag = tool,
                ToolTip = Loc.F(hint, n) + " · " + Loc.T("Sh.DragHint"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { number, path } },
            };
            ShotTools.Children.Add(chip);
            n++;
        }
    }

    /// <summary>One chip per installed input language, in the system order.</summary>
    private void BuildLanguageChips()
    {
        var seen = new HashSet<int>();
        foreach (var layout in LayoutWatcher.InstalledLayouts())
        {
            int id = LayoutWatcher.LangId(layout);
            if (!seen.Add(id)) continue;
            var chip = new ToggleButton
            {
                Content = LayoutWatcher.LanguageCode(layout),
                Tag = id,
                Style = (Style)FindResource("LangChip"),
                ToolTip = System.Globalization.CultureInfo.GetCultureInfo(id).DisplayName,
            };
            chip.Click += (_, _) => _app.SetLanguageSkipped(id, chip.IsChecked == true);
            SkipLanguages.Children.Add(chip);
        }
    }

    private void ShotSwitch_Click(object sender, RoutedEventArgs e) => _app.SetScreenshot(ShotSwitch.IsChecked == true);

    private void SkipSwitch_Click(object sender, RoutedEventArgs e) => _app.SetSkipLanguages(SkipSwitch.IsChecked == true);

    private void OverlaySwitch_Click(object sender, RoutedEventArgs e) => _app.SetLayoutOverlay(OverlaySwitch.IsChecked == true);
    private void WinSwitch_Click(object sender, RoutedEventArgs e) => _app.SetWinBlock(WinSwitch.IsChecked == true);
    private void TaskbarSwitch_Click(object sender, RoutedEventArgs e) => _app.SetTaskbarAutoHide(TaskbarSwitch.IsChecked == true);
    private void DashSwitch_Click(object sender, RoutedEventArgs e) => _app.SetEmDash(DashSwitch.IsChecked == true);

    private void Look_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !IsInitialized) return;
        _app.SetOverlayLook((int)DurationSlider.Value, (int)FontSlider.Value);
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => _app.PreviewOverlay();

    private async void AutostartSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool want = AutostartSwitch.IsChecked == true;
        AutostartSwitch.IsEnabled = false;
        bool ok = await Task.Run(() => want ? Autostart.Enable() : Autostart.Disable());
        AutostartSwitch.IsChecked = ok ? want : !want;
        AutostartSwitch.IsEnabled = true;
        if (!ok)
            MessageBox.Show(this, Loc.T("S.Autostart.Failed"),
                "Simple Windows Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => _app.RequestExit();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
