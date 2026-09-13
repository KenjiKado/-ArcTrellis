using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArcTrellis.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private void CheckPhysicalFilterClicks(List<string> failures, Guid chapterId)
    {
        void Drain() { Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); _sceneFilter?.UpdateLayout(); }
        GetCursorPos(out var original);
        try
        {
            void Click(FrameworkElement target, string name)
            {
                var events = new List<string>();
                var menu = _sceneFilter!;
                void Closed(object? sender, RoutedEventArgs e) => events.Add("menu closed");
                void Inactive(object? sender, EventArgs e) => events.Add("window deactivated");
                void Outside(object sender, MouseButtonEventArgs e) => events.Add("window mouse: " + e.OriginalSource.GetType().Name);
                menu.Closed += Closed; Deactivated += Inactive;
                AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Outside), true);
                try
                {
                    var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                    SetCursorPos((int)point.X, (int)point.Y); Drain();
                    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Drain();
                    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Drain();
                    if (!menu.IsOpen) failures.Add($"Physical {name} click closed filter: {string.Join(", ", events)}");
                }
                finally
                {
                    menu.Closed -= Closed; Deactivated -= Inactive;
                    RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Outside));
                }
            }
            SceneFilter_Click(this, new RoutedEventArgs()); Drain();
            var chapters = _sceneChapterChoices!;
            chapters.Input.Focus(); Drain();
            Click(chapters.Choices[chapterId], "chapter option");
            if (chapters.SelectedIds.Contains(chapterId) == false) failures.Add("Physical chapter click did not select the option");
            CloseSceneFilter(); Drain();

            SceneFilter_Click(this, new RoutedEventArgs()); Drain();
            var tags = _sceneFilterTagsInput!;
            tags.Input.Focus(); tags.Input.Text = "Overlay"; Drain();
            tags.SuggestionList.UpdateLayout();
            if (tags.SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem item)
                failures.Add("Physical tag click had no suggestion to select");
            else
            {
                Click(item, "tag suggestion");
                if (_sceneFilterDraftTags.Count != 1) failures.Add("Physical tag click did not select the suggestion");
            }
            CloseSceneFilter(); Drain();
        }
        finally { SetCursorPos(original.X, original.Y); }
    }
}
