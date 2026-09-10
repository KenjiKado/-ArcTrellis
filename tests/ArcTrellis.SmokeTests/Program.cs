using System.IO.Compression;
using System.Text.Json;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

var failures = new List<string>();
void Check(bool condition, string message)
{
    if (condition) Console.WriteLine("PASS  " + message);
    else { Console.WriteLine("FAIL  " + message); failures.Add(message); }
}

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string templatePath = Path.Combine(root, "templates");
var templates = new TemplateService(templatePath);
var projectService = new ProjectService();
var exports = new ExportService();
var list = templates.ListTemplates();
Check(list.Count >= 5, "at least five templates are discoverable");

foreach (var template in list)
{
    var project = templates.CreateFromTemplate(template);
    Check(project.Books.Count > 0, $"{template.Name}: contains a book");
    Check(project.Plotlines.Count > 0, $"{template.Name}: contains a plotline");
    Check(project.Books.SelectMany(x => x.Chapters).Any(), $"{template.Name}: contains a chapter");
    var books = project.Books.Select(x => x.Id).ToHashSet();
    var chapters = project.Books.SelectMany(x => x.Chapters).Select(x => x.Id).ToHashSet();
    var plots = project.Plotlines.ToDictionary(x => x.Id);
    Check(project.Plotlines.All(x => books.Contains(x.BookId)) && project.Books.All(book => project.Plotlines.Any(plotline => plotline.BookId == book.Id)), $"{template.Name}: every plotline belongs to a book");
    Check(project.Scenes.All(x => books.Contains(x.BookId) && chapters.Contains(x.ChapterId) && plots.TryGetValue(x.PlotlineId, out Plotline? plotline) && plotline.BookId == x.BookId), $"{template.Name}: scene references are valid and book-scoped");
}

var example = templates.CreateFromTemplate(list.First(x => x.Name.Contains("Glass Horizon")));
string serialized = projectService.Serialize(example);
var roundTrip = projectService.Deserialize(serialized);
Check(roundTrip.Title == example.Title && roundTrip.Scenes.Count == example.Scenes.Count, "project JSON round-trip preserves content");
Check(SearchService.Search(roundTrip, "compass").Count >= 2, "full-project search finds matching story data");

var series = templates.CreateFromTemplate(list.First(x => x.Name.Contains("Three-book series")));
var firstSeriesBook = series.Books.OrderBy(x => x.Order).First();
var secondSeriesBook = series.Books.OrderBy(x => x.Order).Skip(1).First();
var repairedScene = new Scene
{
    Title = "Reference repair",
    BookId = firstSeriesBook.Id,
    ChapterId = secondSeriesBook.Chapters.First().Id,
    PlotlineId = series.Plotlines.First(plotline => plotline.BookId == secondSeriesBook.Id).Id
};
series.Scenes.Add(repairedScene);
var repairedSeries = projectService.Deserialize(projectService.Serialize(series));
Check(repairedSeries.Scenes.Count == 1 && repairedSeries.Scenes[0].BookId == secondSeriesBook.Id, "scene book ownership is repaired from its chapter without data loss");

var legacyFirstBook = new Book { Title = "Legacy One", Order = 0 };
legacyFirstBook.Chapters.Add(new Chapter { Title = "One", Order = 0 });
var legacySecondBook = new Book { Title = "Legacy Two", Order = 1 };
legacySecondBook.Chapters.Add(new Chapter { Title = "Two", Order = 0 });
var sharedLegacyPlotline = new Plotline { Name = "Shared before migration", Order = 0 };
var legacyProject = new StoryProject
{
    FormatVersion = 1,
    Books = [legacyFirstBook, legacySecondBook],
    Plotlines = [sharedLegacyPlotline],
    Scenes =
    [
        new Scene { Title = "First legacy scene", BookId = legacyFirstBook.Id, ChapterId = legacyFirstBook.Chapters[0].Id, PlotlineId = sharedLegacyPlotline.Id },
        new Scene { Title = "Second legacy scene", BookId = legacySecondBook.Id, ChapterId = legacySecondBook.Chapters[0].Id, PlotlineId = sharedLegacyPlotline.Id }
    ]
};
var migratedLegacy = projectService.Deserialize(projectService.Serialize(legacyProject));
Check(migratedLegacy.FormatVersion == 4 && migratedLegacy.Plotlines.Count == 2, "legacy shared plotlines migrate to one independent copy per book");
Check(migratedLegacy.Scenes.All(scene => migratedLegacy.Plotlines.Any(plotline => plotline.Id == scene.PlotlineId && plotline.BookId == scene.BookId)), "legacy scenes retain their plotline in the correct book");
var migratedFirstPlotline = migratedLegacy.Plotlines.Single(plotline => plotline.BookId == migratedLegacy.Books[0].Id);
var migratedSecondPlotline = migratedLegacy.Plotlines.Single(plotline => plotline.BookId == migratedLegacy.Books[1].Id);
migratedSecondPlotline.Name = "Second book only";
Check(migratedFirstPlotline.Name == "Shared before migration", "renaming a migrated plotline does not rename another book's copy");

var progressProject = templates.CreateBlank();
progressProject.FormatVersion = 2;
progressProject.CurrentWordCount = 1234;
var progressMigrated = projectService.Deserialize(projectService.Serialize(progressProject));
Check(progressMigrated.Books[0].CurrentWordCount == 1234, "legacy single-book word total is retained");
progressMigrated.Books.Add(new Book { CurrentWordCount = 500, WordCountGoal = 2000 });
var progressReloaded = projectService.Deserialize(projectService.Serialize(progressMigrated));
Check(progressReloaded.Books[0].CurrentWordCount == 1234 && progressReloaded.Books[1].CurrentWordCount == 500 && progressReloaded.Books[1].WordCountGoal == 2000, "independent book progress survives save and reopen");

var legacySubtitle = templates.CreateBlank();
legacySubtitle.FormatVersion = 3;
legacySubtitle.Books[0].Summary = "Establish the central promise";
var subtitleMigrated = projectService.Deserialize(projectService.Serialize(legacySubtitle));
Check(subtitleMigrated.Books[0].Subtitle == "Establish the central promise", "legacy displayed book description populates subtitle");
Check(subtitleMigrated.Books[0].Summary == "Establish the central promise", "subtitle migration preserves original summary");
subtitleMigrated.Books[0].Subtitle = "";
Check(projectService.Deserialize(projectService.Serialize(subtitleMigrated)).Books[0].Subtitle == "", "clearing a migrated subtitle persists");

string temp = Path.Combine(Path.GetTempPath(), "ArcTrellis-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string projectFile = Path.Combine(temp, "example.arctrellis");
    await projectService.SaveAsync(roundTrip, projectFile);
    var loaded = await projectService.LoadAsync(projectFile);
    Check(loaded.Id == roundTrip.Id, "project saves atomically and reloads");

    string docx = Path.Combine(temp, "example.docx");
    string md = Path.Combine(temp, "example.md");
    string csv = Path.Combine(temp, "example.csv");
    exports.ExportDocx(loaded, docx); exports.ExportMarkdown(loaded, md); exports.ExportCsv(loaded, csv); exports.ExportScrivenerFolder(loaded, temp);
    using var wordArchive = ZipFile.OpenRead(docx);
    Check(wordArchive.GetEntry("word/document.xml") is not null, "Word export is a valid Open XML package");
    Check(File.ReadAllText(md).Contains("The needle turns"), "Markdown export contains scene data");
    Check(File.ReadAllLines(csv).Length > 2, "CSV timeline export contains rows");
    Check(Directory.GetDirectories(temp, "*.scriv").Length == 1, "Scrivener project folder is generated");

    string import = Path.Combine(temp, "import.md");
    await File.WriteAllTextAsync(import, "# Imported Story\n## Book One\n### Chapter One\n#### Arrival\nScene text.");
    var imported = new ImportService().ImportMarkdown(import);
    Check(imported.Scenes.Count == 1 && imported.Scenes[0].Content.Contains("Scene text"), "Markdown outline imports into a project");
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}


var typing = new TextEditHistory("");
var clock = DateTime.UtcNow;
string typed = "";
foreach (char c in "First second")
{
    var before = new TextEditHistory.State(typed, typed.Length);
    typed += c; typing.Record(before, new(typed, typed.Length), clock);
    clock = clock.AddMilliseconds(50);
}
Check(typing.Undo()?.Text == "First ", "text undo removes the last word, not the entire entry");
Check(typing.Undo()?.Text == "First", "text undo keeps a separate whitespace unit");
Check(typing.Redo()?.Text == "First " && typing.Redo()?.Text == "First second", "text redo restores typing groups");
typing.Record(new(typed, typed.Length), new(typed + "X", typed.Length + 1), clock.AddSeconds(2));
Check(typing.Undo()?.Text == typed, "typing pauses end an undo group");
typing.Record(new(typed, 0), new("X" + typed, 1), clock.AddSeconds(3));
Check(typing.Undo()?.Text == typed, "moving the caret starts a separate group");
typing.Record(new(typed, 0, typed.Length), new("Pasted replacement", 18), clock.AddSeconds(4), atomic: true);
Check(typing.Undo() is { Text: "First second", Start: 0, Length: 12 }, "replacement undo restores text and selection");
Check(typing.Redo()?.Text == "Pasted replacement", "paste redo restores one paste operation");
var deletion = new TextEditHistory("First second");
string deleted = "First second";
for (int i = 0; i < 6; i++)
{
    string next = deleted[..^1]; deletion.Record(new(deleted, deleted.Length), new(next, next.Length), clock);
    deleted = next; clock = clock.AddMilliseconds(50);
}
Check(deletion.Undo()?.Text == "First second", "consecutive backspaces undo as a word");
var scoped = new ScopedHistory();
string h0 = "{\"Chapters\":[{\"Id\":\"one\",\"Title\":\"One\",\"Status\":\"Planned\"},{\"Id\":\"two\",\"Title\":\"Two\"}]}";
string h1 = h0.Replace("One", "Edited one"), h2 = h1.Replace("Two", "Edited two");
scoped.Record("chapters/one", h0, h1); scoped.Record("chapters/two", h1, h2);
string h3 = scoped.Apply("chapters/one", h2, false)!;
Check(h3.Contains("Edited two") && !h3.Contains("Edited one"), "chapter one undo preserves chapter two edits");
Check(scoped.Apply("timeline/one", h3, false) is null, "empty tab history never falls back to another tab");
string h4 = h3.Replace("Planned", "Drafted"); scoped.Record("scenes/one", h3, h4);
string h5 = scoped.Apply("chapters/one", h4, true)!;
Check(h5.Contains("Edited one") && h5.Contains("Drafted") && h5.Contains("Edited two"), "redo survives unrelated changes and preserves unrelated fields");
Check(scoped.Apply("chapters/one", h5.Replace("Edited one", "Later external edit"), false) is null, "undo never overwrites conflicting changes");
var membership = new ScopedHistory();
string emptyMembers = "{\"Scenes\":[]}";
string firstMember = "{\"Scenes\":[{\"Id\":\"a\",\"Title\":\"A\"}]}";
string bothMembers = "{\"Scenes\":[{\"Id\":\"a\",\"Title\":\"A\"},{\"Id\":\"b\",\"Title\":\"B\"}]}";
membership.Record("one", emptyMembers, firstMember);
string withoutFirst = membership.Apply("one", bothMembers, false)!;
Check(!withoutFirst.Contains("\"A\"") && withoutFirst.Contains("\"B\""), "undo scene creation preserves another chapter's new scene");
Check(membership.Apply("one", withoutFirst, true)!.Contains("\"A\""), "redo restores only the removed scene");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed.");
    return 1;
}
Console.WriteLine("All ArcTrellis smoke tests passed.");
return 0;

