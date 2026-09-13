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

    internal static void OpenFitted(FrameworkElement target, FrameworkElement content, Popup menu)
    {
        content.Measure(new Size(content.Width, double.PositiveInfinity));
        Point Fit()
        {
            var transform = PresentationSource.FromVisual(target)!.CompositionTarget.TransformToDevice;
            // DesiredSize can be capped by the popup; arranged content includes
            // chip rows added after it opens.
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
        void Place()
        {
            if (!menu.IsOpen) return;
            var screen = Fit();
            var local = target.PointFromScreen(screen);
            if (Math.Abs(menu.HorizontalOffset - local.X) > 0.5) menu.HorizontalOffset = local.X;
            if (Math.Abs(menu.VerticalOffset - local.Y) > 0.5) menu.VerticalOffset = local.Y;
            menu.UpdateLayout();
            // Relative placement can center the popup around a narrow button.
            // Position its HWND from the button's left edge after WPF places it.
            if (PresentationSource.FromVisual(content) is HwndSource source)
                SetWindowPos(source.Handle, IntPtr.Zero, (int)Math.Round(screen.X), (int)Math.Round(screen.Y), 0, 0, 0x0015);
        }
        bool queued = false;
        SizeChangedEventHandler resized = (_, _) =>
        {
            if (!menu.IsOpen || queued) return;
            queued = true;
            menu.Dispatcher.BeginInvoke(new Action(() =>
            {
                queued = false;
                Place();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        content.SizeChanged += resized;
        menu.Closed += (_, _) => content.SizeChanged -= resized;
        menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(new Action(Place), System.Windows.Threading.DispatcherPriority.Loaded);
        // Relative uses the full work area to measure the filter's natural
        // height; nested dropdowns can then take and restore popup capture.
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Relative;
        var initial = target.PointFromScreen(Fit());
        menu.HorizontalOffset = initial.X;
        menu.VerticalOffset = initial.Y;
        menu.IsOpen = true;
    }

    internal static void Open(FrameworkElement target, Point screenPoint, PlacementMode placement = PlacementMode.RelativePoint)
    {
        if (target.ContextMenu is not { } menu) return;
        menu.PlacementTarget = target;
        menu.Placement = placement;
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
