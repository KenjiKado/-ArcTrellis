using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArcTrellis.App;

/// <summary>A searchable multiple-choice field for filter drafts. It never creates story data.</summary>
internal sealed class MultiChoiceInput : UserControl
{
    private readonly HashSet<Guid> _selected;
    private readonly List<(Guid Id, string Title)> _options;
    private readonly Dictionary<Guid, CheckBox> _checks = [];
    private readonly Dictionary<Guid, Border> _chips = [];
    private readonly WrapPanel _row = new();
    private readonly Border _dropdown = new() { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1), Padding = new Thickness(4), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _empty = new() { Text = Loc.T("No matches"), Margin = new Thickness(6), Visibility = Visibility.Collapsed };
    internal TextBox Input { get; } = new() { Tag = "FilterDraft", Width = 145, MinHeight = 28, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(2) };
    internal IReadOnlyCollection<Guid> SelectedIds => _selected;
    internal IReadOnlyDictionary<Guid, CheckBox> Choices => _checks;
    internal bool IsOpen => _dropdown.Visibility == Visibility.Visible;

    internal MultiChoiceInput(string label, IEnumerable<(Guid Id, string Title)> options, IEnumerable<Guid> selected)
    {
        _options = options.ToList();
        _selected = selected.Where(id => _options.Any(option => option.Id == id)).ToHashSet();
        AutomationProperties.SetName(Input, Loc.T(label));
        var layout = new StackPanel();
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), MinHeight = 40, Cursor = Cursors.IBeam };
        frame.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var content = new DockPanel();
        var arrow = new Button { Content = "▾", Width = 24, Height = 24, MinHeight = 0, Padding = new Thickness(2), Margin = new Thickness(2), Focusable = false, Cursor = Cursors.Hand };
        AutomationProperties.SetName(arrow, Loc.T(label));
        DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow);
        _row.Children.Add(Input); content.Children.Add(_row); frame.Child = content;
        layout.Children.Add(frame); layout.Children.Add(_dropdown); Content = layout;
        var rows = new StackPanel();
        foreach (var option in _options)
        {
            Guid id = option.Id;
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
            text.SetBinding(TextBlock.TextProperty, new Binding { Source = option.Title });
            var check = new CheckBox { Tag = id, IsChecked = _selected.Contains(id), Margin = new Thickness(3), Cursor = Cursors.Hand, Content = text };
            check.Checked += (_, _) => { _selected.Add(id); RenderChips(); };
            check.Unchecked += (_, _) => { _selected.Remove(id); RenderChips(); };
            _checks.Add(id, check); rows.Children.Add(check);
        }
        rows.Children.Add(_empty);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        _dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _dropdown.Child = new ScrollViewer { Content = rows, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Input.TextChanged += (_, _) => FilterChoices();
        Input.GotKeyboardFocus += (_, _) => { FilterChoices(); _dropdown.Visibility = Visibility.Visible; };
        Input.PreviewKeyDown += (_, e) =>
        {
            var first = _checks.Values.FirstOrDefault(check => check.Visibility == Visibility.Visible);
            if (e.Key == Key.Down && first is not null) { first.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                var unselected = _checks.Values.FirstOrDefault(check => check.Visibility == Visibility.Visible && check.IsChecked != true);
                if (unselected is not null) unselected.IsChecked = true;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { _dropdown.Visibility = Visibility.Collapsed; e.Handled = true; }
        };
        frame.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Input.Focus(); Input.CaretIndex = Input.Text.Length; _dropdown.Visibility = Visibility.Visible; e.Handled = true;
        };
        arrow.Click += (_, _) =>
        {
            if (IsOpen) _dropdown.Visibility = Visibility.Collapsed;
            else { Input.Focus(); FilterChoices(); _dropdown.Visibility = Visibility.Visible; }
        };
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        { if (!IsKeyboardFocusWithin) _dropdown.Visibility = Visibility.Collapsed; }), DispatcherPriority.Input);
        RenderChips();
    }

    private void FilterChoices()
    {
        string query = Input.Text.Trim();
        foreach (var option in _options)
            _checks[option.Id].Visibility = option.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = _checks.Values.Any(check => check.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderChips()
    {
        foreach (var old in _chips.Where(pair => !_selected.Contains(pair.Key)).ToList())
        { _row.Children.Remove(old.Value); _chips.Remove(old.Key); }
        foreach (var option in _options.Where(option => _selected.Contains(option.Id) && !_chips.ContainsKey(option.Id)))
        {
            Guid id = option.Id;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var text = new TextBlock { MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = option.Title };
            text.SetBinding(TextBlock.TextProperty, new Binding { Source = option.Title }); row.Children.Add(text);
            var remove = new Button { Content = "×", Width = 20, Height = 20, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand, Tag = id };
            AutomationProperties.SetName(remove, Loc.T("Remove selection") + ": " + option.Title);
            remove.Click += (_, _) => { _checks[id].IsChecked = false; Input.Focus(); };
            row.Children.Add(remove);
            var chip = new Border { Child = row, Padding = new Thickness(7, 3, 4, 3), Margin = new Thickness(2), CornerRadius = new CornerRadius(12) };
            chip.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            _chips.Add(id, chip); _row.Children.Insert(_row.Children.Count - 1, chip);
        }
    }
}
