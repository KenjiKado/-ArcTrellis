using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private readonly HashSet<Guid> _collapsedSceneChapters = [];

    internal bool IsSceneChapterExpanded(Guid chapterId) => !_collapsedSceneChapters.Contains(chapterId);

    private void SceneChapterToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: Guid id, IsLoaded: true }) _collapsedSceneChapters.Remove(id);
    }

    private void SceneChapterToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: Guid id }) _collapsedSceneChapters.Add(id);
    }

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

        var surface = (UIElement)Content;
        var layer = AdornerLayer.GetAdornerLayer(surface);
        var card = FindVisualChildren<Border>(item).FirstOrDefault(border => ReferenceEquals(border.Tag, scene) && border.BorderBrush is SolidColorBrush);
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => _sceneDragAdorner?.FollowCursor();
        try
        {
            SceneList.Tag = "Dragging";
            _draggedSceneId = scene.Id;
            if (layer is not null && card is not null)
            {
                _sceneDragAdorner = new SceneDragAdorner(surface, card, _listGrabOffset);
                layer.Add(_sceneDragAdorner);
                _sceneDragAdorner.FollowCursor();
                timer.Start();
            }
            DragDrop.DoDragDrop(SceneList, new DataObject(SceneChapterDragFormat, scene.Id.ToString()), DragDropEffects.Move);
        }
        finally
        {
            timer.Stop();
            if (_sceneDragAdorner is not null) layer?.Remove(_sceneDragAdorner);
            _sceneDragAdorner = null;
            _draggedSceneId = null;
            SceneList.Tag = null;
        }
        e.Handled = true;
    }

    private bool TrySceneChapterTarget(object sender, DragEventArgs e, out Scene? scene, out Guid chapterId)
    {
        scene = null;
        chapterId = default;
        if (Vm.SelectedBook is not { } book || sender is not ToggleButton { Tag: Guid target } ||
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
        _collapsedSceneChapters.Remove(chapterId);
        RefreshSceneList();
        var destination = FindVisualChildren<ToggleButton>(SceneList).FirstOrDefault(toggle => Equals(toggle.Tag, chapterId));
        if (destination is not null) destination.IsChecked = true;
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
        RefreshSceneList();
        UpdateLayout();
        List<ToggleButton> ChapterToggles() => FindVisualChildren<ToggleButton>(SceneList).Where(toggle => toggle.Tag is Guid).ToList();
        var targets = ChapterToggles();
        var groups = ((CollectionViewSource)Resources["SceneListView"]).View?.Groups?.Cast<CollectionViewGroup>().ToList();
        if (groups is null || !book.Chapters.All(chapter => groups.Any(group => Equals(group.Name, chapter.Id)) &&
                targets.Any(target => Equals(target.Tag, chapter.Id))))
            failures.Add("Scene accordion does not show every chapter, including empty chapters");

        var originalToggle = targets.FirstOrDefault(target => Equals(target.Tag, originalChapter));
        if (originalToggle is null) failures.Add("Scene accordion is missing the original chapter heading");
        else
        {
            originalToggle.IsChecked = false;
            UpdateLayout();
            var originalGroup = FindVisualChildren<GroupItem>(SceneList)
                .FirstOrDefault(group => group.DataContext is CollectionViewGroup { Name: Guid id } && id == originalChapter);
            if (originalGroup is null || FindVisualChildren<ItemsPresenter>(originalGroup).FirstOrDefault()?.Visibility != Visibility.Collapsed)
                failures.Add("Collapsing a scene chapter did not hide its cards");
            if (originalGroup?.Visibility != Visibility.Visible)
                failures.Add("Collapsing a scene chapter hid its group");
            if (ChapterToggles().All(toggle => !Equals(toggle.Tag, originalChapter) || toggle.Visibility != Visibility.Visible))
                failures.Add("Collapsing a scene chapter hid its header");
            if (!_collapsedSceneChapters.Contains(originalChapter))
                failures.Add($"Collapsing a scene chapter lost its state (loaded={originalToggle.IsLoaded}, tag={originalToggle.Tag}, checked={originalToggle.IsChecked})");
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-chapter-collapsed.png"));
            ((CollectionViewSource)Resources["SceneListView"]).View?.Refresh();
            UpdateLayout();
            originalToggle = ChapterToggles().FirstOrDefault(target => Equals(target.Tag, originalChapter));
            if (originalToggle?.IsChecked != false) failures.Add("Scene accordion lost its collapsed state after a refresh");
            if (originalToggle is not null) originalToggle.IsChecked = true;
            _collapsedSceneChapters.Remove(originalChapter);
        }

        SceneList.Tag = "Dragging";
        UpdateLayout();
        if (SceneList.Items.Count != originalOrder.Count ||
            FindVisualChildren<ListBoxItem>(SceneList).All(item => item.Opacity >= 1) ||
            !book.Chapters.All(chapter => ChapterToggles().Any(toggle => Equals(toggle.Tag, chapter.Id))))
            failures.Add("Scene drag hid the cards or did not dim them while keeping chapter headings available");
        SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-scene-chapter-drop-targets.png"));
        SceneList.Tag = null;
        UpdateLayout();

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

public sealed class SceneChapterExpandedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is Guid id && values[1] is MainWindow window
            ? window.IsSceneChapterExpanded(id) : true;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing, Binding.DoNothing];
}
