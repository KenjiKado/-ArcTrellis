using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private const string SceneChapterDragFormat = "ArcTrellis.SceneChapter";
    private Scene? _pressedListScene;
    private ListBoxItem? _pressedListItem;
    private Point _listPressPosition, _listGrabOffset;

    private ListBoxItem? SceneCardContainer(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, SceneList))
        {
            if (source is ListBoxItem item) return item;
            source = source is Visual ? VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void SceneList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedListScene = null;
        _pressedListItem = null;
        if (SceneCardContainer(e.OriginalSource as DependencyObject) is not ListBoxItem { DataContext: Scene scene } item ||
            scene.BookId != Vm.SelectedBook?.Id) return;
        _pressedListScene = scene;
        _pressedListItem = item;
        _listPressPosition = e.GetPosition(SceneList);
        var card = FindVisualChildren<Border>(item).FirstOrDefault(border => ReferenceEquals(border.Tag, scene));
        _listGrabOffset = card is null ? new Point(6, 6) : e.GetPosition(card);
    }

    private void SceneList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    { _pressedListScene = null; _pressedListItem = null; }

    private void SceneList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _pressedListScene = null; _pressedListItem = null; return; }
        if (_pressedListScene is not { } scene || _pressedListItem is not { } item) return;
        var point = e.GetPosition(SceneList);
        if (Math.Abs(point.X - _listPressPosition.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _listPressPosition.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedListScene = null;
        _pressedListItem = null;

        SceneChapterDropTargets.ItemsSource = Vm.SelectedBook!.Chapters.OrderBy(chapter => chapter.Order).ToList();
        SceneChapterDropOverlay.Visibility = Visibility.Visible;
        SceneChapterDropOverlay.UpdateLayout();
        var surface = (UIElement)Content;
        var layer = AdornerLayer.GetAdornerLayer(surface);
        var card = FindVisualChildren<Border>(item).FirstOrDefault(border => ReferenceEquals(border.Tag, scene) && border.BorderBrush is SolidColorBrush);
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => _sceneDragAdorner?.FollowCursor();
        try
        {
            _draggedSceneId = scene.Id;
            if (layer is not null && card is not null)
            {
                _sceneDragAdorner = new SceneDragAdorner(surface, card, _listGrabOffset);
                layer.Add(_sceneDragAdorner);
                _sceneDragAdorner.FollowCursor();
                timer.Start();
            }
            DragDrop.DoDragDrop(item, new DataObject(SceneChapterDragFormat, scene.Id.ToString()), DragDropEffects.Move);
        }
        finally
        {
            timer.Stop();
            if (_sceneDragAdorner is not null) layer?.Remove(_sceneDragAdorner);
            _sceneDragAdorner = null;
            _draggedSceneId = null;
            SceneChapterDropOverlay.Visibility = Visibility.Collapsed;
            SceneChapterDropTargets.ItemsSource = null;
        }
        e.Handled = true;
    }

    private bool TrySceneChapterTarget(object sender, DragEventArgs e, out Scene? scene, out Guid chapterId)
    {
        scene = null;
        chapterId = default;
        if (Vm.SelectedBook is not { } book || sender is not Border { Tag: Guid target } ||
            !e.Data.GetDataPresent(SceneChapterDragFormat) ||
            !Guid.TryParse(e.Data.GetData(SceneChapterDragFormat)?.ToString(), out var id) ||
            _draggedSceneId != id ||
            book.Chapters.All(chapter => chapter.Id != target)) return false;
        scene = Vm.Project.Scenes.FirstOrDefault(candidate => candidate.Id == id && candidate.BookId == book.Id);
        chapterId = target;
        return scene is not null && scene.ChapterId != target;
    }

    private void SceneChapterTarget_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = TrySceneChapterTarget(sender, e, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void SceneChapterTarget_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!TrySceneChapterTarget(sender, e, out var scene, out Guid target) || scene is null) return;
        if (MoveSceneFromList(scene, target)) e.Effects = DragDropEffects.Move;
    }

    private bool MoveSceneFromList(Scene scene, Guid chapterId)
    {
        if (Vm.SelectedBook is not { } book || scene.BookId != book.Id ||
            !Vm.Project.Scenes.Contains(scene) || book.Chapters.All(chapter => chapter.Id != chapterId) ||
            scene.ChapterId == chapterId) return false;
        Vm.SelectedScene = scene;
        if (SceneList.Items.Contains(scene)) SceneList.SelectedItem = scene;
        Vm.MoveScene(scene, chapterId, scene.PlotlineId);
        RefreshSceneList();
        BuildTimeline();
        return true;
    }

    private void CheckSceneChapterDragging(List<string> failures, string reportPath)
    {
        var book = Vm.SelectedBook!;
        var first = Vm.BookScenes.First();
        var originalChapter = first.ChapterId;
        var originalOrder = Vm.BookScenes.Select(scene => scene.Id).ToList();
        Vm.AddChapter();
        var empty = Vm.SelectedChapter!;
        SceneChapterDropTargets.ItemsSource = book.Chapters.OrderBy(chapter => chapter.Order).ToList();
        SceneChapterDropOverlay.Visibility = Visibility.Visible;
        UpdateLayout();
        var targets = FindVisualChildren<Border>(SceneChapterDropTargets).Where(border => border.Tag is Guid).ToList();
        if (!book.Chapters.All(chapter => targets.Any(target => Equals(target.Tag, chapter.Id))))
            failures.Add("Scene drag targets do not include empty chapters");
        SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-chapter-drop-targets.png"));
        SceneChapterDropOverlay.Visibility = Visibility.Collapsed;
        SceneChapterDropTargets.ItemsSource = null;

        if (MoveSceneFromList(first, originalChapter) || !Vm.BookScenes.Select(scene => scene.Id).SequenceEqual(originalOrder))
            failures.Add("Dropping a scene on its own chapter changed its order");

        var otherPlotline = Vm.BookScenes.First(scene => scene.PlotlineId != first.PlotlineId);
        MoveSceneFromList(otherPlotline, empty.Id);
        MoveSceneFromList(first, empty.Id);
        if (!Vm.BookScenes.Where(scene => scene.ChapterId == empty.Id).Select(scene => scene.Id).SequenceEqual([first.Id, otherPlotline.Id]) ||
            !SceneList.Items.Contains(first) || first.ChapterId != empty.Id ||
            !FindVisualChildren<Border>(TimelineGrid).Any(border => ReferenceEquals(border.Tag, first)))
            failures.Add("Scene chapter drop did not sort the destination by plotline and update Scenes/Timeline");
        var saved = new ProjectService().Deserialize(new ProjectService().Serialize(Vm.Project));
        if (saved.Scenes.Single(scene => scene.Id == first.Id).ChapterId != empty.Id)
            failures.Add("Scene chapter drop did not persist");
        Vm.Undo(); RefreshAll();
        if (Vm.Project.Scenes.Single(scene => scene.Id == first.Id).ChapterId != originalChapter)
            failures.Add("Undo did not restore the dragged scene's chapter");
        Vm.Redo(); RefreshAll();
        if (Vm.Project.Scenes.Single(scene => scene.Id == first.Id).ChapterId != empty.Id)
            failures.Add("Redo did not restore the dragged scene's destination");
    }
}
