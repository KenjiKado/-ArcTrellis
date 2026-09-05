using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcTrellis.App.Views;

public sealed class ColorPickerWindow : Window
{
    private readonly Slider _red = Channel();
    private readonly Slider _green = Channel();
    private readonly Slider _blue = Channel();
    private readonly Border _preview = new() { Height = 42, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 12, 0, 12) };
    public string SelectedColor => $"#{(byte)_red.Value:X2}{(byte)_green.Value:X2}{(byte)_blue.Value:X2}";

    public ColorPickerWindow(Color initial)
    {
        Title = Loc.T("Choose color"); Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        var palette = new WrapPanel();
        foreach (string hex in new[] { "#5B7CFA", "#D9577A", "#2E9D78", "#E39B35", "#8A63D2", "#3B9AB2", "#EF4444", "#EC4899", "#84CC16", "#FACC15", "#FFFFFF", "#64748B" })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button { Width = 52, Height = 34, Background = new SolidColorBrush(color), ToolTip = hex };
            System.Windows.Automation.AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) => SetColor(color);
            palette.Children.Add(swatch);
        }
        panel.Children.Add(palette); panel.Children.Add(_preview);
        foreach (var (label, slider) in new[] { ("Red", _red), ("Green", _green), ("Blue", _blue) })
        {
            panel.Children.Add(new TextBlock { Text = Loc.T(label), Margin = new Thickness(0, 5, 0, 3) });
            System.Windows.Automation.AutomationProperties.SetName(slider, Loc.T(label));
            panel.Children.Add(slider);
            slider.ValueChanged += (_, _) => UpdatePreview();
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var save = new Button { Content = Loc.T("Save"), IsDefault = true };
        save.Click += (_, _) => DialogResult = true;
        actions.Children.Add(save); actions.Children.Add(new Button { Content = Loc.T("Cancel"), IsCancel = true });
        panel.Children.Add(actions); Content = panel; SetColor(initial);
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
    }

    private static Slider Channel() => new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 2, 0, 6) };
    private void SetColor(Color color) { _red.Value = color.R; _green.Value = color.G; _blue.Value = color.B; UpdatePreview(); }
    private void UpdatePreview() => _preview.Background = new SolidColorBrush(Color.FromRgb((byte)_red.Value, (byte)_green.Value, (byte)_blue.Value));
}
