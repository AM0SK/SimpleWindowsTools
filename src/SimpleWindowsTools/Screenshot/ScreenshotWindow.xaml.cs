using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SimpleWindowsTools.Core;
using Path = System.IO.Path;

namespace SimpleWindowsTools.Screenshot;

/// <summary>
/// Full-screen editor over a frozen picture of one monitor.
/// 1. Drag to select an area (dashed 1 px frame, crosshair to the center, size in pixels).
///    Corners resize it, dragging a side moves the whole area.
/// 2. Draw inside it: arrow (default), rectangle, line (Shift = straight), text (drag out a box, type).
///    Shift + drag on a drawn element moves it; the wheel changes its thickness / text size.
/// 3. Enter / Ctrl+C copies, Ctrl+S saves, Esc closes.
/// All coordinates are DIPs of <see cref="Root"/>; the area is snapped to whole physical pixels.
/// </summary>
internal partial class ScreenshotWindow : Window
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xF2, 0x55, 0x5A), // red (critical)
        Color.FromRgb(0xF2, 0xB8, 0x4B), // amber (warning)
        Color.FromRgb(0x3D, 0xD6, 0x8C), // green (success)
        Color.FromRgb(0x4C, 0x8D, 0xFF), // blue (accent end)
        Color.FromRgb(0x9C, 0x8C, 0xFF), // violet (accent)
        Colors.White,
        Color.FromRgb(0x17, 0x16, 0x1D), // near-black (surface)
    };

    private const double HandleHit = 8;  // px around a corner that grabs it
    private const double SideHit = 5;    // px around a side that grabs the whole area

    private enum DragMode { None, Selecting, ResizeSelection, MoveSelection, Drawing, MoveElement }

    private readonly CapturedScreen _shot;
    private readonly AppSettings _settings;
    private readonly Action<string> _toast;
    private readonly List<Annotation> _elements = new();

    private double _scale = 1;
    private Tool _tool = Tool.Arrow;
    private Tool[] _order = ToolOrder.Default;
    private Color _color;

    private Rect _sel;
    private bool _hasSel;
    private DragMode _drag;
    private Point _dragStart;
    private Point _anchor;
    private Rect _selAtStart;
    private Point _lastMouse;

    private Annotation? _drawing;     // being drawn right now
    private Annotation? _picked;      // last drawn or Shift-picked: the wheel resizes it
    private bool _pickedByHand;       // Shift-picked: a color click recolors it
    private TextAnnotation? _editing;

#if DEBUG
    /// <summary>Memory test: open far off screen, don't take focus or inject keys.</summary>
    internal static bool DebugOffscreen;
#endif

    public ScreenshotWindow(CapturedScreen shot, AppSettings settings, Action<string> toast)
    {
        _shot = shot;
        _settings = settings;
        _toast = toast;
        _color = ParseColor(settings.ScreenshotColor) ?? Palette[0];
        InitializeComponent();

        Shot.Source = shot.Image;
        BuildTools();
        BuildSwatches();
        UpdateSizeValue();
        // Clicks on the toolbar's own background must not start a selection or a drawing underneath.
        Toolbar.MouseLeftButtonDown += (_, e) => e.Handled = true;

        SourceInitialized += (_, _) => PlaceOnMonitor();
        Loaded += (_, _) =>
        {
            PlaceOnMonitor(); // again: moving to a monitor with another scale can resize the window
            ApplyScale();
            // We were triggered from a keyboard hook, not by a click, so Windows may refuse to give us
            // focus. Our own (harmless) key press makes us "the last input" and lets us take it.
#if DEBUG
            if (DebugOffscreen) return;
#endif
            InputSender.MaskKey();
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetForegroundWindow(hwnd);
            Activate();
            Focus();
        };
        DpiChanged += (_, _) => ApplyScale();
    }

    // Full pill: radius = half the height (a huge CornerRadius in WPF draws an ellipse).
    private void Hint_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Hint.CornerRadius = new CornerRadius(e.NewSize.Height / 2);

    // ===== Window placement =====

    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _shot.Bounds;
#if DEBUG
        if (DebugOffscreen) b = new Native.RECT { Left = -30000, Top = 0 };
#endif
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, _shot.Width, _shot.Height, Native.SWP_SHOWWINDOW);
    }

    private void ApplyScale()
    {
        _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Shot.Width = _shot.Width / _scale;
        Shot.Height = _shot.Height / _scale;
        double px = 1 / _scale;
        foreach (var shape in new Shape[] { CrossH, CrossV, SelDark, SelLight, PickedBounds, HandleTL, HandleTR, HandleBL, HandleBR })
            shape.StrokeThickness = px;
        UpdateChrome();
    }

    private double ScreenW => _shot.Width / _scale;
    private double ScreenH => _shot.Height / _scale;

    // ===== Mouse =====

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Root);

        if (_editing is not null)
        {
            // Click outside the text box finishes typing; inside it, the TextBox handles the click.
            if (!_editing.Bounds.Contains(p)) CommitText();
            return;
        }

        _dragStart = _lastMouse = p;

        if (!_hasSel)
        {
            StartSelecting(p);
        }
        else if (HitCorner(p) is { } corner)
        {
            _drag = DragMode.ResizeSelection;
            _anchor = OppositeCorner(corner);
        }
        else if (HitSide(p))
        {
            _drag = DragMode.MoveSelection;
            _selAtStart = _sel;
        }
        else if (IsShift && ElementAt(p) is { } element)
        {
            _drag = DragMode.MoveElement;
            Pick(element, byHand: true);
        }
        else if (_sel.Contains(p))
        {
            if (_tool == Tool.Text && _elements.OfType<TextAnnotation>().LastOrDefault(t => t.Hit(p)) is { } text)
            {
                BeginEditText(text);
                return;
            }
            StartDrawing(p);
        }
        else if (_elements.Count == 0)
        {
            StartSelecting(p); // nothing drawn yet: a drag outside starts a new area
        }
        else
        {
            return;
        }

        Root.CaptureMouse();
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        switch (_drag)
        {
            case DragMode.Selecting:
                _sel = SnapRect(new Rect(_dragStart, ClampToScreen(p)));
                UpdateChrome();
                break;

            case DragMode.ResizeSelection:
                _sel = SnapRect(new Rect(_anchor, ClampToScreen(p)));
                UpdateChrome();
                break;

            case DragMode.MoveSelection:
                var r = _selAtStart;
                r.Offset(p - _dragStart);
                r.X = Math.Clamp(r.X, 0, Math.Max(0, ScreenW - r.Width));
                r.Y = Math.Clamp(r.Y, 0, Math.Max(0, ScreenH - r.Height));
                _sel = SnapRect(r);
                UpdateChrome();
                break;

            case DragMode.Drawing:
                UpdateDrawing(p);
                break;

            case DragMode.MoveElement when _picked is not null:
                _picked.Offset(p - _lastMouse);
                _lastMouse = p;
                UpdatePickedBounds();
                break;

            default:
                UpdateCursor(p);
                break;
        }
    }

    private void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Root);
        var mode = _drag;
        _drag = DragMode.None;
        Root.ReleaseMouseCapture();

        switch (mode)
        {
            case DragMode.Selecting:
            case DragMode.ResizeSelection:
                _sel = SnapRect(_sel);
                _hasSel = _sel.Width >= 4 && _sel.Height >= 4;
                if (!_hasSel) Hint.Visibility = Visibility.Visible;
                UpdateChrome();
                break;

            case DragMode.MoveSelection:
                UpdateChrome(); // the toolbar is hidden while dragging; bring it back at the new spot
                break;

            case DragMode.Drawing:
                FinishDrawing(p);
                break;
        }
        UpdateCursor(p);
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        int dir = Math.Sign(e.Delta);
        var target = _editing ?? _picked;
        if (target is not null)
        {
            target.Size = Math.Clamp(target.Size + dir * target.SizeStep, target.MinSize, target.MaxSize);
            if (target is TextAnnotation) _settings.ScreenshotFontSize = target.Size;
            else if (target is BlurAnnotation) _settings.ScreenshotBlur = target.Size;
            else _settings.ScreenshotThickness = target.Size;
            UpdatePickedBounds();
        }
        else if (_tool == Tool.Text)
        {
            _settings.ScreenshotFontSize = Math.Clamp(_settings.ScreenshotFontSize + dir * 2, 8, 120);
        }
        else if (_tool == Tool.Blur)
        {
            _settings.ScreenshotBlur = Math.Clamp(_settings.ScreenshotBlur + dir * 2, 2, 60);
        }
        else
        {
            _settings.ScreenshotThickness = Math.Clamp(_settings.ScreenshotThickness + dir, 1, 40);
        }
        UpdateSizeValue(target);
        e.Handled = true;
    }

    // ===== Selection =====

    private void StartSelecting(Point p)
    {
        _drag = DragMode.Selecting;
        _hasSel = false;
        _sel = SnapRect(new Rect(p, p));
        Hint.Visibility = Visibility.Collapsed;
        UpdateChrome();
    }

    private Point? HitCorner(Point p)
    {
        foreach (var c in new[] { _sel.TopLeft, _sel.TopRight, _sel.BottomLeft, _sel.BottomRight })
            if (Math.Abs(p.X - c.X) <= HandleHit && Math.Abs(p.Y - c.Y) <= HandleHit) return c;
        return null;
    }

    private Point OppositeCorner(Point c) => new(c.X == _sel.Left ? _sel.Right : _sel.Left, c.Y == _sel.Top ? _sel.Bottom : _sel.Top);

    private bool HitSide(Point p)
    {
        bool nearV = (Math.Abs(p.X - _sel.Left) <= SideHit || Math.Abs(p.X - _sel.Right) <= SideHit)
                     && p.Y >= _sel.Top - SideHit && p.Y <= _sel.Bottom + SideHit;
        bool nearH = (Math.Abs(p.Y - _sel.Top) <= SideHit || Math.Abs(p.Y - _sel.Bottom) <= SideHit)
                     && p.X >= _sel.Left - SideHit && p.X <= _sel.Right + SideHit;
        return nearV || nearH;
    }

    private Point ClampToScreen(Point p) => new(Math.Clamp(p.X, 0, ScreenW), Math.Clamp(p.Y, 0, ScreenH));

    /// <summary>Snaps a rect to whole physical pixels so the frame and the crop line up exactly.</summary>
    private Rect SnapRect(Rect r)
    {
        double x1 = Math.Round(r.Left * _scale) / _scale, y1 = Math.Round(r.Top * _scale) / _scale;
        double x2 = Math.Round(r.Right * _scale) / _scale, y2 = Math.Round(r.Bottom * _scale) / _scale;
        return new Rect(new Point(x1, y1), new Point(x2, y2));
    }

    private void UpdateChrome()
    {
        // Dim outside the area
        var full = new RectangleGeometry(new Rect(0, 0, ScreenW, ScreenH));
        Dim.Data = _hasSel || _drag == DragMode.Selecting
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(_sel))
            : full;

        bool show = _hasSel || (_drag == DragMode.Selecting && _sel.Width > 0);
        Chrome.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Layer.Clip = new RectangleGeometry(_sel);
        if (!show)
        {
            Toolbar.Visibility = Visibility.Collapsed;
            return;
        }

        // 1 px frame: dark solid under white dashes, visible on any background.
        PlaceRect(SelDark, _sel);
        PlaceRect(SelLight, _sel);

        // Faint crosshair: from the middle of each side to the center.
        double cx = _sel.Left + _sel.Width / 2, cy = _sel.Top + _sel.Height / 2;
        CrossH.X1 = _sel.Left; CrossH.X2 = _sel.Right; CrossH.Y1 = CrossH.Y2 = cy;
        CrossV.Y1 = _sel.Top; CrossV.Y2 = _sel.Bottom; CrossV.X1 = CrossV.X2 = cx;

        PlaceHandle(HandleTL, _sel.TopLeft);
        PlaceHandle(HandleTR, _sel.TopRight);
        PlaceHandle(HandleBL, _sel.BottomLeft);
        PlaceHandle(HandleBR, _sel.BottomRight);

        // Size in real pixels, above the area (inside it when there is no room).
        SizeText.Text = $"{Math.Round(_sel.Width * _scale)} × {Math.Round(_sel.Height * _scale)}";
        SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelY = _sel.Top - SizeLabel.DesiredSize.Height - 6;
        if (labelY < 2) labelY = _sel.Top + 6;
        Canvas.SetLeft(SizeLabel, Math.Clamp(_sel.Left, 2, Math.Max(2, ScreenW - SizeLabel.DesiredSize.Width - 2)));
        Canvas.SetTop(SizeLabel, labelY);

        PlaceToolbar();
        UpdatePickedBounds();
    }

    private void PlaceToolbar()
    {
        if (!_hasSel || _drag is DragMode.Selecting or DragMode.ResizeSelection or DragMode.MoveSelection)
        {
            Toolbar.Visibility = Visibility.Collapsed;
            return;
        }
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Toolbar.DesiredSize;

        // Below the area, right-aligned; above it if there's no room; inside as a last resort.
        double y = _sel.Bottom + 8;
        if (y + size.Height > ScreenH - 4) y = _sel.Top - size.Height - 8;
        if (y < 4) y = _sel.Bottom - size.Height - 8;
        double x = Math.Clamp(_sel.Right - size.Width, 4, Math.Max(4, ScreenW - size.Width - 4));
        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private static void PlaceRect(Rectangle shape, Rect r)
    {
        Canvas.SetLeft(shape, r.X);
        Canvas.SetTop(shape, r.Y);
        shape.Width = Math.Max(0, r.Width);
        shape.Height = Math.Max(0, r.Height);
    }

    private static void PlaceHandle(Rectangle handle, Point c)
    {
        Canvas.SetLeft(handle, c.X - handle.Width / 2);
        Canvas.SetTop(handle, c.Y - handle.Height / 2);
    }

    private void UpdateCursor(Point p)
    {
        Cursor cursor = Cursors.Cross;
        if (_hasSel)
        {
            if (HitCorner(p) is { } c)
                cursor = (c == _sel.TopLeft || c == _sel.BottomRight) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            else if (HitSide(p))
                cursor = Cursors.SizeAll;
            else if (IsShift && ElementAt(p) is not null)
                cursor = Cursors.SizeAll;
            else if (_sel.Contains(p))
                cursor = _tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            else
                cursor = _elements.Count == 0 ? Cursors.Cross : Cursors.Arrow;
        }
        Root.Cursor = cursor;
    }

    // ===== Drawing =====

    private void StartDrawing(Point p)
    {
        _drag = DragMode.Drawing;
        if (_tool == Tool.Text)
        {
            TextDraft.Visibility = Visibility.Visible;
            PlaceRect(TextDraft, new Rect(p, p));
            return;
        }

        _drawing = _tool switch
        {
            Tool.Blur => new BlurAnnotation(p, _shot.Image, _scale, _settings.ScreenshotBlur),
            Tool.Rectangle => new RectAnnotation(p, _color, _settings.ScreenshotThickness),
            Tool.Line => new LineAnnotation(false, p, _color, _settings.ScreenshotThickness),
            Tool.Pencil => new PencilAnnotation(p, _color, _settings.ScreenshotThickness),
            _ => new LineAnnotation(true, p, _color, _settings.ScreenshotThickness),
        };
        // Blur goes under everything else drawn, so arrows and text stay on top of it.
        if (_drawing is BlurAnnotation) Layer.Children.Insert(0, _drawing.Visual);
        else Layer.Children.Add(_drawing.Visual);
    }

    private void UpdateDrawing(Point p)
    {
        if (_tool == Tool.Text)
        {
            PlaceRect(TextDraft, new Rect(_dragStart, p));
            return;
        }
        switch (_drawing)
        {
            case LineAnnotation line:
                line.B = IsShift ? Straighten(line.A, p) : p;
                line.Update();
                break;
            case RectAnnotation rect:
                rect.B = p;
                rect.Update();
                break;
            case BlurAnnotation blur:
                blur.B = p;
                blur.Update();
                break;
            case PencilAnnotation pencil:
                pencil.AddPoint(p);
                break;
        }
    }

    /// <summary>Shift: lock the line to horizontal or vertical, whichever is closer.</summary>
    internal static Point Straighten(Point a, Point p) =>
        Math.Abs(p.X - a.X) >= Math.Abs(p.Y - a.Y) ? new Point(p.X, a.Y) : new Point(a.X, p.Y);

    private void FinishDrawing(Point p)
    {
        if (_tool == Tool.Text)
        {
            TextDraft.Visibility = Visibility.Collapsed;
            var area = new Rect(_dragStart, p);
            if (area.Width < 20) area.Width = 240; // a click: default box width
            var text = new TextAnnotation(area, _color, _settings.ScreenshotFontSize);
            _elements.Add(text);
            Layer.Children.Add(text.Visual);
            BeginEditText(text);
            return;
        }

        if (_drawing is null) return;
        if (_drawing is PencilAnnotation pencil) pencil.Finish(p);
        if (_drawing.IsEmpty)
        {
            Layer.Children.Remove(_drawing.Visual);
        }
        else
        {
            _elements.Add(_drawing);
            Pick(_drawing, byHand: false);
        }
        _drawing = null;
    }

    private void BeginEditText(TextAnnotation text)
    {
        _editing = text;
        Pick(text, byHand: false);
        text.BeginEdit();
        UpdateSizeValue(text);
    }

    private void CommitText()
    {
        if (_editing is null) return;
        var text = _editing;
        _editing = null;
        text.EndEdit();
        if (text.IsEmpty)
        {
            _elements.Remove(text);
            Layer.Children.Remove(text.Visual);
            if (_picked == text) _picked = null;
        }
        Root.Focus();
        Keyboard.Focus(this);
    }

    private Annotation? ElementAt(Point p) => _elements.LastOrDefault(a => a.Hit(p));

    private void Pick(Annotation element, bool byHand)
    {
        _picked = element;
        _pickedByHand = byHand;
        UpdateSizeValue(element);
        UpdatePickedBounds();
    }

    /// <summary>While Shift is held, a dashed box shows which element the wheel / move will act on.</summary>
    private void UpdatePickedBounds()
    {
        if (_picked is null || !IsShift || _editing is not null)
        {
            PickedBounds.Visibility = Visibility.Collapsed;
            return;
        }
        var b = _picked.Bounds;
        b.Inflate(_picked is TextAnnotation ? 2 : _picked.Size / 2 + 4, _picked is TextAnnotation ? 2 : _picked.Size / 2 + 4);
        PlaceRect(PickedBounds, b);
        PickedBounds.Visibility = Visibility.Visible;
    }

    // ===== Toolbar =====

    /// <summary>Tool buttons in the saved order; the first one is selected. Drag them to reorder.</summary>
    private void BuildTools()
    {
        _order = ToolOrder.Normalize(_settings.ScreenshotTools);
        _tool = _order[0];
        foreach (var tool in _order)
        {
            var (icon, thickness, _) = ToolOrder.Look(tool);
            var button = new RadioButton
            {
                GroupName = "tool",
                Style = (Style)FindResource("ToolRadio"),
                Tag = tool,
                IsChecked = tool == _tool,
                Focusable = false,
                Content = new System.Windows.Shapes.Path { Style = (Style)FindResource("ToolIcon"), StrokeThickness = thickness, Data = Geometry.Parse(icon) },
            };
            button.Checked += Tool_Checked;
            Tools.Children.Add(button);
        }
        UpdateToolTips();
        UI.DragReorder.Attach(Tools, tags =>
        {
            _order = tags.Cast<Tool>().ToArray();
            _settings.ScreenshotTools = ToolOrder.ToNames(_order);
            UpdateToolTips();
        });
    }

    private void UpdateToolTips()
    {
        int n = 1;
        foreach (FrameworkElement b in Tools.Children)
            b.ToolTip = Loc.F(ToolOrder.Look((Tool)b.Tag).HintKey, n++);
    }

    private void SelectTool(Tool tool)
    {
        foreach (RadioButton b in Tools.Children)
            if ((Tool)b.Tag == tool) b.IsChecked = true;
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        CommitText();
        _tool = (Tool)((FrameworkElement)sender).Tag;
        _picked = null;
        UpdateSizeValue();
    }

    private void BuildSwatches()
    {
        foreach (var c in Palette)
        {
            var swatch = new RadioButton
            {
                GroupName = "color",
                Style = (Style)FindResource("ToolRadio"),
                Width = 26,
                IsChecked = c == _color,
                Content = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(c),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                },
                ToolTip = Loc.T("Sh.Color"),
                Focusable = false,
            };
            swatch.Checked += (_, _) => SetColor(c);
            Swatches.Children.Add(swatch);
        }
    }

    private void SetColor(Color c)
    {
        _color = c;
        _settings.ScreenshotColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        // New elements take the color; an element you picked with Shift (or the text being typed) is recolored.
        if (_editing is not null) _editing.Color = c;
        else if (_picked is not null && _pickedByHand && _picked is not BlurAnnotation) _picked.Color = c;
    }

    private void UpdateSizeValue(Annotation? target = null)
    {
        bool isText = target is TextAnnotation || (target is null && _tool == Tool.Text);
        bool isBlur = target is BlurAnnotation || (target is null && _tool == Tool.Blur);
        double value = target?.Size ?? (isText ? _settings.ScreenshotFontSize
                                      : isBlur ? _settings.ScreenshotBlur
                                      : _settings.ScreenshotThickness);
        SizeValue.Text = isText ? $"Aa {value:0}" : isBlur ? $"≋ {value:0}" : $"{value:0} px";
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAndClose();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveAndClose();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ===== Keyboard =====

    private static bool IsShift => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            UpdatePickedBounds();
            UpdateCursor(Mouse.GetPosition(Root));
        }

        if (_editing is not null)
        {
            if (e.Key == Key.Escape)
            {
                CommitText();
                e.Handled = true;
            }
            return; // everything else is typing
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter:
            case Key.C when ctrl:
                CopyAndClose();
                break;
            case Key.S when ctrl:
                SaveAndClose();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            // 1–9 (main row or numpad): the tool at that place on the toolbar
            case >= Key.D1 and <= Key.D9 when !ctrl && ToolOrder.ForKey(_order, e.Key - Key.D0) is { } byKey:
                SelectTool(byKey);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when !ctrl && ToolOrder.ForKey(_order, e.Key - Key.NumPad0) is { } byPad:
                SelectTool(byPad);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            UpdatePickedBounds();
            UpdateCursor(Mouse.GetPosition(Root));
        }
    }

    private void Undo()
    {
        if (_elements.Count == 0) return;
        var last = _elements[^1];
        _elements.RemoveAt(_elements.Count - 1);
        Layer.Children.Remove(last.Visual);
        if (_picked == last) _picked = null;
        UpdatePickedBounds();
    }

    // ===== Output =====

    /// <summary>The selected area with everything drawn on it, at the screen's real resolution.</summary>
    internal BitmapSource RenderArea()
    {
        CommitText();
        var hidden = new UIElement[] { Dim, Chrome, Hint, Toolbar, PickedBounds };
        var was = hidden.Select(h => h.Visibility).ToArray();
        foreach (var h in hidden) h.Visibility = Visibility.Collapsed;
        Root.UpdateLayout();

        var full = new RenderTargetBitmap(_shot.Width, _shot.Height, 96 * _scale, 96 * _scale, PixelFormats.Pbgra32);
        full.Render(Root);

        for (int i = 0; i < hidden.Length; i++) hidden[i].Visibility = was[i];

        var px = new Int32Rect(
            (int)Math.Round(_sel.X * _scale), (int)Math.Round(_sel.Y * _scale),
            (int)Math.Round(_sel.Width * _scale), (int)Math.Round(_sel.Height * _scale));
        px.Width = Math.Min(px.Width, _shot.Width - px.X);
        px.Height = Math.Min(px.Height, _shot.Height - px.Y);
        var cropped = new CroppedBitmap(full, px);
        cropped.Freeze();
        return cropped;
    }

    private void CopyAndClose()
    {
        if (!_hasSel) return;
        var image = RenderArea();
        // The clipboard can be briefly locked by another app (clipboard managers); retry a few times.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                break;
            }
            catch (Exception ex) when (attempt < 5)
            {
                Log.Info($"Clipboard busy, retrying: {ex.Message}");
                System.Threading.Thread.Sleep(60);
            }
            catch (Exception ex)
            {
                Log.Error("Copy to clipboard failed", ex);
                _toast(Loc.T("Sh.CopyFailed"));
                return;
            }
        }
        Close();
        _toast(Loc.T("Sh.Copied"));
    }

    private void SaveAndClose()
    {
        if (!_hasSel) return;
        var image = RenderArea();

        var folder = _settings.ScreenshotFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(folder);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Sh.SaveTitle"),
            InitialDirectory = folder,
            FileName = $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png",
            Filter = "PNG|*.png",
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(dialog.FileName)) encoder.Save(fs);
            _settings.ScreenshotFolder = Path.GetDirectoryName(dialog.FileName);
            Close();
            _toast(Loc.T("Sh.Saved"));
        }
        catch (Exception ex)
        {
            Log.Error("Saving screenshot failed", ex);
            _toast(Loc.T("Sh.SaveFailed"));
        }
    }

#if DEBUG
    /// <summary>For DebugRender: a selection with one of each element, without showing the window.</summary>
    internal FrameworkElement DebugScene()
    {
        ApplyScale();
        _sel = SnapRect(new Rect(300, 160, 760, 420));
        _hasSel = true;
        Hint.Visibility = Visibility.Collapsed;

        var arrow = new LineAnnotation(true, new Point(380, 480), _color, 4) { B = new Point(620, 300) };
        var rect = new RectAnnotation(new Point(660, 220), Palette[3], 3) { B = new Point(980, 360) };
        var line = new LineAnnotation(false, new Point(360, 540), Palette[1], 3) { B = new Point(900, 540) };
        var blur = new BlurAnnotation(new Point(320, 180), _shot.Image, _scale, 12) { B = new Point(600, 280) };
        blur.Update();
        _elements.Add(blur);
        Layer.Children.Insert(0, blur.Visual);
        var pencil = new PencilAnnotation(new Point(1000, 420), Palette[2], 4);
        for (int i = 1; i <= 60; i++)
            pencil.AddPoint(new Point(1000 + i * 2.2 + Math.Sin(i * 0.9) * 1.5, 420 + Math.Sin(i / 6.0) * 40 + Math.Cos(i * 1.3) * 1.5));
        pencil.Finish(new Point(1132, 420 + Math.Sin(10) * 40));
        var text = new TextAnnotation(new Rect(680, 400, 300, 40), Colors.White, 20);
        text.Box.Text = Loc.T("Sh.Caption");
        text.EndEdit();
        foreach (var a in new Annotation[] { arrow, rect, line, pencil, text })
        {
            a.Update();
            _elements.Add(a);
            Layer.Children.Add(a.Visual);
        }
        UpdateChrome();
        var root = (FrameworkElement)Content;
        Content = null;
        return root;
    }
#endif

    private static Color? ParseColor(string? hex)
    {
        try { return hex is null ? null : (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }
}
