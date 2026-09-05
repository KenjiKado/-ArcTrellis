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
        _image = CaptureCard(card);
    }

    internal static BitmapSource CaptureCard(FrameworkElement card)
    {
        // Render at a new origin: rendering the arranged card directly includes its
        // position in the stack, which clips cards below the first out of the bitmap.
        var bounds = new Rect(card.RenderSize);
        var brush = new VisualBrush(card)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds,
            Stretch = Stretch.Fill
        };
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, bounds);
        var dpi = VisualTreeHelper.GetDpi(card);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
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
