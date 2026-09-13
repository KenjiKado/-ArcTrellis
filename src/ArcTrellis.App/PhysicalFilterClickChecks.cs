using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ArcTrellis.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private void CheckPhysicalFilterClicks(List<string> failures, Guid chapterId)
    {
        void Drain() { Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); if (_sceneFilter?.IsOpen == true) SceneFilterSurface.UpdateLayout(); }
        GetCursorPos(out var original);
        try
        {
            string Click(FrameworkElement target, string name)
            {
                var events = new List<string>();
                var menu = _sceneFilter!;
                string State(string phase) => $"{phase}: open={menu.IsOpen}, capture={Mouse.Captured?.GetType().Name ?? "none"}, focus={Keyboard.FocusedElement?.GetType().Name ?? "none"}";
                void Closed(object? sender, EventArgs e) => events.Add(State("menu closed"));
                void Inactive(object? sender, EventArgs e) => events.Add("window deactivated");
                void Outside(object sender, MouseButtonEventArgs e) => events.Add("window mouse: " + e.OriginalSource.GetType().Name);
                menu.Closed += Closed; Deactivated += Inactive;
                AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Outside), true);
                try
                {
                    var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                    events.Add(State("before move"));
                    SetCursorPos((int)point.X, (int)point.Y); Drain();
                    GetCursorPos(out var current);
                    var targetWindow = (PresentationSource.FromVisual(target) as HwndSource)?.Handle;
                    events.Add(State($"hover target={targetWindow} hit={WindowFromPoint(current)} position={current.X},{current.Y} expected={point.X:0},{point.Y:0}"));
                    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Drain();
                    events.Add(State("mouse down"));
                    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Drain();
                    events.Add(State("mouse up"));
                    if (!menu.IsOpen) failures.Add($"Physical {name} click closed filter: {string.Join(", ", events)}");
                    return string.Join(", ", events);
                }
                finally
                {
                    menu.Closed -= Closed; Deactivated -= Inactive;
                    RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Outside));
                }
            }
            Activate(); Drain();
            SceneFilter_Click(this, new RoutedEventArgs()); Drain();
            var chapters = _sceneChapterChoices!;
            chapters.Input.Focus(); Drain();
            string chapterTrace = Click(chapters.Choices[chapterId], "chapter option");
            if (!chapters.SelectedIds.Contains(chapterId) && _sceneFilter?.IsOpen == true)
            {
                chapters.Input.Focus(); Drain();
                chapterTrace = Click(chapters.Choices[chapterId], "chapter option retry");
            }
            if (!chapters.SelectedIds.Contains(chapterId)) failures.Add("Physical chapter click did not select the option: " + chapterTrace);
            CloseSceneFilter(); Drain();

            SceneFilter_Click(this, new RoutedEventArgs()); Drain();
            var tags = _sceneFilterTagsInput!;
            tags.Input.Focus(); tags.Input.Text = "Overlay"; Drain();
            tags.SuggestionList.UpdateLayout();
            if (tags.SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem item)
                failures.Add("Physical tag click had no suggestion to select");
            else
            {
                string tagTrace = Click(item, "tag suggestion");
                if (_sceneFilterDraftTags.Count == 0 && _sceneFilter?.IsOpen == true)
                {
                    tags.Input.Focus(); tags.Input.Text = "Overlay"; Drain();
                    tags.SuggestionList.UpdateLayout();
                    if (tags.SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem retry)
                        tagTrace = Click(retry, "tag suggestion retry");
                }
                if (_sceneFilterDraftTags.Count != 1) failures.Add("Physical tag click did not select the suggestion: " + tagTrace);
            }
            CloseSceneFilter(); Drain();
        }
        finally { SetCursorPos(original.X, original.Y); }
    }
}
