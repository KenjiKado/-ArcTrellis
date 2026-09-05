using System.Text.RegularExpressions;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

public sealed class ImportService
{
    public StoryProject ImportMarkdown(string path)
    {
        var project = new StoryProject { Title = Path.GetFileNameWithoutExtension(path) };
        Book? book = null;
        Chapter? chapter = null;
        Scene? scene = null;
        var content = new List<string>();

        void Flush()
        {
            if (scene is not null) scene.Content = string.Join(Environment.NewLine, content).Trim();
            content.Clear();
        }

        foreach (string raw in File.ReadLines(path))
        {
            var match = Regex.Match(raw, "^(#{1,4})\\s+(.+)$");
            if (!match.Success) { content.Add(raw); continue; }
            Flush();
            string title = match.Groups[2].Value.Trim();
            switch (match.Groups[1].Value.Length)
            {
                case 1: project.Title = title; break;
                case 2:
                    book = new Book { Title = title, Order = project.Books.Count };
                    project.Books.Add(book); chapter = null; scene = null; break;
                case 3:
                    book ??= AddDefaultBook(project);
                    chapter = new Chapter { Title = title, Order = book.Chapters.Count };
                    book.Chapters.Add(chapter); scene = null; break;
                case 4:
                    book ??= AddDefaultBook(project);
                    chapter ??= AddDefaultChapter(book);
                    scene = new Scene { Title = title, BookId = book.Id, ChapterId = chapter.Id, Order = project.Scenes.Count };
                    project.Scenes.Add(scene); break;
            }
        }
        Flush();
        if (project.Books.Count == 0) AddDefaultBook(project);
        foreach (Book importedBook in project.Books)
        {
            var main = new Plotline { BookId = importedBook.Id, Name = "Main Plot", Order = 0 };
            project.Plotlines.Add(main);
            foreach (var item in project.Scenes.Where(scene => scene.BookId == importedBook.Id)) item.PlotlineId = main.Id;
        }
        return project;
    }

    private static Book AddDefaultBook(StoryProject project)
    {
        var book = new Book { Title = "Book One", Order = project.Books.Count };
        project.Books.Add(book);
        return book;
    }
    private static Chapter AddDefaultChapter(Book book)
    {
        var chapter = new Chapter { Title = "Chapter 1", Order = book.Chapters.Count };
        book.Chapters.Add(chapter);
        return chapter;
    }
}
