using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace ArcTrellis.App;

internal static class TimelineMenuPosition
{
    internal static void Attach(FrameworkElement target)
    {
        target.PreviewMouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Open(target, target.PointToScreen(e.GetPosition(target)));
        };
        target.ContextMenuOpening += (_, e) =>
        {
            e.Handled = true;
            if (e.CursorLeft < 0) Open(target, target.PointToScreen(new Point(0, target.ActualHeight)));
        };
    }

    internal static void OpenFitted(FrameworkElement target, FrameworkElement content)
    {
        // Fit the requested position before opening. WPF otherwise measures a
        // tall filter only against the space below its anchor and clips it.
        content.Measure(new Size(content.Width, double.PositiveInfinity));
        if (target.ContextMenu is not { } menu) return;
        Point Fit()
        {
        var transform = PresentationSource.FromVisual(target)!.CompositionTarget.TransformToDevice;
        // DesiredSize is capped by the popup's current available space. A
        // StackPanel still arranges its children at their natural size, so use
        // that size when chips wrap and require the menu to move upward.
        var size = transform.Transform(new Vector(Math.Max(content.DesiredSize.Width, content.ActualWidth) + 2,
            Math.Max(content.DesiredSize.Height, content.ActualHeight) + 2));
        var screen = target.PointToScreen(new Point(0, target.ActualHeight));
        var point = new NativePoint { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromPoint(point, 2), ref monitor))
        {
            screen.X = Math.Max(monitor.Work.Left, Math.Min(screen.X, monitor.Work.Right - size.X));
            screen.Y = Math.Max(monitor.Work.Top, Math.Min(screen.Y, monitor.Work.Bottom - size.Y));
        }
        return screen;
        }
        bool queued = false;
        SizeChangedEventHandler resized = (_, _) =>
        {
            if (!menu.IsOpen || queued) return;
            queued = true;
            menu.Dispatcher.BeginInvoke(new Action(() =>
            {
                queued = false;
                if (!menu.IsOpen) return;
                var local = target.PointFromScreen(Fit());
                if (Math.Abs(menu.HorizontalOffset - local.X) > 0.5) menu.HorizontalOffset = local.X;
                if (Math.Abs(menu.VerticalOffset - local.Y) > 0.5) menu.VerticalOffset = local.Y;
                menu.InvalidateMeasure();
            }));
        };
        content.SizeChanged += resized;
        menu.Closed += (_, _) => content.SizeChanged -= resized;
        Open(target, Fit());
    }

    internal static void Open(FrameworkElement target, Point screenPoint)
    {
        if (target.ContextMenu is not { } menu) return;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.RelativePoint;
        var local = target.PointFromScreen(screenPoint);
        menu.HorizontalOffset = local.X; menu.VerticalOffset = local.Y;
        RoutedEventHandler? opened = null;
        opened = (_, _) =>
        {
            menu.Opened -= opened;
            menu.UpdateLayout();
            // Position the popup HWND after WPF has applied Windows' handedness rules.
            if (PresentationSource.FromVisual(menu) is not HwndSource source) return;
            GetWindowRect(source.Handle, out var bounds);
            var point = new NativePoint { X = (int)Math.Round(screenPoint.X), Y = (int)Math.Round(screenPoint.Y) };
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            int x = point.X, y = point.Y;
            if (GetMonitorInfo(MonitorFromPoint(point, 2), ref monitor))
            {
                x = Math.Max(monitor.Work.Left, Math.Min(x, monitor.Work.Right - (bounds.Right - bounds.Left)));
                y = Math.Max(monitor.Work.Top, Math.Min(y, monitor.Work.Bottom - (bounds.Bottom - bounds.Top)));
            }
            SetWindowPos(source.Handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        };
        menu.Opened += opened;
        menu.IsOpen = true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
