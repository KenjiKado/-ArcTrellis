using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;

namespace ArcTrellis.App.Views;

internal static class GalleryImageSupport
{
    private readonly record struct PreviewCacheKey(
        string Path, int DecodeWidth, long Length, long LastWriteTicks);

    private const int MaxCachedPreviews = 128;
    private static readonly SemaphoreSlim DecodeSlots = new(3, 3);
    private static readonly ConcurrentDictionary<PreviewCacheKey, ImageSource> PreviewCache = new();
    private static readonly ConcurrentQueue<PreviewCacheKey> CacheOrder = new();
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

    internal static async Task<ImageSource?> LoadPreviewAsync(
        string path, int decodeWidth, CancellationToken cancellationToken)
    {
        PreviewCacheKey? key = CreateCacheKey(path, decodeWidth);
        if (key is null) return null;
        if (PreviewCache.TryGetValue(key.Value, out ImageSource? cached)) return cached;

        await DecodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (PreviewCache.TryGetValue(key.Value, out cached)) return cached;
            ImageSource? preview = await Task.Run(
                () => LoadPreview(key.Value.Path, key.Value.DecodeWidth), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (preview is null) return null;
            if (PreviewCache.TryAdd(key.Value, preview))
            {
                CacheOrder.Enqueue(key.Value);
                TrimCache();
            }
            return preview;
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static PreviewCacheKey? CreateCacheKey(string path, int decodeWidth)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            return new PreviewCacheKey(file.FullName, decodeWidth, file.Length,
                file.LastWriteTimeUtc.Ticks);
        }
        catch { return null; }
    }

    private static void TrimCache()
    {
        while (PreviewCache.Count > MaxCachedPreviews && CacheOrder.TryDequeue(out var oldest))
            PreviewCache.TryRemove(oldest, out _);
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
    private readonly Grid _gallerySurface = new();
    private readonly ScrollViewer _galleryScroller;
    private readonly DockPanel _layout;
    private readonly Border _busyOverlay;
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _thumbnailLoadTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _thumbnailLoadCancellation = new();
    private bool _gridInitialized;
    private bool _isBusy;
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
    internal int GalleryColumnCount => _galleryColumnCount;
    internal double GalleryTileWidth => _galleryTileWidth;
    internal double GalleryRightGap => Math.Max(0,
        _galleryScroller.ViewportWidth - _galleryColumnCount * (_galleryTileWidth + TileHorizontalMargin));
    internal bool IsBusy => _isBusy;

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
        AddImageButton.Click += async (_, _) => await ChooseImagesAsync();
        DeleteImageButton.Click += (_, _) => DeleteSelected();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(AddImageButton);
        toolbar.Children.Add(DeleteImageButton);

        PreviewKeyDown += (_, e) =>
        {
            if (_isBusy) { e.Handled = true; return; }
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        };

        _empty.Text = _paths.Count == 0 ? Loc.T("Drop images here") : Loc.T("Loading images...");
        _galleryScroller = new ScrollViewer
        {
            Content = _images,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _gallerySurface.SizeChanged += (_, _) => FitGalleryTiles();
        _galleryScroller.SizeChanged += (_, _) => FitGalleryTiles();
        _gallerySurface.Children.Add(_galleryScroller);
        _gallerySurface.Children.Add(_empty);

        _layout = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(toolbar, Dock.Top);
        _layout.Children.Add(toolbar);
        _layout.Children.Add(_gallerySurface);

        _busyOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 12, 18, 29)),
            Child = CreateBusyIndicator(),
            Visibility = Visibility.Collapsed
        };
        var root = new Grid();
        root.Children.Add(_layout);
        root.Children.Add(_busyOverlay);
        Content = root;

        DragEnter += Gallery_DragOver;
        DragOver += Gallery_DragOver;
        Drop += Gallery_Drop;
        Loaded += (_, _) =>
        {
            Loc.Apply(this);
            FitGalleryTiles();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                new Action(EnsureGridInitialized));
        };
        Closed += (_, _) =>
        {
            _thumbnailLoadCancellation.Cancel();
            _thumbnailLoadCancellation.Dispose();
        };
    }

    internal int AddFilesForTest(IEnumerable<string> paths)
    {
        EnsureGridInitialized();
        List<string> addedPaths = AppendImages(paths);
        if (addedPaths.Count > 0) RefreshGrid(addedPaths[^1]);
        return addedPaths.Count;
    }

    internal void InitializeGridForTest() => EnsureGridInitialized();

    internal void SelectForTest(string path)
    {
        _selectedPaths.Clear();
        if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _selectedPaths.Add(path);
        RefreshSelectionVisuals();
    }

    internal void DeleteSelectedForTest() => DeleteSelected();

    private async Task ChooseImagesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Add image"),
            Multiselect = true,
            CheckFileExists = true,
            Filter = $"{Loc.T("Image files")} (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true) await AddImagesWithFeedbackAsync(dialog.FileNames);
    }

    private List<string> AppendImages(IEnumerable<string> paths)
    {
        var addedPaths = new List<string>();
        foreach (string rawPath in paths)
        {
            if (!GalleryImageSupport.IsSupported(rawPath) || !File.Exists(rawPath)) continue;
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch { continue; }
            if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            _paths.Add(path);
            addedPaths.Add(path);
        }
        return addedPaths;
    }

    private async Task<int> AddImagesWithFeedbackAsync(IEnumerable<string> paths)
    {
        EnsureGridInitialized();
        List<string> addedPaths = AppendImages(paths);
        if (addedPaths.Count == 0) return 0;

        SetBusy(true);
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            RefreshGrid(addedPaths[^1]);
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            Task[] addedLoads = addedPaths
                .Select(path => _thumbnailLoadTasks.GetValueOrDefault(path))
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            if (addedLoads.Length > 0) await Task.WhenAll(addedLoads);
        }
        catch (OperationCanceledException) when (!IsLoaded) { }
        finally
        {
            SetBusy(false);
        }
        return addedPaths.Count;
    }

    private void DeleteSelected()
    {
        if (_isBusy || _selectedPaths.Count == 0) return;
        _paths.RemoveAll(path => _selectedPaths.Contains(path));
        _selectedPaths.Clear();
        RefreshGrid();
    }

    private void RefreshGrid(string? selectedPath = null)
    {
        _thumbnailLoadCancellation.Cancel();
        _thumbnailLoadCancellation.Dispose();
        _thumbnailLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _thumbnailLoadCancellation.Token;
        _thumbnailLoadTasks.Clear();
        _images.Children.Clear();
        _selectedPaths.RemoveWhere(path => !_paths.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (selectedPath is not null)
        {
            _selectedPaths.Clear();
            _selectedPaths.Add(selectedPath);
        }
        foreach (string path in _paths.AsEnumerable().Reverse())
        {
            GalleryTileVisual tile = CreateImageTile(path);
            Border item = tile.Container;
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
            _thumbnailLoadTasks[path] = LoadTilePreviewAsync(tile, path, cancellationToken);
        }
        _empty.Visibility = _paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FitGalleryTiles();
        RefreshSelectionVisuals();
    }

    private void FitGalleryTiles()
    {
        double layoutWidth = _gallerySurface.ActualWidth;
        if (!double.IsFinite(layoutWidth) || layoutWidth <= 0) return;
        int columns = Math.Max(1, (int)Math.Round(
            layoutWidth / TileTargetSlotWidth, MidpointRounding.AwayFromZero));
        double viewportWidth = _galleryScroller.ViewportWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            viewportWidth = layoutWidth;
        double tileWidth = Math.Max(150,
            Math.Floor(viewportWidth / columns - TileHorizontalMargin));
        _galleryColumnCount = columns;
        _galleryTileWidth = tileWidth;
        _images.Width = viewportWidth;
        foreach (Border item in _images.Children) item.Width = tileWidth;
    }

    private static GalleryTileVisual CreateImageTile(string path)
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
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed
        };
        var placeholder = new TextBlock
        {
            Text = Loc.T("Loading images..."),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        grid.Children.Add(image);
        grid.Children.Add(placeholder);
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
        return new GalleryTileVisual(border, image, placeholder);
    }

    private static async Task LoadTilePreviewAsync(
        GalleryTileVisual tile, string path, CancellationToken cancellationToken)
    {
        try
        {
            ImageSource? source = await GalleryImageSupport.LoadPreviewAsync(path, 384,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (source is not null)
            {
                tile.Image.Source = source;
                tile.Image.Visibility = Visibility.Visible;
                tile.Placeholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                tile.Placeholder.Text = "▧";
                tile.Placeholder.FontSize = 38;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EnsureGridInitialized()
    {
        if (_gridInitialized) return;
        _gridInitialized = true;
        RefreshGrid();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _layout.IsHitTestVisible = !busy;
        _layout.Opacity = busy ? 0.48 : 1;
        _busyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AllowDrop = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private static FrameworkElement CreateBusyIndicator()
    {
        const double canvasSize = 48;
        const double dotSize = 7;
        const int dotCount = 10;
        var spinner = new Canvas
        {
            Width = canvasSize,
            Height = canvasSize,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        for (int index = 0; index < dotCount; index++)
        {
            double angle = index * Math.PI * 2 / dotCount;
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Opacity = 0.2 + 0.8 * (index + 1) / dotCount
            };
            dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
            Canvas.SetLeft(dot, canvasSize / 2 + Math.Cos(angle) * 18 - dotSize / 2);
            Canvas.SetTop(dot, canvasSize / 2 + Math.Sin(angle) * 18 - dotSize / 2);
            spinner.Children.Add(dot);
        }
        var rotation = new RotateTransform();
        spinner.RenderTransform = rotation;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360,
            TimeSpan.FromMilliseconds(850)) { RepeatBehavior = RepeatBehavior.Forever });

        var label = new TextBlock
        {
            Text = Loc.T("Loading images..."),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(spinner);
        panel.Children.Add(label);
        return panel;
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
        if (_isBusy || _selectedPaths.FirstOrDefault() is not { } path) return;
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

    private async void Gallery_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        await AddImagesWithFeedbackAsync(DraggedImagePaths(e.Data));
    }

    private static IEnumerable<string> DraggedImagePaths(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, true) ||
            data.GetData(DataFormats.FileDrop, true) is not string[] files) return [];
        return files.Where(path => GalleryImageSupport.IsSupported(path) && File.Exists(path));
    }

    private sealed record GalleryTileVisual(Border Container, Image Image, TextBlock Placeholder);
}
