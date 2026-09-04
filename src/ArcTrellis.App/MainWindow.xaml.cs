using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private static readonly string ThemeSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTrellis", "theme.txt");
    private MainViewModel Vm => (MainViewModel)DataContext;
    private bool _loaded;
    private bool _closingAfterSave;
    private double _timelineCardWidth = 220;
    private bool _isDark;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(CreateBlankProject());
        SetTheme(LoadDarkTheme(), false);
        SourceInitialized += (_, _) => ApplyWindowChromeTheme();
        Vm.ProjectReplaced += Vm_ProjectReplaced;
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        WorkspaceTabs.SelectionChanged += WorkspaceTabs_SelectionChanged;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnyTextChanged));
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _autosaveTimer.Start();
        RefreshAll();
        ApplyLocalization();
        Dispatcher.BeginInvoke(new Action(ApplyLocalization), DispatcherPriority.Loaded);
        string? uiSmokeReport = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => x.StartsWith("--ui-smoke=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(uiSmokeReport))
        {
            SetTheme(true, false);
            WorkspaceTabs.SelectedIndex = 1;
            Dispatcher.BeginInvoke(new Action(() => RunUiSmoke(uiSmokeReport)), DispatcherPriority.ApplicationIdle);
            return;
        }
        if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) is { } startupFile && File.Exists(startupFile))
            _ = OpenProjectAsync(startupFile);
        else
            ShowNewProjectDialog(firstRun: true);
    }

    private void Vm_ProjectReplaced(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshAll));
    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WorkspaceTabs)) return;
        Dispatcher.BeginInvoke(new Action(ApplyLocalization), DispatcherPriority.Loaded);
    }
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
        Loc.Apply(this);
    }

    private void RefreshStats()
    {
        int books = Vm.Project.Books.Count;
        int chapters = Vm.Project.Books.Sum(b => b.Chapters.Count);
        int scenes = Vm.Project.Scenes.Count;
        int drafted = Vm.Project.Scenes.Count(s => s.Status is "Drafted" or "Revised" or "Final");
        StatsText.Text = string.Join("\n", Loc.F("{0} book(s)", books), Loc.F("{0} chapter(s)", chapters), Loc.F("{0} scene card(s)", scenes), Loc.F("{0} plotline(s)", Vm.Project.Plotlines.Count), Loc.F("{0} character(s)", Vm.Project.Characters.Count), Loc.F("{0} place(s)", Vm.Project.Places.Count), Loc.F("{0} scenes drafted", drafted));
        double progress = Vm.Project.WordCountGoal <= 0 ? 0 : Math.Min(100, Vm.Project.CurrentWordCount * 100d / Vm.Project.WordCountGoal);
        ProgressBar.Value = progress;
        ProgressText.Text = Loc.F("{0:N0} / {1:N0} words ({2:0}%)", Vm.Project.CurrentWordCount, Vm.Project.WordCountGoal, progress);
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

        AddTimelineCell(new TextBlock { Text = Loc.T("PLOTLINE / CHAPTER"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8) }, 0, 0, false);
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
                if (stack.Children.Count == 0) stack.Children.Add(new TextBlock { Text = Loc.T("Drop a scene here or double-click"), Foreground = FindBrush("MutedBrush"), Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap });
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
        panel.Children.Add(new TextBlock { Text = Loc.T(scene.Status), FontSize = 11, Foreground = BrushFrom(plot.Color), Margin = new Thickness(0, 5, 0, 0) });
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
            Vm.Status = Loc.F("Autosaved locally at {0}", DateTime.Now.ToShortTimeString());
        }
        catch (Exception ex) { Vm.Status = Loc.F("Autosave failed: {0}", ex.Message); }
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
            if (firstRun) Vm.Status = Loc.T("Blank project ready");
            return;
        }
        var project = string.IsNullOrEmpty(dialog.SelectedTemplate!.FilePath) ? CreateBlankProject() : _templates.CreateFromTemplate(dialog.SelectedTemplate);
        Vm.ReplaceProject(project); RefreshAll();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = Loc.T("Open ArcTrellis project"), Filter = Loc.T("ArcTrellis projects (*.arctrellis)|*.arctrellis|All files (*.*)|*.*") };
        if (dialog.ShowDialog() == true) await OpenProjectAsync(dialog.FileName);
    }

    private async Task OpenProjectAsync(string path)
    {
        try
        {
            var project = await _projects.LoadAsync(path);
            string autosave = _projects.GetAutosavePath(project.Id);
            if (File.Exists(autosave) && File.GetLastWriteTimeUtc(autosave) > File.GetLastWriteTimeUtc(path) && MessageBox.Show(Loc.T("A newer local autosave exists. Recover it?"), Loc.T("Recover project"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                project = await _projects.LoadAsync(autosave);
            Vm.ReplaceProject(project, path); Vm.Status = Loc.F("Opened {0}", Path.GetFileName(path)); RefreshAll();
        }
        catch (Exception ex) { MessageBox.Show(Loc.F("Could not open the project.\n\n{0}", ex.Message), "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = Loc.T("Save reusable template"), Filter = Loc.T("ArcTrellis template (*.json)|*.json"), DefaultExt = ".json", AddExtension = true, InitialDirectory = _templates.UserTemplateDirectory, FileName = SafeName(Vm.Project.Title) + Loc.T(" Template") };
        if (dialog.ShowDialog() != true) return;
        try { _templates.SaveTemplate(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName), Loc.F("Custom template created from {0}", Vm.Project.Title), Vm.Project); Vm.Status = Loc.T("Reusable template saved"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Template save failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        string? path = Vm.FilePath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog { Title = Loc.T("Save ArcTrellis project"), Filter = Loc.T("ArcTrellis project (*.arctrellis)|*.arctrellis"), DefaultExt = ".arctrellis", AddExtension = true, FileName = SafeName(Vm.Project.Title) };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }
        try
        {
            await _projects.SaveAsync(Vm.Project, path!); Vm.FilePath = path; Vm.IsDirty = false; Vm.Status = Loc.F("Saved {0}", Path.GetFileName(path)); Title = Vm.WindowTitle; return true;
        }
        catch (Exception ex) { MessageBox.Show(Loc.F("Could not save the project.\n\n{0}", ex.Message), "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    private void ImportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = Loc.T("Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt") };
        if (dialog.ShowDialog() != true) return;
        try { Vm.ReplaceProject(_imports.ImportMarkdown(dialog.FileName)); Vm.MarkDirty(); RefreshAll(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Import failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportDocx_Click(object sender, RoutedEventArgs e) => ExportFile("Word document (*.docx)|*.docx", ".docx", _exports.ExportDocx);
    private void ExportMarkdown_Click(object sender, RoutedEventArgs e) => ExportFile("Markdown (*.md)|*.md", ".md", _exports.ExportMarkdown);
    private void ExportCsv_Click(object sender, RoutedEventArgs e) => ExportFile("CSV spreadsheet (*.csv)|*.csv", ".csv", _exports.ExportCsv);
    private void ExportFile(string filter, string extension, Action<StoryProject, string> export)
    {
        var dialog = new SaveFileDialog { Filter = Loc.T(filter), DefaultExt = extension, AddExtension = true, FileName = SafeName(Vm.Project.Title) };
        if (dialog.ShowDialog() != true) return;
        try { export(Vm.Project, dialog.FileName); Vm.Status = Loc.F("Exported {0}", Path.GetFileName(dialog.FileName)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportScrivener_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose a folder for the Scrivener project") };
        if (dialog.ShowDialog() != true) return;
        try { _exports.ExportScrivenerFolder(Vm.Project, dialog.FolderName); Vm.Status = Loc.T("Scrivener project exported"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Export failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
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
            case "Scene" or "Сцена": Vm.SelectedScene = Vm.Project.Scenes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 3; break;
            case "Character" or "Персонаж": Vm.SelectedCharacter = Vm.Project.Characters.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 4; break;
            case "Place" or "Место": Vm.SelectedPlace = Vm.Project.Places.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 5; break;
            case "Note" or "Заметка": Vm.SelectedNote = Vm.Project.Notes.FirstOrDefault(x => x.Id == result.ItemId); WorkspaceTabs.SelectedIndex = 6; break;
            case "Chapter" or "Глава": WorkspaceTabs.SelectedIndex = 2; break;
        }
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e) => SetTheme(false);
    private void DarkTheme_Click(object sender, RoutedEventArgs e) => SetTheme(true);
    private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en-US");
    private void RussianLanguage_Click(object sender, RoutedEventArgs e) => ChangeLanguage("ru-RU");
    private void ChangeLanguage(string language)
    {
        Loc.SetLanguage(language);
        Vm.RefreshLocalization();
        RefreshAll();
        ApplyLocalization();
        Dispatcher.BeginInvoke(new Action(() => { RefreshAll(); ApplyLocalization(); }), DispatcherPriority.Loaded);
    }

    private void RunUiSmoke(string reportPath)
    {
        try
        {
            var failures = new List<string>();
            ChangeLanguage("en-US");
            WorkspaceTabs.SelectedIndex = 3;
            ApplyLocalization();
            UpdateLayout();
            if ((WorkspaceTabs.Items[0] as TabItem)?.Header?.ToString() != "Dashboard") failures.Add("English tab localization failed");
            if (FileMenuItem.Header?.ToString() != "File" || LightThemeItem.Header?.ToString() != "Light theme") failures.Add("English menu localization failed");
            var englishText = CollectVisibleText(this).ToList();
            if (!englishText.Contains("Scene card") || !englishText.Contains("Status")) failures.Add("English deferred content localization failed");

            WorkspaceTabs.SelectedIndex = 0;
            RefreshStats();
            ApplyLocalization();
            UpdateLayout();
            if (!StatsText.Text.Contains("book(s)", StringComparison.Ordinal) || StatsText.Text.Contains("Книг:", StringComparison.Ordinal)) failures.Add("English dashboard statistics did not refresh immediately");

            ChangeLanguage("ru-RU");
            WorkspaceTabs.SelectedIndex = 0;
            UpdateLayout();
            if (!StatsText.Text.Contains("Книг:", StringComparison.Ordinal) || StatsText.Text.Contains("book(s)", StringComparison.Ordinal)) failures.Add("Russian dashboard statistics did not refresh immediately");
            WorkspaceTabs.SelectedIndex = 1;
            ApplyLocalization();
            TimelineBookCombo.ApplyTemplate();
            TimelinePlotlineCombo.ApplyTemplate();
            UpdateLayout();
            if ((WorkspaceTabs.Items[0] as TabItem)?.Header?.ToString() != "Обзор") failures.Add("Russian tab localization failed");
            var visibleText = CollectVisibleText(this).ToList();
            if (!visibleText.Contains("Книга:")) failures.Add("Deferred tab content was not translated");
            if (visibleText.Any(x => x.Contains("ArcTrellis.Core.Models", StringComparison.Ordinal))) failures.Add("A model class name is visible instead of its display member");
            if (MainMenu.ActualHeight < 10 || (MainMenu.Items[0] as MenuItem)?.ActualWidth < 20) failures.Add("Top menu is not visible");

            SetTheme(false, false);
            var lightMenu = (SolidColorBrush)Application.Current.Resources[SystemColors.MenuBrushKey];
            var lightMenuText = (SolidColorBrush)Application.Current.Resources[SystemColors.MenuTextBrushKey];
            if (lightMenu.Color == lightMenuText.Color) failures.Add("Light menu text has no contrast");
            var probeInput = new TextBox();
            probeInput.ApplyTemplate();
            if (probeInput.Padding != new Thickness(3, 4, 3, 4) || probeInput.MinHeight < 30) failures.Add("Single-line text input spacing is incorrect");
            var probeEditor = new TextBox { AcceptsReturn = true };
            probeEditor.ApplyTemplate();
            if (probeEditor.Padding != new Thickness(3, 4, 3, 4) || probeEditor.VerticalContentAlignment != VerticalAlignment.Top) failures.Add("Multiline text editor spacing is incorrect");

            SetTheme(true, false);
            FileMenuItem.IsSubmenuOpen = true;
            UpdateLayout();
            if (FileMenuItem.Template.FindName("PART_Popup", FileMenuItem) is System.Windows.Controls.Primitives.Popup { Child: Border popupContent })
            {
                popupContent.UpdateLayout();
                SaveVisualPng(popupContent, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-file-menu.png"));
                if (popupContent.Background is SolidColorBrush popupBackground && popupBackground.Color.R > 64) failures.Add("Dark submenu background is too light");
                if (FindVisualChildren<ScrollViewer>(popupContent).Any(x => x.ComputedVerticalScrollBarVisibility == Visibility.Visible)) failures.Add("Dark submenu shows an unnecessary scrollbar");
            }
            else failures.Add("Dark submenu popup was not created");
            FileMenuItem.IsSubmenuOpen = false;
            var previewOptions = new[] { new TemplateInfo("Blank project", "One book, one chapter, and a main plotline.", "Blank", "") };
            var preview = new NewProjectWindow(previewOptions) { Owner = this };
            preview.Show();
            preview.UpdateLayout();
            if (preview.Background is not SolidColorBrush previewBackground || previewBackground.Color.R > 64) failures.Add("Dark theme did not reach the template window");
            SaveVisualPng(preview, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-template-window.png"));
            preview.Close();
            if (((SolidColorBrush)Application.Current.Resources["InputBrush"]).Color == Colors.White) failures.Add("Dark input palette was not applied");
            WorkspaceTabs.SelectedIndex = 0;
            ChangeLanguage("ru-RU");
            UpdateLayout();
            SaveVisualPng(this, Path.Combine(Path.GetDirectoryName(reportPath)!, "ArcTrellis-dark-main-window.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(reportPath, "FAIL: " + ex); } catch { }
        }
    }

    private static IEnumerable<string> CollectVisibleText(DependencyObject root)
    {
        if (root is TextBlock { Text: { Length: > 0 } } text) yield return text.Text;
        int count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (int i = 0; i < count; i++)
            foreach (string value in CollectVisibleText(VisualTreeHelper.GetChild(root, i))) yield return value;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void SaveVisualPng(FrameworkElement visual, string path)
    {
        int width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private void ApplyLocalization()
    {
        EnglishLanguageItem.IsChecked = !Loc.IsRussian;
        RussianLanguageItem.IsChecked = Loc.IsRussian;
        Loc.Apply(this);
    }
    private void SetTheme(bool dark, bool persist = true)
    {
        _isDark = dark;
        var palette = dark
            ? new Dictionary<string, string> { ["AccentBrush"] = "#7895FF", ["AccentHoverBrush"] = "#8AA3FF", ["AccentPressedBrush"] = "#5A76E8", ["PageBrush"] = "#111722", ["PanelBrush"] = "#192130", ["ElevatedBrush"] = "#222C3D", ["InputBrush"] = "#121925", ["TextBrush"] = "#F2F5FA", ["MutedBrush"] = "#AAB5C6", ["BorderBrush"] = "#354154", ["HoverBrush"] = "#273247", ["SelectedBrush"] = "#304263", ["HeaderBrush"] = "#0E141E", ["DangerBrush"] = "#FF7188" }
            : new Dictionary<string, string> { ["AccentBrush"] = "#5B7CFA", ["AccentHoverBrush"] = "#6D89FA", ["AccentPressedBrush"] = "#4264DF", ["PageBrush"] = "#F4F6FA", ["PanelBrush"] = "#FFFFFF", ["ElevatedBrush"] = "#FFFFFF", ["InputBrush"] = "#FFFFFF", ["TextBrush"] = "#202638", ["MutedBrush"] = "#667085", ["BorderBrush"] = "#D8DEEA", ["HoverBrush"] = "#EEF2FF", ["SelectedBrush"] = "#E2E9FF", ["HeaderBrush"] = "#FFFFFF", ["DangerBrush"] = "#D84B63" };
        foreach (var (key, color) in palette) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        SolidColorBrush ThemeBrush(string key) => new(((SolidColorBrush)Application.Current.Resources[key]).Color);
        Application.Current.Resources[SystemColors.MenuBrushKey] = ThemeBrush("ElevatedBrush");
        Application.Current.Resources[SystemColors.MenuTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.HighlightBrushKey] = ThemeBrush("SelectedBrush");
        Application.Current.Resources[SystemColors.HighlightTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.ControlBrushKey] = ThemeBrush("ElevatedBrush");
        Application.Current.Resources[SystemColors.ControlTextBrushKey] = ThemeBrush("TextBrush");
        Application.Current.Resources[SystemColors.WindowBrushKey] = ThemeBrush("PageBrush");
        Application.Current.Resources[SystemColors.WindowTextBrushKey] = ThemeBrush("TextBrush");
        LightThemeItem.IsChecked = !dark;
        DarkThemeItem.IsChecked = dark;
        ApplyWindowChromeTheme();
        if (persist)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ThemeSettingsPath)!); File.WriteAllText(ThemeSettingsPath, dark ? "dark" : "light"); } catch { }
        }
        BuildTimeline();
    }

    private static bool LoadDarkTheme()
    {
        try { return File.Exists(ThemeSettingsPath) && File.ReadAllText(ThemeSettingsPath).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void ApplyWindowChromeTheme()
    {
        ThemeChrome.Apply(this, _isDark);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(TimelineGrid, Vm.Project.Title + " — " + Loc.T("Timeline"));
    }
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Docs", Loc.IsRussian ? "USER_GUIDE.ru.md" : "USER_GUIDE.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("ArcTrellis 1.1.4\n\n" + Loc.T("A private, local-first visual story planner for Windows.\nNo cloud account, tracking, or network connection required."), Loc.T("About ArcTrellis"), MessageBoxButton.OK, MessageBoxImage.Information);
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
        var result = MessageBox.Show(Loc.T("Save changes before closing?"), "ArcTrellis", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.No) return;
        e.Cancel = true;
        if (await SaveAsync(false)) { _closingAfterSave = true; Close(); }
    }

    private bool ConfirmDiscard()
    {
        if (!Vm.IsDirty) return true;
        var result = MessageBox.Show(Loc.T("This project has unsaved changes. Continue and discard them?"), "ArcTrellis", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
    private static bool ConfirmDelete(string item) => MessageBox.Show(Loc.F("Delete this {0}?", Loc.T(item)), "ArcTrellis", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private StoryProject CreateBlankProject()
    {
        var project = _templates.CreateBlank();
        project.Title = Loc.T(project.Title);
        project.Books[0].Title = Loc.T(project.Books[0].Title);
        project.Books[0].Chapters[0].Title = Loc.T(project.Books[0].Chapters[0].Title);
        project.Books[0].Chapters[0].Section = Loc.T(project.Books[0].Chapters[0].Section);
        project.Plotlines[0].Name = Loc.T(project.Plotlines[0].Name);
        return project;
    }
    private static string SafeName(string name) => string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) is { Length: > 0 } value ? value : "Story";
    private Brush FindBrush(string key) => (Brush)FindResource(key);
    private static SolidColorBrush BrushFrom(string color) => new(BrushColor(color));
    private static Color BrushColor(string color) { try { return (Color)ColorConverter.ConvertFromString(color); } catch { return Colors.SlateBlue; } }
}
