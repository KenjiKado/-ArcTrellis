using System.IO;
using System.Windows.Controls;
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
        var character = Vm.SelectedCharacter is { } selected && Vm.Project.Characters.Contains(selected) ? selected : null;
        var scenes = character is null ? new List<Scene>() : Vm.Project.Scenes.Where(scene => scene.CharacterIds.Contains(character.Id)).ToList();
        CharacterSceneCount.Text = Loc.F("In Scenes ({0})", scenes.Count);
        CharacterChapterCount.Text = Loc.F("In Chapters ({0})", scenes.Select(scene => scene.ChapterId).Distinct().Count());

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
            CharacterRelationsTable.ItemsSource = relations;

        RefreshCharacterBooks(character);
    }

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
        var input = new MultiChoiceInput("Books", books, character.BookIds);
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
        var checkVm = new MainViewModel(new TemplateService().CreateBlank());
        var firstBook = checkVm.SelectedBook!;
        checkVm.AddBook();
        var secondBook = checkVm.SelectedBook!;
        var person = checkVm.AddEntity(checkVm.Project.Characters, "Character");
        if (person.Category != "Main character") failures.Add("New character has no selectable category");
        checkVm.SetCharacterBooks(person, [firstBook.Id, secondBook.Id]);
        if (!person.BookIds.ToHashSet().SetEquals([firstBook.Id, secondBook.Id]))
            failures.Add("Character books did not retain multiple selections");
        checkVm.Undo();
        if (person.BookIds.Count != 0) failures.Add("Undo did not remove selected character books");
        checkVm.Redo();
        if (!person.BookIds.ToHashSet().SetEquals([firstBook.Id, secondBook.Id]))
            failures.Add("Redo did not restore selected character books");

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
            failures.Add("Character editor or category selection did not follow the selected character");

        var expectedScenes = Vm.Project.Scenes.Where(scene => scene.CharacterIds.Contains(existing.Id)).ToList();
        if (CharacterSceneCount.Text != Loc.F("In Scenes ({0})", expectedScenes.Count) ||
            CharacterChapterCount.Text != Loc.F("In Chapters ({0})", expectedScenes.Select(scene => scene.ChapterId).Distinct().Count()))
            failures.Add("Character scene and chapter counts are incorrect");
        if (_characterBooksInput is null || !Vm.Project.Books.All(book => _characterBooksInput.Choices.ContainsKey(book.Id)))
            failures.Add("Character books multiselect does not offer every project book");

        var other = Vm.Project.Characters.Concat(Vm.Project.Places).FirstOrDefault(entity => entity.Id != existing.Id);
        if (other is not null)
        {
            var relation = new Relationship { FromEntityId = existing.Id, ToEntityId = other.Id, Type = "Friend", Description = "Trusted" };
            Vm.Project.Relationships.Add(relation);
            RefreshCharacterOverview();
            if (CharacterRelationsTable.ItemsSource is not IEnumerable<CharacterRelationRow> rows ||
                !rows.Any(row => ReferenceEquals(row.Relation, relation) && row.Other.Name == other.Name))
                failures.Add("Character relations table does not show the related name, type, and description");
            UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
            Vm.Project.Relationships.Remove(relation);
            RefreshCharacterOverview();
        }
        else SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
    }
}
