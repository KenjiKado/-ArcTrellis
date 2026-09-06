using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ProjectService _projects = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private StoryProject _project;
    private Book? _selectedBook;
    private Chapter? _selectedChapter;
    private Plotline? _selectedPlotline;
    private Scene? _selectedScene;
    private StoryEntity? _selectedCharacter;
    private StoryEntity? _selectedPlace;
    private StoryEntity? _selectedNote;
    private Relationship? _selectedRelationship;
    private string _searchText = "";
    private string? _filePath;
    private bool _isDirty;
    private bool _restoringHistory;
    private string _status = Loc.T("Ready");

    public MainViewModel(StoryProject project)
    {
        _project = project;
        SelectDefaults();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProjectReplaced;
    public StoryProject Project { get => _project; private set { _project = value; Raise(); Raise(nameof(WindowTitle)); ProjectReplaced?.Invoke(this, EventArgs.Empty); } }
    public Book? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (_restoringHistory || !Set(ref _selectedBook, value)) return;
            SelectedChapter = value?.Chapters.OrderBy(x => x.Order).FirstOrDefault();
            Raise(nameof(BookPlotlines));
            SelectedPlotline = value is null ? null : Project.Plotlines.Where(plotline => plotline.BookId == value.Id).OrderBy(plotline => plotline.Order).FirstOrDefault();
            SelectedScene = value is null ? null : Project.Scenes.Where(s => s.BookId == value.Id).OrderBy(s => s.Order).FirstOrDefault();
            Raise(nameof(BookScenes));
        }
    }
    public Chapter? SelectedChapter { get => _selectedChapter; set { if (!_restoringHistory && Set(ref _selectedChapter, value)) Raise(nameof(ChapterScenes)); } }
    public Plotline? SelectedPlotline { get => _selectedPlotline; set { if (!_restoringHistory) Set(ref _selectedPlotline, value); } }
    public Scene? SelectedScene { get => _selectedScene; set { if (!_restoringHistory) Set(ref _selectedScene, value); } }
    public StoryEntity? SelectedCharacter { get => _selectedCharacter; set { if (!_restoringHistory) Set(ref _selectedCharacter, value); } }
    public StoryEntity? SelectedPlace { get => _selectedPlace; set { if (!_restoringHistory) Set(ref _selectedPlace, value); } }
    public StoryEntity? SelectedNote { get => _selectedNote; set { if (!_restoringHistory) Set(ref _selectedNote, value); } }
    public Relationship? SelectedRelationship { get => _selectedRelationship; set { if (!_restoringHistory) Set(ref _selectedRelationship, value); } }
    public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
    public string? FilePath { get => _filePath; set { if (Set(ref _filePath, value)) Raise(nameof(WindowTitle)); } }
    public bool IsDirty { get => _isDirty; set { if (Set(ref _isDirty, value)) Raise(nameof(WindowTitle)); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string WindowTitle => $"{Project.Title}{(IsDirty ? " *" : "")} — ArcTrellis";
    public IEnumerable<Plotline> BookPlotlines => SelectedBook is null ? [] : Project.Plotlines.Where(plotline => plotline.BookId == SelectedBook.Id).OrderBy(plotline => plotline.Order);
    public IEnumerable<Scene> BookScenes => SelectedBook is null ? [] : Project.Scenes.Where(s => s.BookId == SelectedBook.Id).OrderBy(s => s.Order);
    public IEnumerable<Scene> ChapterScenes => SelectedChapter is null ? [] : Project.Scenes.Where(s => s.ChapterId == SelectedChapter.Id).OrderBy(s => s.Order);
    public ObservableCollection<SearchResult> SearchResults { get; } = [];
    public IReadOnlyList<SceneStatusOption> SceneStatuses { get; } = [new("Planned", Loc.T("Planned")), new("Drafted", Loc.T("Drafted")), new("Revised", Loc.T("Revised")), new("Final", Loc.T("Final")), new("Cut", Loc.T("Cut"))];
    public void RefreshLocalization() { foreach (var option in SceneStatuses) option.RefreshLocalization(); Raise(nameof(BookPlotlines)); Raise(nameof(BookScenes)); Raise(nameof(ChapterScenes)); Status = Loc.T("Ready"); }

    public void ReplaceProject(StoryProject project, string? path = null)
    {
        Project = project;
        FilePath = path;
        _undo.Clear(); _redo.Clear();
        SelectDefaults();
        IsDirty = false;
    }

    public void AddBook(string? title = null, string subtitle = "")
    {
        Snapshot();
        var book = new Book { Title = title ?? Loc.F("Book {0}", Project.Books.Count + 1), Subtitle = subtitle, Order = Project.Books.Count };
        book.Chapters.Add(new Chapter { Title = Loc.F("Chapter {0}", 1), Section = Loc.T("Act I") });
        var plotline = new Plotline { BookId = book.Id, Name = Loc.T("Main Plot"), Order = 0, Color = "#5B7CFA" };
        Project.Books.Add(book); Project.Plotlines.Add(plotline); SelectedBook = book; Dirty("Book added");
    }

    public void EditPlotline(Plotline plotline, string title, string description, string color)
    {
        if (!Project.Plotlines.Contains(plotline)) return;
        if (plotline.Name == title && plotline.Description == description && plotline.Color == color) return;
        Snapshot();
        plotline.Name = title;
        plotline.Description = description;
        plotline.Color = color;
        Dirty("Plotline updated");
    }

    public void EditBook(Book book, string title, string subtitle)
    {
        if (!Project.Books.Contains(book)) return;
        Snapshot();
        book.Title = title;
        book.Subtitle = subtitle;
        Dirty("Book updated");
    }

    public void DeleteBook()
    {
        if (SelectedBook is null || Project.Books.Count <= 1) return;
        Snapshot();
        var ids = SelectedBook.Chapters.Select(c => c.Id).ToHashSet();
        foreach (var scene in Project.Scenes.Where(s => ids.Contains(s.ChapterId)).ToList()) Project.Scenes.Remove(scene);
        foreach (var plotline in Project.Plotlines.Where(plotline => plotline.BookId == SelectedBook.Id).ToList()) Project.Plotlines.Remove(plotline);
        Project.Books.Remove(SelectedBook); Renumber(Project.Books);
        SelectedBook = Project.Books.OrderBy(x => x.Order).FirstOrDefault(); Dirty("Book deleted");
    }

    public void AddChapter()
    {
        if (SelectedBook is null) return;
        Snapshot();
        var chapter = new Chapter { Title = Loc.F("Chapter {0}", SelectedBook.Chapters.Count + 1), Section = Loc.T("Act I"), Order = SelectedBook.Chapters.Count };
        SelectedBook.Chapters.Add(chapter); SelectedChapter = chapter; Dirty("Chapter added");
    }

    public void DeleteChapter()
    {
        if (SelectedBook is null || SelectedChapter is null || SelectedBook.Chapters.Count <= 1) return;
        Snapshot();
        foreach (var scene in Project.Scenes.Where(s => s.ChapterId == SelectedChapter.Id).ToList()) Project.Scenes.Remove(scene);
        SelectedBook.Chapters.Remove(SelectedChapter); Renumber(SelectedBook.Chapters);
        SelectedChapter = SelectedBook.Chapters.OrderBy(x => x.Order).FirstOrDefault(); Dirty("Chapter deleted");
    }

    public void MoveChapter(int direction)
    {
        if (SelectedBook is null || SelectedChapter is null) return;
        var list = SelectedBook.Chapters.OrderBy(x => x.Order).ToList();
        int old = list.IndexOf(SelectedChapter), next = old + direction;
        if (next < 0 || next >= list.Count) return;
        Snapshot(); (list[old].Order, list[next].Order) = (list[next].Order, list[old].Order); Dirty("Chapter reordered");
    }

    public void AddPlotline()
    {
        if (SelectedBook is null) return;
        Snapshot();
        string[] colors = ["#5B7CFA", "#D9577A", "#2E9D78", "#E39B35", "#8A63D2", "#3B9AB2"];
        var plotlines = BookPlotlines.ToList();
        var plot = new Plotline { BookId = SelectedBook.Id, Name = Loc.F("Plotline {0}", plotlines.Count + 1), Order = plotlines.Count, Color = colors[plotlines.Count % colors.Length] };
        Project.Plotlines.Add(plot); Raise(nameof(BookPlotlines)); SelectedPlotline = plot; Dirty("Plotline added");
    }

    public void DeletePlotline()
    {
        var plotlines = BookPlotlines.ToList();
        if (SelectedPlotline is null || plotlines.Count <= 1 || !plotlines.Contains(SelectedPlotline)) return;
        Snapshot();
        var fallback = plotlines.First(x => x != SelectedPlotline);
        foreach (var scene in Project.Scenes.Where(s => s.PlotlineId == SelectedPlotline.Id)) scene.PlotlineId = fallback.Id;
        Project.Plotlines.Remove(SelectedPlotline); Renumber(plotlines.Where(plotline => plotline != SelectedPlotline)); Raise(nameof(BookPlotlines)); SelectedPlotline = fallback; Dirty("Plotline deleted");
    }

    public void AddScene(Guid? chapterId = null, Guid? plotlineId = null)
    {
        if (SelectedBook is null || (SelectedChapter is null && chapterId is null)) return;
        Chapter? chapter = SelectedBook.Chapters.FirstOrDefault(x => x.Id == (chapterId ?? SelectedChapter!.Id));
        var plotlines = BookPlotlines.ToList();
        Plotline? plotline = plotlineId.HasValue
            ? plotlines.FirstOrDefault(candidate => candidate.Id == plotlineId.Value)
            : (SelectedPlotline is not null && SelectedPlotline.BookId == SelectedBook.Id ? SelectedPlotline : plotlines.FirstOrDefault());
        if (chapter is null || plotline is null) return;
        Snapshot();
        var scene = new Scene { Title = Loc.F("Scene {0}", Project.Scenes.Count + 1), BookId = SelectedBook.Id, ChapterId = chapter.Id,
            PlotlineId = plotline.Id,
            Order = Project.Scenes.Count };
        Project.Scenes.Add(scene); SelectedScene = scene; Raise(nameof(BookScenes)); Raise(nameof(ChapterScenes)); Dirty("Scene added");
    }

    public Scene? DuplicateScene(Scene original)
    {
        if (!Project.Scenes.Contains(original)) return null;
        var copy = System.Text.Json.JsonSerializer.Deserialize<Scene>(System.Text.Json.JsonSerializer.Serialize(original))!;
        copy.Id = Guid.NewGuid();
        copy.Title = Loc.F("{0} (duplicate)", original.Title);
        Snapshot();
        var ordered = Project.Scenes.OrderBy(scene => scene.Order).ToList();
        ordered.Insert(ordered.IndexOf(original) + 1, copy);
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        Project.Scenes.Add(copy);
        SelectedScene = copy;
        Raise(nameof(BookScenes)); Raise(nameof(ChapterScenes)); Dirty("Scene duplicated");
        return copy;
    }

    public void DeleteScene()
    {
        if (SelectedScene is null) return;
        Snapshot(); Project.Scenes.Remove(SelectedScene); SelectedScene = BookScenes.FirstOrDefault();
        Raise(nameof(BookScenes)); Raise(nameof(ChapterScenes)); Dirty("Scene deleted");
    }

    public void MoveScene(Scene scene, Guid chapterId, Guid plotlineId, int? insertionIndex = null)
    {
        var targetBook = Project.Books.FirstOrDefault(book => book.Chapters.Any(chapter => chapter.Id == chapterId));
        if (!Project.Scenes.Contains(scene) || targetBook is null || Project.Plotlines.All(plotline => plotline.Id != plotlineId || plotline.BookId != targetBook.Id)) return;
        var previous = Project.Scenes.OrderBy(item => item.Order).ToList();
        var ordered = previous.Where(item => item != scene).ToList();
        var peers = ordered.Where(item => item.BookId == targetBook.Id && item.ChapterId == chapterId && item.PlotlineId == plotlineId).ToList();
        int slot = Math.Clamp(insertionIndex ?? peers.Count, 0, peers.Count);
        int index = slot < peers.Count ? ordered.IndexOf(peers[slot])
            : peers.Count > 0 ? ordered.IndexOf(peers[^1]) + 1 : ordered.Count;
        ordered.Insert(index, scene);
        if (scene.BookId == targetBook.Id && scene.ChapterId == chapterId && scene.PlotlineId == plotlineId && previous.SequenceEqual(ordered)) return;
        Snapshot();
        scene.BookId = targetBook.Id; scene.ChapterId = chapterId; scene.PlotlineId = plotlineId;
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        Raise(nameof(BookScenes)); Raise(nameof(ChapterScenes)); Dirty("Scene moved");
    }

    public StoryEntity AddEntity(ObservableCollection<StoryEntity> collection, string kind)
    {
        Snapshot();
        var item = new StoryEntity { Name = Loc.T($"New {kind}"), Category = Loc.T(kind == "Note" ? "Research" : "General") };
        collection.Add(item); Dirty(kind + " added"); return item;
    }

    public void DeleteEntity(ObservableCollection<StoryEntity> collection, StoryEntity? entity)
    {
        if (entity is null) return;
        Snapshot(); collection.Remove(entity);
        foreach (var relation in Project.Relationships.Where(r => r.FromEntityId == entity.Id || r.ToEntityId == entity.Id).ToList()) Project.Relationships.Remove(relation);
        Dirty("Item deleted");
    }

    public void AddRelationship()
    {
        var entities = Project.Characters.Concat(Project.Places).ToList();
        if (entities.Count < 2) return;
        Snapshot();
        var relation = new Relationship { FromEntityId = entities[0].Id, ToEntityId = entities[1].Id, Type = Loc.T("Related to") };
        Project.Relationships.Add(relation); SelectedRelationship = relation; Dirty("Relationship added");
    }

    public void RunSearch()
    {
        SearchResults.Clear();
        foreach (var item in SearchService.Search(Project, SearchText)) SearchResults.Add(new SearchResult(Loc.T(item.Kind), item.Title, item.Context, item.ItemId));
        Status = Loc.F("{0} result(s)", SearchResults.Count);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_projects.Serialize(Project)); RestoreHistory(_undo.Pop()); Dirty("Undid last structural change");
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(_projects.Serialize(Project)); RestoreHistory(_redo.Pop()); Dirty("Redid change");
    }

    private void RestoreHistory(string json)
    {
        var bookId = _selectedBook?.Id; var chapterId = _selectedChapter?.Id;
        var plotlineId = _selectedPlotline?.Id; var sceneId = _selectedScene?.Id;
        var characterId = _selectedCharacter?.Id; var placeId = _selectedPlace?.Id;
        var noteId = _selectedNote?.Id; var relationshipId = _selectedRelationship?.Id;
        int bookIndex = _selectedBook is null ? 0 : Project.Books.IndexOf(_selectedBook);
        // Rebind selections to the restored objects before notifying the UI.
        _project = _projects.Deserialize(json);
        _selectedBook = Project.Books.FirstOrDefault(b => b.Id == bookId)
            ?? Project.Books.ElementAtOrDefault(Math.Clamp(bookIndex, 0, Math.Max(0, Project.Books.Count - 1)));
        _selectedChapter = _selectedBook?.Chapters.FirstOrDefault(c => c.Id == chapterId) ?? _selectedBook?.Chapters.OrderBy(c => c.Order).FirstOrDefault();
        _selectedPlotline = BookPlotlines.FirstOrDefault(p => p.Id == plotlineId) ?? BookPlotlines.FirstOrDefault();
        _selectedScene = BookScenes.FirstOrDefault(s => s.Id == sceneId) ?? BookScenes.FirstOrDefault();
        _selectedCharacter = Project.Characters.FirstOrDefault(c => c.Id == characterId) ?? Project.Characters.FirstOrDefault();
        _selectedPlace = Project.Places.FirstOrDefault(p => p.Id == placeId) ?? Project.Places.FirstOrDefault();
        _selectedNote = Project.Notes.FirstOrDefault(n => n.Id == noteId) ?? Project.Notes.FirstOrDefault();
        _selectedRelationship = Project.Relationships.FirstOrDefault(r => r.Id == relationshipId);
        _restoringHistory = true;
        try { RaiseAll(); Raise(nameof(SelectedRelationship)); }
        finally { _restoringHistory = false; }
    }

    public void MarkDirty() { IsDirty = true; }
    private void Snapshot() { _undo.Push(_projects.Serialize(Project)); while (_undo.Count > 50) TrimBottom(_undo); _redo.Clear(); }
    private void Dirty(string message) { IsDirty = true; Status = Loc.T(message); Project.ModifiedUtc = DateTime.UtcNow; ProjectReplaced?.Invoke(this, EventArgs.Empty); }
    private void SelectDefaults()
    {
        _selectedBook = Project.Books.OrderBy(x => x.Order).FirstOrDefault();
        _selectedChapter = _selectedBook?.Chapters.OrderBy(x => x.Order).FirstOrDefault();
        _selectedPlotline = _selectedBook is null ? null : Project.Plotlines.Where(plotline => plotline.BookId == _selectedBook.Id).OrderBy(plotline => plotline.Order).FirstOrDefault();
        _selectedScene = BookScenes.FirstOrDefault();
        _selectedCharacter = Project.Characters.FirstOrDefault();
        _selectedPlace = Project.Places.FirstOrDefault();
        _selectedNote = Project.Notes.FirstOrDefault();
        RaiseAll();
    }
    private static void Renumber<T>(IEnumerable<T> items) where T : ObservableObject
    {
        int i = 0;
        foreach (var item in items)
        {
            if (item is Book b) b.Order = i++;
            else if (item is Chapter c) c.Order = i++;
            else if (item is Plotline p) p.Order = i++;
        }
    }
    private static void TrimBottom(Stack<string> stack)
    {
        var values = stack.Reverse().Skip(1).ToArray(); stack.Clear(); foreach (var value in values) stack.Push(value);
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void RaiseAll()
    {
        foreach (string name in new[] { nameof(Project), nameof(SelectedBook), nameof(SelectedChapter), nameof(SelectedPlotline), nameof(SelectedScene), nameof(SelectedCharacter), nameof(SelectedPlace), nameof(SelectedNote), nameof(BookPlotlines), nameof(BookScenes), nameof(ChapterScenes), nameof(WindowTitle) }) Raise(name);
    }
}
