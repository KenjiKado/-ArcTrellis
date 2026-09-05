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
Check(migratedLegacy.FormatVersion == 2 && migratedLegacy.Plotlines.Count == 2, "legacy shared plotlines migrate to one independent copy per book");
Check(migratedLegacy.Scenes.All(scene => migratedLegacy.Plotlines.Any(plotline => plotline.Id == scene.PlotlineId && plotline.BookId == scene.BookId)), "legacy scenes retain their plotline in the correct book");
var migratedFirstPlotline = migratedLegacy.Plotlines.Single(plotline => plotline.BookId == migratedLegacy.Books[0].Id);
var migratedSecondPlotline = migratedLegacy.Plotlines.Single(plotline => plotline.BookId == migratedLegacy.Books[1].Id);
migratedSecondPlotline.Name = "Second book only";
Check(migratedFirstPlotline.Name == "Shared before migration", "renaming a migrated plotline does not rename another book's copy");

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

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed.");
    return 1;
}
Console.WriteLine("All ArcTrellis smoke tests passed.");
return 0;
