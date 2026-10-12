using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private Popup? _characterFilter;
    private FrameworkElement CharacterFilterSurface => (FrameworkElement)_characterFilter!.Child;
    private readonly List<CheckBox> _characterFilterCategoryChecks = [];
    private MultiChoiceInput? _characterBookChoices;
    private TagInput? _characterFilterTagsInput;
    private ObservableCollection<string> _characterFilterDraftTags = [];
    private bool _refreshingCharacterFilter;

    private void CharacterList_Filter(object sender, FilterEventArgs e) =>
        e.Accepted = e.Item is StoryEntity character &&
            (DataContext is not MainViewModel vm || vm.MatchesCharacterFilter(character));

    private void CloseCharacterDropdowns()
    {
        _characterBookChoices?.CloseDropdown();
        _characterFilterTagsInput?.CloseSuggestions();
    }

    private void CloseCharacterFilter()
    {
        CloseCharacterDropdowns();
        if (_characterFilter is not null) _characterFilter.IsOpen = false;
    }

    private bool IsCharacterDropdownInteraction(DependencyObject? source) =>
        _characterBookChoices?.IsDropdownMouseOver == true ||
        _characterFilterTagsInput?.IsSuggestionsMouseOver == true ||
        DropdownChrome.Contains(_characterBookChoices?.DropdownSurface, source) ||
        DropdownChrome.Contains(_characterFilterTagsInput?.DropdownSurface, source) ||
        DropdownChrome.PointerWithin(_characterBookChoices?.DropdownSurface) ||
        DropdownChrome.PointerWithin(_characterFilterTagsInput?.DropdownSurface);

    private bool IsCharacterFilterInteraction(DependencyObject? source)
    {
        if ((_characterFilter is not null && DropdownChrome.Contains(CharacterFilterSurface, source)) ||
            IsCharacterDropdownInteraction(source)) return true;
        var visited = new HashSet<DependencyObject>();
        while (source is not null && visited.Add(source))
        {
            if (ReferenceEquals(source, _characterFilter) || ReferenceEquals(source, CharacterFilterButton)) return true;
            if (source is Popup popup) source = popup.PlacementTarget;
            else source = LogicalTreeHelper.GetParent(source) ??
                (source is Visual ? VisualTreeHelper.GetParent(source) : null);
        }
        return false;
    }

    private void RefreshCharacterFilter()
    {
        if (_refreshingCharacterFilter || DataContext is not MainViewModel) return;
        var view = ((CollectionViewSource)Resources["CharacterListView"]).View;
        if (view is null || !ReferenceEquals(view.SourceCollection, Vm.Project.Characters)) return;
        _refreshingCharacterFilter = true;
        try
        {
            var selected = Vm.SelectedCharacter;
            view.Refresh();
            if (selected is null || !view.Contains(selected))
                selected = view.Cast<StoryEntity>().FirstOrDefault();
            Vm.SelectedCharacter = selected;
            if (!ReferenceEquals(CharacterList.SelectedItem, selected)) CharacterList.SelectedItem = selected;
        }
        finally { _refreshingCharacterFilter = false; }
    }

    private void CharacterFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_characterFilter?.IsOpen == true) { CloseCharacterFilter(); return; }
        CloseChapterFilter();
        CloseSceneFilter();
        Vm.RefreshCharacterCategories();
        _characterFilterCategoryChecks.Clear();
        _characterFilterDraftTags = new(Vm.CharacterTagFilter);

        var fields = new StackPanel { Margin = new Thickness(12, 12, 12, 6) };
        fields.Children.Add(new TextBlock
        {
            Text = Loc.T("Filter characters"), FontWeight = FontWeights.SemiBold, FontSize = 16
        });
        void Label(string value) => fields.Children.Add(new TextBlock
        {
            Text = Loc.T(value), Margin = new Thickness(0, 12, 0, 5)
        });

        Label("Category");
        var categories = new WrapPanel();
        foreach (var category in Vm.CharacterCategories)
        {
            var check = new CheckBox
            {
                Content = category.Label,
                Tag = category.Code,
                IsChecked = Vm.CharacterCategoryFilter.Contains(category.Code),
                Width = 178,
                Margin = new Thickness(2, 4, 2, 4),
                Cursor = Cursors.Hand
            };
            _characterFilterCategoryChecks.Add(check);
            categories.Children.Add(check);
        }
        fields.Children.Add(categories);

        Label("Books");
        _characterBookChoices = new MultiChoiceInput("Books",
            Vm.Project.Books.OrderBy(book => book.Order).Select(book => (book.Id, book.Title)),
            Vm.CharacterBookFilter);
        fields.Children.Add(_characterBookChoices);

        Label("Tags");
        _characterFilterTagsInput = new TagInput
        {
            Project = Vm.Project,
            Tags = _characterFilterDraftTags,
            ExistingOnly = true
        };
        fields.Children.Add(_characterFilterTagsInput);

        var actions = new WrapPanel { Margin = new Thickness(9, 0, 9, 9) };
        var apply = new Button { Content = Loc.T("Apply") };
        var cancel = new Button { Content = Loc.T("Cancel") };
        var clear = new Button { Content = Loc.T("Clear filters") };
        apply.Click += (_, _) => ApplyCharacterFilter();
        cancel.Click += (_, _) => CloseCharacterFilter();
        clear.Click += (_, _) => ClearCharacterFilters();
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        actions.Children.Add(clear);

        var root = new StackPanel { Width = 404 };
        root.Children.Add(fields);
        root.Children.Add(actions);
        _characterFilter = new Popup
        {
            PlacementTarget = CharacterFilterButton,
            Placement = PlacementMode.Relative,
            StaysOpen = false,
            AllowsTransparency = true
        };
        _characterFilter.Closed += (_, _) => CloseCharacterDropdowns();
        var surface = new Border
        {
            Child = root,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        surface.SetResourceReference(ForegroundProperty, "TextBrush");
        _characterFilter.Child = surface;
        root.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != Key.Escape) return;
            if (_characterBookChoices?.IsOpen == true ||
                _characterFilterTagsInput?.SuggestionsOpen == true) CloseCharacterDropdowns();
            else CloseCharacterFilter();
            key.Handled = true;
        };
        TimelineMenuPosition.OpenFitted(CharacterFilterButton, root, _characterFilter);
    }

    private void ApplyCharacterFilter()
    {
        if (_characterBookChoices is null) return;
        Vm.SetCharacterFilters(
            _characterFilterCategoryChecks.Where(check => check.IsChecked == true)
                .Select(check => (string)check.Tag),
            _characterBookChoices.SelectedIds,
            _characterFilterDraftTags);
        CloseCharacterFilter();
        RefreshCharacterFilter();
    }

    private void ClearCharacterFilters()
    {
        Vm.SetCharacterFilters([], [], []);
        CloseCharacterFilter();
        RefreshCharacterFilter();
    }

    private void CheckCharacterFilters(List<string> failures, string reportPath)
    {
        var checkVm = new MainViewModel(new TemplateService().CreateBlank());
        var firstBook = checkVm.SelectedBook!;
        checkVm.AddBook("Second filter book");
        var secondBook = checkVm.SelectedBook!;
        var both = new StoryEntity { Name = "Both filters", Category = "Main character" };
        both.BookIds.Add(firstBook.Id); both.BookIds.Add(secondBook.Id);
        both.Tags.Add("Red"); both.Tags.Add("Blue");
        var firstOnly = new StoryEntity { Name = "First only", Category = "Main character" };
        firstOnly.BookIds.Add(firstBook.Id); firstOnly.Tags.Add("Red");
        var antagonist = new StoryEntity { Name = "Antagonist", Category = "Antagonist" };
        antagonist.BookIds.Add(secondBook.Id); antagonist.Tags.Add("Blue");
        checkVm.Project.Characters.Add(both); checkVm.Project.Characters.Add(firstOnly);
        checkVm.Project.Characters.Add(antagonist);
        checkVm.IsDirty = false;
        checkVm.SetCharacterFilters(["Main character"], [firstBook.Id], ["Red"]);
        if (!checkVm.Project.Characters.Where(checkVm.MatchesCharacterFilter)
                .Select(character => character.Id).ToHashSet().SetEquals([both.Id, firstOnly.Id]))
            failures.Add("Character category, book, and tag filters did not combine correctly");
        checkVm.SetCharacterFilters(["Main character"], [firstBook.Id, secondBook.Id], ["Red", "Blue"]);
        if (!checkVm.Project.Characters.Where(checkVm.MatchesCharacterFilter)
                .Select(character => character.Id).ToHashSet().SetEquals([both.Id]))
            failures.Add("Multiple character Books and Tags filters did not require every selected value");
        checkVm.SetCharacterFilters(["Main character", "Antagonist"], [], []);
        if (checkVm.Project.Characters.Count(checkVm.MatchesCharacterFilter) != 3)
            failures.Add("Multiple character categories were not OR-matched");
        if (checkVm.IsDirty) failures.Add("Changing character filters marked story data as edited");

        Vm.SetCharacterFilters([], [], []);
        var book = Vm.Project.Books.OrderBy(item => item.Order).First();
        const string filterTag = "CharacterFilterTag";
        var match = Vm.AddEntity(Vm.Project.Characters, "Character");
        match.Name = "Character filter match";
        match.Category = "Main character";
        match.BookIds.Add(book.Id);
        match.Tags.Add(filterTag);
        var categoryMiss = Vm.AddEntity(Vm.Project.Characters, "Character");
        categoryMiss.Name = "Character filter category miss";
        categoryMiss.Category = "Antagonist";
        categoryMiss.BookIds.Add(book.Id);
        categoryMiss.Tags.Add(filterTag);
        var tagMiss = Vm.AddEntity(Vm.Project.Characters, "Character");
        tagMiss.Name = "Character filter tag miss";
        tagMiss.Category = "Main character";
        tagMiss.BookIds.Add(book.Id);
        TagService.Synchronize(Vm.Project);
        WorkspaceTabs.SelectedIndex = 4;
        RefreshAll();
        UpdateLayout();
        int unfilteredCount = CharacterList.Items.Count;
        var toolbar = (Grid)CharacterFilterButton.Parent;
        if (CharacterFilterButton.ActualWidth < 30 ||
            CharacterFilterButton.TranslatePoint(new Point(CharacterFilterButton.ActualWidth, 0), toolbar).X > toolbar.ActualWidth + 1)
            failures.Add("Character filter button is clipped or not placed after Delete");

        CharacterFilter_Click(this, new RoutedEventArgs());
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        UpdateLayout(); CharacterFilterSurface.UpdateLayout();
        var categoryCheck = _characterFilterCategoryChecks.FirstOrDefault(check =>
            Equals(check.Tag, "Main character"));
        if (categoryCheck is null || _characterBookChoices is null ||
            !_characterBookChoices.Choices.ContainsKey(book.Id) || _characterFilterTagsInput is null)
            failures.Add("Character filter does not expose Category, Books, and Tags controls");
        else
        {
            categoryCheck.IsChecked = true;
            _characterBookChoices.Choices[book.Id].IsChecked = true;
            _characterFilterDraftTags.Add(filterTag);
            if (Vm.HasCharacterFilters || CharacterList.Items.Count != unfilteredCount)
                failures.Add("Editing a character filter draft changed the list before Apply");
            SaveVisualPng(CharacterFilterSurface,
                Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-character-filter.png"));
            ApplyCharacterFilter();
            Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            UpdateLayout();
            if (!CharacterList.Items.Cast<StoryEntity>().Select(character => character.Id)
                    .ToHashSet().SetEquals([match.Id]) || !ReferenceEquals(Vm.SelectedCharacter, match))
                failures.Add("Applied character filters did not narrow the list and selection to the match");
        }

        CharacterFilter_Click(this, new RoutedEventArgs());
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var draftCategory = _characterFilterCategoryChecks.FirstOrDefault(check =>
            Equals(check.Tag, "Main character"));
        if (draftCategory is not null) draftCategory.IsChecked = false;
        CloseCharacterFilter();
        if (!Vm.CharacterCategoryFilter.SetEquals(["Main character"]) || CharacterList.Items.Count != 1)
            failures.Add("Canceling a character filter changed the active filters");

        ClearCharacterFilters();
        if (Vm.HasCharacterFilters || CharacterList.Items.Count != unfilteredCount)
            failures.Add("Clearing character filters did not restore every character");
        Vm.SetCharacterFilters(["__missing_character_category__"], [], []);
        RefreshCharacterFilter();
        UpdateLayout();
        if (CharacterList.Items.Count != 0 || Vm.SelectedCharacter is not null || CharacterEditor.IsEnabled)
            failures.Add("No-match character filter left an active character editor");
        ClearCharacterFilters();
    }
}
