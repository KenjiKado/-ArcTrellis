using System.Windows;
using System.Windows.Media;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App.Views;

public partial class EditPlotlineWindow : Window
{
    public string PlotlineTitle => TitleInput.Text;
    public string PlotlineDescription => DescriptionInput.Text;
    public string PlotlineColor { get; private set; }

    public EditPlotlineWindow(Plotline plotline)
    {
        InitializeComponent();
        Title = Loc.T("Edit plotline");
        TitleInput.Text = plotline.Name;
        DescriptionInput.Text = plotline.Description;
        PlotlineColor = plotline.Color;
        UpdatePreview();
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
        Loaded += (_, _) => { Loc.Apply(this); TitleInput.Focus(); TitleInput.SelectAll(); };
    }

    private Color PreviewColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(PlotlineColor); }
        catch { return Color.FromRgb(91, 124, 250); }
    }
    private void UpdatePreview() => ColorPreview.Background = new SolidColorBrush(PreviewColor());
    private void ChooseColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(PreviewColor()) { Owner = this };
        if (picker.ShowDialog() != true) return;
        PlotlineColor = picker.SelectedColor;
        UpdatePreview();
    }
    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
