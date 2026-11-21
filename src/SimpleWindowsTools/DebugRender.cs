#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimpleWindowsTools.UI;

namespace SimpleWindowsTools;

/// <summary>
/// Debug only: `SimpleWindowsTools.exe --render out-dir` draws the settings window and the overlay
/// to PNGs without showing anything on screen — for checking the UI.
/// </summary>
internal static class DebugRender
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        using var app = new AppController();

        // settings.png in Ukrainian, then the same top part in English
        foreach (var (lang, suffix) in new[] { (Core.UiLanguage.En, "-en"), (Core.UiLanguage.Uk, "") })
        {
            Core.Loc.Set(lang);
            var s = new SettingsWindow(app);
            Save((FrameworkElement)s.Content, 640, 1000, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, $"settings{suffix}.png"));
            s.Close();
        }
        SettingsWindow settings;

        // Bottom of the window: language, system, what's new — in English and Ukrainian
        foreach (var (lang, suffix) in new[] { (Core.UiLanguage.En, "-en"), (Core.UiLanguage.Uk, "-uk") })
        {
            Core.Loc.Set(lang);
            var s = new SettingsWindow(app);
            if (s.FindName("TabApp") is System.Windows.Controls.RadioButton tab) tab.IsChecked = true;
            if (s.FindName("Scroll") is System.Windows.Controls.ScrollViewer sc) sc.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            Save((FrameworkElement)s.Content, 640, 2400, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, $"settings-full{suffix}.png"));
            s.Close();
        }
        Core.Loc.Set(Core.UiLanguage.Uk);

        // App tab
        foreach (var (lang, suffix) in new[] { (Core.UiLanguage.En, "-en"), (Core.UiLanguage.Uk, "") })
        {
            Core.Loc.Set(lang);
            var s = new SettingsWindow(app);
            s.ShowAppTab();
            Save((FrameworkElement)s.Content, 640, 1400, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, $"settings-app{suffix}.png"));
            s.Close();
            // Same tab with an update on offer: banner shown, "check now" hidden
            app.FakeUpdateOffer(new Core.UpdateInfo(new System.Version(9, 9, 9), "https://example.invalid/setup.exe", "https://example.invalid", 1));
            s = new SettingsWindow(app);
            s.ShowAppTab();
            Save((FrameworkElement)s.Content, 640, 1400, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, $"settings-app-update{suffix}.png"));
            app.FakeUpdateOffer(null);
            s.Close();
        }
        Core.Loc.Set(Core.UiLanguage.Uk);

        // Same window, tall and with the screenshot accordion open, to see the whole content.
        settings = new SettingsWindow(app);
        if (settings.FindName("ShotHelp") is FrameworkElement help && help.Parent is System.Windows.Controls.Expander expander)
            expander.IsExpanded = true;
        if (settings.FindName("Scroll") is System.Windows.Controls.ScrollViewer sv) sv.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        Save((FrameworkElement)settings.Content, 640, 3000, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, "settings-full.png"));
        settings.Close();

        var overlay = new OverlayWindow();
        overlay.Label.Text = "RU";
        overlay.Pill.Opacity = 1;
        var gray = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4A));
        var pillSize = PillSize(overlay);
        Save((FrameworkElement)overlay.Content, pillSize.Width, pillSize.Height, gray, Path.Combine(dir, "overlay.png"));
        overlay.Close();

        // Screenshot editor over the rendered settings picture
        var shotImage = new BitmapImage(new System.Uri(Path.Combine(dir, "settings.png")));
        var fake = new SimpleWindowsTools.Screenshot.CapturedScreen(shotImage,
            new SimpleWindowsTools.Core.Native.RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 });
        var editor = new SimpleWindowsTools.Screenshot.ScreenshotWindow(fake, app.Settings, _ => { });
        Save(editor.DebugScene(), 1280, 720, Brushes.Black, Path.Combine(dir, "screenshot-editor.png"));
        editor.Close();

        // Installer window
        foreach (var (lang, suffix) in new[] { (Core.UiLanguage.En, "-en"), (Core.UiLanguage.Uk, "") })
        {
            Core.Loc.Set(lang);
            var setup = new SimpleWindowsTools.Setup.InstallerWindow();
            Save((FrameworkElement)setup.Content, 520, 760, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, $"installer{suffix}.png"));
            setup.Close();
        }

        // Tray icon: same loading path as TrayIcon, saved at 32 px.
        var info = Application.GetResourceStream(new System.Uri("pack://application:,,,/Assets/app.ico"));
        using var icon = new System.Drawing.Icon(info!.Stream, 32, 32);
        using var iconBmp = icon.ToBitmap();
        iconBmp.Save(Path.Combine(dir, "icon32.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// `--render-docs docs/screenshots`: English, default settings — the README pictures.
    /// </summary>
    public static void RunDocs(string dir)
    {
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(Path.GetTempPath(), "swt-docs-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Core.SettingsStore.Folder = temp; // defaults, not the user's own settings
        Core.Loc.Set(Core.UiLanguage.En);
        using var app = new AppController();
        app.Settings.SkippedLanguages.Add(0x0419); // show RU as skipped
        app.ForgetHotkeyConflicts();

        var bg = (Brush)Application.Current.Resources["Surface0"];
        var s = new SettingsWindow(app);
        app.ForgetHotkeyConflicts();
        Save((FrameworkElement)s.Content, 640, 860, bg, Path.Combine(dir, "settings-features.png"), fixSwitches: true);
        s.Close();
        s = new SettingsWindow(app);
        app.ForgetHotkeyConflicts();
        s.ShowAppTab();
        // Public picture: show the usual settings folder, not this machine's temp path with the user name
        s.VersionText.Text = Core.Loc.F("S.Version",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3), @"%APPDATA%\SimpleWindowsTools");
        Save((FrameworkElement)s.Content, 640, 860, bg, Path.Combine(dir, "settings-app.png"), fixSwitches: true);
        s.Close();

        // Screenshot editor over the settings picture, and the layout pill
        // Top 1280×720 px of the 2x settings picture: right proportions for a 1280×720 "screen".
        var full = new BitmapImage(new System.Uri(Path.GetFullPath(Path.Combine(dir, "settings-features.png"))));
        var shotImage = new CroppedBitmap(full, new System.Windows.Int32Rect(0, 0, 1280, 720));
        var fake = new SimpleWindowsTools.Screenshot.CapturedScreen(shotImage,
            new SimpleWindowsTools.Core.Native.RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 });
        var editor = new SimpleWindowsTools.Screenshot.ScreenshotWindow(fake, app.Settings, _ => { });
        Save(editor.DebugScene(), 1280, 720, Brushes.Black, Path.Combine(dir, "screenshot-editor.png"));
        editor.Close();

        var overlay = new OverlayWindow();
        overlay.Label.Text = "EN";
        overlay.Label.FontSize = new Core.AppSettings().OverlayFontSize; // what a new install shows
        overlay.Pill.Opacity = 1;
        var size = PillSize(overlay);
        Save((FrameworkElement)overlay.Content, size.Width, size.Height, new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4A)), Path.Combine(dir, "layout-pill.png"));
        overlay.Close();

        var setup = new SimpleWindowsTools.Setup.InstallerWindow();
        Save((FrameworkElement)setup.Content, 520, 640, (Brush)Application.Current.Resources["Surface0"], Path.Combine(dir, "installer.png"), fixSwitches: true);
        setup.Close();

        try { Directory.Delete(temp, recursive: true); } catch { }
    }

    /// <summary>The overlay window sizes to its content; render it at that size, not stretched into a box.</summary>
    private static Size PillSize(OverlayWindow overlay)
    {
        var content = (FrameworkElement)overlay.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(System.Math.Ceiling(content.DesiredSize.Width), System.Math.Ceiling(content.DesiredSize.Height));
    }

    /// <summary>Switch thumbs move by a storyboard, which doesn't run in an offscreen render — set the end state.</summary>
    private static void FixSwitches(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.CheckBox cb && cb.Template?.FindName("ThumbX", cb) is TranslateTransform x
                && cb.Template.FindName("TrackOn", cb) is UIElement on)
            {
                x.X = cb.IsChecked == true ? 18 : 0;
                on.Opacity = cb.IsChecked == true ? 1 : 0;
            }
            FixSwitches(child);
        }
    }

    private static void Save(FrameworkElement root, double w, double h, Brush background, string file, bool fixSwitches = false)
    {
        if (root.Parent is Window win) win.Content = null;
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        if (fixSwitches) { FixSwitches(root); root.UpdateLayout(); }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
#endif
