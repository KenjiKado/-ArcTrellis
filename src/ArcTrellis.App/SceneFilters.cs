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
    private Popup? _sceneFilter;
    private FrameworkElement SceneFilterSurface => (FrameworkElement)_sceneFilter!.Child;
    private readonly List<CheckBox> _sceneFilterStatusChecks = [];
    private MultiChoiceInput? _sceneChapterChoices, _scenePlotlineChoices, _sceneCharacterChoices;
    private TagInput? _sceneFilterTagsInput;
    private ObservableCollection<string> _sceneFilterDraftTags = [];

    private void SceneList_Filter(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is Scene scene && (DataContext is not MainViewModel vm || vm.MatchesSceneFilter(scene));

    private void CloseSceneDropdowns()
    {
        _sceneChapterChoices?.CloseDropdown(); _scenePlotlineChoices?.CloseDropdown(); _sceneCharacterChoices?.CloseDropdown();
        _sceneFilterTagsInput?.CloseSuggestions();
    }
    private void CloseSceneFilter() { CloseSceneDropdowns(); if (_sceneFilter is not null) _sceneFilter.IsOpen = false; }
    private bool IsSceneDropdownInteraction(DependencyObject? source) =>
        _sceneChapterChoices?.IsDropdownMouseOver == true || _scenePlotlineChoices?.IsDropdownMouseOver == true || _sceneCharacterChoices?.IsDropdownMouseOver == true || _sceneFilterTagsInput?.IsSuggestionsMouseOver == true
        || DropdownChrome.Contains(_sceneChapterChoices?.DropdownSurface, source) || DropdownChrome.Contains(_scenePlotlineChoices?.DropdownSurface, source)
        || DropdownChrome.Contains(_sceneCharacterChoices?.DropdownSurface, source) || DropdownChrome.Contains(_sceneFilterTagsInput?.DropdownSurface, source)
        || DropdownChrome.PointerWithin(_sceneChapterChoices?.DropdownSurface) || DropdownChrome.PointerWithin(_scenePlotlineChoices?.DropdownSurface)
        || DropdownChrome.PointerWithin(_sceneCharacterChoices?.DropdownSurface) || DropdownChrome.PointerWithin(_sceneFilterTagsInput?.DropdownSurface);

    private bool IsSceneFilterInteraction(DependencyObject? source)
    {
        if ((_sceneFilter is not null && DropdownChrome.Contains(SceneFilterSurface, source)) || IsSceneDropdownInteraction(source)) return true;
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
        var fields = new StackPanel { Margin = new Thickness(12, 12, 12, 6) };
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
        Label("Characters");
        _sceneCharacterChoices = new MultiChoiceInput("Characters", Vm.Project.Characters.OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase).Select(character => (character.Id, character.Name)), Vm.SceneCharacterFilter);
        fields.Children.Add(_sceneCharacterChoices);
        Label("Tags");
        _sceneFilterTagsInput = new TagInput { Project = Vm.Project, Tags = _sceneFilterDraftTags, ExistingOnly = true };
        fields.Children.Add(_sceneFilterTagsInput);

        var actions = new WrapPanel { Margin = new Thickness(9, 0, 9, 9) };
        var apply = new Button { Content = Loc.T("Apply") };
        var cancel = new Button { Content = Loc.T("Cancel") };
        var clear = new Button { Content = Loc.T("Clear filters") };
        apply.Click += (_, _) => ApplySceneFilter();
        cancel.Click += (_, _) => CloseSceneFilter();
        clear.Click += (_, _) => ClearSceneFilters();
        actions.Children.Add(apply); actions.Children.Add(cancel); actions.Children.Add(clear);
        var root = new StackPanel { Width = 404 };
        root.Children.Add(fields); root.Children.Add(actions);
        _sceneFilter = new Popup { PlacementTarget = SceneFilterButton, Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true };
        _sceneFilter.Closed += (_, _) => CloseSceneDropdowns();
        var surface = new Border { Child = root, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5) };
        surface.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        surface.SetResourceReference(ForegroundProperty, "TextBrush");
        _sceneFilter.Child = surface;
        root.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != Key.Escape) return;
            if (_sceneChapterChoices?.IsOpen == true || _scenePlotlineChoices?.IsOpen == true || _sceneCharacterChoices?.IsOpen == true || _sceneFilterTagsInput?.SuggestionsOpen == true) CloseSceneDropdowns();
            else CloseSceneFilter();
            key.Handled = true;
        };
        TimelineMenuPosition.OpenFitted(SceneFilterButton, root, _sceneFilter);
    }

    private void ApplySceneFilter()
    {
        if (_sceneChapterChoices is null || _scenePlotlineChoices is null || _sceneCharacterChoices is null) return;
        Vm.SetSceneFilters(_sceneFilterStatusChecks.Where(check => check.IsChecked == true).Select(check => (string)check.Tag),
            _sceneChapterChoices.SelectedIds, _scenePlotlineChoices.SelectedIds, _sceneFilterDraftTags, _sceneCharacterChoices.SelectedIds);
        CloseSceneFilter(); RefreshSceneList();
    }

    private void ClearSceneFilters()
    {
        Vm.SetSceneFilters([], [], [], []);
        CloseSceneFilter(); RefreshSceneList();
    }
}
