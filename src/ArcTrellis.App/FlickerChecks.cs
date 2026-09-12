using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArcTrellis.App;

public partial class MainWindow
{
    private void CheckStableTagHistory(List<string> failures)
    {
        WorkspaceTabs.SelectedIndex = 3;
        RefreshAll();
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
        var scene = Vm.SelectedScene!;
        var project = Vm.Project;
        var tags = scene.Tags;
        var input = FindVisualChildren<TagInput>(SceneEditor).Single();
        Vm.EditTag(tags, "PersistentChipProbe", false);
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
        var retainedChip = FindVisualChildren<Border>(input).Single(border => Equals(border.Tag, "PersistentChipProbe"));
        var plotline = FindVisualChildren<ComboBox>(SceneEditor).Single(box => Equals(box.Tag, "ScenePlotline"));
        var source = plotline.ItemsSource;
        object selectedPlotline = plotline.SelectedValue;
        var card = SceneList.ItemContainerGenerator.ContainerFromItem(scene) as ListBoxItem;
        if (card is null) failures.Add("Flicker regression fixture has no selected scene card");
        int selectionChanges = 0, unloads = 0;
        SelectionChangedEventHandler selection = (_, _) => selectionChanges++;
        RoutedEventHandler unloaded = (_, _) => unloads++;
        plotline.SelectionChanged += selection;
        if (card is not null) card.Unloaded += unloaded;
        var timelineHeader = TimelineGrid.Children[0];
        var relationSource = RelationFrom.ItemsSource;
        try
        {
            Vm.EditTag(tags, "FlickerProbe", false);
            for (int i = 0; i < 3; i++)
            {
                input.Input.Focus(); Undo_Click(this, new RoutedEventArgs());
                Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
                if (scene.Tags.Contains("FlickerProbe")) failures.Add("Tag undo did not remove the probe");
                input.Input.Focus(); Redo_Click(this, new RoutedEventArgs());
                Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
                if (!scene.Tags.Contains("FlickerProbe")) failures.Add("Tag redo did not restore the probe");
            }
            if (!ReferenceEquals(Vm.Project, project) || !ReferenceEquals(Vm.SelectedScene, scene) || !ReferenceEquals(input.Tags, tags))
                failures.Add("Tag history replaced live project, scene, or tags");
            if (selectionChanges != 0 || unloads != 0 || !ReferenceEquals(card, SceneList.ItemContainerGenerator.ContainerFromItem(scene)))
                failures.Add($"Tag history flickered: plotline changes={selectionChanges}, card unloads={unloads}");
            if (!ReferenceEquals(source, plotline.ItemsSource) || !Equals(selectedPlotline, plotline.SelectedValue))
                failures.Add("Tag history reset the plotline dropdown");
            if (!ReferenceEquals(timelineHeader, TimelineGrid.Children[0]) || !ReferenceEquals(relationSource, RelationFrom.ItemsSource))
                failures.Add("Tag history rebuilt unrelated timeline or relationship controls");
            if (!FindVisualChildren<Border>(input).Contains(retainedChip)) failures.Add("Tag history recreated an unchanged chip");

            SceneList.Focus();
            var surface = input.ClickSurface;
            var hit = surface.InputHitTest(new Point(surface.ActualWidth - 7, surface.ActualHeight / 2)) as UIElement;
            if (hit is null) failures.Add("The tags input has dead space on its right side");
            else
            {
                hit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                if (!input.Input.IsKeyboardFocused) failures.Add("Clicking tags input empty space did not focus typing");
            }
            var remove = FindVisualChildren<Button>(input).Single(button => Equals(button.Tag, "FlickerProbe"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (tags.Contains("FlickerProbe")) failures.Add("Full-width tag hit area intercepted chip removal");
        }
        finally
        {
            plotline.SelectionChanged -= selection;
            if (card is not null) card.Unloaded -= unloaded;
        }
    }
}
