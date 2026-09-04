using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public static class SearchService
{
    public static IReadOnlyList<SearchResult> Search(StoryProject project, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        bool Has(string? text) => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
        var results = new List<SearchResult>();

        foreach (var book in project.Books)
        {
            if (Has(book.Title) || Has(book.Summary)) results.Add(new("Book", book.Title, book.Summary, book.Id));
            foreach (var chapter in book.Chapters.Where(c => Has(c.Title) || Has(c.Summary)))
                results.Add(new("Chapter", chapter.Title, chapter.Summary, chapter.Id));
        }
        foreach (var scene in project.Scenes.Where(s => Has(s.Title) || Has(s.Summary) || Has(s.Content) || Has(s.EditingNotes) || s.Tags.Any(Has)))
            results.Add(new("Scene", scene.Title, Snippet(scene.Summary, scene.Content), scene.Id));
        foreach (var (kind, items) in new[] { ("Character", project.Characters), ("Place", project.Places), ("Note", project.Notes) })
            foreach (var item in items.Where(e => Has(e.Name) || Has(e.Summary) || Has(e.Description) || e.Tags.Any(Has) || e.Fields.Any(f => Has(f.Name) || Has(f.Value))))
                results.Add(new(kind, item.Name, Snippet(item.Summary, item.Description), item.Id));
        return results.Take(250).ToList();
    }

    private static string Snippet(params string[] values)
    {
        string text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ReplaceLineEndings(" ").Trim();
        return text.Length <= 180 ? text : text[..177] + "...";
    }
}
