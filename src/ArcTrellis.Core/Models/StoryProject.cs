using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ArcTrellis.Core.Models;

public sealed class StoryProject : ObservableObject
{
    private string _title = "Untitled Series";
    private string _author = "";
    private string _description = "";
    private string _genre = "";
    private int _wordCountGoal = 80000;
    private int _currentWordCount;
    private DateTime _modifiedUtc = DateTime.UtcNow;

    public int FormatVersion { get; set; } = 2;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Author { get => _author; set => Set(ref _author, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string Genre { get => _genre; set => Set(ref _genre, value); }
    public int WordCountGoal { get => _wordCountGoal; set => Set(ref _wordCountGoal, Math.Max(0, value)); }
    public int CurrentWordCount { get => _currentWordCount; set => Set(ref _currentWordCount, Math.Max(0, value)); }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get => _modifiedUtc; set => Set(ref _modifiedUtc, value); }
    public ObservableCollection<Book> Books { get; set; } = [];
    public ObservableCollection<Plotline> Plotlines { get; set; } = [];
    public ObservableCollection<Scene> Scenes { get; set; } = [];
    public ObservableCollection<StoryEntity> Characters { get; set; } = [];
    public ObservableCollection<StoryEntity> Places { get; set; } = [];
    public ObservableCollection<StoryEntity> Notes { get; set; } = [];
    public ObservableCollection<Relationship> Relationships { get; set; } = [];
    public ObservableCollection<string> Tags { get; set; } = [];
    public ObservableCollection<string> Categories { get; set; } = ["Research", "Worldbuilding", "Continuity", "Editing"];
}

public sealed class Book : ObservableObject
{
    private string _title = "New Book";
    private string _subtitle = "";
    private string _summary = "";
    private int _order;
    private int _wordCountGoal = 80000;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    public int Order { get => _order; set => Set(ref _order, value); }
    public int WordCountGoal { get => _wordCountGoal; set => Set(ref _wordCountGoal, Math.Max(0, value)); }
    public ObservableCollection<Chapter> Chapters { get; set; } = [];
}

public sealed class Chapter : ObservableObject
{
    private string _title = "New Chapter";
    private string _summary = "";
    private int _order;
    private string _section = "Act I";
    private int _wordCount;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    public int Order { get => _order; set => Set(ref _order, value); }
    public string Section { get => _section; set => Set(ref _section, value); }
    public int WordCount { get => _wordCount; set => Set(ref _wordCount, Math.Max(0, value)); }
}

public sealed class Plotline : ObservableObject
{
    private string _name = "Main Plot";
    private string _description = "";
    private string _color = "#5B7CFA";
    private int _order;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public int Order { get => _order; set => Set(ref _order, value); }
}

public sealed class Scene : ObservableObject
{
    private string _title = "New Scene";
    private string _summary = "";
    private string _content = "";
    private string _status = "Planned";
    private string _pov = "";
    private string _setting = "";
    private string _editingNotes = "";
    private int _order;
    private int _wordCount;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid PlotlineId { get; set; }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    public string Content { get => _content; set => Set(ref _content, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string PointOfView { get => _pov; set => Set(ref _pov, value); }
    public string Setting { get => _setting; set => Set(ref _setting, value); }
    public string EditingNotes { get => _editingNotes; set => Set(ref _editingNotes, value); }
    public int Order { get => _order; set => Set(ref _order, value); }
    public int WordCount { get => _wordCount; set => Set(ref _wordCount, Math.Max(0, value)); }
    public ObservableCollection<string> Tags { get; set; } = [];
    [JsonIgnore]
    public string TagsText
    {
        get => string.Join(", ", Tags);
        set { Tags.Clear(); foreach (string tag in SplitTags(value)) Tags.Add(tag); Raise(); }
    }
    public ObservableCollection<Guid> CharacterIds { get; set; } = [];
    public ObservableCollection<Guid> PlaceIds { get; set; } = [];
    public ObservableCollection<CustomField> Fields { get; set; } = [];
    private static IEnumerable<string> SplitTags(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase);
}

public sealed class StoryEntity : ObservableObject
{
    private string _name = "New Item";
    private string _category = "General";
    private string _summary = "";
    private string _description = "";
    private string _imagePath = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Category { get => _category; set => Set(ref _category, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string ImagePath { get => _imagePath; set => Set(ref _imagePath, value); }
    public ObservableCollection<string> Tags { get; set; } = [];
    [JsonIgnore]
    public string TagsText
    {
        get => string.Join(", ", Tags);
        set { Tags.Clear(); foreach (string tag in SplitTags(value)) Tags.Add(tag); Raise(); }
    }
    public ObservableCollection<Guid> BookIds { get; set; } = [];
    public ObservableCollection<CustomField> Fields { get; set; } = [];
    private static IEnumerable<string> SplitTags(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase);
}

public sealed class CustomField : ObservableObject
{
    private string _name = "Field";
    private string _value = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
}

public sealed class Relationship : ObservableObject
{
    private string _type = "Related to";
    private string _description = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    public string Type { get => _type; set => Set(ref _type, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
}

public sealed record SearchResult(string Kind, string Title, string Context, Guid ItemId);
