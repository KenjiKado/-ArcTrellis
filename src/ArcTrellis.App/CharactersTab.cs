using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private StoryEntity? _shownCharacterGalleryOwner;
    private List<string> _shownCharacterGalleryPaths = [];
    private CharacterGalleryPreview? _characterGalleryPreview;
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
                if (relation.FromEntityId != character.Id) continue;
                Guid otherId = relation.ToEntityId;
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
        RefreshCharacterGallery(character);
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
        var relation = Vm.AddCharacterRelationship(source, target, [dialog.RelationType], dialog.RelationDescription);
        if (relation is null) return;
        RefreshCharacterOverview();
        SelectCharacterRelation(relation);
    }

    private void CharacterRelationsTable_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject sourceVisual ||
            ItemsControl.ContainerFromElement(CharacterRelationsTable, sourceVisual) is not DataGridRow ||
            CharacterRelationsTable.SelectedItem is not CharacterRelationRow row ||
            Vm.SelectedCharacter is not { } source) return;

        var targets = Vm.Project.Characters.Where(character => character.Id != source.Id).ToList();
        var existingTypes = Vm.Project.Relationships.SelectMany(relation => SplitRelationTypes(relation.Type))
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
        var dialog = new CharacterRelationWindow(source, targets, existingTypes, row.Relation) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.TargetCharacter is not { } target) return;
        if (!Vm.UpdateCharacterRelationship(row.Relation, source, target,
                dialog.RelationType, dialog.RelationDescription)) return;
        RefreshCharacterOverview();
        SelectCharacterRelation(row.Relation);
        e.Handled = true;
    }

    private void SelectCharacterRelation(Relationship relation)
    {
        if (CharacterRelationsTable.ItemsSource is not IEnumerable<CharacterRelationRow> rows ||
            rows.FirstOrDefault(row => ReferenceEquals(row.Relation, relation)) is not { } selected) return;
        CharacterRelationsTable.SelectedItem = selected;
        CharacterRelationsTable.ScrollIntoView(selected);
    }

    private void DeleteCharacterRelation_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterRelationsTable.SelectedItem is not CharacterRelationRow row ||
            !ConfirmDelete("relation")) return;
        Vm.DeleteRelationship(row.Relation);
        RefreshCharacterOverview();
    }

    private void CharacterRelationDescriptionHeader_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGridColumnHeader header) HideOuterRelationHeaderGrip(header);
    }

    private void HideOuterRelationHeaderGrip(DataGridColumnHeader header)
    {
        header.ApplyTemplate();
        header.UpdateLayout();
        var outerGrip = FindVisualChildren<Thumb>(header)
            .OrderByDescending(grip => grip.TranslatePoint(new Point(), header).X)
            .FirstOrDefault();
        if (outerGrip is null) return;
        header.Tag = outerGrip;
        outerGrip.Visibility = Visibility.Collapsed;
        outerGrip.IsHitTestVisible = false;
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

    private void RefreshCharacterGallery(StoryEntity? character)
    {
        if (character is null)
        {
            _shownCharacterGalleryOwner = null;
            _shownCharacterGalleryPaths = [];
            _characterGalleryPreview = null;
            CharacterGalleryHost.Content = null;
            return;
        }

        var paths = character.GalleryImages.ToList();
        if (ReferenceEquals(_shownCharacterGalleryOwner, character) &&
            _shownCharacterGalleryPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase) &&
            _characterGalleryPreview is not null) return;

        var preview = new CharacterGalleryPreview(paths);
        preview.OpenRequested += (_, _) => OpenCharacterGallery(character);
        _shownCharacterGalleryOwner = character;
        _shownCharacterGalleryPaths = paths;
        _characterGalleryPreview = preview;
        CharacterGalleryHost.Content = preview;
    }

    private void OpenCharacterGallery(StoryEntity character)
    {
        if (!Vm.Project.Characters.Contains(character)) return;
        var dialog = new CharacterGalleryWindow(character.Name, character.GalleryImages) { Owner = this };
        dialog.ShowDialog();
        Vm.SetCharacterGallery(character, dialog.ImagePaths);
        RefreshCharacterGallery(character);
    }

    private void CharacterGalleryHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedCharacter is not { } character) return;
        OpenCharacterGallery(character);
        e.Handled = true;
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

        string galleryDirectory = Path.Combine(Path.GetDirectoryName(reportPath)!, "gallery-smoke");
        Directory.CreateDirectory(galleryDirectory);
        var galleryPaths = new List<string>();
        for (int index = 0; index < 6; index++)
        {
            string imagePath = Path.Combine(galleryDirectory, $"portrait-{index + 1}.png");
            int stride = 64 * 4;
            var pixels = new byte[stride * 64];
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                pixels[pixel] = (byte)(60 + index * 20);
                pixels[pixel + 1] = (byte)(90 + index * 12);
                pixels[pixel + 2] = (byte)(170 - index * 12);
                pixels[pixel + 3] = 255;
            }
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(imagePath)) encoder.Save(stream);
            galleryPaths.Add(imagePath);
        }
        checkVm.SetCharacterGallery(person, galleryPaths);
        if (!person.GalleryImages.SequenceEqual(galleryPaths) || person.ImagePath != galleryPaths[^1])
            failures.Add("Character gallery did not retain its ordered image paths");
        checkVm.Undo();
        if (person.GalleryImages.Count != 0) failures.Add("Undo did not remove character gallery images");
        checkVm.Redo();
        if (!person.GalleryImages.SequenceEqual(galleryPaths)) failures.Add("Redo did not restore character gallery images");
        var compactGallery = new CharacterGalleryPreview(person.GalleryImages);
        if (compactGallery.PreviewCount != 6 || compactGallery.HiddenImageCount != 1 ||
            compactGallery.BadgeText != "+1" || compactGallery.ImagePreviewToolTipCount != 0 ||
            !compactGallery.DisplayedPaths.SequenceEqual(galleryPaths.AsEnumerable().Reverse().Take(5)))
            failures.Add("Character gallery preview order, hidden count, or tooltip behavior is incorrect");
        var shortGallery = new CharacterGalleryPreview(galleryPaths.Take(3));
        if (shortGallery.PreviewCount != 4 || shortGallery.BadgeText != "+" ||
            !shortGallery.DisplayedPaths.SequenceEqual(galleryPaths.Take(3).Reverse()))
            failures.Add("Character gallery preview does not end a short gallery with the add button");
        var elevenPaths = galleryPaths.Concat(Enumerable.Range(7, 5)
            .Select(index => Path.Combine(galleryDirectory, $"portrait-{index}.png"))).ToList();
        var overflowGallery = new CharacterGalleryPreview(elevenPaths);
        if (overflowGallery.HiddenImageCount != 6 || overflowGallery.BadgeText != "+6" ||
            CharacterGalleryPreview.FormatHiddenCount(4_000) != "+4k" ||
            CharacterGalleryPreview.FormatHiddenCount(4_500) != "+4.5k")
            failures.Add("Character gallery overflow badge did not format its hidden-image count");

        var galleryDialog = new CharacterGalleryWindow(person.Name, person.GalleryImages) { Owner = this };
        galleryDialog.Show();
        galleryDialog.UpdateLayout();
        if (!galleryDialog.DisplayedGridPaths.SequenceEqual(galleryPaths.AsEnumerable().Reverse()))
            failures.Add("Gallery grid does not show the newest image first");
        if (galleryDialog.GalleryRightGap > 6)
            failures.Add($"Gallery grid leaves {galleryDialog.GalleryRightGap:0.##} pixels unused on the right");
        string droppedImage = Path.Combine(galleryDirectory, "dropped.png");
        File.Copy(galleryPaths[0], droppedImage, true);
        if (galleryDialog.AddFilesForTest([droppedImage]) != 1 || galleryDialog.GridItemCount != 7 ||
            galleryDialog.DisplayedGridPaths.FirstOrDefault() != droppedImage)
            failures.Add("Gallery did not add a dropped Windows image to its grid");
        galleryDialog.SelectForTest(droppedImage);
        galleryDialog.DeleteSelectedForTest();
        galleryDialog.UpdateLayout();
        if (galleryDialog.ImagePaths.Contains(droppedImage, StringComparer.OrdinalIgnoreCase) ||
            !File.Exists(droppedImage) || galleryDialog.GridItemCount != 6 ||
            galleryDialog.RenderedGridItemCount != 6)
            failures.Add("Gallery Delete removed the source file or retained its gallery reference");
        SaveVisualPng(galleryDialog,
            Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-character-gallery.png"));
        galleryDialog.Close();

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
            ["Friend"], "Complicated history");
        if (createdRelation is null || createdRelation.Type != "Friend" ||
            createdRelation.Description != "Complicated history")
            failures.Add("Character relationship creation lost selected type or description");
        else if (!checkVm.UpdateCharacterRelationship(createdRelation, person, relationTarget,
                     "Rival", "Now competing") ||
                 createdRelation.Type != "Rival" || createdRelation.Description != "Now competing")
            failures.Add("Character relationship editing did not update the selected relation");
        if (createdRelation is not null &&
            checkVm.UpdateCharacterRelationship(createdRelation, relationTarget, person,
                "Reverse relation", "Should not be accepted"))
            failures.Add("A target character was allowed to edit a source-only relation");
        checkVm.DeleteRelationship(createdRelation);
        if (createdRelation is not null && checkVm.Project.Relationships.Contains(createdRelation))
            failures.Add("Character relationship deletion did not remove the selected relation");

        var relationDialog = new CharacterRelationWindow(person, [relationTarget], ["Custom bond"]) { Owner = this };
        relationDialog.Show();
        relationDialog.UpdateLayout();
        if (relationDialog.TargetInput.IsOpen)
            failures.Add("Target character autocomplete opened when the relation popup opened");
        relationDialog.TargetInput.ToggleFieldForTest();
        relationDialog.UpdateLayout();
        if (!relationDialog.TargetInput.IsOpen)
            failures.Add("The first target character field click did not open its dropdown");
        relationDialog.TargetInput.ToggleFieldForTest();
        relationDialog.UpdateLayout();
        if (relationDialog.TargetInput.IsOpen)
            failures.Add("The second target character field click did not close its dropdown");
        string[] writeoGroups =
            ["FAMILY", "ROMANTIC", "SOCIAL", "HIERARCHY", "CONFLICT", "ALLIANCE", "SUPERNATURAL", "PROFESSIONAL"];
        if (relationDialog.RelationTypesInput.Choices.Count < CharacterRelationWindow.DefaultRelationTypes.Count ||
            !relationDialog.RelationTypesInput.Choices.Contains("Friend", StringComparer.CurrentCultureIgnoreCase) ||
            !relationDialog.RelationTypesInput.Groups.Take(writeoGroups.Length).SequenceEqual(writeoGroups))
            failures.Add("Relation type field does not expose the ordered Writeo-style grouped select options");
        if (!relationDialog.RelationTypesInput.HasSeparatedGroupHeaders ||
            relationDialog.RelationTypesInput.ColoredDotCount != relationDialog.RelationTypesInput.Choices.Count)
            failures.Add("Relation type groups or colored option dots are not visually distinct");
        relationDialog.SelectTargetForTest(relationTarget);
        relationDialog.AddTypeForTest("Friend");
        relationDialog.AddTypeForTest("Custom bond");
        relationDialog.DescriptionInput.Text = "Dialog description";
        relationDialog.UpdateLayout();
        if (!relationDialog.SaveButton.IsEnabled ||
            relationDialog.RelationType != "Custom bond" ||
            relationDialog.RelationTypesInput.SelectedValues.Count != 1 ||
            relationDialog.TargetInput.Input.SelectionLength != 0)
            failures.Add("Add relation popup did not retain exactly one relation type or its custom value");
        SaveVisualPng(relationDialog, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-add-relation.png"));
        relationDialog.RelationTypesInput.OpenForTest();
        relationDialog.UpdateLayout();
        SaveVisualPng(relationDialog.RelationTypesInput.DropdownSurface,
            Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-relation-types.png"));
        relationDialog.RelationTypesInput.CloseDropdown();
        relationDialog.Close();

        var editableRelation = new Relationship
        {
            FromEntityId = person.Id,
            ToEntityId = relationTarget.Id,
            Type = "Friend",
            Description = "Existing description"
        };
        var editDialog = new CharacterRelationWindow(person, [relationTarget], ["Friend"], editableRelation)
            { Owner = this };
        editDialog.Show();
        editDialog.UpdateLayout();
        if (!editDialog.IsEditing || editDialog.Title != Loc.T("Edit relation") ||
            editDialog.SaveButton.Content?.ToString() != Loc.T("Save") ||
            !ReferenceEquals(editDialog.TargetCharacter, relationTarget) ||
            editDialog.RelationType != "Friend" ||
            editDialog.RelationDescription != "Existing description" ||
            editDialog.TargetInput.IsOpen)
            failures.Add("Edit relation popup was not pre-populated or did not use Save");
        SaveVisualPng(editDialog,
            Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-edit-relation.png"));
        editDialog.Close();

        WorkspaceTabs.SelectedIndex = 4;
        var existing = Vm.Project.Characters.FirstOrDefault();
        if (existing is null) existing = Vm.AddEntity(Vm.Project.Characters, "Character");
        Vm.SelectedCharacter = null;
        RefreshCharacterOverview();
        UpdateLayout();
        if (CharacterEditor.IsEnabled) failures.Add("Empty character selection left the editor active");
        Vm.SelectedCharacter = existing;
        Vm.SetCharacterGallery(existing, galleryPaths);
        RefreshAll();
        UpdateLayout();
        if (!CharacterEditor.IsEnabled || !Equals(CharacterCategorySelector.SelectedValue, existing.Category))
            failures.Add($"Character editor or category selection did not follow the selected character (enabled={CharacterEditor.IsEnabled}, model={existing.Category}, selected={CharacterCategorySelector.SelectedValue}, options={string.Join(",", Vm.CharacterCategories.Select(option => option.Code))})");

        if (_characterBooksInput is null || !Vm.Project.Books.All(book => _characterBooksInput.Choices.ContainsKey(book.Id)))
            failures.Add("Character books multiselect does not offer every project book");
        if (CharacterGalleryHost.Content is not CharacterGalleryPreview shownGallery ||
            shownGallery.PreviewCount != 6 || shownGallery.BadgeText != "+1" ||
            !shownGallery.DisplayedPaths.SequenceEqual(galleryPaths.AsEnumerable().Reverse().Take(5)))
            failures.Add("Selected character did not show newest-first circular gallery previews and the count badge");
        if (CharacterRelationsTable.RowHeight != 44 ||
            CharacterRelationsTable.RowStyle is null || CharacterRelationsTable.CellStyle is null)
            failures.Add("Character relations table does not match the Chapters scene table style");
        var descriptionHeader = FindVisualChildren<DataGridColumnHeader>(CharacterRelationsTable)
            .FirstOrDefault(header => ReferenceEquals(header.Column, CharacterRelationsTable.Columns[2]));
        if (descriptionHeader is not null) HideOuterRelationHeaderGrip(descriptionHeader);
        var outerDescriptionGrip = descriptionHeader?.Tag as Thumb;
        var innerDescriptionGrip = descriptionHeader is null ? null :
            FindVisualChildren<Thumb>(descriptionHeader).FirstOrDefault(grip =>
                !ReferenceEquals(grip, outerDescriptionGrip) &&
                grip.IsHitTestVisible && grip.Visibility == Visibility.Visible);
        if (!CharacterRelationsTable.CanUserResizeColumns || CharacterRelationsTable.CanUserResizeRows ||
            CharacterRelationsTable.CanUserReorderColumns ||
            CharacterRelationsTable.Columns.Count != 3 ||
            CharacterRelationsTable.Columns.Any(column => !column.CanUserResize) ||
            innerDescriptionGrip is null ||
            outerDescriptionGrip is null || outerDescriptionGrip.IsHitTestVisible ||
            outerDescriptionGrip.Visibility != Visibility.Collapsed ||
            CharacterRelationsTable.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
            failures.Add("The Description divider is not resizable or the table outline still has an active resize grip");

        var other = Vm.Project.Characters.FirstOrDefault(entity => entity.Id != existing.Id);
        if (other is not null)
        {
            var relation = new Relationship { FromEntityId = existing.Id, ToEntityId = other.Id, Type = "Friend", Description = "Trusted confidant with a long shared history that must stay inside the Description column without creating horizontal scrolling." };
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

            Vm.SelectedCharacter = other;
            RefreshCharacterOverview();
            UpdateLayout();
            if (CharacterRelationsTable.ItemsSource is IEnumerable<CharacterRelationRow> targetRows &&
                targetRows.Any(row => ReferenceEquals(row.Relation, relation)))
                failures.Add("A source character relation was also displayed for its target character");
            Vm.SelectedCharacter = existing;
            RefreshCharacterOverview();
            UpdateLayout();
            if (CharacterRelationsTable.ItemsSource is IEnumerable<CharacterRelationRow> sourceRows &&
                sourceRows.FirstOrDefault(row => ReferenceEquals(row.Relation, relation)) is { } sourceRow)
                CharacterRelationsTable.SelectedItem = sourceRow;

            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
            CharacterRelationsTable.BringIntoView();
            UpdateLayout();
            SaveVisualPng(this,
                Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-character-relations.png"));
            Vm.Project.Relationships.Remove(relation);
            RefreshCharacterOverview();
        }
        else SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-characters-tab.png"));
    }
}
