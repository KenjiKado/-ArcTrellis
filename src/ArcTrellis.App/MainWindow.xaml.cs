using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.App.Views;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;
using Microsoft.Win32;

namespace ArcTrellis.App;

public partial class MainWindow : Window
{
    private readonly ProjectService _projects = new();
    private readonly TemplateService _templates = new();
    private readonly ExportService _exports = new();
    private readonly ImportService _imports = new();
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private MainViewModel Vm => (MainViewModel)DataContext;
    private bool _loaded;
    private bool _closingAfterSave;
    private double _timelineCardWidth = 220;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(_templates.CreateBlank());
        Vm.ProjectReplaced += Vm_ProjectReplaced;
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnyTextChanged));
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _autosaveTimer.Start();
        RefreshAll();
        if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() is { } startupFile && File.Exists(startupFile))
            _ = OpenProjectAsync(startupFile);
        else
            ShowNewProjectDialog(firstRun: true);
    }

    private void Vm_ProjectReplaced(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshAll));
    private void AnyTextChanged(object sender, TextChangedEventArgs e) { if (_loaded && e.OriginalSource is TextBox box && box.IsKeyboardFocusWithin) { Vm.MarkDirty(); Title = Vm.WindowTitle; RefreshStats(); } }
    private void AnySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (e.OriginalSource is not ComboBox box || !box.IsKeyboardFocusWithin) return;
        if (box.SelectedItem is Book) Dispatcher.BeginInvoke(new Action(BuildTimeline), DispatcherPriority.Background);
        else if (box.SelectedItem is not StoryEntity) Vm.MarkDirty();
    }

    private void RefreshAll()
    {
        Title = Vm.WindowTitle;
        BuildTimeline();
        RefreshRelations();
        RefreshStats();
    }

    private void RefreshStats()
    {
        int books = Vm.Project.Books.Count;
        int chapters = Vm.Project.Books.Sum(b => b.Chapters.Count);
        int scenes = Vm.Project.Scenes.Count;
        int drafted = Vm.Project.Scenes.Count(s => s.Status is "Drafted" or "Revised" or "Final");
        StatsText.Text = $"{books} book(s)\n{chapters} chapter(s)\n{scenes} scene card(s)\n{Vm.Project.Plotlines.Count} plotline(s)\n{Vm.Project.Characters.Count} character(s)\n{Vm.Project.Places.Count} place(s)\n{drafted} scenes drafted";
        double progress = Vm.Project.WordCountGoal <= 0 ? 0 : Math.Min(100, Vm.Project.CurrentWordCount * 100d / Vm.Project.WordCountGoal);
        ProgressBar.Value = progress;
        ProgressText.Text = $"{Vm.Project.CurrentWordCount:N0} / {Vm.Project.WordCountGoal:N0} words ({progress:0}%)";
    }

    private void BuildTimeline()
    {
        TimelineGrid.Children.Clear(); TimelineGrid.RowDefinitions.Clear(); TimelineGrid.ColumnDefinitions.Clear();
        var book = Vm.SelectedBook;
        if (book is null) return;
        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var plotlines = Vm.Project.Plotlines.OrderBy(p => p.Order).ToList();
        TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        foreach (var _ in chapters) TimelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_timelineCardWidth) });
        TimelineGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in plotlines) TimelineGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddTimelineCell(new TextBlock { Text = "PLOTLINE / CHAPTER", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8) }, 0, 0, false);
        for (int c = 0; c < chapters.Count; c++)
        {
            var header = new StackPanel { Margin = new Thickness(5) };
            header.Children.Add(new TextBlock { Text = chapters[c].Section, Foreground = FindBrush("MutedBrush"), FontSize = 11 });
            header.Children.Add(new TextBlock { Text = chapters[c].Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            AddTimelineCell(header, 0, c + 1, false);
        }
        for (int r = 0; r < plotlines.Count; r++)
        {
            var plot = plotlines[r];
            var label = new Border { BorderThickness = new Thickness(5, 0, 0, 0), BorderBrush = BrushFrom(plot.Color), Padding = new Thickness(8), Child = new TextBlock { Text = plot.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } };
            AddTimelineCell(label, r + 1, 0, false);
            for (int c = 0; c < chapters.Count; c++)
            {
                var stack = new StackPanel { MinHeight = 105 };
                foreach (var scene in Vm.Project.Scenes.Where(s => s.BookId == book.Id && s.ChapterId == chapters[c].Id && s.PlotlineId == plot.Id).OrderBy(s => s.Order))
                    stack.Children.Add(CreateSceneCard(scene, plot));
                if (stack.Children.Count == 0) stack.Children.Add(new TextBlock { Text = "Drop a scene here or double-click", Foreground = FindBrush("MutedBrush"), Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap });
                var border = AddTimelineCell(stack, r + 1, c + 1, true);
                border.Tag = (chapters[c].Id, plot.Id);
                border.Drop += TimelineCell_Drop;
                border.MouseLeftButtonDown += TimelineCell_MouseLeftButtonDown;
            }
        }
    }

    private Border AddTimelineCell(UIElement child, int row, int column, bool allowDrop)
    {
        var border = new Border { Background = FindBrush("PanelBrush"), BorderBrush = FindBrush("BorderBrush"), BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(5), AllowDrop = allowDrop, Child = child };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); TimelineGrid.Children.Add(border); return border;
    }

    private Border CreateSceneCard(Scene scene, Plotline plot)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = scene.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(scene.Summary)) panel.Children.Add(new TextBlock { Text = scene.Summary, TextWrapping = TextWrapping.Wrap, MaxHeight = 54, Foreground = FindBrush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = scene.Status, FontSize = 11, Foreground = BrushFrom(plot.Color), Margin = new Thickness(0, 5, 0, 0) });
        var card = new Border { Tag = scene, Child = panel, Background = new SolidColorBrush(Color.FromArgb(24, BrushColor(plot.Color).R, BrushColor(plot.Color).G, BrushColor(plot.Color).B)), BorderBrush = BrushFrom(plot.Color), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(5), Margin = new Thickness(2, 3, 2, 3), Padding = new Thickness(9), Cursor = Cursors.Hand };
        card.PreviewMouseMove += SceneCard_MouseMove;
        card.MouseLeftButtonDown += SceneCard_MouseLeftButtonDown;
        return card;
    }

    private void SceneCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Scene scene }) return;
        Vm.SelectedScene = scene;
        if (e.ClickCount == 2) WorkspaceTabs.SelectedIndex = 3;
        e.Handled = true;
    }

    private void SceneCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is Border { Tag: Scene scene })
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("ArcTrellis.Scene", scene.Id.ToString()), DragDropEffects.Move);
    }

    private void TimelineCell_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: ValueTuple<Guid, Guid> target }) return;
        if (!e.Data.GetDataPresent("ArcTrellis.Scene") || !Guid.TryParse(e.Data.GetData("ArcTrellis.Scene")?.ToString(), out var id)) return;
        var scene = Vm.Project.Scenes.FirstOrDefault(s => s.Id == id);
        if (scene is null) return;
        Vm.MoveScene(scene, target.Item1, target.Item2); BuildTimeline();
    }

    private void TimelineCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Border { Tag: ValueTuple<Guid, Guid> target }) return;
        Vm.SelectedChapter = Vm.SelectedBook?.Chapters.FirstOrDefault(c => c.Id == target.Item1);
        Vm.SelectedPlotline = Vm.Project.Plotlines.FirstOrDefault(p => p.Id == target.Item2);
        Vm.AddScene(target.Item1, target.Item2); BuildTimeline();
    }

    private void RefreshRelations()
    {
        var entities = Vm.Project.Characters.Concat(Vm.Project.Places).Concat(Vm.Project.Notes).ToList();
        RelationFrom.ItemsSource = entities; RelationTo.ItemsSource = entities;
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (!Vm.IsDirty) return;
        try
        {
            await _projects.SaveAsync(Vm.Project, _projects.GetAutosavePath(Vm.Project.Id), false);
            Vm.Status = "Autosaved locally at " + DateTime.Now.ToShortTimeString();
        }
        catch (Exception ex) { Vm.Status = "Autosave failed: " + ex.Message; }
    }

    private void New_Click(object sender, RoutedEventArgs e) => ShowNewProjectDialog(false);
    private void ShowNewProjectDialog(bool firstRun)
    {
        if (!firstRun && !ConfirmDiscard()) return;
        var blank = new TemplateInfo("Blank project", "One book, one chapter, and a main plotline.", "Blank", "");
        var options = new[] { blank }.Concat(_templates.ListTemplates()).ToList();
        var dialog = new NewProjectWindow(options) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            if (firstRun) Vm.Status = "Blank project ready";
            return;
        }
        var project = string.IsNullOrEmpty(dialog.SelectedTemplate!.FilePath) ? _templates.CreateBlank() : _templates.CreateFromTemplate(dialog.SelectedTemplate);
        Vm.ReplaceProject(project); RefreshAll();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Open ArcTrellis project", Filter = "ArcTrellis projects (*.arctrellis)|*.arctrellis|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) await OpenProjectAsync(dialog.FileName);
    }

    private async Task OpenProjectAsync(string path)
    {
        try
        {
            var project = await _projects.LoadAsync(path);
            string autosave = _projects.GetAutosavePath(project.Id);
            if (File.Exists(autosave) && File.GetLastWriteTimeUtc(autosave) > File.GetLastWriteTimeUtc(path) && MessageBox.Show("A newer local autosave exists. Recover it?", "Recover project", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                project = await _projects.LoadAsync(autosave);
            Vm.ReplaceProject(project, path); Vm.Status = "Opened " + Path.GetFileName(path); RefreshAll();
        }
        catch (Exception ex) { MessageBox.Show("Could not open the project.\n\n" + ex.Message, "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save reusable template", Filter = "ArcTrellis template (*.json)|*.json", DefaultExt = ".json", AddExtension = true, InitialDirectory = _templates.UserTemplateDirectory, FileName = SafeName(Vm.Project.Title) + " Template" };
        if (dialog.ShowDialog() != true) return;
        try { _templates.SaveTemplate(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName), "Custom template created from " + Vm.Project.Title, Vm.Project); Vm.Status = "Reusable template saved"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Template save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        string? path = Vm.FilePath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog { Title = "Save ArcTrellis project", Filter = "ArcTrellis project (*.arctrellis)|*.arctrellis", DefaultExt = ".arctrellis", AddExtension = true, FileName = SafeName(Vm.Project.Title) };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }
        try
        {
            await _projects.SaveAsync(Vm.Project, path!); Vm.FilePath = path; Vm.IsDirty = false; Vm.Status = "Saved " + Path.GetFileName(path); Title = Vm.WindowTitle; return true;
        }
        catch (Exception ex) { MessageBox.Show("Could not save the project.\n\n" + ex.Message, "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    private void ImportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt" };
        if (dialog.ShowDialog() != true) return;
        try { Vm.ReplaceProject(_imports.ImportMarkdown(dialog.FileName)); Vm.MarkDirty(); RefreshAll(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportDocx_Click(object sender, RoutedEventArgs e) => ExportFile("Word document (*.docx)|*.docx", ".docx", _exports.ExportDocx);
    private void ExportMarkdown_Click(object sender, RoutedEventArgs e) => ExportFile("Markdown (*.md)|*.md", ".md", _exports.ExportMarkdown);
    private void ExportCsv_Click(object sender, RoutedEventArgs e) => ExportFile("CSV spreadsheet (*.csv)|*.csv", ".csv", _exports.ExportCsv);
    private void ExportFile(string filter, string extension, Action<StoryProject, string> export)
    {
        var dialog = new SaveFileDialog { Filter = filter, DefaultExt = extension, AddExtension = true, FileName = SafeName(Vm.Project.Title) };
        if (dialog.ShowDialog() != true) return;
        try { export(Vm.Project, dialog.FileName); Vm.Status = "Exported " + Path.GetFileName(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportScrivener_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for the Scrivener project" };
        if (dialog.ShowDialog() != true) return;
        try { _exports.ExportScrivenerFolder(Vm.Project, dialog.FolderName); Vm.Status = "Scrivener project exported"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void AddBook_Click(object sender, RoutedEventArgs e) { Vm.AddBook(); RefreshAll(); }
    private void DeleteBook_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("book and all of its scenes")) { Vm.DeleteBook(); RefreshAll(); } }
    private void AddChapter_Click(object sender, RoutedEventArgs e) { Vm.AddChapter(); RefreshAll(); }
    private void DeleteChapter_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("chapter and all of its scenes")) { Vm.DeleteChapter(); RefreshAll(); } }
    private void ChapterUp_Click(object sender, RoutedEventArgs e) { Vm.MoveChapter(-1); RefreshAll(); }
    private void ChapterDown_Click(object sender, RoutedEventArgs e) { Vm.MoveChapter(1); RefreshAll(); }
    private void AddPlotline_Click(object sender, RoutedEventArgs e) { Vm.AddPlotline(); RefreshAll(); }
    private void DeletePlotline_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("plotline (its scenes will move to another plotline)")) { Vm.DeletePlotline(); RefreshAll(); } }
    private void AddScene_Click(object sender, RoutedEventArgs e) { Vm.AddScene(); WorkspaceTabs.SelectedIndex = 3; RefreshAll(); }
    private void DeleteScene_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("scene")) { Vm.DeleteScene(); RefreshAll(); } }
    private void AddCharacter_Click(object sender, RoutedEventArgs e) { Vm.SelectedCharacter = Vm.AddEntity(Vm.Project.Characters, "Character"); RefreshAll(); }
    private void DeleteCharacter_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("character")) { Vm.DeleteEntity(Vm.Project.Characters, Vm.SelectedCharacter); Vm.SelectedCharacter = Vm.Project.Characters.FirstOrDefault(); RefreshAll(); } }
    private void AddPlace_Click(object sender, RoutedEventArgs e) { Vm.SelectedPlace = Vm.AddEntity(Vm.Project.Places, "Place"); RefreshAll(); }
    private void DeletePlace_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("place")) { Vm.DeleteEntity(Vm.Project.Places, Vm.SelectedPlace); Vm.SelectedPlace = Vm.Project.Places.FirstOrDefault(); RefreshAll(); } }
    private void AddNote_Click(object sender, RoutedEventArgs e) { Vm.SelectedNote = Vm.AddEntity(Vm.Project.Notes, "Note"); RefreshAll(); }
    private void DeleteNote_Click(object sender, RoutedEventArgs e) { if (ConfirmDelete("note")) { Vm.DeleteEntity(Vm.Project.Notes, Vm.SelectedNote); Vm.SelectedNote = Vm.Project.Notes.FirstOrDefault(); RefreshAll(); } }
    private void AddRelationship_Click(object sender, RoutedEventArgs e) { Vm.AddRelationship(); RefreshAll(); }
    private void DeleteRelationship_Click(object sender, RoutedEventArgs e) { if (Vm.SelectedRelationship is { } r) { Vm.Project.Relationships.Remove(r); Vm.SelectedRelationship = Vm.Project.Relationships.FirstOrDefault(); Vm.MarkDirty(); } }
    private void Search_Click(object sender, RoutedEventArgs e) => Vm.RunSearch();
    private void Undo_Click(object sender, RoutedEventArgs e) { Vm.Undo(); RefreshAll(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { Vm.Redo(); RefreshAll(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _timelineCardWidth = Math.Min(380, _timelineCardWidth + 30); BuildTimeline(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _timelineCardWidth = Math.Max(130, _timelineCardWidth - 30); BuildTimeline(); }

    private void SearchResult_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView { SelectedItem: SearchResult result }) return;
        switch (result.Kind)
        {
            case "Scene": Vm.SelectedScene = Vm.Project.Scenes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 3; break;
            case "Character": Vm.SelectedCharacter = Vm.Project.Characters.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 4; break;
            case "Place": Vm.SelectedPlace = Vm.Project.Places.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 5; break;
            case "Note": Vm.SelectedNote = Vm.Project.Notes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 6; break;
            case "Chapter": WorkspaceTabs.SelectedIndex = 2; break;
        }
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e) => SetTheme(false);
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => SetTheme(true);
    private void SetTheme(bool dark)
    {
        Application.Current.Resources["PageBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#171A22" : "#F4F6FA"));
        Application.Current.Resources["PanelBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#222733" : "#FFFFFF"));
        Application.Current.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#EEF1F7" : "#202638"));
        Application.Current.Resources["MutedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#AAB3C5" : "#667085"));
        Application.Current.Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#394154" : "#D8DEEA"));
        BuildTimeline();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(TimelineGrid, Vm.Project.Title + " timeline");
    }
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Docs", "USER_GUIDE.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("ArcTrellis 1.0\n\nA private, local-first visual story planner for Windows.\nNo cloud account, tracking, or network connection required.", "About ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Save_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) { SaveAs_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { Open_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { New_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { Undo_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { Redo_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { RefreshAll(); e.Handled = true; }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterSave || !Vm.IsDirty) return;
        var result = MessageBox.Show("Save changes before closing?", "ArcTrellis", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.No) return;
        e.Cancel = true;
        if (await SaveAsync(false)) { _closingAfterSave = true; Close(); }
    }

    private bool ConfirmDiscard()
    {
        if (!Vm.IsDirty) return true;
        var result = MessageBox.Show("This project has unsaved changes. Continue and discard them?", "ArcTrellis", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
    private static bool ConfirmDelete(string item) => MessageBox.Show($"Delete this {item}?", "ArcTrellis", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private static string SafeName(string name) => string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) is { Length: > 0 } value ? value : "Story";
    private Brush FindBrush(string key) => (Brush)FindResource(key);
    private static SolidColorBrush BrushFrom(string color) => new(BrushColor(color));
    private static Color BrushColor(string color) { try { return (Color)ColorConverter.ConvertFromString(color); } catch { return Colors.SlateBlue; } }
}
