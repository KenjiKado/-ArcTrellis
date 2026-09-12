using ArcTrellis.Core.Models;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private object?[]? _timelineState, _sceneListState, _chapterFilterState;

    private static bool ViewChanged(ref object?[]? previous, IEnumerable<object?> state)
    {
        var next = state.ToArray();
        if (previous is not null && previous.SequenceEqual(next)) return false;
        previous = next;
        return true;
    }

    private IEnumerable<object?> TimelineState()
    {
        yield return (Vm.Project, Vm.SelectedBook, _timelineZoom, _timelineCardWidth, _isDark, Loc.Language);
        if (Vm.SelectedBook is not { } book) yield break;
        yield return (book.TimelineHeaderHeight, book.TimelineLabelWidth);
        foreach (var chapter in book.Chapters.OrderBy(chapter => chapter.Order))
            yield return (chapter, chapter.Title, chapter.Section, chapter.TimelineWidth);
        foreach (var plot in Vm.BookPlotlines)
            yield return (plot, plot.Name, plot.Description, plot.Color, plot.TimelineHeight);
        foreach (var scene in Vm.BookScenes)
            yield return (scene, scene.ChapterId, scene.PlotlineId, scene.Order, scene.Title, scene.Summary, scene.Status);
    }

    private IEnumerable<object?> SceneListState()
    {
        yield return (Vm.Project, Vm.SelectedBook, Loc.Language);
        if (Vm.SelectedBook is not { } book) yield break;
        foreach (var chapter in book.Chapters) yield return (chapter, chapter.Title, chapter.Order);
        foreach (var plot in Vm.BookPlotlines) yield return (plot, plot.Color, plot.Order);
        foreach (var scene in Vm.BookScenes) yield return (scene, scene.ChapterId, scene.PlotlineId, scene.Order);
    }

    private IEnumerable<object?> ChapterFilterState(object view)
    {
        yield return view;
        if (Vm.SelectedBook is not { } book) yield break;
        foreach (var chapter in book.Chapters) yield return (chapter, chapter.Order, Vm.MatchesChapterFilter(chapter));
    }
}
