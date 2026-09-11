using System.Collections.ObjectModel;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public static class TagService
{
    public static IEnumerable<ObservableCollection<string>> Collections(StoryProject project) =>
        project.Books.SelectMany(b => b.Chapters).Select(c => c.Tags)
            .Concat(project.Scenes.Select(s => s.Tags))
            .Concat(project.Characters.Concat(project.Places).Concat(project.Notes).Select(e => e.Tags));

    public static IEnumerable<string> Existing(StoryProject project) => Collections(project)
        .SelectMany(tags => tags).Select(tag => tag.Trim()).Where(tag => tag.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase);

    public static IReadOnlyList<string> Suggest(StoryProject project, IEnumerable<string> selected, string input)
    {
        string prefix = input.Trim();
        if (prefix.Length < 3) return [];
        var present = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Existing(project).Where(tag => !present.Contains(tag) && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public static void Synchronize(StoryProject project)
    {
        var used = Existing(project).ToList();
        if (project.Tags.SequenceEqual(used)) return;
        project.Tags.Clear(); foreach (string tag in used) project.Tags.Add(tag);
    }
}
