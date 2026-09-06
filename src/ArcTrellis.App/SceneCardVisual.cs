using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App;

internal static class SceneCardVisual
{
    internal static Border Create(Scene scene, Color accent)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = scene.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(scene.Summary)) panel.Children.Add(new TextBlock { Text = scene.Summary, TextWrapping = TextWrapping.Wrap, MaxHeight = 54, Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = Loc.T(scene.Status), FontSize = 11, Foreground = new SolidColorBrush(accent), Margin = new Thickness(0, 5, 0, 0) });
        var card = new Border { Tag = scene, Child = panel, Background = new SolidColorBrush(Color.FromArgb(24, accent.R, accent.G, accent.B)), BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(5), Margin = new Thickness(2, 3, 2, 3), Padding = new Thickness(9), Cursor = Cursors.Hand };
        return card;
    }
}
