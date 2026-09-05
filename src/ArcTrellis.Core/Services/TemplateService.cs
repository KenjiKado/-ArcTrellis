using System.Text.Json;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public sealed record TemplateInfo(string Name, string Description, string Category, string FilePath);

public sealed class TemplateService
{
    private readonly string _templateDirectory;
    private readonly string _userTemplateDirectory;
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public TemplateService(string? templateDirectory = null)
    {
        _templateDirectory = templateDirectory ?? Path.Combine(AppContext.BaseDirectory, "Templates");
        _userTemplateDirectory = templateDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTrellis", "Templates")
            : templateDirectory;
    }

    public string UserTemplateDirectory { get { Directory.CreateDirectory(_userTemplateDirectory); return _userTemplateDirectory; } }

    public IReadOnlyList<TemplateInfo> ListTemplates()
    {
        var result = new List<TemplateInfo>();
        var directories = new[] { _templateDirectory, _userTemplateDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists);
        foreach (string path in directories.SelectMany(d => Directory.GetFiles(d, "*.json")).OrderBy(Path.GetFileName))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(path);
                string description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                string category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "General" : "General";
                result.Add(new TemplateInfo(name, description, category, path));
            }
            catch (JsonException) { }
        }
        return result;
    }

    public void SaveTemplate(string path, string name, string description, StoryProject project)
    {
        var wrapper = new { name, description, category = "My Templates", project };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(wrapper, Options));
    }

    public StoryProject CreateFromTemplate(TemplateInfo template)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(template.FilePath));
        var projectElement = doc.RootElement.GetProperty("project");
        var project = new ProjectService().Deserialize(projectElement.GetRawText());
        RegenerateIds(project);
        project.CreatedUtc = project.ModifiedUtc = DateTime.UtcNow;
        return project;
    }

    public StoryProject CreateBlank()
    {
        var book = new Book { Title = "Book One", Order = 0 };
        var chapter = new Chapter { Title = "Chapter 1", Order = 0 };
        book.Chapters.Add(chapter);
        var plotline = new Plotline { BookId = book.Id, Name = "Main Plot", Order = 0, Color = "#5B7CFA" };
        return new StoryProject
        {
            Title = "Untitled Series",
            Books = [book],
            Plotlines = [plotline]
        };
    }

    private static void RegenerateIds(StoryProject project)
    {
        project.Id = Guid.NewGuid();
        var bookMap = project.Books.ToDictionary(x => x.Id, _ => Guid.NewGuid());
        var chapterMap = project.Books.SelectMany(x => x.Chapters).ToDictionary(x => x.Id, _ => Guid.NewGuid());
        var plotMap = project.Plotlines.ToDictionary(x => x.Id, _ => Guid.NewGuid());
        var entityMap = project.Characters.Concat(project.Places).Concat(project.Notes).ToDictionary(x => x.Id, _ => Guid.NewGuid());
        foreach (var b in project.Books)
        {
            b.Id = bookMap[b.Id];
            foreach (var ch in b.Chapters) ch.Id = chapterMap[ch.Id];
        }
        foreach (var p in project.Plotlines)
        {
            p.BookId = bookMap[p.BookId];
            p.Id = plotMap[p.Id];
        }
        foreach (var e in project.Characters.Concat(project.Places).Concat(project.Notes))
        {
            var old = e.Id;
            e.Id = entityMap[old];
            e.BookIds = new(e.BookIds.Where(bookMap.ContainsKey).Select(x => bookMap[x]));
        }
        foreach (var s in project.Scenes)
        {
            s.Id = Guid.NewGuid();
            if (bookMap.TryGetValue(s.BookId, out var b)) s.BookId = b;
            if (chapterMap.TryGetValue(s.ChapterId, out var c)) s.ChapterId = c;
            if (plotMap.TryGetValue(s.PlotlineId, out var p)) s.PlotlineId = p;
            s.CharacterIds = new(s.CharacterIds.Where(entityMap.ContainsKey).Select(x => entityMap[x]));
            s.PlaceIds = new(s.PlaceIds.Where(entityMap.ContainsKey).Select(x => entityMap[x]));
        }
        foreach (var r in project.Relationships)
        {
            r.Id = Guid.NewGuid();
            if (entityMap.TryGetValue(r.FromEntityId, out var from)) r.FromEntityId = from;
            if (entityMap.TryGetValue(r.ToEntityId, out var to)) r.ToEntityId = to;
        }
    }
}
