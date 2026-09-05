using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArcTrellis.App;

internal sealed class SceneDragAdorner : Adorner
{
    private readonly ImageSource _image;
    private readonly Size _size;
    private readonly Point _grabOffset;
    private Point _position;
    private Rect? _insertion;

    public SceneDragAdorner(UIElement surface, FrameworkElement card, Point grabOffset) : base(surface)
    {
        IsHitTestVisible = false;
        _size = card.RenderSize;
        _grabOffset = grabOffset;
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(_size.Width)), Math.Max(1, (int)Math.Ceiling(_size.Height)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card); bitmap.Freeze(); _image = bitmap;
    }

    public void FollowCursor()
    {
        if (!GetCursorPos(out NativePoint point)) return;
        MoveTo(AdornedElement.PointFromScreen(new Point(point.X, point.Y)));
    }

    public void MoveTo(Point pointer) { _position = pointer; InvalidateVisual(); }

    public void ShowInsertion(Rect? bounds) { _insertion = bounds; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        if (_insertion is Rect target)
        {
            var brush = (Brush)Application.Current.FindResource("AccentBrush");
            dc.DrawLine(new Pen(brush, 3), target.TopLeft, target.TopRight);
        }
        Rect card = new(new Point(_position.X - _grabOffset.X, _position.Y - _grabOffset.Y), _size);
        dc.PushOpacity(0.92);
        dc.DrawRoundedRectangle((Brush)Application.Current.FindResource("PanelBrush"), new Pen((Brush)Application.Current.FindResource("AccentBrush"), 2), card, 5, 5);
        dc.DrawImage(_image, card);
        dc.Pop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
