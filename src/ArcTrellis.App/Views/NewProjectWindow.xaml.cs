using System.Windows;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App.Views;

public partial class NewProjectWindow : Window
{
    public TemplateInfo? SelectedTemplate { get; private set; }

    public NewProjectWindow(IEnumerable<TemplateInfo> templates)
    {
        InitializeComponent();
        TemplateList.ItemsSource = templates.Select(t => new TemplateInfo(Loc.T(t.Name), Loc.T(t.Description), Loc.T(t.Category), t.FilePath)).ToList();
        Loaded += (_, _) => Loc.Apply(this);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = TemplateList.SelectedItem as TemplateInfo;
        if (SelectedTemplate is null) return;
        DialogResult = true;
    }
}
