using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcTrellis.App.Views;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;
using Microsoft.Win32;

namespace ArcTrellis.App;

public partial class MainWindow : Window
{
    private const string NumericInputTag = "Numeric";
    private readonly ProjectService _projects = new();
    private readonly TemplateService _templates = new();
    private readonly ExportService _exports = new();
    private readonly ImportService _imports = new();
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private static readonly string ThemeSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTrellis", "theme.txt");
    private MainViewModel Vm => (MainViewModel)DataContext;
    private bool _loaded;
    private bool _closingAfterSave;
    private double _timelineCardWidth = 220;
    private bool _isDark;
    private SceneDragAdorner? _sceneDragAdorner;
    private Guid? _draggedSceneId;
    private Point _sceneGrabOffset;
    private Border? _pressedSceneCard;
    private Point _scenePressPosition;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(CreateBlankProject());
        SetTheme(LoadDarkTheme(), false);
        SourceInitialized += (_, _) => ApplyWindowChromeTheme();
        Vm.ProjectReplaced += Vm_ProjectReplaced;
        Vm.PropertyChanged += (_, args) =>
        {
            if (_loaded && args.PropertyName == nameof(MainViewModel.SelectedBook))
                Dispatcher.BeginInvoke(new Action(() => { RefreshStats(); BuildTimeline(); }));
        };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        WorkspaceTabs.SelectionChanged += WorkspaceTabs_SelectionChanged;
        AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(NumericTextBox_PreviewTextInput));
        AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(NumericTextBox_Pasting));
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnyTextChanged));
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _autosaveTimer.Start();
        RefreshAll();
        ApplyLocalization();
        Dispatcher.BeginInvoke(new Action(ApplyLocalization), DispatcherPriority.Loaded);
        string? uiSmokeReport = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => x.StartsWith("--ui-smoke=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(uiSmokeReport))
        {
            SetTheme(true, false);
            WorkspaceTabs.SelectedIndex = 1;
            Dispatcher.BeginInvoke(new Action(() => RunUiSmoke(uiSmokeReport)), DispatcherPriority.ApplicationIdle);
            return;
        }
        if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) is { } startupFile && File.Exists(startupFile))
            _ = OpenProjectAsync(startupFile);
        else
            ShowNewProjectDialog(firstRun: true);
    }

    private void Vm_ProjectReplaced(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshAll));
    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WorkspaceTabs)) return;
        Dispatcher.BeginInvoke(new Action(ApplyLocalization), DispatcherPriority.Loaded);
    }
    private void AnyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || e.OriginalSource is not TextBox box || !box.IsKeyboardFocusWithin) return;
        if (IsNumericInput(box) && NormalizeNumericInput(box)) return;
        Vm.MarkDirty();
        Title = Vm.WindowTitle;
        RefreshStats();
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is not TextBox box || !IsNumericInput(box)) return;
        SelectZeroForReplacement(box);
        e.Handled = !IsValidNumericCandidate(ReplaceSelection(box, e.Text));
    }

    private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.OriginalSource is not TextBox box || !IsNumericInput(box)) return;
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) { e.CancelCommand(); return; }
        SelectZeroForReplacement(box);
        string pasted = e.DataObject.GetData(DataFormats.UnicodeText)?.ToString() ?? string.Empty;
        if (!IsValidNumericCandidate(ReplaceSelection(box, pasted))) e.CancelCommand();
    }

    private static bool IsNumericInput(TextBox box) => Equals(box.Tag, NumericInputTag);
    private static bool IsValidNumericCandidate(string value) => int.TryParse(value, out int number) && number >= 0;
    private static string ReplaceSelection(TextBox box, string value) => box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, value);
    private static void SelectZeroForReplacement(TextBox box) { if (box.Text == "0" && box.SelectionLength == 0) box.SelectAll(); }
    private static bool NormalizeNumericInput(TextBox box)
    {
        if (!string.IsNullOrEmpty(box.Text)) return false;
        box.Text = "0";
        box.CaretIndex = 1;
        return true;
    }
    private void AnySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (e.OriginalSource is not ComboBox box || !box.IsKeyboardFocusWithin) return;
        if (box.SelectedItem is Book) Dispatcher.BeginInvoke(new Action(BuildTimeline), DispatcherPriority.Background);
        else if (box.SelectedItem is not StoryEntity) Vm.MarkDirty();
    }

    private void RefreshAll()
    {
        Title = Vm.WindowTitle;
        BuildTimeline();
        RefreshRelations();
        RefreshStats();
        Loc.Apply(this);
    }

    private void RefreshStats()
    {
        int books = Vm.Project.Books.Count;
        int chapters = Vm.Project.Books.Sum(b => b.Chapters.Count);
        int scenes = Vm.Project.Scenes.Count;
        int drafted = Vm.Project.Scenes.Count(s => s.Status is "Drafted" or "Revised" or "Final");
        StatsText.Text = string.Join("\n", Loc.F("{0} book(s)", books), Loc.F("{0} chapter(s)", chapters), Loc.F("{0} scene card(s)", scenes), Loc.F("{0} plotline(s)", Vm.Project.Plotlines.Count), Loc.F("{0} character(s)", Vm.Project.Characters.Count), Loc.F("{0} place(s)", Vm.Project.Places.Count), Loc.F("{0} scenes drafted", drafted));
        int currentWords = Vm.SelectedBook?.CurrentWordCount ?? 0;
        int goal = Vm.SelectedBook?.WordCountGoal ?? 0;
        double progress = goal <= 0 ? 0 : Math.Min(100, currentWords * 100d / goal);
        ProgressBar.Value = progress;
        ProgressText.Text = Loc.F("{0:N0} / {1:N0} words ({2:0}%)", currentWords, goal, progress);
    }

    private void BuildTimeline()
    {
        TimelineGrid.Children.Clear(); TimelineGrid.RowDefinitions.Clear(); TimelineGrid.ColumnDefinitions.Clear();
        var book = Vm.SelectedBook;
        if (book is null) return;
        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var plotlines = Vm.BookPlotlines.ToList();
        TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(130, book.TimelineLabelWidth)) });
        foreach (var chapter in chapters) TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(chapter.TimelineWidth >= 130 ? chapter.TimelineWidth : _timelineCardWidth) });
        TimelineGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = book.TimelineHeaderHeight });
        foreach (var plotline in plotlines) TimelineGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = plotline.TimelineHeight });

        AddTimelineCell(new TextBlock { Text = Loc.T("PLOTLINE / CHAPTER"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8) }, 0, 0, false);
        for (int c = 0; c < chapters.Count; c++)
        {
            var header = new StackPanel { Margin = new Thickness(5) };
            header.Children.Add(new TextBlock { Text = chapters[c].Section, Foreground = FindBrush("MutedBrush"), FontSize = 11 });
            header.Children.Add(new TextBlock { Text = chapters[c].Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            AddTimelineCell(header, 0, c + 1, false);
        }
        for (int r = 0; r < plotlines.Count; r++)
        {
            var plot = plotlines[r];
            var plotName = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            plotName.SetBinding(TextBlock.TextProperty, new Binding(nameof(Plotline.Name)) { Source = plot });
            var plotText = new StackPanel();
            plotText.Children.Add(plotName);
            var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FindBrush("MutedBrush"), Margin = new Thickness(0, 6, 0, 0) };
            description.SetBinding(TextBlock.TextProperty, new Binding(nameof(Plotline.Description)) { Source = plot });
            var descriptionStyle = new Style(typeof(TextBlock));
            foreach (object? empty in new object?[] { null, "" })
            {
                var trigger = new DataTrigger { Binding = new Binding(nameof(Plotline.Description)) { Source = plot }, Value = empty };
                trigger.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
                descriptionStyle.Triggers.Add(trigger);
            }
            description.Style = descriptionStyle;
            plotText.Children.Add(description);
            var label = new Border { Tag = plot, Cursor = Cursors.Hand, BorderThickness = new Thickness(5, 0, 0, 0), BorderBrush = BrushFrom(plot.Color), Padding = new Thickness(8), Child = plotText };
            label.MouseLeftButtonDown += PlotlineLabel_MouseLeftButtonDown;
            var plotCell = AddTimelineCell(label, r + 1, 0, false);
            plotCell.ContextMenu = CreatePlotlineMenu(plot);
            AnchorTimelineContextMenu(plotCell);
            for (int c = 0; c < chapters.Count; c++)
            {
                var stack = new StackPanel { MinHeight = 105 };
                foreach (var scene in Vm.Project.Scenes.Where(s => s.BookId == book.Id && s.ChapterId == chapters[c].Id && s.PlotlineId == plot.Id).OrderBy(s => s.Order))
                    stack.Children.Add(CreateSceneCard(scene, plot));
                if (stack.Children.Count == 0) stack.Children.Add(new TextBlock { Text = Loc.T("Drop a scene here or double-click"), Foreground = FindBrush("MutedBrush"), Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap });
                var border = AddTimelineCell(stack, r + 1, c + 1, true);
                border.Tag = (chapters[c].Id, plot.Id);
                border.Drop += TimelineCell_Drop;
                border.DragOver += TimelineCell_DragOver;
                border.DragLeave += (_, _) => _sceneDragAdorner?.ShowInsertion(null);
                border.MouseLeftButtonDown += TimelineCell_MouseLeftButtonDown;
            }
        }
        AddTimelineResizeHandles(book, chapters, plotlines);
    }

    private void AddTimelineResizeHandles(Book book, List<Chapter> chapters, List<Plotline> plotlines)
    {
        for (int c = 0; c < TimelineGrid.ColumnDefinitions.Count; c++)
        {
            int index = c;
            AddTimelineResizeHandle(true, c, value =>
            {
                if (index == 0) book.TimelineLabelWidth = value;
                else chapters[index - 1].TimelineWidth = value;
            });
        }
        for (int r = 0; r < TimelineGrid.RowDefinitions.Count; r++)
        {
            int index = r;
            AddTimelineResizeHandle(false, r, value =>
            {
                if (index == 0) book.TimelineHeaderHeight = value;
                else plotlines[index - 1].TimelineHeight = value;
            });
        }
    }

    private void AddTimelineResizeHandle(bool column, int index, Action<double> save)
    {
        var handle = new Thumb
        {
            Cursor = column ? Cursors.SizeWE : Cursors.SizeNS,
            Background = Brushes.Transparent,
            HorizontalAlignment = column ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            VerticalAlignment = column ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
            Tag = column ? "TimelineColumnResize" : "TimelineRowResize",
            Focusable = false
        };
        // An explicit template avoids the default raised button appearance.
        var visual = new FrameworkElementFactory(typeof(Border));
        visual.SetBinding(Border.BackgroundProperty, new Binding(nameof(Thumb.Background)) { Source = handle });
        handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = visual };
        if (column)
        {
            handle.Width = 6;
            Grid.SetColumn(handle, index);
            Grid.SetRowSpan(handle, TimelineGrid.RowDefinitions.Count);
        }
        else
        {
            handle.Height = 6;
            Grid.SetRow(handle, index);
            Grid.SetColumnSpan(handle, TimelineGrid.ColumnDefinitions.Count);
        }
        Panel.SetZIndex(handle, 10);
        double size = 0, original = 0;
        handle.DragStarted += (_, e) =>
        {
            size = column ? TimelineGrid.ColumnDefinitions[index].ActualWidth : TimelineGrid.RowDefinitions[index].ActualHeight;
            original = column ? TimelineGrid.ColumnDefinitions[index].Width.Value : TimelineGrid.RowDefinitions[index].MinHeight;
            e.Handled = true;
        };
        handle.DragDelta += (_, e) =>
        {
            size = Math.Clamp(size + (column ? e.HorizontalChange : e.VerticalChange), column ? 130 : 40, 4000);
            if (column) TimelineGrid.ColumnDefinitions[index].Width = new GridLength(size);
            else TimelineGrid.RowDefinitions[index].MinHeight = size;
            e.Handled = true;
        };
        handle.DragCompleted += (_, e) =>
        {
            if (e.Canceled)
            {
                if (column) TimelineGrid.ColumnDefinitions[index].Width = new GridLength(original);
                else TimelineGrid.RowDefinitions[index].MinHeight = original;
            }
            else
            {
                save(size);
                Vm.Project.ModifiedUtc = DateTime.UtcNow;
                Vm.MarkDirty();
            }
            handle.Background = Brushes.Transparent;
            e.Handled = true;
        };
        TimelineGrid.Children.Add(handle);
    }

    private Border AddTimelineCell(UIElement child, int row, int column, bool allowDrop)
    {
        var border = new Border { Background = FindBrush("PanelBrush"), BorderBrush = FindBrush("BorderBrush"), BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(5), AllowDrop = allowDrop, Child = child };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); TimelineGrid.Children.Add(border); return border;
    }

    private Border CreateSceneCard(Scene scene, Plotline plot)
    {
        var card = SceneCardVisual.Create(scene, BrushColor(plot.Color));
        card.ContextMenu = CreateSceneMenu(scene);
        AnchorTimelineContextMenu(card);
        card.PreviewMouseMove += SceneCard_MouseMove;
        card.GiveFeedback += (_, _) => _sceneDragAdorner?.FollowCursor();
        card.MouseLeftButtonDown += SceneCard_MouseLeftButtonDown;
        card.MouseLeftButtonUp += SceneCard_MouseLeftButtonUp;
        card.LostMouseCapture += (_, _) => { if (ReferenceEquals(_pressedSceneCard, card)) _pressedSceneCard = null; };
        return card;
    }

    private void SceneCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Scene } card) return;
        _pressedSceneCard = card;
        _scenePressPosition = e.GetPosition(this);
        _sceneGrabOffset = e.GetPosition(card);
        card.CaptureMouse();
        e.Handled = true;
    }

    private void OpenTimelineScene(Scene scene)
    {
        var book = Vm.Project.Books.FirstOrDefault(b => b.Id == scene.BookId);
        if (book is null || !Vm.Project.Scenes.Contains(scene)) return;
        Vm.SelectedBook = book;
        Vm.SelectedChapter = book.Chapters.FirstOrDefault(c => c.Id == scene.ChapterId);
        Vm.SelectedPlotline = Vm.BookPlotlines.FirstOrDefault(p => p.Id == scene.PlotlineId);
        Vm.SelectedScene = scene;
        WorkspaceTabs.SelectedIndex = 3;
    }

    private void SceneCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Scene scene } card || !ReferenceEquals(card, _pressedSceneCard)) return;
        _pressedSceneCard = null;
        bool releasedOnCard = new Rect(card.RenderSize).Contains(e.GetPosition(card));
        card.ReleaseMouseCapture();
        if (releasedOnCard) OpenTimelineScene(scene);
        e.Handled = true;
    }

    private void SceneCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Border { Tag: Scene scene } card || !ReferenceEquals(card, _pressedSceneCard)) return;
        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - _scenePressPosition.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _scenePressPosition.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedSceneCard = null;
        card.ReleaseMouseCapture();
        var surface = (UIElement)Content;
        var layer = AdornerLayer.GetAdornerLayer(surface);
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => _sceneDragAdorner?.FollowCursor();
        try
        {
            _draggedSceneId = scene.Id;
            _sceneDragAdorner = new SceneDragAdorner(surface, card, _sceneGrabOffset);
            layer?.Add(_sceneDragAdorner);
            _sceneDragAdorner.FollowCursor();
            card.Opacity = 0.3;
            timer.Start();
            DragDrop.DoDragDrop(card, new DataObject("ArcTrellis.Scene", scene.Id.ToString()), DragDropEffects.Move);
        }
        finally
        {
            timer.Stop();
            card.Opacity = 1;
            if (_sceneDragAdorner is not null) layer?.Remove(_sceneDragAdorner);
            _sceneDragAdorner = null;
            _draggedSceneId = null;
        }
        e.Handled = true;
    }

    private void TimelineCell_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: ValueTuple<Guid, Guid> target }) return;
        if (!e.Data.GetDataPresent("ArcTrellis.Scene") || !Guid.TryParse(e.Data.GetData("ArcTrellis.Scene")?.ToString(), out var id)) return;
        var scene = Vm.Project.Scenes.FirstOrDefault(s => s.Id == id);
        if (scene is null) return;
        Vm.MoveScene(scene, target.Item1, target.Item2, GetTimelineInsertion((Border)sender, e.GetPosition((Border)sender).Y, scene.Id).Index);
        _sceneDragAdorner?.ShowInsertion(null);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        BuildTimeline();
    }

    private static (int Index, double Y) GetTimelineInsertion(Border border, double pointerY, Guid draggedId)
    {
        if (border.Child is not StackPanel stack) return (0, 8);
        var cards = stack.Children.OfType<Border>().Where(card => card.Tag is Scene scene && scene.Id != draggedId).ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            double top = cards[i].TranslatePoint(new Point(0, 0), border).Y;
            if (pointerY < top + cards[i].ActualHeight / 2) return (i, top - 2);
        }
        return (cards.Count, cards.Count == 0 ? 8 : cards[^1].TranslatePoint(new Point(0, cards[^1].ActualHeight), border).Y + 2);
    }

    private void TimelineCell_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (sender is not Border border || !e.Data.GetDataPresent("ArcTrellis.Scene") ||
            !Guid.TryParse(e.Data.GetData("ArcTrellis.Scene")?.ToString(), out Guid id) || id != _draggedSceneId) return;
        var slot = GetTimelineInsertion(border, e.GetPosition(border).Y, id);
        Point start = border.TranslatePoint(new Point(6, slot.Y), (UIElement)Content);
        _sceneDragAdorner?.ShowInsertion(new Rect(start, new Size(Math.Max(0, border.ActualWidth - 12), 0)));
        _sceneDragAdorner?.FollowCursor();
        e.Effects = DragDropEffects.Move;
    }

    private void TimelineCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ValueTuple<Guid, Guid> target }) return;
        if (Vm.BookPlotlines.FirstOrDefault(p => p.Id == target.Item2) is { } plotline)
            SelectTimelinePlotline(plotline);
        if (e.ClickCount != 2) return;
        Vm.SelectedChapter = Vm.SelectedBook?.Chapters.FirstOrDefault(c => c.Id == target.Item1);
        Vm.AddScene(target.Item1, target.Item2); BuildTimeline();
    }

    private void PlotlineLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Plotline plotline }) return;
        SelectTimelinePlotline(plotline);
        e.Handled = true;
        if (e.ClickCount == 2) EditTimelinePlotline(plotline);
    }

    private void EditTimelinePlotline(Plotline plotline)
    {
        SelectTimelinePlotline(plotline);
        var editor = new EditPlotlineWindow(plotline) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Vm.EditPlotline(plotline, editor.PlotlineTitle, editor.PlotlineDescription, editor.PlotlineColor);
        RefreshAll();
    }

    private static void AnchorTimelineContextMenu(FrameworkElement target)
    {
        target.ContextMenuOpening += (_, e) =>
        {
            if (target.ContextMenu is not { } menu) return;
            var point = e.CursorLeft < 0 ? new Point(0, target.ActualHeight) : Mouse.GetPosition(target);
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Custom;
            menu.HorizontalOffset = point.X;
            menu.VerticalOffset = point.Y;
            menu.CustomPopupPlacementCallback = (_, _, offset) =>
                new[] { new CustomPopupPlacement(offset, PopupPrimaryAxis.Horizontal) };
        };
    }

    private static ContextMenu TimelineContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.BackgroundProperty, "ElevatedBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.PaddingProperty, new Thickness(3));
        frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = frame };
        return menu;
    }

    private static MenuItem TimelineMenuItem(string text, Action action)
    {
        var item = new MenuItem { Header = Loc.T(text) };
        item.Click += (_, e) => { e.Handled = true; action(); };
        return item;
    }

    private ContextMenu CreatePlotlineMenu(Plotline plotline)
    {
        var menu = TimelineContextMenu();
        menu.Items.Add(TimelineMenuItem("Edit plotline", () => EditTimelinePlotline(plotline)));
        var delete = TimelineMenuItem("Delete plotline", () =>
        {
            SelectTimelinePlotline(plotline);
            DeletePlotline_Click(this, new RoutedEventArgs());
        });
        menu.Items.Add(delete);
        menu.Opened += (_, _) => delete.IsEnabled = Vm.BookPlotlines.Count() > 1;
        return menu;
    }

    private ContextMenu CreateSceneMenu(Scene scene)
    {
        var menu = TimelineContextMenu();
        menu.Items.Add(TimelineMenuItem("Edit scene", () => OpenTimelineScene(scene)));
        menu.Items.Add(TimelineMenuItem("Duplicate scene", () => { Vm.DuplicateScene(scene); RefreshAll(); }));
        menu.Items.Add(TimelineMenuItem("Delete scene", () =>
        {
            Vm.SelectedScene = scene;
            DeleteScene_Click(this, new RoutedEventArgs());
        }));
        return menu;
    }

    private void SelectTimelinePlotline(Plotline plotline)
    {
        Vm.SelectedPlotline = plotline;
    }

    private void RefreshRelations()
    {
        var entities = Vm.Project.Characters.Concat(Vm.Project.Places).Concat(Vm.Project.Notes).ToList();
        RelationFrom.ItemsSource = entities; RelationTo.ItemsSource = entities;
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (!Vm.IsDirty) return;
        try
        {
            await _projects.SaveAsync(Vm.Project, _projects.GetAutosavePath(Vm.Project.Id), false);
            Vm.Status = Loc.F("Autosaved locally at {0}", DateTime.Now.ToShortTimeString());
        }
        catch (Exception ex) { Vm.Status = Loc.F("Autosave failed: {0}", ex.Message); }
    }

    private void New_Click(object sender, RoutedEventArgs e) => ShowNewProjectDialog(false);
    private void ShowNewProjectDialog(bool firstRun)
    {
        if (!firstRun && !ConfirmDiscard()) return;
        var blank = new TemplateInfo("Blank project", "One book, one chapter, and a main plotline.", "Blank", "");
        var options = new[] { blank }.Concat(_templates.ListTemplates()).ToList();
        var dialog = new NewProjectWindow(options) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            if (firstRun) Vm.Status = Loc.T("Blank project ready");
            return;
        }
        var project = string.IsNullOrEmpty(dialog.SelectedTemplate!.FilePath) ? CreateBlankProject() : _templates.CreateFromTemplate(dialog.SelectedTemplate);
        Vm.ReplaceProject(project); RefreshAll();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = Loc.T("Open ArcTrellis project"), Filter = Loc.T("ArcTrellis projects (*.arctrellis)|*.arctrellis|All files (*.*)|*.*") };
        if (dialog.ShowDialog() == true) await OpenProjectAsync(dialog.FileName);
    }

    private async Task OpenProjectAsync(string path)
    {
        try
        {
            var project = await _projects.LoadAsync(path);
            string autosave = _projects.GetAutosavePath(project.Id);
            if (File.Exists(autosave) && File.GetLastWriteTimeUtc(autosave) > File.GetLastWriteTimeUtc(path) && MessageBox.Show(Loc.T("A newer local autosave exists. Recover it?"), Loc.T("Recover project"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                project = await _projects.LoadAsync(autosave);
            Vm.ReplaceProject(project, path); Vm.Status = Loc.F("Opened {0}", Path.GetFileName(path)); RefreshAll();
        }
        catch (Exception ex) { MessageBox.Show(Loc.F("Could not open the project.\n\n{0}", ex.Message), "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = Loc.T("Save reusable template"), Filter = Loc.T("ArcTrellis template (*.json)|*.json"), DefaultExt = ".json", AddExtension = true, InitialDirectory = _templates.UserTemplateDirectory, FileName = SafeName(Vm.Project.Title) + Loc.T(" Template") };
        if (dialog.ShowDialog() != true) return;
        try { _templates.SaveTemplate(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName), Loc.F("Custom template created from {0}", Vm.Project.Title), Vm.Project); Vm.Status = Loc.T("Reusable template saved"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Template save failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        string? path = Vm.FilePath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog { Title = Loc.T("Save ArcTrellis project"), Filter = Loc.T("ArcTrellis project (*.arctrellis)|*.arctrellis"), DefaultExt = ".arctrellis", AddExtension = true, FileName = SafeName(Vm.Project.Title) };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }
        try
        {
            await _projects.SaveAsync(Vm.Project, path!); Vm.FilePath = path; Vm.IsDirty = false; Vm.Status = Loc.F("Saved {0}", Path.GetFileName(path)); Title = Vm.WindowTitle; return true;
        }
        catch (Exception ex) { MessageBox.Show(Loc.F("Could not save the project.\n\n{0}", ex.Message), "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    private void ImportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = Loc.T("Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt") };
        if (dialog.ShowDialog() != true) return;
        try { Vm.ReplaceProject(_imports.ImportMarkdown(dialog.FileName)); Vm.MarkDirty(); RefreshAll(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Import failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportDocx_Click(object sender, RoutedEventArgs e) => ExportFile("Word document (*.docx)|*.docx", ".docx", _exports.ExportDocx);
    private void ExportMarkdown_Click(object sender, RoutedEventArgs e) => ExportFile("Markdown (*.md)|*.md", ".md", _exports.ExportMarkdown);
    private void ExportCsv_Click(object sender, RoutedEventArgs e) => ExportFile("CSV spreadsheet (*.csv)|*.csv", ".csv", _exports.ExportCsv);
    private void ExportFile(string filter, string extension, Action<StoryProject, string> export)
    {
        var dialog = new SaveFileDialog { Filter = Loc.T(filter), DefaultExt = extension, AddExtension = true, FileName = SafeName(Vm.Project.Title) };
        if (dialog.ShowDialog() != true) return;
        try { export(Vm.Project, dialog.FileName); Vm.Status = Loc.F("Exported {0}", Path.GetFileName(dialog.FileName)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportScrivener_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose a folder for the Scrivener project") };
        if (dialog.ShowDialog() != true) return;
        try { _exports.ExportScrivenerFolder(Vm.Project, dialog.FolderName); Vm.Status = Loc.T("Scrivener project exported"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void EditBook_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedBook is not { } book) return;
        var editor = new EditBookWindow(book) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            Vm.EditBook(book, editor.BookTitle, editor.BookSubtitle);
            RefreshAll();
        }
    }

    private void AddBook_Click(object sender, RoutedEventArgs e)
    {
        var draft = new Book { Title = Loc.F("Book {0}", Vm.Project.Books.Count + 1) };
        var editor = new EditBookWindow(draft, adding: true) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Vm.AddBook(editor.BookTitle, editor.BookSubtitle);
        RefreshAll();
    }

    private void DeleteBook_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("book and all of its scenes")) { Vm.DeleteBook(); RefreshAll(); } }
    private void AddChapter_Click(object sender, RoutedEventArgs e) { Vm.AddChapter(); if (sender is MenuItem) WorkspaceTabs.SelectedIndex = 2; RefreshAll(); }
    private void DeleteChapter_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("chapter and all of its scenes")) { Vm.DeleteChapter(); RefreshAll(); } }
    private void ChapterUp_Click(object sender, RoutedEventArgs e) { Vm.MoveChapter(-1); RefreshAll(); }
    private void ChapterDown_Click(object sender, RoutedEventArgs e) { Vm.MoveChapter(1); RefreshAll(); }
    private void AddPlotline_Click(object sender, RoutedEventArgs e) { Vm.AddPlotline(); if (sender is MenuItem) WorkspaceTabs.SelectedIndex = 1; RefreshAll(); }
    private void DeletePlotline_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("plotline (its scenes will move to another plotline)")) { Vm.DeletePlotline(); RefreshAll(); } }
    private void AddScene_Click(object sender, RoutedEventArgs e) { Vm.AddScene(); WorkspaceTabs.SelectedIndex = 3; RefreshAll(); }
    private void DeleteScene_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("scene")) { Vm.DeleteScene(); RefreshAll(); } }
    private void AddCharacter_Click(object sender, RoutedEventArgs e) { Vm.SelectedCharacter = Vm.AddEntity(Vm.Project.Characters, "Character"); RefreshAll(); }
    private void DeleteCharacter_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("character")) { Vm.DeleteEntity(Vm.Project.Characters, Vm.SelectedCharacter); Vm.SelectedCharacter = Vm.Project.Characters.FirstOrDefault(); RefreshAll(); } }
    private void AddPlace_Click(object sender, RoutedEventArgs e) { Vm.SelectedPlace = Vm.AddEntity(Vm.Project.Places, "Place"); RefreshAll(); }
    private void DeletePlace_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("place")) { Vm.DeleteEntity(Vm.Project.Places, Vm.SelectedPlace); Vm.SelectedPlace = Vm.Project.Places.FirstOrDefault(); RefreshAll(); } }
    private void AddNote_Click(object sender, RoutedEventArgs e) { Vm.SelectedNote = Vm.AddEntity(Vm.Project.Notes, "Note"); RefreshAll(); }
    private void DeleteNote_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("note")) { Vm.DeleteEntity(Vm.Project.Notes, Vm.SelectedNote); Vm.SelectedNote = Vm.Project.Notes.FirstOrDefault(); RefreshAll(); } }
    private void AddRelationship_Click(object sender, RoutedEventArgs e) { Vm.AddRelationship(); RefreshAll(); }
    private void DeleteRelationship_Click(object sender, RoutedEventArgs e) { if (Vm.SelectedRelationship is { } r) { Vm.Project.Relationships.Remove(r); Vm.SelectedRelationship = Vm.Project.Relationships.FirstOrDefault(); Vm.MarkDirty(); } }
    private void Search_Click(object sender, RoutedEventArgs e) => Vm.RunSearch();
    private void Undo_Click(object sender, RoutedEventArgs e) { Vm.Undo(); RefreshAll(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { Vm.Redo(); RefreshAll(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _timelineCardWidth = Math.Min(380, _timelineCardWidth + 30); BuildTimeline(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _timelineCardWidth = Math.Max(130, _timelineCardWidth - 30); BuildTimeline(); }

    private void SearchResult_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView { SelectedItem: SearchResult result }) return;
        switch (result.Kind)
        {
            case "Scene" or "Сцена": Vm.SelectedScene = Vm.Project.Scenes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 3; break;
            case "Character" or "Персонаж": Vm.SelectedCharacter = Vm.Project.Characters.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 4; break;
            case "Place" or "Место": Vm.SelectedPlace = Vm.Project.Places.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 5; break;
            case "Note" or "Заметка": Vm.SelectedNote = Vm.Project.Notes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 6; break;
            case "Chapter" or "Глава": WorkspaceTabs.SelectedIndex = 2; break;
        }
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e) => SetTheme(false);
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => SetTheme(true);
    private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en-US");
    private void RussianLanguage_Click(object sender, RoutedEventArgs e) => ChangeLanguage("ru-RU");
    private void ChangeLanguage(string language)
    {
        Loc.SetLanguage(language);
        Vm.RefreshLocalization();
        RefreshAll();
        ApplyLocalization();
        Dispatcher.BeginInvoke(new Action(() => { RefreshAll(); ApplyLocalization(); }), DispatcherPriority.Loaded);
    }

    private static void VerifySceneReordering(List<string> failures)
    {
        var project = new TemplateService().CreateBlank();
        var vm = new MainViewModel(project);
        vm.AddScene(); var first = vm.SelectedScene!;
        vm.AddScene(); var second = vm.SelectedScene!;
        vm.AddScene(); var third = vm.SelectedScene!;
        Guid chapter = first.ChapterId, plotline = first.PlotlineId;
        vm.MoveScene(third, chapter, plotline, 0);
        if (!vm.BookScenes.Select(scene => scene.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id })) failures.Add("Dragging up did not reorder scenes");
        vm.MoveScene(third, chapter, plotline, 2);
        if (!vm.BookScenes.Select(scene => scene.Id).SequenceEqual(new[] { first.Id, second.Id, third.Id })) failures.Add("Dragging down did not reorder scenes");
        vm.AddPlotline(); var otherPlotline = vm.SelectedPlotline!;
        vm.MoveScene(second, chapter, otherPlotline.Id, 0);
        vm.MoveScene(third, chapter, otherPlotline.Id, 0);
        var peers = vm.BookScenes.Where(scene => scene.PlotlineId == otherPlotline.Id).Select(scene => scene.Id);
        if (!peers.SequenceEqual(new[] { third.Id, second.Id })) failures.Add("Cross-cell insertion did not preserve requested order");
        var service = new ProjectService();
        var restored = service.Deserialize(service.Serialize(vm.Project));
        if (!restored.Scenes.Where(scene => scene.PlotlineId == otherPlotline.Id).OrderBy(scene => scene.Order).Select(scene => scene.Id).SequenceEqual(new[] { third.Id, second.Id })) failures.Add("Scene order did not survive reopening");
        vm.Undo();
        if (vm.Project.Scenes.Single(scene => scene.Id == third.Id).PlotlineId != plotline) failures.Add("Undo did not restore the scene's original cell");
        vm.Redo();
        if (vm.Project.Scenes.Single(scene => scene.Id == third.Id).PlotlineId != otherPlotline.Id) failures.Add("Redo did not restore the scene move");
    }

    private void RunUiSmoke(string reportPath)
    {
        try
        {
            var failures = new List<string>();
            VerifySceneReordering(failures);
            var saturationProbe = new ColorPickerWindow(Color.FromRgb(200, 100, 50));
            saturationProbe.Saturation = 0;
            if (saturationProbe.SelectedColor != "#C8C8C8") failures.Add("Zero saturation is not gray");
            saturationProbe.Saturation = 100;
            if (saturationProbe.SelectedColor != "#C84300") failures.Add("Saturation did not retain hue and brightness");
            saturationProbe.Close();
            foreach (string language in new[] { "en-US", "ru-RU" })
            {
                Loc.SetLanguage(language);
                var confirmation = new DeleteConfirmationWindow("scene") { Owner = this };
                confirmation.Show(); confirmation.UpdateLayout();
                var buttons = FindVisualChildren<Button>(confirmation).Select(b => b.Content?.ToString()).ToList();
                if (!buttons.Contains(Loc.T("Yes")) || !buttons.Contains(Loc.T("No"))) failures.Add("Delete confirmation buttons did not use app language");
                if (language == "ru-RU" && Loc.F("Delete this {0}?", Loc.T("scene")) != "Удалить сцену?") failures.Add("Russian delete prompt has incorrect punctuation");
                SaveVisualPng(confirmation, Path.Combine(Path.GetDirectoryName(reportPath)!, $"ArcTrellis-delete-{language}.png"));
                confirmation.Close();
            }

            var duplicateVm = new MainViewModel(new TemplateService().CreateBlank());
            duplicateVm.AddScene();
            var sourceScene = duplicateVm.SelectedScene!;
            sourceScene.Summary = "Summary"; sourceScene.Content = "Draft"; sourceScene.Status = "Revised";
            sourceScene.Tags.Add("tag"); sourceScene.CharacterIds.Add(Guid.NewGuid());
            var duplicate = duplicateVm.DuplicateScene(sourceScene)!;
            if (duplicate.Id == sourceScene.Id || duplicate.Title != Loc.F("{0} (duplicate)", sourceScene.Title) || duplicate.Summary != sourceScene.Summary || duplicate.Content != sourceScene.Content || duplicate.Status != sourceScene.Status || duplicate.ChapterId != sourceScene.ChapterId || duplicate.PlotlineId != sourceScene.PlotlineId || duplicate.Order != sourceScene.Order + 1 || !duplicate.CharacterIds.SequenceEqual(sourceScene.CharacterIds)) failures.Add("Scene duplication lost data or placement");
            duplicate.Tags.Add("independent");
            if (sourceScene.Tags.Contains("independent")) failures.Add("Duplicate scene shares mutable data");
            duplicateVm.Undo();
            if (duplicateVm.Project.Scenes.Count != 1) failures.Add("Undo failed for duplicate scene");
            foreach (Window titleEditor in new Window[] { new EditBookWindow(new Book()), new EditPlotlineWindow(new Plotline()) })
            {
                var input = (TextBox)titleEditor.FindName("TitleInput");
                var save = (Button)titleEditor.FindName("SaveButton");
                foreach (string blank in new[] { "", "   " }) { input.Text = blank; if (save.IsEnabled) failures.Add("Save enabled for blank title"); }
                input.Text = "Valid title";
                if (!save.IsEnabled) failures.Add("Save did not enable for valid title");
                titleEditor.Close();
            }

            var editProject = new TemplateService().CreateBlank();
            var editVm = new MainViewModel(editProject);
            var editPlot = editVm.SelectedPlotline!;
            editPlot.Description = "Original description";
            var plotEditor = new EditPlotlineWindow(editPlot) { Owner = this };
            if (plotEditor.PlotlineTitle != editPlot.Name || plotEditor.PlotlineDescription != editPlot.Description || plotEditor.PlotlineColor != editPlot.Color)
                failures.Add("Plotline editor did not prefill all fields");
            plotEditor.Show();
            plotEditor.UpdateLayout();
            ((TextBox)plotEditor.FindName("TitleInput")).Text = "Canceled title";
            ((TextBox)plotEditor.FindName("DescriptionInput")).Text = "Canceled description";
            SaveVisualPng(plotEditor, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-edit-plotline.png"));
            plotEditor.Close();
            if (editPlot.Name == "Canceled title" || editPlot.Description != "Original description") failures.Add("Canceled plotline edit changed project data");
            editVm.EditPlotline(editPlot, "Edited plotline", "Edited description", "#2E9D78");
            if (editPlot.Name != "Edited plotline" || editPlot.Description != "Edited description" || editPlot.Color != "#2E9D78") failures.Add("Plotline edit did not save all fields");
            editVm.Undo();
            if (editVm.Project.Plotlines.Single(p => p.Id == editPlot.Id).Description != "Original description") failures.Add("Undo did not restore plotline details");


            // The selected scene must keep its stored code while option labels change.
            Vm.AddScene();
            var languageScene = Vm.SelectedScene!;
            WorkspaceTabs.SelectedIndex = 3;
            UpdateLayout();
            foreach (string code in new[] { "Planned", "Drafted", "Revised", "Final", "Cut" })
            {
                languageScene.Status = code;
                foreach (string language in new[] { "ru-RU", "en-US", "ru-RU" })
                {
                    ChangeLanguage(language);
                    UpdateLayout();
                    var statusBox = FindVisualChildren<ComboBox>(this).FirstOrDefault(box => box.SelectedValuePath == "Code");
                    if (languageScene.Status != code || statusBox?.SelectedValue as string != code)
                        failures.Add($"Language switch cleared scene status {code} in {language}");
                    if (statusBox?.SelectedItem is not SceneStatusOption option || option.Label != Loc.T(code))
                        failures.Add($"Status option did not translate: {code} in {language}");
                    WorkspaceTabs.SelectedIndex = 1;
                    BuildTimeline();
                    UpdateLayout();
                    var card = FindVisualChildren<Border>(TimelineGrid).FirstOrDefault(b => ReferenceEquals(b.Tag, languageScene));
                    if (card is null || !FindVisualChildren<TextBlock>(card).Any(t => t.Text == Loc.T(code)))
                        failures.Add($"Timeline lost localized scene status {code}");
                    WorkspaceTabs.SelectedIndex = 3;
                    UpdateLayout();
                }
            }
            languageScene.Status = "Planned";

            ChangeLanguage("en-US");
            WorkspaceTabs.SelectedIndex = 3;
            ApplyLocalization();
            UpdateLayout();
            if ((WorkspaceTabs.Items[0] as TabItem)?.Header?.ToString() != "Dashboard") failures.Add("English tab localization failed");
            if (FileMenuItem.Header?.ToString() != "File" || LightThemeItem.Header?.ToString() != "Light theme") failures.Add("English menu localization failed");
            var englishText = CollectVisibleText(this).ToList();
            if (!englishText.Contains("Scene card") || !englishText.Contains("Status")) failures.Add("English deferred content localization failed");

            WorkspaceTabs.SelectedIndex = 0;
            RefreshStats();
            ApplyLocalization();
            UpdateLayout();
            if (!StatsText.Text.Contains("book(s)", StringComparison.Ordinal) || StatsText.Text.Contains("Книг:", StringComparison.Ordinal)) failures.Add("English dashboard statistics did not refresh immediately");

            ChangeLanguage("ru-RU");
            WorkspaceTabs.SelectedIndex = 0;
            UpdateLayout();
            if (!StatsText.Text.Contains("Книг:", StringComparison.Ordinal) || StatsText.Text.Contains("book(s)", StringComparison.Ordinal)) failures.Add("Russian dashboard statistics did not refresh immediately");
            WorkspaceTabs.SelectedIndex = 1;
            ApplyLocalization();
            TimelineBookCombo.ApplyTemplate();
            UpdateLayout();
            if ((WorkspaceTabs.Items[0] as TabItem)?.Header?.ToString() != "Обзор") failures.Add("Russian tab localization failed");
            var visibleText = CollectVisibleText(this).ToList();
            if (!visibleText.Contains("Книга:")) failures.Add("Deferred tab content was not translated");
            if (visibleText.Any(x => x.Contains("ArcTrellis.Core.Models", StringComparison.Ordinal))) failures.Add("A model class name is visible instead of its display member");
            if (MainMenu.ActualHeight < 10 || (MainMenu.Items[0] as MenuItem)?.ActualWidth < 20) failures.Add("Top menu is not visible");
            if (Loc.T(null) != string.Empty) failures.Add("Null localization values are not handled safely");

            AddPlotline_Click(new MenuItem(), new RoutedEventArgs());
            if (WorkspaceTabs.SelectedIndex != 1) failures.Add("Add Plotline menu action did not open Timeline");
            AddChapter_Click(new MenuItem(), new RoutedEventArgs());
            if (WorkspaceTabs.SelectedIndex != 2) failures.Add("Add Chapter menu action did not open Outline");
            AddScene_Click(new MenuItem(), new RoutedEventArgs());
            if (WorkspaceTabs.SelectedIndex != 3) failures.Add("Add Scene menu action did not open Scenes");

            Book firstBook = Vm.SelectedBook!;
            Scene firstScene = Vm.SelectedScene!;
            WorkspaceTabs.SelectedIndex = 1;
            OpenTimelineScene(firstScene);
            if (WorkspaceTabs.SelectedIndex != 3 || !ReferenceEquals(Vm.SelectedScene, firstScene))
                failures.Add("Timeline scene click did not open the matching scene editor");
            int firstBookPlotlineCount = Vm.BookPlotlines.Count();
            Plotline firstBookMainPlotline = Vm.BookPlotlines.First();
            string firstBookMainPlotlineName = firstBookMainPlotline.Name;
            Vm.AddBook();
            Book secondBook = Vm.SelectedBook!;
            if (Vm.BookPlotlines.Count() != 1 || Vm.BookPlotlines.Any(plotline => plotline.BookId != secondBook.Id)) failures.Add("A new book did not receive its own independent plotline");
            Plotline secondBookMainPlotline = Vm.BookPlotlines.First();
            secondBookMainPlotline.Name = "Second Book Main Plot";
            if (firstBookMainPlotline.Name != firstBookMainPlotlineName) failures.Add("Renaming a plotline changed another book's plotline");
            Vm.AddPlotline();
            if (Vm.Project.Plotlines.Count(plotline => plotline.BookId == firstBook.Id) != firstBookPlotlineCount || Vm.BookPlotlines.Count() != 2) failures.Add("Adding a plotline affected more than the selected book");
            Vm.AddChapter();
            Chapter secondChapter = Vm.SelectedChapter!;
            Vm.AddScene();
            Scene secondScene = Vm.SelectedScene!;
            if (secondScene.BookId != secondBook.Id || secondScene.ChapterId != secondChapter.Id) failures.Add("A scene was assigned across book/chapter boundaries");
            Vm.SelectedBook = firstBook;
            if (!Vm.BookScenes.Contains(firstScene)) failures.Add("First-book scene disappeared after switching books");
            if (Vm.SelectedPlotline?.BookId != firstBook.Id || Vm.BookPlotlines.Any(plotline => plotline.BookId != firstBook.Id)) failures.Add("First-book plotline selection was not isolated");
            Vm.SelectedBook = secondBook;
            BuildTimeline();
            UpdateLayout();
            if (!Vm.BookScenes.Contains(secondScene) || !Vm.Project.Scenes.Contains(secondScene) || !FindVisualChildren<Border>(TimelineGrid).Any(border => ReferenceEquals(border.Tag, secondScene))) failures.Add("Second-book scene disappeared from the timeline after switching books");
            Plotline selectedFromTimeline = Vm.BookPlotlines.Last();
            SelectTimelinePlotline(selectedFromTimeline);
            UpdateLayout();
            if (!ReferenceEquals(Vm.SelectedPlotline, selectedFromTimeline)) failures.Add("Timeline plotline label did not select its plotline");
            if (!FindVisualChildren<Border>(TimelineGrid).Any(border => ReferenceEquals(border.Tag, selectedFromTimeline))) failures.Add("Timeline plotline label is not clickable");
            string livePlotlineName = "Live Rename Test";
            selectedFromTimeline.Name = livePlotlineName;
            selectedFromTimeline.Description = "A story conflict that changes the world.";
            UpdateLayout();
            if (!FindVisualChildren<TextBlock>(TimelineGrid).Any(text => text.Text == livePlotlineName)) failures.Add("Timeline plotline name did not update live");
            if (!FindVisualChildren<TextBlock>(TimelineGrid).Any(text => text.Text == selectedFromTimeline.Description && text.Visibility == Visibility.Visible)) failures.Add("Timeline plotline description did not update live");

            firstBook.CurrentWordCount = 100;
            firstBook.WordCountGoal = 1000;
            secondBook.CurrentWordCount = 450;
            secondBook.WordCountGoal = 900;
            Vm.SelectedBook = firstBook;
            RefreshStats();
            if (ProgressBar.Value != 10) failures.Add("First book progress is incorrect");
            Vm.SelectedBook = secondBook;
            RefreshStats();
            if (ProgressBar.Value != 50 || firstBook.CurrentWordCount != 100) failures.Add("Book progress is not independent");
            var pickerProbe = new ColorPickerWindow(Color.FromRgb(91, 124, 250));
            if (pickerProbe.SelectedColor != "#5B7CFA") failures.Add("Color picker did not load existing color");
            var addProbe = new EditBookWindow(new Book { Title = "Book 3" }, adding: true);
            if (addProbe.BookTitle != "Book 3" || addProbe.BookSubtitle != "") failures.Add("Add book dialog defaults are incorrect");
            var subtitleProbe = new EditBookWindow(new Book { Title = "Book One", Subtitle = "Establish the central promise" });
            if (subtitleProbe.BookSubtitle != "Establish the central promise") failures.Add("Edit book dialog did not load subtitle");
            subtitleProbe.Owner = this;
            subtitleProbe.Show(); subtitleProbe.UpdateLayout();
            SaveVisualPng(subtitleProbe, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-edit-book.png"));
            subtitleProbe.Close();
            pickerProbe.Owner = this;
            pickerProbe.Show(); pickerProbe.UpdateLayout();
            var firstSwatch = FindVisualChildren<Button>(pickerProbe).First(button => button.ToolTip?.ToString() == "#D9577A");
            firstSwatch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (pickerProbe.SelectedColor != "#D9577A") failures.Add("Color swatch selection failed");
            SaveVisualPng(pickerProbe, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-color-picker.png"));
            pickerProbe.Close();
            var originalBookTitle = secondBook.Title;
            var cancelEditor = new EditBookWindow(secondBook);
            cancelEditor.TitleInput.Text = "Discard this title";
            if (secondBook.Title != originalBookTitle) failures.Add("Book dialog edits changed the book before Save");
            Vm.EditBook(secondBook, "Edited Book", "Edited subtitle");
            if (secondBook.Title != "Edited Book" || secondBook.Subtitle != "Edited subtitle") failures.Add("Book editing did not save title and subtitle");

            SetTheme(false, false);
            var lightMenu = (SolidColorBrush)Application.Current.Resources[SystemColors.MenuBrushKey];
            var lightMenuText = (SolidColorBrush)Application.Current.Resources[SystemColors.MenuTextBrushKey];
            if (lightMenu.Color == lightMenuText.Color) failures.Add("Light menu text has no contrast");
            WorkspaceTabs.SelectedIndex = 0;
            ApplyLocalization();
            UpdateLayout();
            var renderedInputs = FindVisualChildren<TextBox>(this).ToList();
            var probeInput = renderedInputs.First(x => !x.AcceptsReturn);
            if (probeInput.Padding != new Thickness(3, 4, 3, 4) || probeInput.MinHeight < 30) failures.Add("Single-line text input spacing is incorrect");
            var probeEditor = renderedInputs.First(x => x.AcceptsReturn);
            if (probeEditor.Padding != new Thickness(3, 4, 3, 4) || probeEditor.VerticalContentAlignment != VerticalAlignment.Top) failures.Add("Multiline text editor spacing is incorrect");
            var numericInputs = renderedInputs.Where(IsNumericInput).ToList();
            if (numericInputs.Count != 2 || numericInputs.Any(Validation.GetHasError)) failures.Add("Dashboard numeric inputs are not configured correctly");
            if (IsValidNumericCandidate("12a")) failures.Add("Numeric input accepts letters");
            var emptyNumericProbe = new TextBox { Tag = NumericInputTag, Text = string.Empty };
            NormalizeNumericInput(emptyNumericProbe);
            if (emptyNumericProbe.Text != "0" || new NonNegativeIntegerConverter().ConvertBack(string.Empty, typeof(int), null!, System.Globalization.CultureInfo.InvariantCulture) is not 0) failures.Add("Empty numeric input did not default to zero");

            SetTheme(true, false);
            FileMenuItem.IsSubmenuOpen = true;
            UpdateLayout();
            if (FileMenuItem.Template.FindName("PART_Popup", FileMenuItem) is System.Windows.Controls.Primitives.Popup { Child: Border popupContent })
            {
                popupContent.UpdateLayout();
                SaveVisualPng(popupContent, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-file-menu.png"));
                if (popupContent.Background is SolidColorBrush popupBackground && popupBackground.Color.R > 64) failures.Add("Dark submenu background is too light");
                if (FindVisualChildren<ScrollViewer>(popupContent).Any(x => x.ComputedVerticalScrollBarVisibility == Visibility.Visible)) failures.Add("Dark submenu shows an unnecessary scrollbar");
                Point expectedPopupOrigin = FileMenuItem.PointToScreen(new Point(0, FileMenuItem.ActualHeight));
                Point actualPopupOrigin = popupContent.PointToScreen(new Point(0, 0));
                if (Math.Abs(expectedPopupOrigin.X - actualPopupOrigin.X) > 12 || Math.Abs(expectedPopupOrigin.Y - actualPopupOrigin.Y) > 12) failures.Add("Top submenu is not aligned beneath its parent");
            }
            else failures.Add("Dark submenu popup was not created");
            FileMenuItem.IsSubmenuOpen = false;
            LanguageMenuItem.IsSubmenuOpen = true;
            UpdateLayout();
            if (LanguageMenuItem.Template.FindName("PART_Popup", LanguageMenuItem) is System.Windows.Controls.Primitives.Popup { Child: Border languagePopup })
            {
                languagePopup.UpdateLayout();
                Point expectedLanguageOrigin = LanguageMenuItem.PointToScreen(new Point(0, LanguageMenuItem.ActualHeight));
                Point actualLanguageOrigin = languagePopup.PointToScreen(new Point(0, 0));
                if (Math.Abs(expectedLanguageOrigin.X - actualLanguageOrigin.X) > 12 || Math.Abs(expectedLanguageOrigin.Y - actualLanguageOrigin.Y) > 12) failures.Add("Language submenu is not left-aligned beneath its parent");
            }
            else failures.Add("Language submenu popup was not created");
            LanguageMenuItem.IsSubmenuOpen = false;
            var previewOptions = new[] { new TemplateInfo("Blank project", "One book, one chapter, and a main plotline.", "Blank", "") };
            var preview = new NewProjectWindow(previewOptions) { Owner = this };
            preview.Show();
            preview.UpdateLayout();
            if (preview.Background is not SolidColorBrush previewBackground || previewBackground.Color.R > 64) failures.Add("Dark theme did not reach the template window");
            SaveVisualPng(preview, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-template-window.png"));
            preview.Close();
            if (((SolidColorBrush)Application.Current.Resources["InputBrush"]).Color == Colors.White) failures.Add("Dark input palette was not applied");
            WorkspaceTabs.SelectedIndex = 1;
            BuildTimeline();
            UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-timeline.png"));
            var menuCard = FindVisualChildren<Border>(TimelineGrid).First(b => b.Tag is Scene);
            var sceneMenu = menuCard.ContextMenu!;
            if (sceneMenu.Items.Count != 3) failures.Add("Scene context menu actions missing");
            sceneMenu.PlacementTarget = menuCard;
            sceneMenu.IsOpen = true;
            UpdateLayout(); sceneMenu.UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-menu-host.png"));
            SaveVisualPng(sceneMenu, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-context-menu.png"));
            sceneMenu.IsOpen = false;

            var widthGrip = TimelineGrid.Children.OfType<Thumb>().First(t => Equals(t.Tag, "TimelineColumnResize") && Grid.GetColumn(t) == 1);
            var heightGrip = TimelineGrid.Children.OfType<Thumb>().First(t => Equals(t.Tag, "TimelineRowResize") && Grid.GetRow(t) == 1);
            double oldWidth = TimelineGrid.ColumnDefinitions[1].ActualWidth;
            double oldHeight = TimelineGrid.RowDefinitions[1].ActualHeight;
            foreach (var grip in new[] { widthGrip, heightGrip })
            {
                grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                grip.RaiseEvent(new DragDeltaEventArgs(60, 60) { RoutedEvent = Thumb.DragDeltaEvent });
                grip.RaiseEvent(new DragCompletedEventArgs(60, 60, false) { RoutedEvent = Thumb.DragCompletedEvent });
            }
            BuildTimeline();
            UpdateLayout();
            if (Math.Abs(TimelineGrid.ColumnDefinitions[1].ActualWidth - oldWidth - 60) > 1) failures.Add("Chapter resize did not survive timeline rebuild");
            if (TimelineGrid.RowDefinitions[1].ActualHeight < oldHeight + 59) failures.Add("Plotline resize did not survive timeline rebuild");
            var savedLayout = System.Text.Json.JsonSerializer.Deserialize<StoryProject>(System.Text.Json.JsonSerializer.Serialize(Vm.Project))!;
            if (savedLayout.Books.First(b => b.Id == Vm.SelectedBook!.Id).Chapters.First(c => c.Id == Vm.SelectedBook!.Chapters.OrderBy(c => c.Order).First().Id).TimelineWidth < oldWidth + 59) failures.Add("Column width did not persist in project JSON");
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-resized-timeline.png"));
            // Identical cards at different stack offsets must produce equally complete previews.
            var originalPreviewScene = Vm.BookScenes.First();
            Vm.SelectedChapter = Vm.SelectedBook!.Chapters.First(chapter => chapter.Id == originalPreviewScene.ChapterId);
            Vm.SelectedPlotline = Vm.BookPlotlines.First(plotline => plotline.Id == originalPreviewScene.PlotlineId);
            originalPreviewScene.Title = "Drag preview text";
            originalPreviewScene.Summary = "Visible at every stack position";
            for (int i = 0; i < 2; i++)
            {
                Vm.AddScene();
                Vm.SelectedScene!.Title = originalPreviewScene.Title;
                Vm.SelectedScene.Summary = originalPreviewScene.Summary;
                Vm.SelectedScene.Status = originalPreviewScene.Status;
            }
            BuildTimeline();
            UpdateLayout();
            var previewCards = FindVisualChildren<Border>(TimelineGrid)
                .Where(border => border.Tag is Scene scene && scene.ChapterId == originalPreviewScene.ChapterId && scene.PlotlineId == originalPreviewScene.PlotlineId).ToList();
            int referenceTextPixels = 0;
            for (int i = 0; i < previewCards.Count; i++)
            {
                var bitmap = SceneDragAdorner.CaptureCard(previewCards[i]);
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                int textPixels = 0;
                for (int pixel = 0; pixel < pixels.Length; pixel += 4)
                    if (pixels[pixel] > 170 && pixels[pixel + 1] > 170 && pixels[pixel + 2] > 170) textPixels++;
                if (i == 0) referenceTextPixels = textPixels;
                if (textPixels < 30 || textPixels < referenceTextPixels * 0.8) failures.Add($"Drag preview lost text at stack position {i + 1}");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var imageFile = File.Create(Path.Combine(Path.GetDirectoryName(reportPath)!, $"ArcTrellis-drag-card-{i + 1}.png"));
                encoder.Save(imageFile);
            }
            if (previewCards.Count < 3) failures.Add("Multi-card drag preview regression fixture is incomplete");
            var dragSurface = (UIElement)Content;
            var dragLayer = AdornerLayer.GetAdornerLayer(dragSurface);
            var previewCard = previewCards.Skip(1).FirstOrDefault();
            if (dragLayer is null || previewCard is null) failures.Add("Timeline drag preview layer is unavailable");
            else
            {
                var dragPreview = new SceneDragAdorner(dragSurface, previewCard, new Point(10, 10));
                dragLayer.Add(dragPreview);
                Point previewOrigin = previewCard.TranslatePoint(new Point(70, 40), dragSurface);
                dragPreview.MoveTo(previewOrigin);
                dragPreview.ShowInsertion(new Rect(previewCard.TranslatePoint(new Point(0, previewCard.ActualHeight + 5), dragSurface), new Size(previewCard.ActualWidth, 0)));
                previewCard.Opacity = 0.3;
                UpdateLayout();
                SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-drag-preview.png"));
                previewCard.Opacity = 1;
                dragLayer.Remove(dragPreview);
            }

            WorkspaceTabs.SelectedIndex = 0;
            ChangeLanguage("ru-RU");
            UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-main-window.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(reportPath, "FAIL: " + ex); } catch { }
        }
    }

    private static IEnumerable<string> CollectVisibleText(DependencyObject root)
    {
        if (root is TextBlock { Text: { Length: > 0 } } text) yield return text.Text;
        int count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (int i = 0; i < count; i++)
            foreach (string value in CollectVisibleText(VisualTreeHelper.GetChild(root, i))) yield return value;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void SaveVisualPng(FrameworkElement visual, string path)
    {
        int width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private void ApplyLocalization()
    {
        EnglishLanguageItem.IsChecked = !Loc.IsRussian;
        RussianLanguageItem.IsChecked = Loc.IsRussian;
        Loc.Apply(this);
    }
    private void SetTheme(bool dark, bool persist = true)
    {
        _isDark = dark;
        var palette = dark
            ? new Dictionary<string, string> { ["AccentBrush"] = "#7895FF", ["AccentHoverBrush"] = "#8AA3FF", ["AccentPressedBrush"] = "#5A76E8", ["PageBrush"] = "#111722", ["PanelBrush"] = "#192130", ["ElevatedBrush"] = "#222C3D", ["InputBrush"] = "#121925", ["TextBrush"] = "#F2F5FA", ["MutedBrush"] = "#AAB5C6", ["BorderBrush"] = "#354154", ["HoverBrush"] = "#273247", ["SelectedBrush"] = "#304263", ["HeaderBrush"] = "#0E141E", ["DangerBrush"] = "#FF7188" }
            : new Dictionary<string, string> { ["AccentBrush"] = "#5B7CFA", ["AccentHoverBrush"] = "#6D89FA", ["AccentPressedBrush"] = "#4264DF", ["PageBrush"] = "#F4F6FA", ["PanelBrush"] = "#FFFFFF", ["ElevatedBrush"] = "#FFFFFF", ["InputBrush"] = "#FFFFFF", ["TextBrush"] = "#202638", ["MutedBrush"] = "#667085", ["BorderBrush"] = "#D8DEEA", ["HoverBrush"] = "#EEF2FF", ["SelectedBrush"] = "#E2E9FF", ["HeaderBrush"] = "#FFFFFF", ["DangerBrush"] = "#D84B63" };
        foreach (var (key, color) in palette) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        SolidColorBrush ThemeBrush(string key) => new(((SolidColorBrush)Application.Current.Resources[key]).Color);
        Application.Current.Resources[SystemColors.MenuBrushKey] = ThemeBrush("ElevatedBrush");
        Application.Current.Resources[SystemColors.MenuTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.HighlightBrushKey] = ThemeBrush("SelectedBrush");
        Application.Current.Resources[SystemColors.HighlightTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.ControlBrushKey] = ThemeBrush("ElevatedBrush");
        Application.Current.Resources[SystemColors.ControlTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.WindowBrushKey] = ThemeBrush("PageBrush");
        Application.Current.Resources[SystemColors.WindowTextBrushKey] = ThemeBrush("TextBrush");
        LightThemeItem.IsChecked = !dark;
        DarkThemeItem.IsChecked = dark;
        ApplyWindowChromeTheme();
        if (persist)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ThemeSettingsPath)!); File.WriteAllText(ThemeSettingsPath, dark ? "dark" : "light"); } catch { }
        }
        BuildTimeline();
    }

    private static bool LoadDarkTheme()
    {
        try { return File.Exists(ThemeSettingsPath) && File.ReadAllText(ThemeSettingsPath).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void ApplyWindowChromeTheme()
    {
        ThemeChrome.Apply(this, _isDark);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(TimelineGrid, Vm.Project.Title + " — " + Loc.T("Timeline"));
    }
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Docs", Loc.IsRussian ? "USER_GUIDE.ru.md" : "USER_GUIDE.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("ArcTrellis 1.1.19\n\n" + Loc.T("A private, local-first visual story planner for Windows.\nNo cloud account, tracking, or network connection required."), Loc.T("About ArcTrellis"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Save_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) { SaveAs_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { Open_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { New_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { Undo_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { Redo_Click(sender, e); e.Handled = true; }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterSave || !Vm.IsDirty) return;
        var result = MessageBox.Show(Loc.T("Save changes before closing?"), "ArcTrellis", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.No) return;
        e.Cancel = true;
        if (await SaveAsync(false)) { _closingAfterSave = true; Close(); }
    }

    private bool ConfirmDiscard()
    {
        if (!Vm.IsDirty) return true;
        var result = MessageBox.Show(Loc.T("This project has unsaved changes. Continue and discard them?"), "ArcTrellis", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
    private bool ConfirmDelete(string item) => new DeleteConfirmationWindow(item) { Owner = this }.ShowDialog() == true;
    private StoryProject CreateBlankProject()
    {
        var project = _templates.CreateBlank();
        project.Title = Loc.T(project.Title);
        project.Books[0].Title = Loc.T(project.Books[0].Title);
        project.Books[0].Chapters[0].Title = Loc.T(project.Books[0].Chapters[0].Title);
        project.Books[0].Chapters[0].Section = Loc.T(project.Books[0].Chapters[0].Section);
        project.Plotlines[0].Name = Loc.T(project.Plotlines[0].Name);
        return project;
    }
    private static string SafeName(string name) => string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) is { Length: > 0 } value ? value : "Story";
    private Brush FindBrush(string key) => (Brush)FindResource(key);
    private static SolidColorBrush BrushFrom(string color) => new(BrushColor(color));
    private static Color BrushColor(string color) { try { return (Color)ColorConverter.ConvertFromString(color); } catch { return Colors.SlateBlue; } }
}
