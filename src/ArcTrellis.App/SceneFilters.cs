using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private ContextMenu? _sceneFilter;
    private readonly List<CheckBox> _sceneFilterStatusChecks = [];
    private MultiChoiceInput? _sceneChapterChoices, _scenePlotlineChoices;
    private TagInput? _sceneFilterTagsInput;
    private ObservableCollection<string> _sceneFilterDraftTags = [];

    private void SceneList_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is Scene scene && (DataContext is not MainViewModel vm || vm.MatchesSceneFilter(scene));

    private void CloseSceneFilter() { if (_sceneFilter is not null) _sceneFilter.IsOpen = false; }

    private bool IsSceneFilterInteraction(DependencyObject? source)
    {
        var visited = new HashSet<DependencyObject>();
        while (source is not null && visited.Add(source))
        {
            if (ReferenceEquals(source, _sceneFilter) || ReferenceEquals(source, SceneFilterButton)) return true;
            if (source is Popup popup) source = popup.PlacementTarget;
            else source = LogicalTreeHelper.GetParent(source) ?? (source is Visual ? VisualTreeHelper.GetParent(source) : null);
        }
        return false;
    }

    private void SceneFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_sceneFilter?.IsOpen == true) { CloseSceneFilter(); return; }
        if (Vm.SelectedBook is not { } book) return;
        CloseChapterFilter();
        _sceneFilterStatusChecks.Clear();
        _sceneFilterDraftTags = new(Vm.SceneTagFilter);
        var fields = new StackPanel { Margin = new Thickness(12) };
        fields.Children.Add(new TextBlock { Text = Loc.T("Filter scenes"), FontWeight = FontWeights.SemiBold, FontSize = 16 });
        void Label(string value) => fields.Children.Add(new TextBlock { Text = Loc.T(value), Margin = new Thickness(0, 12, 0, 5) });
        Label("Status");
        var statuses = new WrapPanel();
        foreach (var status in Vm.SceneStatuses)
        {
            var check = new CheckBox { Content = status.Label, Tag = status.Code, IsChecked = Vm.SceneStatusFilter.Contains(status.Code), Width = 170, Margin = new Thickness(2, 4, 2, 4), Cursor = Cursors.Hand };
            _sceneFilterStatusChecks.Add(check); statuses.Children.Add(check);
        }
        fields.Children.Add(statuses);
        Label("Chapters");
        _sceneChapterChoices = new MultiChoiceInput("Chapters", book.Chapters.OrderBy(chapter => chapter.Order).Select(chapter => (chapter.Id, chapter.Title)), Vm.SceneChapterFilter);
        fields.Children.Add(_sceneChapterChoices);
        Label("Plotlines");
        _scenePlotlineChoices = new MultiChoiceInput("Plotlines", Vm.BookPlotlines.Select(plot => (plot.Id, plot.Name)), Vm.ScenePlotlineFilter);
        fields.Children.Add(_scenePlotlineChoices);
        Label("Tags");
        _sceneFilterTagsInput = new TagInput { Project = Vm.Project, Tags = _sceneFilterDraftTags, ExistingOnly = true, InlineSuggestions = true };
        fields.Children.Add(_sceneFilterTagsInput);

        var actions = new WrapPanel { Margin = new Thickness(9, 0, 9, 9) };
        var apply = new Button { Content = Loc.T("Apply") };
        var cancel = new Button { Content = Loc.T("Cancel") };
        var clear = new Button { Content = Loc.T("Clear filters") };
        apply.Click += (_, _) => ApplySceneFilter();
        cancel.Click += (_, _) => CloseSceneFilter();
        clear.Click += (_, _) => ClearSceneFilters();
        actions.Children.Add(apply); actions.Children.Add(cancel); actions.Children.Add(clear);
        var root = new DockPanel { Width = 404 };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        root.Children.Add(new ScrollViewer { Content = fields, MaxHeight = Math.Max(220, Math.Min(580, SystemParameters.WorkArea.Height - 150)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _sceneFilter = new ContextMenu { PlacementTarget = SceneFilterButton, Placement = PlacementMode.Custom, StaysOpen = true, Padding = new Thickness(0) };
        _sceneFilter.SetResourceReference(ForegroundProperty, "TextBrush");
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        _sceneFilter.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        _sceneFilter.CustomPopupPlacementCallback = (_, size, _) => [new CustomPopupPlacement(new Point(0, size.Height), PopupPrimaryAxis.Vertical)];
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        _sceneFilter.Items.Add(new MenuItem { Header = root, StaysOpenOnClick = true, Focusable = false, Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter } });
        _sceneFilter.PreviewMouseDown += (_, click) =>
        {
            Point point = click.GetPosition(_sceneFilter);
            if ((point.X < 0 || point.Y < 0 || point.X > _sceneFilter.ActualWidth || point.Y > _sceneFilter.ActualHeight) && !SceneFilterButton.IsMouseOver)
                CloseSceneFilter();
        };
        _sceneFilter.AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, new MouseButtonEventHandler((_, _) =>
        { if (!SceneFilterButton.IsMouseOver) CloseSceneFilter(); }));
        _sceneFilter.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { CloseSceneFilter(); key.Handled = true; } };
        SceneFilterButton.ContextMenu = _sceneFilter;
        TimelineMenuPosition.Open(SceneFilterButton, SceneFilterButton.PointToScreen(new Point(0, SceneFilterButton.ActualHeight)));
    }

    private void ApplySceneFilter()
    {
        if (_sceneChapterChoices is null || _scenePlotlineChoices is null) return;
        Vm.SetSceneFilters(_sceneFilterStatusChecks.Where(check => check.IsChecked == true).Select(check => (string)check.Tag),
            _sceneChapterChoices.SelectedIds, _scenePlotlineChoices.SelectedIds, _sceneFilterDraftTags);
        CloseSceneFilter(); RefreshSceneList();
    }

    private void ClearSceneFilters()
    {
        Vm.SetSceneFilters([], [], [], []);
        CloseSceneFilter(); RefreshSceneList();
    }
}
