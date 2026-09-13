using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace ArcTrellis.App;

public static class DropdownChrome
{
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal static bool ForegroundBelongsToApp()
    {
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out uint processId) != 0 && processId == Environment.ProcessId;
    }

    // A captured context menu can report itself as the event source even when
    // the pointer is over a separate popup HWND. Test screen coordinates too.
    internal static bool PointerWithin(FrameworkElement? surface)
    {
        if (surface is not { IsVisible: true } || PresentationSource.FromVisual(surface) is null || !GetCursorPos(out var cursor)) return false;
        var local = surface.PointFromScreen(new Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.Y >= 0 && local.X <= surface.ActualWidth && local.Y <= surface.ActualHeight;
    }
    internal static bool Contains(DependencyObject? root, DependencyObject? source)
    {
        if (root is null) return false;
        var visited = new HashSet<DependencyObject>();
        while (source is not null && visited.Add(source))
        {
            if (ReferenceEquals(root, source)) return true;
            source = LogicalTreeHelper.GetParent(source) ?? (source is Visual ? VisualTreeHelper.GetParent(source) : null);
        }
        return false;
    }
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(DropdownChrome), new PropertyMetadata(false, Changed));
    public static bool GetCompact(DependencyObject element) => (bool)element.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject element, bool value) => element.SetValue(CompactProperty, value);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element || args.NewValue is not true) return;
        element.Resources[typeof(ScrollBar)] = Application.Current.FindResource("DropdownScrollBarStyle");
        element.Resources[SystemParameters.VerticalScrollBarWidthKey] = 8d;
        element.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 8d;
    }
}
