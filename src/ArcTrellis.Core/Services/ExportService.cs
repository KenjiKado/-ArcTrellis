using System.IO.Compression;
using System.Text;
using System.Xml;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public sealed class ExportService
{
    public void ExportMarkdown(StoryProject project, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {project.Title}").AppendLine();
        if (!string.IsNullOrWhiteSpace(project.Author)) sb.AppendLine($"**Author:** {project.Author}").AppendLine();
        if (!string.IsNullOrWhiteSpace(project.Description)) sb.AppendLine(project.Description).AppendLine();
        foreach (var book in project.Books.OrderBy(b => b.Order))
        {
            sb.AppendLine($"## {book.Title}").AppendLine();
            if (!string.IsNullOrWhiteSpace(book.Summary)) sb.AppendLine(book.Summary).AppendLine();
            foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
            {
                sb.AppendLine($"### {chapter.Title}").AppendLine();
                if (!string.IsNullOrWhiteSpace(chapter.Summary)) sb.AppendLine(chapter.Summary).AppendLine();
                foreach (var scene in project.Scenes.Where(s => s.ChapterId == chapter.Id).OrderBy(s => s.Order))
                {
                    sb.AppendLine($"#### {scene.Title}").AppendLine();
                    if (!string.IsNullOrWhiteSpace(scene.Summary)) sb.AppendLine(scene.Summary).AppendLine();
                    if (!string.IsNullOrWhiteSpace(scene.Content)) sb.AppendLine(scene.Content).AppendLine();
                    if (!string.IsNullOrWhiteSpace(scene.EditingNotes)) sb.AppendLine($"> Editing note: {scene.EditingNotes.ReplaceLineEndings(" ")}").AppendLine();
                }
            }
        }
        WriteUtf8(path, sb.ToString());
    }

    public void ExportCsv(StoryProject project, string path)
    {
        var sb = new StringBuilder("Book,Chapter,Section,Plotline,Scene,Status,POV,Setting,Words,Tags,Summary\r\n");
        foreach (var book in project.Books.OrderBy(b => b.Order))
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        foreach (var scene in project.Scenes.Where(s => s.ChapterId == chapter.Id).OrderBy(s => s.Order))
        {
            string plot = project.Plotlines.FirstOrDefault(p => p.Id == scene.PlotlineId)?.Name ?? "";
            sb.AppendLine(string.Join(',', new[] { book.Title, chapter.Title, chapter.Section, plot, scene.Title, scene.Status,
                scene.PointOfView, scene.Setting, scene.WordCount.ToString(), string.Join("; ", scene.Tags), scene.Summary }.Select(Csv)));
        }
        WriteUtf8(path, sb.ToString());
    }

    public void ExportDocx(StoryProject project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        WriteEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");

        var body = new StringBuilder();
        Paragraph(body, project.Title, "Title");
        if (!string.IsNullOrWhiteSpace(project.Author)) Paragraph(body, "by " + project.Author);
        if (!string.IsNullOrWhiteSpace(project.Description)) Paragraph(body, project.Description);
        foreach (var book in project.Books.OrderBy(b => b.Order))
        {
            Paragraph(body, book.Title, "Heading1");
            if (!string.IsNullOrWhiteSpace(book.Summary)) Paragraph(body, book.Summary);
            foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
            {
                Paragraph(body, chapter.Title, "Heading2");
                if (!string.IsNullOrWhiteSpace(chapter.Summary)) Paragraph(body, chapter.Summary);
                foreach (var scene in project.Scenes.Where(s => s.ChapterId == chapter.Id).OrderBy(s => s.Order))
                {
                    Paragraph(body, scene.Title, "Heading3");
                    if (!string.IsNullOrWhiteSpace(scene.Summary)) Paragraph(body, scene.Summary);
                    foreach (string line in scene.Content.ReplaceLineEndings("\n").Split('\n')) Paragraph(body, line);
                    if (!string.IsNullOrWhiteSpace(scene.EditingNotes)) Paragraph(body, "Editing note: " + scene.EditingNotes);
                }
            }
        }
        string document = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr></w:body></w:document>";
        WriteEntry(zip, "word/document.xml", document);
    }

    public void ExportScrivenerFolder(StoryProject project, string folder)
    {
        string package = Path.Combine(folder, SafeFileName(project.Title) + ".scriv");
        Directory.CreateDirectory(Path.Combine(package, "Files", "Data"));
        var binder = new StringBuilder();
        int index = 1;
        foreach (var book in project.Books.OrderBy(b => b.Order))
        {
            string bookId = Guid.NewGuid().ToString("N").ToUpperInvariant();
            binder.Append($"<BinderItem UUID=\"{bookId}\" Type=\"Folder\"><Title>{Xml(book.Title)}</Title><Children>");
            foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
            {
                string chapterId = Guid.NewGuid().ToString("N").ToUpperInvariant();
                binder.Append($"<BinderItem UUID=\"{chapterId}\" Type=\"Folder\"><Title>{Xml(chapter.Title)}</Title><Children>");
                foreach (var scene in project.Scenes.Where(s => s.ChapterId == chapter.Id).OrderBy(s => s.Order))
                {
                    string sceneId = Guid.NewGuid().ToString("N").ToUpperInvariant();
                    string dataDir = Path.Combine(package, "Files", "Data", sceneId);
                    Directory.CreateDirectory(dataDir);
                    WriteUtf8(Path.Combine(dataDir, "content.rtf"), ToRtf(scene));
                    binder.Append($"<BinderItem UUID=\"{sceneId}\" Type=\"Text\" Created=\"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}\" Modified=\"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}\"><Title>{Xml(scene.Title)}</Title><MetaData><IncludeInCompile>Yes</IncludeInCompile></MetaData></BinderItem>");
                    index++;
                }
                binder.Append("</Children></BinderItem>");
            }
            binder.Append("</Children></BinderItem>");
        }
        string scrivx = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ScrivenerProject Version=\"2.0\" Identifier=\"{Guid.NewGuid():N}\"><ProjectTitle>{Xml(project.Title)}</ProjectTitle><Binder><BinderItem UUID=\"{Guid.NewGuid():N}\" Type=\"DraftFolder\"><Title>Draft</Title><Children>{binder}</Children></BinderItem></Binder></ScrivenerProject>";
        WriteUtf8(Path.Combine(package, SafeFileName(project.Title) + ".scrivx"), scrivx);
    }

    private static void Paragraph(StringBuilder sb, string text, string? style = null)
    {
        sb.Append("<w:p>");
        if (style is not null) sb.Append($"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>");
        sb.Append("<w:r><w:t xml:space=\"preserve\">").Append(Xml(text)).Append("</w:t></w:r></w:p>");
    }

    private static string ToRtf(Scene scene)
    {
        return "{\\rtf1\\ansi\\uc1\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\fs24\\b " + RtfEscape(scene.Title) +
               "\\b0\\par\n" + RtfEscape(scene.Summary) + "\\par\\par\n" + RtfEscape(scene.Content) + "}";
    }

    private static string RtfEscape(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text.ReplaceLineEndings("\n"))
        {
            if (c == '\n') sb.Append("\\par\n");
            else if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c > 127) sb.Append("\\u").Append(unchecked((short)c)).Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"").ReplaceLineEndings(" ") + "\"";
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? "";
    private static string SafeFileName(string value) => string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim() is { Length: > 0 } clean ? clean : "Project";
    private static void WriteUtf8(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
