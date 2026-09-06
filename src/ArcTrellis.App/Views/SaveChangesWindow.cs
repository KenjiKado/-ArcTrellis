using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcTrellis.App.Views;

public sealed class SaveChangesWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;
    public SaveChangesWindow()
    {
        Title = "ArcTrellis"; Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Save changes before closing?"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, result) in new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No) })
        {
            var button = new Button { Content = Loc.T(label), MinWidth = 76 };
            button.Click += (_, _) => { Result = result; DialogResult = true; };
            actions.Children.Add(button);
        }
        var cancel = new Button { Content = Loc.T("Cancel"), MinWidth = 76, IsCancel = true, IsDefault = true };
        actions.Children.Add(cancel); panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) => cancel.Focus();
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
    }
}
