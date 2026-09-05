using System.Windows;
using System.Windows.Media;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App.Views;

public partial class EditBookWindow : Window
{
    public string BookTitle => TitleInput.Text;
    public string BookSubtitle => SubtitleInput.Text;

    public EditBookWindow(Book book)
    {
        InitializeComponent();
        TitleInput.Text = book.Title;
        SubtitleInput.Text = book.Subtitle;
        SourceInitialized += (_, _) => ThemeChrome.Apply(this,
            Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
        Loaded += (_, _) => { Loc.Apply(this); TitleInput.Focus(); TitleInput.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
