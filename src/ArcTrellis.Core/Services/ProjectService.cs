using System.Text.Json;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public sealed class ProjectService
{
    public const string Extension = ".arctrellis";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task SaveAsync(StoryProject project, string path, bool createBackup = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        project.ModifiedUtc = DateTime.UtcNow;

        if (createBackup && File.Exists(fullPath)) CreateBackup(fullPath);

        string temporary = fullPath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await JsonSerializer.SerializeAsync(stream, project, JsonOptions);
            await stream.FlushAsync();
        }
        File.Move(temporary, fullPath, true);
    }

    public async Task<StoryProject> LoadAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var project = await JsonSerializer.DeserializeAsync<StoryProject>(stream, JsonOptions)
            ?? throw new InvalidDataException("The project file is empty or invalid.");
        Normalize(project);
        return project;
    }

    public StoryProject Clone(StoryProject project)
    {
        string json = JsonSerializer.Serialize(project, JsonOptions);
        var clone = JsonSerializer.Deserialize<StoryProject>(json, JsonOptions)!;
        Normalize(clone);
        return clone;
    }

    public string Serialize(StoryProject project) => JsonSerializer.Serialize(project, JsonOptions);

    public StoryProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<StoryProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Snapshot is invalid.");
        Normalize(project);
        return project;
    }

    public string GetAutosavePath(Guid projectId)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTrellis", "Autosave");
        Directory.CreateDirectory(root);
        return Path.Combine(root, projectId + Extension);
    }

    public IReadOnlyList<string> FindBackups(string projectPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        string backupDirectory = Path.Combine(directory!, "ArcTrellis Backups");
        if (!Directory.Exists(backupDirectory)) return [];
        string stem = Path.GetFileNameWithoutExtension(projectPath);
        return Directory.GetFiles(backupDirectory, stem + "-*.backup" + Extension)
            .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }

    private static void CreateBackup(string fullPath)
    {
        string directory = Path.Combine(Path.GetDirectoryName(fullPath)!, "ArcTrellis Backups");
        Directory.CreateDirectory(directory);
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string backup = Path.Combine(directory, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmssfff}.backup{Extension}");
        File.Copy(fullPath, backup, false);

        foreach (string old in Directory.GetFiles(directory, stem + "-*.backup" + Extension)
                     .OrderByDescending(File.GetLastWriteTimeUtc).Skip(20))
            File.Delete(old);
    }

    private static void Normalize(StoryProject p)
    {
        p.Books ??= [];
        p.Plotlines ??= [];
        p.Scenes ??= [];
        p.Characters ??= [];
        p.Places ??= [];
        p.Notes ??= [];
        p.Relationships ??= [];
        p.Tags ??= [];
        p.Categories ??= [];
        foreach (var b in p.Books) b.Chapters ??= [];
        var chapterOwners = p.Books
            .SelectMany(book => book.Chapters.Select(chapter => (chapter.Id, BookId: book.Id)))
            .GroupBy(x => x.Id)
            .ToDictionary(group => group.Key, group => group.First().BookId);
        var bookById = p.Books.GroupBy(book => book.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var s in p.Scenes)
        {
            if (chapterOwners.TryGetValue(s.ChapterId, out Guid ownerBookId)) s.BookId = ownerBookId;
            else
            {
                Book? fallbackBook = bookById.GetValueOrDefault(s.BookId) ?? p.Books.FirstOrDefault(book => book.Chapters.Count > 0);
                Chapter? fallbackChapter = fallbackBook?.Chapters.OrderBy(chapter => chapter.Order).FirstOrDefault();
                if (fallbackBook is not null && fallbackChapter is not null) { s.BookId = fallbackBook.Id; s.ChapterId = fallbackChapter.Id; }
            }
            s.Tags ??= [];
            s.CharacterIds ??= [];
            s.PlaceIds ??= [];
            s.Fields ??= [];
        }
        MigrateLegacyPlotlines(p, bookById.Keys.ToHashSet());
        foreach (Book book in p.Books)
        {
            if (p.Plotlines.Any(plotline => plotline.BookId == book.Id)) continue;
            p.Plotlines.Add(new Plotline { BookId = book.Id, Name = "Main Plot", Order = 0, Color = "#5B7CFA" });
        }
        var plotlineById = p.Plotlines.GroupBy(plotline => plotline.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (Scene scene in p.Scenes)
        {
            if (plotlineById.TryGetValue(scene.PlotlineId, out Plotline? plotline) && plotline.BookId == scene.BookId) continue;
            if (p.Plotlines.Where(candidate => candidate.BookId == scene.BookId).OrderBy(candidate => candidate.Order).FirstOrDefault() is { } fallbackPlotline)
                scene.PlotlineId = fallbackPlotline.Id;
        }
        foreach (var e in p.Characters.Concat(p.Places).Concat(p.Notes))
        {
            e.Tags ??= [];
            e.BookIds ??= [];
            e.Fields ??= [];
        }
        if (p.FormatVersion < 3)
        {
            // A series total cannot be assigned to individual books without guessing.
            // Preserve it in the legacy project field; use scene totals for multi-book projects.
            foreach (Book book in p.Books)
                book.CurrentWordCount = p.Books.Count == 1 ? p.CurrentWordCount
                    : (int)Math.Min(int.MaxValue, p.Scenes.Where(scene => scene.BookId == book.Id).Sum(scene => (long)scene.WordCount));
        }
        p.FormatVersion = 3;
    }

    private static void MigrateLegacyPlotlines(StoryProject project, HashSet<Guid> validBookIds)
    {
        var books = project.Books.OrderBy(book => book.Order).ToList();
        var legacyPlotlines = project.Plotlines.Where(plotline => !validBookIds.Contains(plotline.BookId)).ToList();
        if (books.Count == 0 || legacyPlotlines.Count == 0) return;

        var replacements = new Dictionary<(Guid BookId, Guid PlotlineId), Guid>();
        foreach (Plotline legacy in legacyPlotlines)
        {
            Guid legacyId = legacy.Id;
            for (int index = 0; index < books.Count; index++)
            {
                Book book = books[index];
                Plotline scoped;
                if (index == 0)
                {
                    legacy.BookId = book.Id;
                    scoped = legacy;
                }
                else
                {
                    scoped = new Plotline
                    {
                        BookId = book.Id,
                        Name = legacy.Name,
                        Description = legacy.Description,
                        Color = legacy.Color,
                        Order = legacy.Order
                    };
                    project.Plotlines.Add(scoped);
                }
                replacements[(book.Id, legacyId)] = scoped.Id;
            }
        }

        foreach (Scene scene in project.Scenes)
            if (replacements.TryGetValue((scene.BookId, scene.PlotlineId), out Guid replacementId))
                scene.PlotlineId = replacementId;
    }
}
