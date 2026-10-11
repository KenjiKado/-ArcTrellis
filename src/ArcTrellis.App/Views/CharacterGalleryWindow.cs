using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private const int PreviewLimit = 5;
    private readonly StackPanel _previews = new() { Orientation = Orientation.Horizontal };
    internal IReadOnlyList<string> DisplayedPaths { get; }
    internal int HiddenImageCount { get; }
    internal string BadgeText { get; }
    internal int PreviewCount => _previews.Children.Count;
    internal int ImagePreviewToolTipCount => _previews.Children.OfType<Grid>()
        .Take(DisplayedPaths.Count)
        .Count(surface => surface.ToolTip is not null ||
            surface.Children.OfType<FrameworkElement>().Any(child => child.ToolTip is not null));
    internal event EventHandler? OpenRequested;

    internal CharacterGalleryPreview(IEnumerable<string> imagePaths)
    {
        var paths = imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        DisplayedPaths = paths.TakeLast(PreviewLimit).Reverse().ToList();
        HiddenImageCount = Math.Max(0, paths.Count - DisplayedPaths.Count);
        BadgeText = HiddenImageCount == 0 ? "+" : FormatHiddenCount(HiddenImageCount);
        Cursor = Cursors.Hand;
        Focusable = true;
        AutomationProperties.SetName(this, Loc.T("Gallery"));
        Content = _previews;

        for (int index = 0; index < DisplayedPaths.Count; index++)
        {
            string path = DisplayedPaths[index];
            _previews.Children.Add(CreateCircle(path, "▧", index > 0));
        }
        string badgeToolTip = HiddenImageCount == 0
            ? Loc.T("Add image")
            : string.Format(Loc.T("{0} more images"), HiddenImageCount);
        _previews.Children.Add(CreateBadge(BadgeText, badgeToolTip, DisplayedPaths.Count > 0));

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

    internal static string FormatHiddenCount(int count)
    {
        if (count < 1000) return $"+{Math.Max(0, count)}";
        if (count < 1_000_000) return $"+{FormatCompact(count, 1_000)}k";
        if (count < 1_000_000_000) return $"+{FormatCompact(count, 1_000_000)}m";
        return $"+{FormatCompact(count, 1_000_000_000)}b";
    }

    private static string FormatCompact(int count, double unit)
    {
        double scaled = count / unit;
        scaled = scaled < 10 ? Math.Floor(scaled * 10) / 10 : Math.Floor(scaled);
        return scaled.ToString(scaled % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
    }

    private static Grid CreateCircle(string path, string fallback, bool overlap)
    {
        const double size = 56;
        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 2
        };
        circle.SetResourceReference(Shape.StrokeProperty, "PanelBrush");
        ImageSource? preview = GalleryImageSupport.LoadPreview(path, 160);
        if (preview is null) circle.SetResourceReference(Shape.FillProperty, "ElevatedBrush");
        else circle.Fill = new ImageBrush(preview) { Stretch = Stretch.UniformToFill };

        var surface = new Grid
        {
            Width = size,
            Height = size,
            Margin = overlap ? new Thickness(-10, 0, 0, 0) : new Thickness(0)
        };
        surface.Children.Add(circle);
        if (preview is null)
        {
            var label = new TextBlock
            {
                Text = fallback,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            surface.Children.Add(label);
        }
        Panel.SetZIndex(surface, overlap ? 1 : 0);
        return surface;
    }

    private static Grid CreateBadge(string text, string toolTip, bool overlap)
    {
        const double size = 56;
        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 2,
            ToolTip = toolTip
        };
        circle.SetResourceReference(Shape.FillProperty, "MutedBrush");
        circle.SetResourceReference(Shape.StrokeProperty, "PanelBrush");

        var label = new TextBlock
        {
            Text = text,
            FontSize = text.Length >= 4 ? 15 : 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "PanelBrush");

        var surface = new Grid
        {
            Width = size,
            Height = size,
            Margin = overlap ? new Thickness(-10, 0, 0, 0) : new Thickness(0),
            ToolTip = toolTip
        };
        surface.Children.Add(circle);
        surface.Children.Add(label);
        Panel.SetZIndex(surface, 2);
        return surface;
    }
}

public sealed class CharacterGalleryWindow : Window
{
    private const double TileTargetSlotWidth = 210;
    private const double TileHorizontalMargin = 10;
    private readonly List<string> _paths;
    private readonly WrapPanel _images = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _galleryScroller;
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _galleryColumnCount = 1;
    private double _galleryTileWidth = 190;
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
    internal int GridItemCount => _images.Children.Count;
    internal IReadOnlyList<string> DisplayedGridPaths => _images.Children.Cast<FrameworkElement>()
        .Select(item => item.Tag).OfType<string>().ToList();
    internal int RenderedGridItemCount => _images.Children.Cast<FrameworkElement>()
        .Count(item => item.IsVisible && item.ActualWidth > 0 && item.ActualHeight > 0);
    internal double GalleryRightGap => Math.Max(0,
        _galleryScroller.ViewportWidth - _galleryColumnCount * (_galleryTileWidth + TileHorizontalMargin));

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

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        };

        _empty.Text = Loc.T("Drop images here");
        var gallerySurface = new Grid();
        _galleryScroller = new ScrollViewer
        {
            Content = _images,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _galleryScroller.SizeChanged += (_, _) => FitGalleryTiles();
        gallerySurface.Children.Add(_galleryScroller);
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
        Loaded += (_, _) =>
        {
            Loc.Apply(this);
            FitGalleryTiles();
        };
    }

    internal int AddFilesForTest(IEnumerable<string> paths) => AddImages(paths);

    internal void SelectForTest(string path)
    {
        _selectedPaths.Clear();
        if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _selectedPaths.Add(path);
        RefreshSelectionVisuals();
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
        if (_selectedPaths.Count == 0) return;
        _paths.RemoveAll(path => _selectedPaths.Contains(path));
        _selectedPaths.Clear();
        RefreshGrid();
    }

    private void RefreshGrid(string? selectedPath = null)
    {
        _images.Children.Clear();
        _selectedPaths.RemoveWhere(path => !_paths.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (selectedPath is not null)
        {
            _selectedPaths.Clear();
            _selectedPaths.Add(selectedPath);
        }
        foreach (string path in _paths.AsEnumerable().Reverse())
        {
            var item = CreateImageTile(path);
            item.Tag = path;
            item.Margin = new Thickness(5);
            item.ToolTip = path;
            item.Cursor = Cursors.Hand;
            item.Focusable = true;
            AutomationProperties.SetName(item, Path.GetFileName(path));
            item.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) _selectedPaths.Clear();
                if (!_selectedPaths.Add(path) && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    _selectedPaths.Remove(path);
                item.Focus();
                RefreshSelectionVisuals();
                if (e.ClickCount == 2) OpenImage(path);
                e.Handled = true;
            };
            _images.Children.Add(item);
        }
        _empty.Visibility = _paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FitGalleryTiles();
        RefreshSelectionVisuals();
    }

    private void FitGalleryTiles()
    {
        double viewportWidth = _galleryScroller.ViewportWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            viewportWidth = _galleryScroller.ActualWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0) return;

        _galleryColumnCount = Math.Max(1, (int)Math.Round(
            viewportWidth / TileTargetSlotWidth, MidpointRounding.AwayFromZero));
        _galleryTileWidth = Math.Max(150,
            Math.Floor(viewportWidth / _galleryColumnCount - TileHorizontalMargin));
        foreach (Border item in _images.Children) item.Width = _galleryTileWidth;
    }

    private static Border CreateImageTile(string path)
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

    private void RefreshSelectionVisuals()
    {
        foreach (Border item in _images.Children)
        {
            bool selected = item.Tag is string path && _selectedPaths.Contains(path);
            item.BorderThickness = new Thickness(selected ? 3 : 1);
            item.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
        }
        DeleteImageButton.IsEnabled = _selectedPaths.Count > 0;
    }

    private void OpenSelected()
    {
        if (_selectedPaths.FirstOrDefault() is not { } path) return;
        OpenImage(path);
    }

    private void OpenImage(string path)
    {
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
