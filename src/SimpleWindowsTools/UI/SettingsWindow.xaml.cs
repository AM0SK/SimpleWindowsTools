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
using System.Windows.Input;
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
        BuildLaunchRows();
        BuildCharRows();
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
        CharsSwitch.IsChecked = s.CharsEnabled;
        CharRows.Opacity = s.CharsEnabled ? 1 : 0.45;
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
        foreach (var editor in _launchEditors) editor.Refresh();
        foreach (var editor in _charEditors) editor.Refresh();
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
        // An update is already on offer: its banner is the action; "check now" would only add noise.
        CheckUpdatesButton.Visibility = UpdateBanner.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatus.Text = _app.CheckingUpdates ? Loc.T("S.Updates.Checking")
            : _app.UpdateError is string err ? Loc.F("S.Updates.Failed", err)
            : _app.LastUpdateCheck is DateTime t ? (update is null ? Loc.F("S.Updates.UpToDate", FormatCheckTime(t)) : "")
            : Loc.T("S.Updates.Never");
        UpdateStatus.SetResourceReference(TextBlock.ForegroundProperty, _app.UpdateError is null ? "InkMuted" : "Warning");
        UpdateStatus.Visibility = string.IsNullOrEmpty(UpdateStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
        // Nothing left under the banner (no status, no button): no gap under it either.
        bool rowEmpty = UpdateStatus.Visibility == Visibility.Collapsed && CheckUpdatesButton.Visibility == Visibility.Collapsed;
        UpdateBanner.Margin = new Thickness(0, 0, 0, rowEmpty ? 0 : 12);
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
        try { Modules.AppLauncher.OpenUrl(url); }
        catch (Exception ex) { Log.Error("Opening the release page failed", ex); }
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e)
    {
        try { Modules.AppLauncher.OpenUrl(Updater.RepoUrl); }
        catch (Exception ex) { Log.Error("Opening the GitHub page failed", ex); }
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

    // ===== Launch apps =====

    private readonly List<HotkeyEditor> _launchEditors = new();

    /// <summary>One row per launcher: icon, name + path, hotkey chip, remove button.</summary>
    private void BuildLaunchRows(string? recordId = null)
    {
        LaunchRows.Children.Clear();
        _launchEditors.Clear();
        foreach (var entry in _app.Launchers.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon = AppLauncher.Icon(entry.Path) is { } image
                ? new Image { Source = image, Width = 24, Height = 24 }
                : new TextBlock { Text = "\uE8E5", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, Width = 24, TextAlignment = TextAlignment.Center };
            if (icon is TextBlock glyph) glyph.SetResourceReference(TextBlock.ForegroundProperty, "InkMuted");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 12, 0);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            bool missing = !System.IO.File.Exists(entry.Path) && !System.IO.Directory.Exists(entry.Path);
            var name = new TextBlock { Text = AppLauncher.DisplayName(entry.Path), Style = (Style)FindResource("Text"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var path = new TextBlock
            {
                Text = missing ? Loc.F("S.Launch.Missing", entry.Path) : entry.Path,
                Style = (Style)FindResource("Muted"),
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = entry.Path,
            };
            if (missing) path.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0), Children = { name, path } };
            Grid.SetColumn(text, 1);

            var editor = new HotkeyEditor { VerticalAlignment = VerticalAlignment.Top };
            editor.InitLauncher(_app, entry.Id);
            Grid.SetColumn(editor, 2);
            _launchEditors.Add(editor);

            var id = entry.Id;
            var remove = new Button
            {
                Style = (Style)FindResource("CaptionButton"),
                Width = 30,
                Height = 30,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 12,
                Content = "\uE74D", // trash can
                ToolTip = Loc.T("S.Launch.Remove"),
                VerticalAlignment = VerticalAlignment.Top,
            };
            remove.Click += (_, _) => { _app.RemoveLauncher(id); BuildLaunchRows(); };
            Grid.SetColumn(remove, 3);

            row.Children.Add(icon);
            row.Children.Add(text);
            row.Children.Add(editor);
            row.Children.Add(remove);
            LaunchRows.Children.Add(row);

            if (id == recordId) Dispatcher.BeginInvoke(editor.StartRecording, System.Windows.Threading.DispatcherPriority.Input);
        }
        LaunchRows.Visibility = _app.Launchers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows / hides the built-in programs; one click on a chip adds it and asks for its hotkey.</summary>
    private void StandardApps_Click(object sender, RoutedEventArgs e)
    {
        bool show = StandardApps.Visibility != Visibility.Visible;
        StandardApps.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        StandardAppsChevron.Text = show ? "\uE70E" : "\uE70D"; // chevron up / down
        if (!show) return;

        StandardApps.Children.Clear();
        foreach (var (nameKey, path) in AppLauncher.Standard)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (AppLauncher.Icon(path) is { } image)
            {
                var icon = new Image { Source = image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                content.Children.Add(icon);
            }
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.TextProperty, nameKey);
            content.Children.Add(label);

            var chip = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = content,
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = path,
            };
            var target = path;
            chip.Click += (_, _) =>
            {
                StandardApps.Visibility = Visibility.Collapsed;
                StandardAppsChevron.Text = "\uE70D";
                var entry = _app.AddLauncher(target);
                BuildLaunchRows(recordId: entry.Id); // ask for its hotkey right away
            };
            StandardApps.Children.Add(chip);
        }
    }

#if DEBUG
    internal void DebugShowStandardApps() => StandardApps_Click(this, new RoutedEventArgs());
#endif

    private void AddLauncher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("S.Launch.Pick"),
            Filter = Loc.T("S.Launch.Filter") + "|*.exe;*.lnk;*.bat;*.cmd|" + Loc.T("S.Launch.AllFiles") + "|*.*",
            DereferenceLinks = false, // keep the .lnk itself: it carries the arguments and the right app
            InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonStartMenu),
        };
        if (dialog.ShowDialog(this) != true) return;
        var entry = _app.AddLauncher(dialog.FileName);
        BuildLaunchRows(recordId: entry.Id); // ask for its hotkey right away
    }

    // ===== Characters =====

    private readonly List<HotkeyEditor> _charEditors = new();

    /// <summary>One row per character: glyph box, name + code, hotkey chip, remove button.</summary>
    private void BuildCharRows(string? recordId = null)
    {
        CharRows.Children.Clear();
        _charEditors.Clear();
        foreach (var entry in _app.Chars.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = GlyphBox(entry.Text, 30, entry.Text.Length > 2 ? 12 : 16);
            glyph.VerticalAlignment = VerticalAlignment.Top;
            glyph.Margin = new Thickness(0, 0, 12, 0);

            var name = new TextBlock { Style = (Style)FindResource("Text"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            if (CharTyper.NameKey(entry.Text) is { } key) name.SetResourceReference(TextBlock.TextProperty, key);
            else name.SetResourceReference(TextBlock.TextProperty, "S.Chars.Own");
            var code = new TextBlock
            {
                Text = CharTyper.Codes(entry.Text),
                Style = (Style)FindResource("Muted"),
                FontFamily = (FontFamily)FindResource("FontMono"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, 12, 0), Children = { name, code } };
            Grid.SetColumn(text, 1);

            var editor = new HotkeyEditor { VerticalAlignment = VerticalAlignment.Top };
            editor.InitChar(_app, entry.Id);
            Grid.SetColumn(editor, 2);
            _charEditors.Add(editor);

            var id = entry.Id;
            var remove = new Button
            {
                Style = (Style)FindResource("CaptionButton"),
                Width = 30,
                Height = 30,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 12,
                Content = "\uE74D", // trash can
                ToolTip = Loc.T("S.Launch.Remove"),
                VerticalAlignment = VerticalAlignment.Top,
            };
            remove.Click += (_, _) => { _app.RemoveChar(id); BuildCharRows(); };
            Grid.SetColumn(remove, 3);

            row.Children.Add(glyph);
            row.Children.Add(text);
            row.Children.Add(editor);
            row.Children.Add(remove);
            CharRows.Children.Add(row);

            if (id == recordId) Dispatcher.BeginInvoke(editor.StartRecording, System.Windows.Threading.DispatcherPriority.Input);
        }
        CharRows.Visibility = _app.Chars.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (PopularChars.Visibility == Visibility.Visible) BuildPopularChars();
    }

    /// <summary>Square pill with the character in it (invisible spaces get a stand-in).</summary>
    private Border GlyphBox(string text, double size, double fontSize)
    {
        var glyph = new TextBlock
        {
            Text = CharTyper.Visible(text),
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var box = new Border
        {
            MinWidth = size,
            Height = size,
            MaxWidth = 90,
            Padding = new Thickness(4, 0, 4, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("RadiusSm"),
            Child = glyph,
            ToolTip = CharTyper.Visible(text),
        };
        box.SetResourceReference(Border.BackgroundProperty, "PillNeutralBg");
        box.SetResourceReference(Border.BorderBrushProperty, "PillNeutralRing");
        return box;
    }

    /// <summary>Shows / hides the field for an own character (pasted, or typed as a code).</summary>
    private void AddChar_Click(object sender, RoutedEventArgs e)
    {
        bool show = CharInput.Visibility != Visibility.Visible;
        CharInput.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        HidePopularChars();
        CharBox.Text = "";
        UpdateCharPreview();
        Dispatcher.BeginInvoke(() => CharBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CharBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCharPreview();

    private void UpdateCharPreview()
    {
        var parsed = CharTyper.ParseInput(CharBox.Text);
        CharPreview.Text = parsed is null ? "" : CharTyper.Visible(parsed);
        CharPreview.FontSize = parsed is { Length: > 2 } ? 11 : 18;
        CharAddButton.IsEnabled = parsed is not null;
        bool bad = parsed is null && CharBox.Text.Trim().Length > 0;
        CharHint.SetResourceReference(TextBlock.TextProperty, bad ? "S.Chars.Bad" : "S.Chars.Hint");
        CharHint.SetResourceReference(TextBlock.ForegroundProperty, bad ? "Warning" : "InkMuted");
    }

    private void CharBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; CharConfirm_Click(sender, e); }
        else if (e.Key == Key.Escape) { e.Handled = true; CharInput.Visibility = Visibility.Collapsed; }
    }

    private void CharConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (CharTyper.ParseInput(CharBox.Text) is not { } text) return;
        CharInput.Visibility = Visibility.Collapsed;
        AddCharRow(text);
    }

    private void AddCharRow(string text)
    {
        var entry = _app.AddChar(text);
        BuildCharRows(recordId: entry.Id); // ask for its hotkey right away
    }

    private void PopularChars_Click(object sender, RoutedEventArgs e)
    {
        if (PopularChars.Visibility == Visibility.Visible) { HidePopularChars(); return; }
        CharInput.Visibility = Visibility.Collapsed;
        PopularChars.Visibility = Visibility.Visible;
        PopularCharsChevron.Text = "\uE70E"; // chevron up
        BuildPopularChars();
    }

    private void HidePopularChars()
    {
        PopularChars.Visibility = Visibility.Collapsed;
        PopularCharsChevron.Text = "\uE70D"; // chevron down
    }

    /// <summary>One chip per popular character; those already in the list are dimmed.</summary>
    private void BuildPopularChars()
    {
        PopularChars.Children.Clear();
        foreach (var (text, nameKey) in CharTyper.Popular)
        {
            bool added = _app.Chars.Any(c => c.Text == text);
            var chip = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = CharTyper.Visible(text),
                FontSize = 16,
                FontWeight = FontWeights.Normal,
                MinWidth = 40,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = Loc.T(nameKey) + "  ·  " + CharTyper.Codes(text),
                IsEnabled = !added,
            };
            ToolTipService.SetShowOnDisabled(chip, true);
            var target = text;
            chip.Click += (_, _) =>
            {
                HidePopularChars();
                AddCharRow(target);
            };
            PopularChars.Children.Add(chip);
        }
    }

#if DEBUG
    internal void DebugShowPopularChars() => PopularChars_Click(this, new RoutedEventArgs());
#endif

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
    private void CharsSwitch_Click(object sender, RoutedEventArgs e) => _app.SetChars(CharsSwitch.IsChecked == true);

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
