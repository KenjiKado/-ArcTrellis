using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private void CheckSceneFilters(List<string> failures, string reportPath)
    {
        void Drain() { Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout(); _sceneFilter?.UpdateLayout(); }
        void MenuButton(string text) => FindVisualChildren<Button>(_sceneFilter!).First(button => Equals(button.Content, Loc.T(text))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void TypeTag(string tag)
        {
            var input = _sceneFilterTagsInput!.Input;
            input.Focus(); input.Text = tag;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        var previousBook = Vm.SelectedBook!;
        WorkspaceTabs.SelectedIndex = 3;
        Vm.AddBook("Scene filter checks");
        var book = Vm.SelectedBook!;
        var north = Vm.SelectedChapter!; north.Title = "North Chapter";
        Vm.AddChapter(); var south = Vm.SelectedChapter!; south.Title = "South Chapter";
        var journey = Vm.BookPlotlines.First(); journey.Name = "Journey";
        Vm.AddPlotline(); var mystery = Vm.SelectedPlotline!; mystery.Name = "Mystery";
        Vm.AddScene(north.Id, journey.Id, "Filter A", "Planned"); var a = Vm.SelectedScene!; Vm.EditTag(a.Tags, "FilterRed", false);
        Vm.AddScene(south.Id, mystery.Id, "Filter B", "Drafted"); var b = Vm.SelectedScene!; Vm.EditTag(b.Tags, "FilterBlue", false);
        Vm.AddScene(south.Id, journey.Id, "Filter C", "Final"); var c = Vm.SelectedScene!; Vm.EditTag(c.Tags, "FilterRed", false);
        Vm.AddScene(north.Id, mystery.Id, "Filter D", "Revised");
        RefreshAll(); Drain();
        var characters = FindVisualChildren<CharacterInput>(SceneEditor).Single();
        SceneList.Focus();
        var hit = characters.ClickSurface.InputHitTest(new Point(characters.ClickSurface.ActualWidth - 7, characters.ClickSurface.ActualHeight / 2)) as UIElement;
        if (hit is null) failures.Add("Character input has dead space at the right edge");
        else
        {
            hit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            if (!characters.Input.IsKeyboardFocused) failures.Add("Clicking empty character input space did not focus typing");
        }
        var toolbar = (Grid)SceneFilterButton.Parent;
        if (SceneFilterButton.ActualWidth < 30 || SceneFilterButton.TranslatePoint(new Point(SceneFilterButton.ActualWidth, 0), toolbar).X > toolbar.ActualWidth + 1)
            failures.Add("Scene filter button is clipped");
        Vm.IsDirty = false;
        SceneFilter_Click(this, new RoutedEventArgs()); Drain();
        _sceneChapterChoices!.Input.Focus(); Drain();
        if (!_sceneChapterChoices.IsOpen || _sceneChapterChoices.Choices.Count != 2 || _sceneChapterChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Chapter filter does not initially show all current-book chapters");
        _sceneChapterChoices.Input.Text = "nOr";
        if (_sceneChapterChoices.Choices[north.Id].Visibility != Visibility.Visible || _sceneChapterChoices.Choices[south.Id].Visibility != Visibility.Collapsed)
            failures.Add("Chapter autocomplete does not filter typed text case-insensitively");
        _sceneChapterChoices.Choices[north.Id].IsChecked = true;
        _sceneChapterChoices.Input.Clear(); _sceneChapterChoices.Choices[south.Id].IsChecked = true;
        _scenePlotlineChoices!.Input.Focus(); Drain();
        if (!_scenePlotlineChoices.IsOpen || _scenePlotlineChoices.Choices.Count != 2 || _scenePlotlineChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Plotline filter does not initially show all current-book plotlines");
        _scenePlotlineChoices.Input.Text = "yst";
        if (_scenePlotlineChoices.Choices[mystery.Id].Visibility != Visibility.Visible || _scenePlotlineChoices.Choices[journey.Id].Visibility != Visibility.Collapsed)
            failures.Add("Plotline autocomplete does not match text inside the name");
        _scenePlotlineChoices.Choices[mystery.Id].IsChecked = true;
        _scenePlotlineChoices.Input.Clear(); _scenePlotlineChoices.Choices[journey.Id].IsChecked = true;
        foreach (var check in _sceneFilterStatusChecks) check.IsChecked = check.Tag is "Planned" or "Drafted";
        TypeTag("FilterRed"); TypeTag("FilterBlue"); Drain();
        if (_sceneFilterDraftTags.Count != 2 || _sceneChapterChoices.SelectedIds.Count != 2 || _scenePlotlineChoices.SelectedIds.Count != 2 || _sceneFilter?.IsOpen != true)
            failures.Add("Scene filter menu did not retain multiple choices while staying open");
        if (Vm.HasSceneFilters || SceneList.Items.Count != 4) failures.Add("Editing a filter draft changed the scene list before Apply");
        _sceneChapterChoices.Input.Focus(); Drain();
        foreach (var label in new[] { "Apply", "Cancel", "Clear filters" })
        {
            var action = FindVisualChildren<Button>(_sceneFilter!).First(button => Equals(button.Content, Loc.T(label)));
            var bounds = action.TransformToAncestor(_sceneFilter!).TransformBounds(new Rect(action.RenderSize));
            if (bounds.Top < 0 || bounds.Bottom > _sceneFilter!.ActualHeight || bounds.Right > _sceneFilter.ActualWidth || action.ActualHeight < 20)
                failures.Add($"Expanded scene filter clips the {label} button: {bounds} inside {_sceneFilter!.RenderSize}");
        }
        SaveVisualPng(_sceneFilter!, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scenes-filter.png"));
        MenuButton("Apply"); Drain();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, b.Id])) failures.Add("Applied scene filters did not combine categories correctly");
        if (_sceneFilter?.IsOpen == true || Vm.BookScenes.Count() != 4) failures.Add("Applying scene filters changed the underlying book scenes or left menu open");
        // Each category must independently narrow results, using OR within a category.
        Vm.SetSceneFilters([], [south.Id], [], []); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([b.Id, c.Id])) failures.Add("Chapter-only scene filter failed");
        Vm.SetSceneFilters([], [], [journey.Id], ["filterred"]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id])) failures.Add("Plotline and tag scene filters failed");
        SceneFilter_Click(this, new RoutedEventArgs()); Drain();
        _sceneFilterStatusChecks[0].IsChecked = true;
        MenuButton("Cancel"); Drain();
        if (Vm.SceneStatusFilter.Count != 0 || !SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id]))
            failures.Add("Cancel changed the active scene filters");
        SceneFilter_Click(this, new RoutedEventArgs()); Drain(); MenuButton("Clear filters"); Drain();
        if (Vm.HasSceneFilters || SceneList.Items.Count != 4) failures.Add("Clear scene filters did not restore all scenes");
        if (Vm.IsDirty) failures.Add("Using scene filters marked the story data as edited");
        Vm.SetSceneFilters(["Cut"], [], [], []); RefreshSceneList(); Drain();
        if (SceneList.Items.Count != 0 || Vm.SelectedScene is not null || SceneEditor.IsEnabled) failures.Add("No-match scene filter left an active scene editor");
        ClearSceneFilters(); Drain();
        Vm.SetSceneFilters(["Planned"], [north.Id], [journey.Id], ["FilterRed"]); RefreshSceneList();
        SceneFilter_Click(this, new RoutedEventArgs()); Drain();
        Vm.SelectedBook = previousBook; Drain();
        if (Vm.HasSceneFilters || _sceneFilter?.IsOpen == true) failures.Add("Switching books did not clear and close all scene filters");
        Vm.SelectedBook = book; Drain();
        if (SceneList.Items.Count != 4) failures.Add("Returning to a book restored stale scene filters");
    }
}
