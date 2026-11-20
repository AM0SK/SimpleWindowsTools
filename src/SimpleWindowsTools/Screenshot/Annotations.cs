using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SimpleWindowsTools.Screenshot;

public enum Tool { Arrow, Rectangle, Line, Text, Blur, Pencil }

/// <summary>
/// Something drawn on the screenshot. Coordinates are DIPs of the editor canvas. Each annotation owns
/// one WPF element (<see cref="Visual"/>) and redraws it in <see cref="Update"/>.
/// "Size" is the line thickness for shapes and the font size for text — the mouse wheel changes it.
/// </summary>
public abstract class Annotation
{
    private Color _color;
    private double _size;

    protected Annotation(Color color, double size)
    {
        _color = color;
        _size = size;
    }

    public abstract FrameworkElement Visual { get; }

    public Color Color { get => _color; set { _color = value; Update(); } }
    public double Size { get => _size; set { _size = value; Update(); } }

    public abstract double MinSize { get; }
    public abstract double MaxSize { get; }
    public abstract double SizeStep { get; }

    public abstract void Update();
    public abstract bool Hit(Point p);
    public abstract void Offset(Vector v);
    public abstract Rect Bounds { get; }

    /// <summary>Too small to keep after the mouse is released (a click without a drag).</summary>
    public abstract bool IsEmpty { get; }

    protected SolidColorBrush Brush => new(_color);

    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 0.0001) return (p - a).Length;
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + t * ab)).Length;
    }
}

/// <summary>Straight line or arrow (arrowhead at the end point).</summary>
public sealed class LineAnnotation : Annotation
{
    private readonly Path _path = new()
    {
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        SnapsToDevicePixels = false,
    };

    public Point A { get; set; }
    public Point B { get; set; }
    public bool IsArrow { get; }

    public LineAnnotation(bool isArrow, Point start, Color color, double size) : base(color, size)
    {
        IsArrow = isArrow;
        A = B = start;
        Update();
    }

    public override FrameworkElement Visual => _path;
    public override double MinSize => 1;
    public override double MaxSize => 40;
    public override double SizeStep => 1;
    public override bool IsEmpty => (B - A).Length < 3;
    public override Rect Bounds => new(A, B);

    public override void Update()
    {
        _path.Stroke = Brush;
        _path.Fill = Brush;
        _path.StrokeThickness = Size;

        var dir = B - A;
        double len = dir.Length;
        var geometry = new PathGeometry();

        if (!IsArrow || len < 0.5)
        {
            geometry.Figures.Add(new PathFigure(A, new[] { new LineSegment(B, true) }, false) { IsFilled = false });
        }
        else
        {
            dir /= len;
            var normal = new Vector(-dir.Y, dir.X);
            // Head grows with the line but never longer than ~half the arrow.
            double headLen = Math.Min(Size * 3.2 + 10, len * 0.6);
            double headHalf = headLen * 0.55;
            var baseCenter = B - dir * headLen;
            var shaftEnd = B - dir * (headLen * 0.6);

            geometry.Figures.Add(new PathFigure(A, new[] { new LineSegment(shaftEnd, true) }, false) { IsFilled = false });
            geometry.Figures.Add(new PathFigure(B, new PathSegment[]
            {
                new LineSegment(baseCenter + normal * headHalf, true),
                new LineSegment(baseCenter - normal * headHalf, true),
            }, true) { IsFilled = true });
        }
        _path.Data = geometry;
    }

    public override bool Hit(Point p) => DistanceToSegment(p, A, B) <= Size / 2 + 6;

    public override void Offset(Vector v)
    {
        A += v;
        B += v;
        Update();
    }
}

public sealed class RectAnnotation : Annotation
{
    private readonly Rectangle _rect = new() { StrokeLineJoin = PenLineJoin.Miter, Fill = Brushes.Transparent };

    public Point A { get; set; }
    public Point B { get; set; }

    public RectAnnotation(Point start, Color color, double size) : base(color, size)
    {
        A = B = start;
        Update();
    }

    public override FrameworkElement Visual => _rect;
    public override double MinSize => 1;
    public override double MaxSize => 40;
    public override double SizeStep => 1;
    public override bool IsEmpty => Math.Abs(B.X - A.X) < 3 || Math.Abs(B.Y - A.Y) < 3;
    public override Rect Bounds => new(A, B);

    public override void Update()
    {
        var r = Bounds;
        // The stroke is drawn centered on the edge, like the drag outline the user sees.
        double half = Size / 2;
        Canvas.SetLeft(_rect, r.X - half);
        Canvas.SetTop(_rect, r.Y - half);
        _rect.Width = r.Width + Size;
        _rect.Height = r.Height + Size;
        _rect.Stroke = Brush;
        _rect.StrokeThickness = Size;
    }

    public override bool Hit(Point p)
    {
        var r = Bounds;
        r.Inflate(Size / 2 + 6, Size / 2 + 6);
        return r.Contains(p);
    }

    public override void Offset(Vector v)
    {
        A += v;
        B += v;
        Update();
    }
}

/// <summary>Text in a box the user dragged out. Width is fixed by the drag; height grows with the text.</summary>
public sealed class TextAnnotation : Annotation
{
    private readonly Grid _root = new();
    private readonly Rectangle _frame = new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 4, 3 },
        IsHitTestVisible = false,
    };

    public TextBox Box { get; } = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 2, 4, 2),
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontWeight = FontWeights.SemiBold,
    };

    public Point TopLeft { get; private set; }
    public double BoxWidth { get; }
    public double MinBoxHeight { get; }

    public TextAnnotation(Rect area, Color color, double fontSize) : base(color, fontSize)
    {
        TopLeft = area.TopLeft;
        BoxWidth = Math.Max(area.Width, 120);
        MinBoxHeight = Math.Max(area.Height, fontSize * 1.6);
        _root.Children.Add(_frame);
        _root.Children.Add(Box);
        Update();
    }

    public override FrameworkElement Visual => _root;
    public override double MinSize => 8;
    public override double MaxSize => 120;
    public override double SizeStep => 2;
    public override bool IsEmpty => string.IsNullOrWhiteSpace(Box.Text);

    public override Rect Bounds => new(TopLeft, new Size(BoxWidth, Math.Max(_root.ActualHeight, MinBoxHeight)));

    public bool IsEditing { get; private set; }

    public void BeginEdit()
    {
        IsEditing = true;
        Box.IsReadOnly = false;
        Box.IsHitTestVisible = true;
        Box.Focusable = true;
        Box.CaretBrush = Brush;
        _frame.Visibility = Visibility.Visible;
        Box.Focus();
        Box.CaretIndex = Box.Text.Length;
    }

    public void EndEdit()
    {
        IsEditing = false;
        Box.IsReadOnly = true;
        Box.IsHitTestVisible = false; // clicks go to the editor (Shift+drag moves it)
        Box.Focusable = false;
        _frame.Visibility = Visibility.Collapsed;
    }

    public override void Update()
    {
        Canvas.SetLeft(_root, TopLeft.X);
        Canvas.SetTop(_root, TopLeft.Y);
        _root.Width = BoxWidth;
        _root.MinHeight = MinBoxHeight;
        Box.Foreground = Brush;
        Box.FontSize = Size;
        if (IsEditing) Box.CaretBrush = Brush;
    }

    public override bool Hit(Point p) => Bounds.Contains(p);

    public override void Offset(Vector v)
    {
        TopLeft += v;
        Update();
    }
}

/// <summary>
/// Blurred area: shows the screenshot under it through a blur. "Size" is the blur strength (radius in
/// screen pixels). The crop is taken a bit larger than the area and the result clipped to the area, so
/// the edges stay solid instead of fading to transparent. Moving it re-crops: it always blurs whatever
/// is under it now. Drawn below the other elements, so arrows and text stay readable on top.
/// </summary>
public sealed class BlurAnnotation : Annotation
{
    private readonly BitmapSource _shot;
    private readonly double _scale; // DIP → screenshot pixels
    private readonly Canvas _root = new() { IsHitTestVisible = true, Background = Brushes.Transparent };
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly System.Windows.Media.Effects.BlurEffect _blur = new()
    {
        KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
        RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
    };

    public Point A { get; set; }
    public Point B { get; set; }

    public BlurAnnotation(Point start, BitmapSource shot, double scale, double strength) : base(Colors.Transparent, strength)
    {
        _shot = shot;
        _scale = scale;
        _image.Effect = _blur;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _root.Children.Add(_image);
        A = B = start;
        Update();
    }

    public override FrameworkElement Visual => _root;
    public override double MinSize => 2;
    public override double MaxSize => 60;
    public override double SizeStep => 2;
    public override bool IsEmpty => Math.Abs(B.X - A.X) < 4 || Math.Abs(B.Y - A.Y) < 4;
    public override Rect Bounds => new(A, B);

    public override void Update()
    {
        var r = Bounds;
        Canvas.SetLeft(_root, r.X);
        Canvas.SetTop(_root, r.Y);
        _root.Width = r.Width;
        _root.Height = r.Height;
        _root.Clip = new RectangleGeometry(new Rect(0, 0, r.Width, r.Height));
        _blur.Radius = Size;

        if (r.Width < 1 || r.Height < 1)
        {
            _image.Source = null;
            return;
        }

        // Crop area + margin (in screenshot pixels), clamped to the picture.
        double margin = Size * 2;
        int x1 = (int)Math.Floor((r.X - margin) * _scale), y1 = (int)Math.Floor((r.Y - margin) * _scale);
        int x2 = (int)Math.Ceiling((r.Right + margin) * _scale), y2 = (int)Math.Ceiling((r.Bottom + margin) * _scale);
        x1 = Math.Clamp(x1, 0, _shot.PixelWidth - 1);
        y1 = Math.Clamp(y1, 0, _shot.PixelHeight - 1);
        x2 = Math.Clamp(x2, x1 + 1, _shot.PixelWidth);
        y2 = Math.Clamp(y2, y1 + 1, _shot.PixelHeight);

        _image.Source = new CroppedBitmap(_shot, new Int32Rect(x1, y1, x2 - x1, y2 - y1));
        Canvas.SetLeft(_image, x1 / _scale - r.X);
        Canvas.SetTop(_image, y1 / _scale - r.Y);
        _image.Width = (x2 - x1) / _scale;
        _image.Height = (y2 - y1) / _scale;
    }

    public override bool Hit(Point p) => Bounds.Contains(p);

    public override void Offset(Vector v)
    {
        A += v;
        B += v;
        Update();
    }
}

/// <summary>
/// Freehand line. Always smoothed a little, so a shaky hand doesn't give sharp corners or jerks:
/// incoming points are thinned (closer than <see cref="MinStep"/> are skipped) and pulled towards the
/// previous point (<see cref="Follow"/>, a light stabilizer); the curve is drawn as a Catmull-Rom spline
/// through them, and on release one more averaging pass evens out what's left.
/// </summary>
public sealed class PencilAnnotation : Annotation
{
    internal const double MinStep = 2.5;  // DIPs
    internal const double Follow = 0.45;  // 1 = raw input, lower = smoother but lags behind the cursor

    private readonly Path _path = new()
    {
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        SnapsToDevicePixels = false,
    };
    private readonly List<Point> _points = new();
    private Point _lastRaw;

    public IReadOnlyList<Point> Points => _points;

    public PencilAnnotation(Point start, Color color, double size) : base(color, size)
    {
        _points.Add(start);
        _lastRaw = start;
        Update();
    }

    public override FrameworkElement Visual => _path;
    public override double MinSize => 1;
    public override double MaxSize => 40;
    public override double SizeStep => 1;
    public override bool IsEmpty => Length() < 3;

    public override Rect Bounds
    {
        get
        {
            double x1 = _points.Min(p => p.X), y1 = _points.Min(p => p.Y);
            double x2 = _points.Max(p => p.X), y2 = _points.Max(p => p.Y);
            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }
    }

    /// <summary>Mouse moved while drawing.</summary>
    public void AddPoint(Point raw)
    {
        if ((raw - _lastRaw).Length < MinStep) return;
        _lastRaw = raw;
        var last = _points[^1];
        _points.Add(last + (raw - last) * Follow);
        Update();
    }

    /// <summary>Mouse released: catch up with the cursor and even out the remaining jitter.</summary>
    public void Finish(Point raw)
    {
        var last = _points[^1];
        if ((raw - last).Length >= MinStep) _points.Add(last + (raw - last) * 0.8);
        Relax(_points);
        Update();
    }

    /// <summary>One pass of a 1-2-1 average over the inner points (ends stay where they are).</summary>
    internal static void Relax(List<Point> pts)
    {
        if (pts.Count < 3) return;
        var copy = pts.ToArray();
        for (int i = 1; i < copy.Length - 1; i++)
            pts[i] = new Point((copy[i - 1].X + 2 * copy[i].X + copy[i + 1].X) / 4,
                               (copy[i - 1].Y + 2 * copy[i].Y + copy[i + 1].Y) / 4);
    }

    /// <summary>Catmull-Rom through the points, as Bézier segments.</summary>
    internal static PathGeometry Spline(IReadOnlyList<Point> pts)
    {
        var geometry = new PathGeometry();
        if (pts.Count == 0) return geometry;
        var figure = new PathFigure { StartPoint = pts[0], IsFilled = false, IsClosed = false };
        if (pts.Count == 1)
        {
            figure.Segments.Add(new LineSegment(pts[0], true)); // a dot (round caps)
        }
        else
        {
            var bez = new PolyBezierSegment { IsStroked = true };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Math.Max(i - 1, 0)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Math.Min(i + 2, pts.Count - 1)];
                bez.Points.Add(p1 + (p2 - p0) / 6);
                bez.Points.Add(p2 - (p3 - p1) / 6);
                bez.Points.Add(p2);
            }
            figure.Segments.Add(bez);
        }
        geometry.Figures.Add(figure);
        return geometry;
    }

    private double Length()
    {
        double len = 0;
        for (int i = 1; i < _points.Count; i++) len += (_points[i] - _points[i - 1]).Length;
        return len;
    }

    public override void Update()
    {
        _path.Stroke = Brush;
        _path.StrokeThickness = Size;
        _path.Data = Spline(_points);
    }

    public override bool Hit(Point p)
    {
        double reach = Size / 2 + 6;
        if (_points.Count == 1) return (p - _points[0]).Length <= reach;
        for (int i = 1; i < _points.Count; i++)
            if (DistanceToSegment(p, _points[i - 1], _points[i]) <= reach) return true;
        return false;
    }

    public override void Offset(Vector v)
    {
        for (int i = 0; i < _points.Count; i++) _points[i] += v;
        _lastRaw += v;
        Update();
    }
}
