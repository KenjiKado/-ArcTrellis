using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ArcTrellis.App;

public static class DropdownChrome
{
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
