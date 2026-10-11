using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;

namespace ArcTrellis.App.Views;

internal static class GalleryImageSupport
{
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".avif"
        };

    internal static bool IsSupported(string path) =>
        !string.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path));

    internal static ImageSource? LoadPreview(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }
}

internal sealed class CharacterGalleryPreview : UserControl
{
    private readonly StackPanel _previews = new() { Orientation = Orientation.Horizontal };
    internal IReadOnlyList<string> DisplayedPaths { get; }
    internal int PreviewCount => _previews.Children.Count;
    internal event EventHandler? OpenRequested;

    internal CharacterGalleryPreview(IEnumerable<string> imagePaths)
    {
        var paths = imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        DisplayedPaths = paths.TakeLast(5).ToList();
        Cursor = Cursors.Hand;
        Focusable = true;
        ToolTip = Loc.T("Gallery");
        AutomationProperties.SetName(this, Loc.T("Gallery"));
        Content = _previews;

        if (DisplayedPaths.Count == 0) _previews.Children.Add(CreateCircle(null, "+", Loc.T("Add image"), false));
        else
        {
            for (int index = 0; index < DisplayedPaths.Count; index++)
            {
                string path = DisplayedPaths[index];
                _previews.Children.Add(CreateCircle(path, "▧", Path.GetFileName(path), index > 0));
            }
        }

        MouseLeftButtonUp += (_, e) =>
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            OpenRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    private static Grid CreateCircle(string? path, string fallback, string toolTip, bool overlap)
    {
        const double size = 56;
        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 2,
            ToolTip = toolTip
        };
        circle.SetResourceReference(Shape.StrokeProperty, "PanelBrush");
        ImageSource? preview = path is null ? null : GalleryImageSupport.LoadPreview(path, 160);
        if (preview is null) circle.SetResourceReference(Shape.FillProperty, "ElevatedBrush");
        else circle.Fill = new ImageBrush(preview) { Stretch = Stretch.UniformToFill };

        var surface = new Grid
        {
            Width = size,
            Height = size,
            Margin = overlap ? new Thickness(-10, 0, 0, 0) : new Thickness(0),
            ToolTip = toolTip
        };
        surface.Children.Add(circle);
        if (preview is null)
        {
            var label = new TextBlock
            {
                Text = fallback,
                FontSize = path is null ? 28 : 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty,
                path is null ? "AccentBrush" : "MutedBrush");
            surface.Children.Add(label);
        }
        Panel.SetZIndex(surface, overlap ? 1 : 0);
        return surface;
    }
}

public sealed class CharacterGalleryWindow : Window
{
    private readonly List<string> _paths;
    private readonly ListBox _images = new()
    {
        SelectionMode = SelectionMode.Extended,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        HorizontalContentAlignment = HorizontalAlignment.Left
    };
    private readonly TextBlock _empty = new()
    {
        FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };

    internal Button AddImageButton { get; }
    internal Button DeleteImageButton { get; }
    internal IReadOnlyList<string> ImagePaths => _paths;
    internal int GridItemCount => _images.Items.Count;

    public CharacterGalleryWindow(string characterName, IEnumerable<string> imagePaths)
    {
        _paths = imagePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Title = $"{Loc.T("Gallery")} — {characterName}";
        Width = 940;
        Height = 680;
        MinWidth = 620;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AllowDrop = true;

        AddImageButton = new Button { Content = Loc.T("Add image") };
        DeleteImageButton = new Button { Content = Loc.T("Delete"), IsEnabled = false };
        AutomationProperties.SetName(AddImageButton, Loc.T("Add image"));
        AutomationProperties.SetName(DeleteImageButton, Loc.T("Delete"));
        AddImageButton.Click += (_, _) => ChooseImages();
        DeleteImageButton.Click += (_, _) => DeleteSelected();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(AddImageButton);
        toolbar.Children.Add(DeleteImageButton);

        var itemPanel = new FrameworkElementFactory(typeof(WrapPanel));
        itemPanel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        _images.ItemsPanel = new ItemsPanelTemplate(itemPanel);
        _images.SelectionChanged += (_, _) => DeleteImageButton.IsEnabled = _images.SelectedItems.Count > 0;
        _images.PreviewMouseDoubleClick += Images_MouseDoubleClick;
        _images.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        };

        _empty.Text = Loc.T("Drop images here");
        var gallerySurface = new Grid();
        gallerySurface.Children.Add(new ScrollViewer
        {
            Content = _images,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        gallerySurface.Children.Add(_empty);

        var layout = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(gallerySurface);
        Content = layout;

        DragEnter += Gallery_DragOver;
        DragOver += Gallery_DragOver;
        Drop += Gallery_Drop;
        RefreshGrid();
        Loaded += (_, _) => Loc.Apply(this);
    }

    internal int AddFilesForTest(IEnumerable<string> paths) => AddImages(paths);

    internal void SelectForTest(string path)
    {
        _images.SelectedItems.Clear();
        if (_images.Items.Cast<ListBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Tag as string, path, StringComparison.OrdinalIgnoreCase)) is { } match)
            match.IsSelected = true;
    }

    internal void DeleteSelectedForTest() => DeleteSelected();

    private void ChooseImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Add image"),
            Multiselect = true,
            CheckFileExists = true,
            Filter = $"{Loc.T("Image files")} (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true) AddImages(dialog.FileNames);
    }

    private int AddImages(IEnumerable<string> paths)
    {
        string? lastAdded = null;
        int count = 0;
        foreach (string rawPath in paths)
        {
            if (!GalleryImageSupport.IsSupported(rawPath) || !File.Exists(rawPath)) continue;
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch { continue; }
            if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            _paths.Add(path);
            lastAdded = path;
            count++;
        }
        if (count > 0) RefreshGrid(lastAdded);
        return count;
    }

    private void DeleteSelected()
    {
        var selected = _images.SelectedItems.Cast<ListBoxItem>()
            .Select(item => item.Tag as string)
            .Where(path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) return;
        _paths.RemoveAll(path => selected.Contains(path));
        RefreshGrid();
    }

    private void RefreshGrid(string? selectedPath = null)
    {
        _images.Items.Clear();
        foreach (string path in _paths)
        {
            var item = new ListBoxItem
            {
                Tag = path,
                Content = CreateImageTile(path),
                Padding = new Thickness(0),
                Margin = new Thickness(5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = path
            };
            _images.Items.Add(item);
            if (string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase)) item.IsSelected = true;
        }
        _empty.Visibility = _paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteImageButton.IsEnabled = _images.SelectedItems.Count > 0;
        if (_images.SelectedItem is not null) _images.ScrollIntoView(_images.SelectedItem);
    }

    private static FrameworkElement CreateImageTile(string path)
    {
        var border = new Border
        {
            Width = 190,
            Height = 170,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true
        };
        border.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid();
        ImageSource? source = GalleryImageSupport.LoadPreview(path, 480);
        if (source is not null) grid.Children.Add(new Image { Source = source, Stretch = Stretch.UniformToFill });
        else
        {
            var unavailable = new TextBlock
            {
                Text = "▧",
                FontSize = 38,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            unavailable.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            grid.Children.Add(unavailable);
        }
        var name = new TextBlock
        {
            Text = Path.GetFileName(path),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = new SolidColorBrush(Color.FromArgb(190, 18, 24, 38)),
            Foreground = Brushes.White,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        grid.Children.Add(name);
        border.Child = grid;
        return border;
    }

    private void Images_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(_images, source) is not ListBoxItem item) return;
        item.IsSelected = true;
        OpenSelected();
        e.Handled = true;
    }

    private void OpenSelected()
    {
        if (_images.SelectedItem is not ListBoxItem { Tag: string path }) return;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(this, Loc.T("The image file could not be opened."), Loc.T("Gallery"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Gallery_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DraggedImagePaths(e.Data).Any() ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Gallery_Drop(object sender, DragEventArgs e)
    {
        AddImages(DraggedImagePaths(e.Data));
        e.Handled = true;
    }

    private static IEnumerable<string> DraggedImagePaths(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, true) ||
            data.GetData(DataFormats.FileDrop, true) is not string[] files) return [];
        return files.Where(path => GalleryImageSupport.IsSupported(path) && File.Exists(path));
    }
}
