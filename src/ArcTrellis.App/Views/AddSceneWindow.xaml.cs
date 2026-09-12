using System.Windows;
using System.Windows.Media;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App.Views;

public partial class AddSceneWindow : Window
{
    public string SceneTitle => TitleInput.Text.Trim();
    public string SceneStatus => (string)StatusInput.SelectedValue;
    public Guid PlotlineId => (Guid)PlotlineInput.SelectedValue;

    public AddSceneWindow(string title, IEnumerable<SceneStatusOption> statuses, IEnumerable<Plotline> plotlines, Guid? selectedPlotlineId)
    {
        InitializeComponent();
        Title = Loc.T("Add Scene");
        TitleInput.Text = title;
        StatusInput.ItemsSource = statuses;
        StatusInput.SelectedValue = "Planned";
        var available = plotlines.OrderBy(p => p.Order).ToList();
        PlotlineInput.ItemsSource = available;
        PlotlineInput.SelectedValue = available.FirstOrDefault(p => p.Id == selectedPlotlineId)?.Id ?? available.FirstOrDefault()?.Id;
        TitleInput.TextChanged += (_, _) => UpdateSaveEnabled();
        StatusInput.SelectionChanged += (_, _) => UpdateSaveEnabled();
        PlotlineInput.SelectionChanged += (_, _) => UpdateSaveEnabled();
        UpdateSaveEnabled();
        _ = new TextUndoController(this, () => 0);
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
        Loaded += (_, _) => { Loc.Apply(this); TitleInput.Focus(); TitleInput.CaretIndex = TitleInput.Text.Length; TitleInput.SelectionLength = 0; };
    }
    private void UpdateSaveEnabled() => SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleInput.Text)
        && StatusInput.SelectedItem is SceneStatusOption && PlotlineInput.SelectedItem is Plotline;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveButton.IsEnabled) DialogResult = true;
    }
}
