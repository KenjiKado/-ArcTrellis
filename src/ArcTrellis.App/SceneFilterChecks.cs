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
        void Drain() { Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout(); if (_sceneFilter?.IsOpen == true) SceneFilterSurface.UpdateLayout(); }
        void MenuButton(string text) => FindVisualChildren<Button>(SceneFilterSurface).First(button => Equals(button.Content, Loc.T(text))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        Vm.AddScene(north.Id, journey.Id, "Filter A", "Planned"); var a = Vm.SelectedScene!; Vm.EditTag(a.Tags, "FilterRed", false); Vm.EditTag(a.Tags, "FilterBlue", false);
        Vm.AddScene(south.Id, mystery.Id, "Filter B", "Drafted"); var b = Vm.SelectedScene!; Vm.EditTag(b.Tags, "FilterBlue", false);
        Vm.AddScene(south.Id, journey.Id, "Filter C", "Final"); var c = Vm.SelectedScene!; Vm.EditTag(c.Tags, "FilterRed", false);
        Vm.AddScene(north.Id, mystery.Id, "Filter D", "Revised");
        var alice = new StoryEntity { Name = "Filter Alice" };
        var bob = new StoryEntity { Name = "Filter Bob" };
        var unassigned = new StoryEntity { Name = "Filter Unassigned" };
        Vm.Project.Characters.Add(alice); Vm.Project.Characters.Add(bob); Vm.Project.Characters.Add(unassigned);
        for (int index = 0; index < 24; index++)
        {
            Vm.Project.Characters.Add(new StoryEntity { Name = $"Overlay Character {index + 1:00}" });
            a.Tags.Add($"Overlay tag {index + 1:00}");
        }
        a.CharacterIds.Add(alice.Id); a.CharacterIds.Add(bob.Id); b.CharacterIds.Add(bob.Id); c.CharacterIds.Add(alice.Id);
        int characterCount = Vm.Project.Characters.Count;
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
        CheckFilterDropdownLayout(failures, reportPath);
        _sceneChapterChoices!.Input.Focus(); Drain();
        if (!_sceneChapterChoices.IsOpen || _sceneChapterChoices.Choices.Count != 2 || _sceneChapterChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Chapter filter does not initially show all current-book chapters");
        _sceneChapterChoices.Input.Text = "nOr";
        if (_sceneChapterChoices.Choices[north.Id].Visibility != Visibility.Visible || _sceneChapterChoices.Choices[south.Id].Visibility != Visibility.Collapsed)
            failures.Add("Chapter autocomplete does not filter typed text case-insensitively");
        _sceneChapterChoices.Choices[north.Id].IsChecked = true;
        if (_sceneChapterChoices.Input.Text.Length != 0 || _sceneChapterChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Choosing a chapter did not clear autocomplete text and restore all choices");
        _sceneChapterChoices.Choices[south.Id].IsChecked = true;
        _scenePlotlineChoices!.Input.Focus(); Drain();
        if (!_scenePlotlineChoices.IsOpen || _scenePlotlineChoices.Choices.Count != 2 || _scenePlotlineChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Plotline filter does not initially show all current-book plotlines");
        _scenePlotlineChoices.Input.Text = "yst";
        if (_scenePlotlineChoices.Choices[mystery.Id].Visibility != Visibility.Visible || _scenePlotlineChoices.Choices[journey.Id].Visibility != Visibility.Collapsed)
            failures.Add("Plotline autocomplete does not match text inside the name");
        var plotInput = _scenePlotlineChoices.Input;
        plotInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(plotInput), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        if (!_scenePlotlineChoices.SelectedIds.Contains(mystery.Id) || plotInput.Text.Length != 0 || _scenePlotlineChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Choosing a plotline with Enter did not clear the autocomplete search");
        _scenePlotlineChoices.Choices[journey.Id].IsChecked = true;
        _sceneCharacterChoices!.Input.Focus(); Drain();
        if (!_sceneCharacterChoices.IsOpen || !_sceneCharacterChoices.Choices.Keys.ToHashSet().SetEquals(Vm.Project.Characters.Select(character => character.Id))
            || _sceneCharacterChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Character filter does not initially show every character in the project");
        _sceneCharacterChoices.Input.Text = "aLi";
        if (_sceneCharacterChoices.Choices[alice.Id].Visibility != Visibility.Visible || _sceneCharacterChoices.Choices[bob.Id].Visibility != Visibility.Collapsed)
            failures.Add("Character autocomplete does not filter names case-insensitively");
        _sceneCharacterChoices.Choices[alice.Id].IsChecked = true;
        if (_sceneCharacterChoices.Input.Text.Length != 0 || _sceneCharacterChoices.Choices.Values.Any(check => check.Visibility != Visibility.Visible))
            failures.Add("Choosing a character did not clear autocomplete text and restore all choices");
        _sceneCharacterChoices.Choices[bob.Id].IsChecked = true;
        var removeBob = FindVisualChildren<Button>(_sceneCharacterChoices).Single(button => Equals(button.Tag, bob.Id));
        removeBob.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (_sceneCharacterChoices.SelectedIds.Contains(bob.Id) || !Vm.Project.Characters.Contains(bob) || Vm.Project.Characters.Count != characterCount)
            failures.Add("Removing a character filter chip changed project characters or left the selection active");
        _sceneCharacterChoices.Choices[bob.Id].IsChecked = true;
        foreach (var check in _sceneFilterStatusChecks) check.IsChecked = check.Tag is "Planned" or "Drafted";
        TypeTag("FilterRed"); TypeTag("FilterBlue"); Drain();
        if (_sceneFilterDraftTags.Count != 2 || _sceneChapterChoices.SelectedIds.Count != 2 || _scenePlotlineChoices.SelectedIds.Count != 2 || _sceneCharacterChoices.SelectedIds.Count != 2 || _sceneFilter?.IsOpen != true)
            failures.Add("Scene filter menu did not retain multiple choices while staying open");
        if (Vm.HasSceneFilters || SceneList.Items.Count != 4) failures.Add("Editing a filter draft changed the scene list before Apply");
        _sceneCharacterChoices.Input.Focus(); Drain();
        foreach (var label in new[] { "Apply", "Cancel", "Clear filters" })
        {
            var action = FindVisualChildren<Button>(SceneFilterSurface).First(button => Equals(button.Content, Loc.T(label)));
            var bounds = action.TransformToAncestor(SceneFilterSurface).TransformBounds(new Rect(action.RenderSize));
            if (bounds.Top < 0 || bounds.Bottom > SceneFilterSurface.ActualHeight || bounds.Right > SceneFilterSurface.ActualWidth || action.ActualHeight < 20)
                failures.Add($"Expanded scene filter clips the {label} button: {bounds} inside {SceneFilterSurface.RenderSize}");
        }
        SaveVisualPng(SceneFilterSurface, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scenes-filter.png"));
        SaveVisualPng(_sceneCharacterChoices.DropdownSurface, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-filter-autocomplete.png"));
        MenuButton("Apply"); Drain();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id])) failures.Add("Applied scene filters did not require both selected tags");
        if (_sceneFilter?.IsOpen == true || _sceneCharacterChoices.IsOpen || Vm.BookScenes.Count() != 4) failures.Add("Applying scene filters changed the underlying book scenes or left a popup open");
        if (!Vm.SceneCharacterFilter.SetEquals([alice.Id, bob.Id])) failures.Add("Apply did not retain multiple character filters");
        // Chapters/plotlines/statuses use OR; scenes must have every selected tag and character.
        Vm.SetSceneFilters([], [], [], ["filterred"]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id])) failures.Add("A single tag did not match both scenes sharing it");
        Vm.SetSceneFilters([], [], [], ["FILTERRED", "filterblue"]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id])) failures.Add("Multiple selected tags did not narrow to the scene containing all of them");
        Vm.SetSceneFilters([], [south.Id], [], []); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([b.Id, c.Id])) failures.Add("Chapter-only scene filter failed");
        Vm.SetSceneFilters([], [], [], [], [alice.Id]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id])) failures.Add("A single character did not match both scenes containing that character");
        Vm.SetSceneFilters([], [], [], [], [alice.Id, bob.Id]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id])) failures.Add("Multiple selected characters did not narrow to the scene containing all of them");
        Vm.SetSceneFilters([], [], [], [], [bob.Id]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, b.Id])) failures.Add("Bob-only scene filter failed");
        Vm.SetSceneFilters([], [], [journey.Id], ["filterred"], [alice.Id]); RefreshSceneList();
        if (!SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id])) failures.Add("Plotline and tag scene filters failed");
        SceneFilter_Click(this, new RoutedEventArgs()); Drain();
        _sceneFilterStatusChecks[0].IsChecked = true;
        _sceneCharacterChoices!.Choices[alice.Id].IsChecked = false;
        MenuButton("Cancel"); Drain();
        if (Vm.SceneStatusFilter.Count != 0 || !Vm.SceneCharacterFilter.SetEquals([alice.Id]) || !SceneList.Items.Cast<Scene>().Select(scene => scene.Id).ToHashSet().SetEquals([a.Id, c.Id]))
            failures.Add("Cancel changed the active scene filters");
        SceneFilter_Click(this, new RoutedEventArgs()); Drain(); MenuButton("Clear filters"); Drain();
        if (Vm.HasSceneFilters || SceneList.Items.Count != 4) failures.Add("Clear scene filters did not restore all scenes");
        if (Vm.IsDirty) failures.Add("Using scene filters marked the story data as edited");
        Vm.SetSceneFilters([], [], [], [], [unassigned.Id]); RefreshSceneList(); Drain();
        if (SceneList.Items.Count != 0 || Vm.SelectedScene is not null || SceneEditor.IsEnabled) failures.Add("No-match scene filter left an active scene editor");
        ClearSceneFilters(); Drain();
        Vm.SetSceneFilters(["Planned"], [north.Id], [journey.Id], ["FilterRed"], [alice.Id]); RefreshSceneList();
        SceneFilter_Click(this, new RoutedEventArgs()); Drain();
        _sceneChapterChoices!.Input.Focus(); Drain();
        Vm.SelectedBook = previousBook; Drain();
        if (Vm.HasSceneFilters || _sceneFilter?.IsOpen == true || _sceneChapterChoices.IsOpen) failures.Add("Switching books did not clear and close all scene filters");
        Vm.SelectedBook = book; Drain();
        if (SceneList.Items.Count != 4) failures.Add("Returning to a book restored stale scene filters");
    }
}
