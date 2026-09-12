using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private ContextMenu? _chapterFilter;
    private TagInput? _filterTagsInput;
    private readonly List<CheckBox> _filterStatusChecks = [];
    private ObservableCollection<string> _filterDraftTags = [];

    private void CloseChapterFilter()
    {
        if (_chapterFilter is not null) _chapterFilter.IsOpen = false;
    }
    private bool IsChapterFilterInteraction(DependencyObject? source)
    {
        var visited = new HashSet<DependencyObject>();
        while (source is not null && visited.Add(source))
        {
            if (ReferenceEquals(source, _chapterFilter) || ReferenceEquals(source, ChapterFilterButton)) return true;
            if (source is Popup popup) source = popup.PlacementTarget;
            else source = LogicalTreeHelper.GetParent(source) ?? (source is Visual ? VisualTreeHelper.GetParent(source) : null);
        }
        return false;
    }
    private bool _refreshingChapterFilter;
    private void ChapterList_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is Chapter chapter && (DataContext is not MainViewModel vm || vm.MatchesChapterFilter(chapter));

    private void RefreshChapterFilter()
    {
        if (_refreshingChapterFilter || DataContext is not MainViewModel) return;
        var view = ((CollectionViewSource)Resources["ChapterListView"]).View;
        if (view is null || !ReferenceEquals(view.SourceCollection, Vm.SelectedBook?.Chapters)) return;
        _refreshingChapterFilter = true;
        try
        {
            view.Refresh();
            if (Vm.SelectedChapter is null || !view.Contains(Vm.SelectedChapter))
                Vm.SelectedChapter = view.Cast<Chapter>().FirstOrDefault();
        }
        finally { _refreshingChapterFilter = false; }
    }
    private void UpdateChapterFilterButton()
    {
        ChapterFilterButton.SetResourceReference(BorderBrushProperty, Vm.HasChapterFilters ? "AccentBrush" : "BorderBrush");
        RefreshChapterFilter();
    }

    private void ChapterFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterFilter?.IsOpen == true) { CloseChapterFilter(); return; }
        _filterStatusChecks.Clear();
        _filterDraftTags = new(Vm.ChapterTagFilter);
        var panel = new StackPanel { Width = 340, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Filter chapters"), FontWeight = FontWeights.SemiBold, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = Loc.T("Status"), Margin = new Thickness(0, 12, 0, 5) });
        foreach (var status in Vm.SceneStatuses)
        {
            var check = new CheckBox { Content = status.Label, Tag = status.Code, IsChecked = Vm.ChapterStatusFilter.Contains(status.Code), Margin = new Thickness(2, 4, 2, 4), Cursor = Cursors.Hand };
            _filterStatusChecks.Add(check); panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock { Text = Loc.T("Tags"), Margin = new Thickness(0, 12, 0, 5) });
        _filterTagsInput = new TagInput { Project = Vm.Project, Tags = _filterDraftTags, ExistingOnly = true, InlineSuggestions = true };
        panel.Children.Add(_filterTagsInput);
        panel.Children.Add(new TextBlock { Text = Loc.T("Any selected status and any selected tag"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 10) });
        var actions = new WrapPanel();
        var apply = new Button { Content = Loc.T("Apply") };
        var cancel = new Button { Content = Loc.T("Cancel") };
        var clear = new Button { Content = Loc.T("Clear filters") };
        apply.Click += (_, _) => ApplyChapterFilter();
        cancel.Click += (_, _) => CloseChapterFilter();
        clear.Click += (_, _) => { Vm.SetChapterFilters([], []); CloseChapterFilter(); };
        actions.Children.Add(apply); actions.Children.Add(cancel); actions.Children.Add(clear); panel.Children.Add(actions);

        // A dropdown menu attached to the toolbar, with no dialog window or modal state.
        _chapterFilter = new ContextMenu { PlacementTarget = ChapterFilterButton, Placement = PlacementMode.Custom, StaysOpen = true, Padding = new Thickness(0) };
        _chapterFilter.SetResourceReference(ForegroundProperty, "TextBrush");
        var menuBorder = new FrameworkElementFactory(typeof(Border));
        menuBorder.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        menuBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        menuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        menuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        menuBorder.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        _chapterFilter.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = menuBorder };
        _chapterFilter.CustomPopupPlacementCallback = (_, size, _) => [new CustomPopupPlacement(new Point(0, size.Height), PopupPrimaryAxis.Vertical)];
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var item = new MenuItem { Header = panel, StaysOpenOnClick = true, Focusable = false, Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter } };
        _chapterFilter.Items.Add(item);
        _chapterFilter.PreviewMouseDown += (_, click) =>
        {
            Point point = click.GetPosition(_chapterFilter);
            if ((point.X < 0 || point.Y < 0 || point.X > _chapterFilter.ActualWidth || point.Y > _chapterFilter.ActualHeight)
                && _filterTagsInput?.IsSuggestionsMouseOver != true && !ChapterFilterButton.IsMouseOver) CloseChapterFilter();
        };
        _chapterFilter.AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, new MouseButtonEventHandler((_, _) =>
        {
            if (_filterTagsInput?.IsSuggestionsMouseOver != true && !ChapterFilterButton.IsMouseOver) CloseChapterFilter();
        }));
        _chapterFilter.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { CloseChapterFilter(); key.Handled = true; } };
        ChapterFilterButton.ContextMenu = _chapterFilter;
        TimelineMenuPosition.Open(ChapterFilterButton, ChapterFilterButton.PointToScreen(new Point(0, ChapterFilterButton.ActualHeight)));
    }
    private void ApplyChapterFilter()
    {
        Vm.SetChapterFilters(_filterStatusChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag), _filterDraftTags);
        CloseChapterFilter();
    }

    private void CheckChapterFilters(List<string> failures, string reportPath)
    {
        var vm = new MainViewModel(new TemplateService().CreateBlank());
        var first = vm.SelectedChapter!; first.Status = "Planned"; first.Tags.Add("red");
        vm.AddChapter(); vm.SelectedChapter!.Status = "Revised"; vm.SelectedChapter.Tags.Add("blue");
        vm.AddChapter(); vm.SelectedChapter!.Status = "Drafted"; vm.SelectedChapter.Tags.Add("red");
        vm.AddScene(title: "An unrelated scene status", status: "Final");
        vm.SetChapterFilters(["Planned", "Revised"], ["red", "blue"]);
        if (vm.SelectedBook!.Chapters.Count(vm.MatchesChapterFilter) != 2) failures.Add("Chapter filters did not combine multiple statuses and tags");
        if (vm.ChapterScenes.Count() != 1) failures.Add("Chapter filters incorrectly filtered scenes");
        vm.SetChapterFilters(["Revised"], ["red"]);
        if (vm.SelectedBook.Chapters.Any(vm.MatchesChapterFilter)) failures.Add("Chapter status and tag filters were not combined");
        vm.SetChapterFilters([], []);
        if (vm.SelectedBook.Chapters.Count(vm.MatchesChapterFilter) != 3) failures.Add("Clearing filters did not restore all chapters");
        ChapterFilter_Click(this, new RoutedEventArgs());
        _filterStatusChecks[0].IsChecked = true;
        CloseChapterFilter();
        if (Vm.HasChapterFilters) failures.Add("Canceling filters changed the active filters");
        ChapterFilter_Click(this, new RoutedEventArgs());
        if (_filterStatusChecks.Any(c => c.IsChecked == true)) failures.Add("Canceled filter draft remained selected");
        var taggedChapter = Vm.SelectedChapter!;
        taggedChapter.Tags.Add("__filter_alpha");
        taggedChapter.Tags.Add("__filter_beta");
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        foreach (string tag in new[] { "__filter_alpha", "__filter_beta" })
        {
            _filterTagsInput!.Input.Focus();
            _filterTagsInput.Input.Text = tag;
            _chapterFilter!.UpdateLayout();
            var list = _filterTagsInput.SuggestionList;
            list.UpdateLayout();
            var container = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (!_filterTagsInput.SuggestionsOpen || container is null)
                failures.Add("Filter tag suggestions did not open");
            else
                container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (_chapterFilter.IsOpen != true || !_filterDraftTags.Contains(tag))
                failures.Add("Choosing a tag closed the filter or did not select the tag");
            if (Vm.HasChapterFilters) failures.Add("Selecting draft tags applied filters prematurely");
        }
        _filterDraftTags.Clear();
        taggedChapter.Tags.Remove("__filter_alpha");
        taggedChapter.Tags.Remove("__filter_beta");
        _filterStatusChecks[0].IsChecked = true; _filterStatusChecks[1].IsChecked = true;
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        _chapterFilter!.UpdateLayout();
        SaveVisualPng(_chapterFilter, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-chapter-filter.png"));
        ApplyChapterFilter();
        if (Vm.ChapterStatusFilter.Count != 2) failures.Add("Apply did not save multiple filter statuses");
        ChapterFilter_Click(this, new RoutedEventArgs());
        RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        if (_chapterFilter?.IsOpen == true) failures.Add("Outside click did not close chapter filters");
        Vm.SetChapterFilters([], []);
        int chapterCount = Vm.SelectedBook!.Chapters.Count;
        Vm.SetChapterFilters([], ["__no_matching_chapter_tag__"]);
        if (ChapterList.Items.Count != 0 || Vm.SelectedChapter is not null) failures.Add("Chapter list did not hide nonmatching chapters");
        Vm.SetChapterFilters([], []);
        if (ChapterList.Items.Count != chapterCount || Vm.SelectedChapter is null) failures.Add("Clearing chapter filters did not restore the list and selection");
    }
}


