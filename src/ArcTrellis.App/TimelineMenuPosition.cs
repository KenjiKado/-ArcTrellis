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
