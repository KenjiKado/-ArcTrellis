using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArcTrellis.App.Views;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public sealed record CharacterRelationRow(StoryEntity Other, Relationship Relation);

public partial class MainWindow
{
    private StoryEntity? _shownCharacterBooksOwner;
    private MultiChoiceInput? _characterBooksInput;
    private List<(Guid Id, string Title)> _shownCharacterBooks = [];
    private object?[]? _characterRelationState;

    private void RefreshCharacterOverview()
    {
        if (CharacterEditor is null) return;
        Vm.RefreshCharacterCategories();
        var character = Vm.SelectedCharacter is { } selected && Vm.Project.Characters.Contains(selected) ? selected : null;
        var relations = new List<CharacterRelationRow>();
        if (character is not null)
        {
            foreach (var relation in Vm.Project.Relationships)
            {
                if (relation.FromEntityId != character.Id && relation.ToEntityId != character.Id) continue;
                Guid otherId = relation.FromEntityId == character.Id ? relation.ToEntityId : relation.FromEntityId;
                var other = Vm.Project.Characters.Concat(Vm.Project.Places).FirstOrDefault(entity => entity.Id == otherId);
                if (other is not null) relations.Add(new CharacterRelationRow(other, relation));
            }
        }
        var relationState = new List<object?> { Vm.Project, character };
        relationState.AddRange(relations.Select(row => (object?)(row.Relation, row.Other, row.Relation.FromEntityId, row.Relation.ToEntityId)));
        if (ViewChanged(ref _characterRelationState, relationState))
        {
            CharacterRelationsTable.ItemsSource = relations;
            CharacterRelationsTable.SelectedItem = null;
            DeleteCharacterRelationButton.IsEnabled = false;
        }

        RefreshCharacterBooks(character);
    }

    private static IEnumerable<string> SplitRelationTypes(string value) =>
        value.Split([',', ';', '•', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void AddCharacterRelation_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedCharacter is not { } source) return;
        var targets = Vm.Project.Characters.Where(character => character.Id != source.Id).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(Loc.T("Add another character before creating a relation."),
                Loc.T("Add relation"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existingTypes = Vm.Project.Relationships.SelectMany(relation => SplitRelationTypes(relation.Type))
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
        var dialog = new CharacterRelationWindow(source, targets, existingTypes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.TargetCharacter is not { } target) return;
        var relation = Vm.AddCharacterRelationship(source, target, dialog.RelationTypes, dialog.RelationDescription);
        if (relation is null) return;
        RefreshCharacterOverview();
        if (CharacterRelationsTable.ItemsSource is IEnumerable<CharacterRelationRow> rows &&
            rows.FirstOrDefault(row => ReferenceEquals(row.Relation, relation)) is { } selected)
        {
            CharacterRelationsTable.SelectedItem = selected;
            CharacterRelationsTable.ScrollIntoView(selected);
        }
    }

    private void DeleteCharacterRelation_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterRelationsTable.SelectedItem is not CharacterRelationRow row ||
            !ConfirmDelete("relation")) return;
        Vm.DeleteRelationship(row.Relation);
        RefreshCharacterOverview();
    }

    private void CharacterRelationsTable_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteCharacterRelationButton.IsEnabled =
            CharacterRelationsTable.SelectedItem is CharacterRelationRow;

    private void RefreshCharacterBooks(StoryEntity? character)
    {
        if (character is null)
        {
            _characterBooksInput?.CloseDropdown();
            _characterBooksInput = null;
            _shownCharacterBooksOwner = null;
            CharacterBooksHost.Content = null;
            return;
        }

        var books = Vm.Project.Books.Select(book => (book.Id, book.Title)).ToList();
        if (ReferenceEquals(_shownCharacterBooksOwner, character) &&
            _characterBooksInput is { } current &&
            _shownCharacterBooks.SequenceEqual(books) &&
            current.SelectedIds.ToHashSet().SetEquals(character.BookIds)) return;

        _characterBooksInput?.CloseDropdown();
        var input = new MultiChoiceInput("In Books", books, character.BookIds, includeAll: true);
        input.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(Vm.SelectedCharacter, character))
                Vm.SetCharacterBooks(character, input.SelectedIds);
        };
        _characterBooksInput = input;
        _shownCharacterBooksOwner = character;
        _shownCharacterBooks = books;
        CharacterBooksHost.Content = input;
    }

    private void CheckCharactersTab(List<string> failures, string reportPath)
    {
        var legacyProject = new TemplateService().CreateBlank();
        legacyProject.Characters.Add(new StoryEntity { Name = "Legacy", Category = "General" });
        var legacyVm = new MainViewModel(legacyProject);
        if (legacyVm.Project.Characters[0].Category != "Main character" ||
            legacyVm.CharacterCategories.Any(option => string.Equals(option.Code, "General", StringComparison.OrdinalIgnoreCase)))
            failures.Add("Legacy General character category was not removed and migrated");

        var checkVm = new MainViewModel(new TemplateService().CreateBlank());
        var firstBook = checkVm.SelectedBook!;
        checkVm.AddBook();
        var secondBook = checkVm.SelectedBook!;
        var person = checkVm.AddEntity(checkVm.Project.Characters, "Character");
        if (person.Category != "Main character") failures.Add("New character has no selectable category");
        string[] categoryLabelKeys =
        [
            "Main", "Deuteragonist", "Tritagonist", "Antagonist",
            "Supporting", "Secondary", "Recurring", "Guest / episodic",
            "Minor", "Cameo", "Background", "Off-scene"
        ];
        if (!checkVm.CharacterCategories.Take(categoryLabelKeys.Length).Select(option => option.Label)
            .SequenceEqual(categoryLabelKeys.Select(Loc.T)))
            failures.Add("Character category labels do not use the consistent role style");
        checkVm.SetCharacterBooks(person, [firstBook.Id, secondBook.Id]);
        if (!person.BookIds.ToHashSet().SetEquals([firstBook.Id, secondBook.Id]))
            failures.Add("Character books did not retain multiple selections");
        checkVm.Undo();
        if (person.BookIds.Count != 0) failures.Add("Undo did not remove selected character books");
        checkVm.Redo();
        if (!person.BookIds.ToHashSet().SetEquals([firstBook.Id, secondBook.Id]))
            failures.Add("Redo did not restore selected character books");

        var allBooksInput = new MultiChoiceInput("In Books",
            checkVm.Project.Books.Select(book => (book.Id, book.Title)), [], includeAll: true);
        int allSelectionEvents = 0;
        allBooksInput.SelectionChanged += (_, _) => allSelectionEvents++;
        if (!allBooksInput.Choices.TryGetValue(Guid.Empty, out var allBooksChoice))
            failures.Add("Character books multiselect does not offer All");
        else
        {
            if (allBooksInput.Choices.Keys.FirstOrDefault() != Guid.Empty)
                failures.Add("Character books multiselect does not place All at the top");
            allBooksChoice.IsChecked = true;
            var allBookIds = checkVm.Project.Books.Select(book => book.Id).ToHashSet();
            if (!allBooksInput.SelectedIds.ToHashSet().SetEquals(allBookIds) ||
                !allBookIds.All(id => allBooksInput.Choices[id].IsChecked == true))
                failures.Add("All did not check every existing character book");
            if (allSelectionEvents != 1)
                failures.Add("Selecting All raised more than one character books change");
        }

        var relationTarget = checkVm.AddEntity(checkVm.Project.Characters, "Character");
        relationTarget.Name = "Relation target";
        var createdRelation = checkVm.AddCharacterRelationship(person, relationTarget,
            ["Friend", "Rival", "Friend"], "Complicated history");
        if (createdRelation is null || createdRelation.Type != "Friend, Rival" ||
            createdRelation.Description != "Complicated history")
            failures.Add("Character relationship creation lost selected types or description");
        checkVm.DeleteRelationship(createdRelation);
        if (createdRelation is not null && checkVm.Project.Relationships.Contains(createdRelation))
            failures.Add("Character relationship deletion did not remove the selected relation");

        var relationDialog = new CharacterRelationWindow(person, [relationTarget], ["Custom bond"]) { Owner = this };
        relationDialog.Show();
        relationDialog.UpdateLayout();
        relationDialog.SelectTargetForTest(relationTarget);
        relationDialog.AddTypeForTest("Friend");
        relationDialog.AddTypeForTest("Custom bond");
        relationDialog.DescriptionInput.Text = "Dialog description";
        relationDialog.UpdateLayout();
        if (!relationDialog.SaveButton.IsEnabled ||
            !relationDialog.RelationTypes.ToHashSet(StringComparer.CurrentCultureIgnoreCase)
                .SetEquals(["Friend", "Custom bond"]) ||
            relationDialog.TargetInput.Input.SelectionLength != 0)
            failures.Add("Add relation popup did not retain its target, multiple types, or custom type");
        SaveVisualPng(relationDialog, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-add-relation.png"));
        relationDialog.Close();

        WorkspaceTabs.SelectedIndex = 4;
        var existing = Vm.Project.Characters.FirstOrDefault();
        if (existing is null) existing = Vm.AddEntity(Vm.Project.Characters, "Character");
        Vm.SelectedCharacter = null;
        RefreshCharacterOverview();
        UpdateLayout();
        if (CharacterEditor.IsEnabled) failures.Add("Empty character selection left the editor active");
        Vm.SelectedCharacter = existing;
        RefreshAll();
        UpdateLayout();
        if (!CharacterEditor.IsEnabled || !Equals(CharacterCategorySelector.SelectedValue, existing.Category))
            failures.Add($"Character editor or category selection did not follow the selected character (enabled={CharacterEditor.IsEnabled}, model={existing.Category}, selected={CharacterCategorySelector.SelectedValue}, options={string.Join(",", Vm.CharacterCategories.Select(option => option.Code))})");

        if (_characterBooksInput is null || !Vm.Project.Books.All(book => _characterBooksInput.Choices.ContainsKey(book.Id)))
            failures.Add("Character books multiselect does not offer every project book");

        var other = Vm.Project.Characters.FirstOrDefault(entity => entity.Id != existing.Id);
        if (other is not null)
        {
            var relation = new Relationship { FromEntityId = existing.Id, ToEntityId = other.Id, Type = "Friend", Description = "Trusted" };
            Vm.Project.Relationships.Add(relation);
            RefreshCharacterOverview();
            if (CharacterRelationsTable.ItemsSource is not IEnumerable<CharacterRelationRow> rows ||
                !rows.Any(row => ReferenceEquals(row.Relation, relation) && row.Other.Name == other.Name))
                failures.Add("Character relations table does not show the related name, type, and description");
            else
            {
                CharacterRelationsTable.SelectedItem = rows.First(row => ReferenceEquals(row.Relation, relation));
                UpdateLayout();
                if (!DeleteCharacterRelationButton.IsEnabled)
                    failures.Add("Character relation Delete button did not follow the selected row");
            }
            if (!AddCharacterRelationButton.IsEnabled)
                failures.Add("Character relation Add button is inactive for a selected character");
            UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
            Vm.Project.Relationships.Remove(relation);
            RefreshCharacterOverview();
        }
        else SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
    }
}
