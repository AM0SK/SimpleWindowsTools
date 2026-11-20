using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SimpleWindowsTools.UI;

/// <summary>
/// Lets the user reorder the children of a horizontal panel by dragging them with the mouse. A press
/// that moves less than a few pixels stays a normal click; past that the item follows the cursor and
/// the others make room. When it is dropped, <c>done</c> gets the children's <c>Tag</c>s in the new order.
/// </summary>
internal sealed class DragReorder
{
    private const double Threshold = 5;

    private readonly Panel _panel;
    private readonly Action<IReadOnlyList<object>> _done;
    private FrameworkElement? _item;
    private Point _start;
    private double _grab;
    private bool _dragging;

    private DragReorder(Panel panel, Action<IReadOnlyList<object>> done)
    {
        _panel = panel;
        _done = done;
        panel.PreviewMouseLeftButtonDown += Down;
        panel.PreviewMouseMove += Move;
        panel.PreviewMouseLeftButtonUp += Up;
        panel.LostMouseCapture += (_, _) => { if (_dragging) End(); };
    }

    public static void Attach(Panel panel, Action<IReadOnlyList<object>> done) => new DragReorder(panel, done);

    public static IReadOnlyList<object> Tags(Panel panel) =>
        panel.Children.OfType<FrameworkElement>().Select(c => c.Tag).ToList();

    private void Down(object sender, MouseButtonEventArgs e)
    {
        _item = ItemOf(e.OriginalSource as DependencyObject);
        _start = e.GetPosition(_panel);
        _dragging = false;
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (_item is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_panel);
        if (!_dragging)
        {
            if ((p - _start).Length < Threshold) return;
            _dragging = true;
            _grab = _start.X - X(_item);
            Mouse.Capture(_panel, CaptureMode.SubTree); // takes the press away from the button: no click
            Panel.SetZIndex(_item, 1);
            _item.Opacity = 0.85;
        }

        // Where would it go: past the centers of how many other items.
        int index = 0;
        foreach (var c in _panel.Children.OfType<FrameworkElement>())
            if (c != _item && X(c) + c.ActualWidth / 2 < p.X - _grab + _item.ActualWidth / 2) index++;
        int current = _panel.Children.IndexOf(_item);
        if (index != current)
        {
            _panel.Children.RemoveAt(current);
            _panel.Children.Insert(index, _item);
            _panel.UpdateLayout();
        }
        _item.RenderTransform = new TranslateTransform(p.X - _grab - X(_item), 0);
        e.Handled = true;
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            End();
            e.Handled = true;
        }
        _item = null;
    }

    private void End()
    {
        _dragging = false;
        if (_item is not null)
        {
            _item.RenderTransform = null;
            _item.Opacity = 1;
            Panel.SetZIndex(_item, 0);
        }
        if (Mouse.Captured == _panel) _panel.ReleaseMouseCapture();
        _done(Tags(_panel));
    }

    /// <summary>Layout position inside the panel (without the drag offset).</summary>
    private double X(FrameworkElement child) => VisualTreeHelper.GetOffset(child).X;

    private FrameworkElement? ItemOf(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.Parent == _panel) return fe;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
