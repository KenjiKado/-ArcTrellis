using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcTrellis.App.Views;

public sealed class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow(string item)
    {
        Title = "ArcTrellis"; Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = Loc.F("Delete this {0}?", Loc.T(item)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var yes = new Button { Content = Loc.T("Yes"), MinWidth = 76 };
        yes.Click += (_, _) => DialogResult = true;
        var no = new Button { Content = Loc.T("No"), MinWidth = 76, IsCancel = true, IsDefault = true };
        actions.Children.Add(yes); actions.Children.Add(no);
        panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) => no.Focus();
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
    }
}
