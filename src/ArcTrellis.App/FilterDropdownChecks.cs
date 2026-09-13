using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private void CheckFilterDropdownLayout(List<string> failures, string reportPath)
    {
        void Drain() { Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); _sceneFilter!.UpdateLayout(); }
        double menuHeight = _sceneFilter!.ActualHeight;
        var captured = Mouse.Captured;
        foreach (var choice in new[] { _sceneChapterChoices!, _scenePlotlineChoices!, _sceneCharacterChoices! })
        {
            double tagsTop = _sceneFilterTagsInput!.TranslatePoint(new Point(), _sceneFilter).Y;
            choice.Input.Focus(); Drain(); choice.DropdownSurface.UpdateLayout();
            if (!choice.IsOpen || ReferenceEquals(PresentationSource.FromVisual(choice.DropdownSurface), PresentationSource.FromVisual(_sceneFilter)))
                failures.Add("Autocomplete suggestions are not in a floating popup");
            if (Math.Abs(menuHeight - _sceneFilter.ActualHeight) > 1 || Math.Abs(tagsTop - _sceneFilterTagsInput.TranslatePoint(new Point(), _sceneFilter).Y) > 1)
                failures.Add("Opening autocomplete moves filter fields or changes the menu height");
            if (!ReferenceEquals(captured, Mouse.Captured)) failures.Add("Autocomplete stole mouse capture from the filter menu");
            var check = choice.Choices.Values.First();
            check.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            if (!_sceneFilter.IsOpen) failures.Add("Clicking a floating autocomplete closed the filter menu");
        }
        var characterList = FindVisualChildren<ScrollViewer>(_sceneCharacterChoices!.DropdownSurface).Single();
        characterList.ScrollToEnd(); Drain();
        var characterBar = FindVisualChildren<ScrollBar>(characterList).FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible);
        if (characterList.ScrollableHeight <= 0 || characterList.VerticalOffset <= 0 || characterBar is null || characterBar.ActualWidth > 8.5)
            failures.Add("Long autocomplete choices do not use a working slim scrollbar");
        characterList.ScrollToHome(); Drain();
        SaveVisualPng(_sceneCharacterChoices.DropdownSurface, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-autocomplete-dropdown.png"));

        var tags = _sceneFilterTagsInput!;
        tags.Input.Focus(); tags.Input.Text = "Overlay"; Drain(); tags.DropdownSurface.UpdateLayout();
        if (!tags.SuggestionsOpen || ReferenceEquals(PresentationSource.FromVisual(tags.DropdownSurface), PresentationSource.FromVisual(_sceneFilter))
            || Math.Abs(menuHeight - _sceneFilter.ActualHeight) > 1)
            failures.Add("Tag suggestions do not float independently of the filter layout");
        if (!ReferenceEquals(captured, Mouse.Captured)) failures.Add("Tag suggestions stole filter mouse capture");
        var tagList = FindVisualChildren<ScrollViewer>(tags.SuggestionList).Single();
        tagList.ScrollToEnd(); Drain();
        var tagBar = FindVisualChildren<ScrollBar>(tagList).FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible);
        if (tagList.ScrollableHeight <= 0 || tagList.VerticalOffset <= 0 || tagBar is null || tagBar.ActualWidth > 8.5)
            failures.Add("Long tag suggestions do not use a working slim scrollbar");
        tagList.ScrollToHome(); Drain();
        SaveVisualPng(tags.DropdownSurface, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-tag-dropdown.png"));
        var item = tags.SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        if (item is null) failures.Add("Floating tag suggestion is not clickable");
        else
        {
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Drain();
            if (!_sceneFilter.IsOpen || _sceneFilterDraftTags.Count != 1 || tags.Input.Text.Length != 0)
                failures.Add("Choosing a floating tag did not retain the filter menu and commit the chip");
        }
        _sceneFilterDraftTags.Clear(); tags.Input.Clear(); tags.CloseSuggestions(); Drain();
        if (FindVisualChildren<ScrollBar>(_sceneFilter).Any(bar => bar.IsVisible)) failures.Add("The filter menu still has a general scrollbar");
        var apply = FindVisualChildren<Button>(_sceneFilter).First(button => Equals(button.Content, Loc.T("Apply")));
        double tagsBottom = tags.TranslatePoint(new Point(0, tags.ActualHeight), _sceneFilter).Y;
        double gap = apply.TranslatePoint(new Point(), _sceneFilter).Y - tagsBottom;
        if (gap < 0 || gap > 20) failures.Add($"The gap between Tags and filter buttons is {gap}");
        SaveVisualPng(_sceneFilter, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-compact-scene-filter.png"));
    }
}
