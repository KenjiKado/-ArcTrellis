using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private ContextMenu? _chapterSceneFilter;
    private TagInput? _filterTagsInput;
    private readonly List<CheckBox> _filterStatusChecks = [];
    private ObservableCollection<string> _filterDraftTags = [];

    private void CloseChapterSceneFilter()
    {
        if (_chapterSceneFilter is not null) _chapterSceneFilter.IsOpen = false;
    }
    private void UpdateChapterSceneFilterButton() => ChapterSceneFilterButton.SetResourceReference(BorderBrushProperty,
        Vm.HasChapterSceneFilters ? "AccentBrush" : "BorderBrush");

    private void ChapterSceneFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterSceneFilter?.IsOpen == true) { CloseChapterSceneFilter(); return; }
        _filterStatusChecks.Clear();
        _filterDraftTags = new(Vm.ChapterSceneTagFilter);
        var panel = new StackPanel { Width = 340, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Filter scenes"), FontWeight = FontWeights.SemiBold, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = Loc.T("Status"), Margin = new Thickness(0, 12, 0, 5) });
        foreach (var status in Vm.SceneStatuses)
        {
            var check = new CheckBox { Content = status.Label, Tag = status.Code, IsChecked = Vm.ChapterSceneStatusFilter.Contains(status.Code), Margin = new Thickness(2, 4, 2, 4), Cursor = Cursors.Hand };
            _filterStatusChecks.Add(check); panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock { Text = Loc.T("Tags"), Margin = new Thickness(0, 12, 0, 5) });
        _filterTagsInput = new TagInput { Project = Vm.Project, Tags = _filterDraftTags, ExistingOnly = true };
        panel.Children.Add(_filterTagsInput);
        panel.Children.Add(new TextBlock { Text = Loc.T("Any selected status and any selected tag"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 10) });
        var actions = new WrapPanel();
        var apply = new Button { Content = Loc.T("Apply") };
        var cancel = new Button { Content = Loc.T("Cancel") };
        var clear = new Button { Content = Loc.T("Clear filters") };
        apply.Click += (_, _) => ApplyChapterSceneFilter();
        cancel.Click += (_, _) => CloseChapterSceneFilter();
        clear.Click += (_, _) => { Vm.SetChapterSceneFilters([], []); CloseChapterSceneFilter(); };
        actions.Children.Add(apply); actions.Children.Add(cancel); actions.Children.Add(clear); panel.Children.Add(actions);

        // A dropdown menu attached to the toolbar, with no dialog window or modal state.
        _chapterSceneFilter = new ContextMenu { PlacementTarget = ChapterSceneFilterButton, Placement = PlacementMode.Custom, StaysOpen = true, Padding = new Thickness(0) };
        _chapterSceneFilter.CustomPopupPlacementCallback = (_, size, _) => [new CustomPopupPlacement(new Point(0, size.Height), PopupPrimaryAxis.Vertical)];
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var item = new MenuItem { Header = panel, StaysOpenOnClick = true, Focusable = false, Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter } };
        _chapterSceneFilter.Items.Add(item);
        _chapterSceneFilter.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { CloseChapterSceneFilter(); key.Handled = true; } };
        ChapterSceneFilterButton.ContextMenu = _chapterSceneFilter;
        TimelineMenuPosition.Open(ChapterSceneFilterButton, ChapterSceneFilterButton.PointToScreen(new Point(0, ChapterSceneFilterButton.ActualHeight)));
    }
    private void ApplyChapterSceneFilter()
    {
        Vm.SetChapterSceneFilters(_filterStatusChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag), _filterDraftTags);
        CloseChapterSceneFilter();
    }

    private void CheckChapterSceneFilters(List<string> failures, string reportPath)
    {
        var vm = new MainViewModel(new TemplateService().CreateBlank());
        vm.AddScene(title: "Planned red", status: "Planned"); vm.SelectedScene!.Tags.Add("red");
        vm.AddScene(title: "Revised blue", status: "Revised"); vm.SelectedScene!.Tags.Add("blue");
        vm.AddScene(title: "Drafted red", status: "Drafted"); vm.SelectedScene!.Tags.Add("red");
        vm.SetChapterSceneFilters(["Planned", "Revised"], ["red", "blue"]);
        if (vm.ChapterScenes.Count() != 2) failures.Add("Scene filters did not combine multiple statuses and tags");
        vm.SetChapterSceneFilters(["Revised"], ["red"]);
        if (vm.ChapterScenes.Any()) failures.Add("Status and tag filters were not combined");
        vm.SetChapterSceneFilters([], []);
        if (vm.ChapterScenes.Count() != 3) failures.Add("Clearing filters did not restore all scenes");
        ChapterSceneFilter_Click(this, new RoutedEventArgs());
        _filterStatusChecks[0].IsChecked = true;
        CloseChapterSceneFilter();
        if (Vm.HasChapterSceneFilters) failures.Add("Canceling filters changed the active filters");
        ChapterSceneFilter_Click(this, new RoutedEventArgs());
        if (_filterStatusChecks.Any(c => c.IsChecked == true)) failures.Add("Canceled filter draft remained selected");
        _filterStatusChecks[0].IsChecked = true; _filterStatusChecks[1].IsChecked = true;
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        _chapterSceneFilter!.UpdateLayout();
        SaveVisualPng(_chapterSceneFilter, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-filter.png"));
        ApplyChapterSceneFilter();
        if (Vm.ChapterSceneStatusFilter.Count != 2) failures.Add("Apply did not save multiple filter statuses");
        ChapterSceneFilter_Click(this, new RoutedEventArgs());
        RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        if (_chapterSceneFilter?.IsOpen == true) failures.Add("Outside click did not close scene filters");
        Vm.SetChapterSceneFilters([], []);
    }
}
