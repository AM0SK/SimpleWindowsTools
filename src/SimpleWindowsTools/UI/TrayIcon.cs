using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimpleWindowsTools.UI;

/// <summary>Tray icon and its menu, in the dark design-system colors.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppController _app;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _skipItem;
    private readonly ToolStripMenuItem _winItem;
    private readonly ToolStripMenuItem _taskbarItem;
    private readonly ToolStripMenuItem _dashItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _shotItem;
    private readonly ToolStripMenuItem _settingsItem;

    public event Action? OpenSettingsRequested;
    /// <summary>Settings window on the "App" tab, where the update can be installed.</summary>
    public event Action? OpenUpdatesRequested;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripSeparator _updateSeparator = new();

    public TrayIcon(AppController app)
    {
        _app = app;

        _overlayItem = new ToolStripMenuItem("", null, (_, _) => _app.SetLayoutOverlay(!_app.Settings.LayoutOverlayEnabled));
        _skipItem = new ToolStripMenuItem("", null, (_, _) => _app.SetSkipLanguages(!_app.Settings.SkipLanguagesEnabled, notify: true));
        _winItem = new ToolStripMenuItem("", null, (_, _) => _app.SetWinBlock(!_app.Settings.WinBlockEnabled));
        _dashItem = new ToolStripMenuItem("", null, (_, _) => _app.SetEmDash(!_app.Settings.EmDashEnabled));
        _taskbarItem = new ToolStripMenuItem("", null, (_, _) => _app.SetTaskbarAutoHide(!Modules.TaskbarAutoHide.IsOn));

        _exitItem = new ToolStripMenuItem("", null, (_, _) => _app.RequestExit());

        _shotItem = new ToolStripMenuItem("", null, async (_, _) =>
        {
            await System.Threading.Tasks.Task.Delay(250); // let the menu disappear before the capture
            _app.TakeScreenshot();
        });

        _settingsItem = new ToolStripMenuItem("", null, (_, _) => OpenSettingsRequested?.Invoke());
        _settingsItem.Font = new Font(_settingsItem.Font, FontStyle.Bold);

        _updateItem = new ToolStripMenuItem("", null, (_, _) => OpenUpdatesRequested?.Invoke()) { Visible = false };
        _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);
        _updateSeparator.Visible = false;

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(4),
            Font = new Font("Segoe UI", 9.5f),
        };
        menu.Opening += (_, _) => Refresh(); // taskbar auto-hide may have been changed in Windows settings
        menu.Items.AddRange(new ToolStripItem[]
        {
            _updateItem,
            _updateSeparator,
            _settingsItem,
            _shotItem,
            new ToolStripSeparator(),
            _overlayItem,
            _skipItem,
            _winItem,
            _taskbarItem,
            _dashItem,
            new ToolStripSeparator(),
            _exitItem,
        });
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = DarkRenderer.Ink;
            item.Padding = new Padding(2, 5, 2, 5);
        }

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Simple Windows Tools",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenSettingsRequested?.Invoke();
        };

        _app.Changed += Refresh;
        Core.Loc.Changed += Refresh;
        _app.UpdateStateChanged += Refresh;
        _app.UpdateAnnounced += Announce;
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonOpensUpdates) OpenUpdatesRequested?.Invoke();
            else OpenSettingsRequested?.Invoke();
        };
        Refresh();
    }

    private void Refresh()
    {
        foreach (ToolStripItem item in _icon?.ContextMenuStrip?.Items ?? new ToolStripItemCollection(new ToolStrip(), Array.Empty<ToolStripItem>()))
            item.ForeColor = DarkRenderer.Ink;
        _settingsItem.Text = Core.Loc.T("T.Settings");
        _shotItem.Text = Core.Loc.T("T.Shot");
        _overlayItem.Text = Core.Loc.T("S.Overlay.Title");
        _skipItem.Text = Core.Loc.T("S.Skip.Title");
        _winItem.Text = Core.Loc.T("T.Win");
        _taskbarItem.Text = Core.Loc.T("T.Taskbar");
        _taskbarItem.Checked = Modules.TaskbarAutoHide.IsOn; // Windows' own state, re-read whenever the menu opens
        _taskbarItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.Taskbar).Display;
        _taskbarItem.ShowShortcutKeys = true;
        _dashItem.Text = Core.Loc.T("S.Dash.Title");
        _exitItem.Text = Core.Loc.T("T.Exit");
        var update = _app.AvailableUpdate;
        _updateItem.Visible = _updateSeparator.Visible = update is not null;
        if (update is not null) _updateItem.Text = Core.Loc.F("T.Update", update.Version);
        _overlayItem.Checked = _app.Settings.LayoutOverlayEnabled;
        _winItem.Checked = _app.Settings.WinBlockEnabled;
        _skipItem.Checked = _app.Settings.SkipLanguagesEnabled;
        _skipItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.ToggleSkip).Display;
        _dashItem.Checked = _app.Settings.EmDashEnabled;
        _overlayItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.ToggleOverlay).Display;
        _winItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.ToggleWinBlock).Display;
        _dashItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.EmDash).Display;
        _exitItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.Exit).Display;
        _shotItem.ShortcutKeyDisplayString = _app.GetHotkey(HotkeyId.Screenshot).Display;
        _shotItem.ShowShortcutKeys = true;
        foreach (var item in new[] { _overlayItem, _skipItem, _winItem, _dashItem, _exitItem }) item.ShowShortcutKeys = true;
    }

    /// <summary>Windows notification ("balloon" → a toast on 10/11). Clicking it opens the updates.</summary>
    private void Announce(Core.UpdateInfo update)
    {
        _balloonOpensUpdates = true;
        _icon.ShowBalloonTip(10000, Core.Loc.F("S.Updates.Available", update.Version), Core.Loc.T("N.UpdateText"), ToolTipIcon.Info);
    }

    /// <summary>After a fresh install: where the app is. Clicking it opens the settings.</summary>
    public void ShowWelcome()
    {
        _balloonOpensUpdates = false;
        _icon.ShowBalloonTip(8000, Core.Loc.T("N.WelcomeTitle"), Core.Loc.T("N.WelcomeText"), ToolTipIcon.Info);
    }

    private bool _balloonOpensUpdates;

    private static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = info!.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _app.Changed -= Refresh;
        Core.Loc.Changed -= Refresh;
        _app.UpdateStateChanged -= Refresh;
        _app.UpdateAnnounced -= Announce;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        // Same tokens as Theme/Dark.xaml.
        public static readonly Color Ink = Color.FromArgb(0xF2, 0xF1, 0xF4);
        private static readonly Color Muted = Color.FromArgb(0x9B, 0x98, 0xA9);
        private static readonly Color Surface100 = Color.FromArgb(0x17, 0x16, 0x1D);
        private static readonly Color Surface200 = Color.FromArgb(0x1F, 0x1E, 0x28);
        private static readonly Color Border = Color.FromArgb(0x2B, 0x29, 0x35);
        private static readonly Color AccentStart = Color.FromArgb(0x6D, 0x5D, 0xF6);
        private static readonly Color AccentEnd = Color.FromArgb(0x4C, 0x8D, 0xFF);

        public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Surface100);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);
            using var path = Rounded(r, 6);
            using var brush = new SolidBrush(Surface200);
            g.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            int size = 14;
            var box = new Rectangle(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);
            using var path = Rounded(box, 4);
            using var fill = new LinearGradientBrush(box, AccentStart, AccentEnd, 45f);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + 3.5f, box.Y + 7.2f),
                new PointF(box.X + 6f, box.Y + 9.8f),
                new PointF(box.X + 10.5f, box.Y + 4.5f),
            });
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Shortcut text on the right is muted, like hotkey chips in the window.
            bool isShortcut = e.Item is ToolStripMenuItem mi && e.Text == mi.ShortcutKeyDisplayString;
            e.TextColor = isShortcut ? Muted : Ink;
            base.OnRenderItemText(e);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface100;
            public override Color ImageMarginGradientBegin => Surface100;
            public override Color ImageMarginGradientMiddle => Surface100;
            public override Color ImageMarginGradientEnd => Surface100;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Surface200;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
        }
    }
}
