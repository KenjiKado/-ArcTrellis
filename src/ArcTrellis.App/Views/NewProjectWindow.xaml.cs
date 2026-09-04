using System.Windows;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App.Views;

public partial class NewProjectWindow : Window
{
    public TemplateInfo? SelectedTemplate { get; private set; }

    public NewProjectWindow(IEnumerable<TemplateInfo> templates)
    {
        InitializeComponent();
        TemplateList.ItemsSource = templates;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = TemplateList.SelectedItem as TemplateInfo;
        if (SelectedTemplate is null) return;
        DialogResult = true;
    }
}
