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
    var plots = project.Plotlines.Select(x => x.Id).ToHashSet();
    Check(project.Scenes.All(x => books.Contains(x.BookId) && chapters.Contains(x.ChapterId) && plots.Contains(x.PlotlineId)), $"{template.Name}: scene references are valid");
}

var example = templates.CreateFromTemplate(list.First(x => x.Name.Contains("Glass Horizon")));
string serialized = projectService.Serialize(example);
var roundTrip = projectService.Deserialize(serialized);
Check(roundTrip.Title == example.Title && roundTrip.Scenes.Count == example.Scenes.Count, "project JSON round-trip preserves content");
Check(SearchService.Search(roundTrip, "compass").Count >= 2, "full-project search finds matching story data");

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
