using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArcTrellis.App.Views;

/// <summary>A searchable relation-type multiselect with preset and custom values.</summary>
internal sealed class RelationTypeInput : UserControl
{
    private readonly ObservableCollection<string> _selected;
    private readonly List<string> _options = [];
    private readonly Dictionary<string, CheckBox> _checks =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, Border> _chips =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly WrapPanel _row = new();
    private readonly StackPanel _choiceRows = new();
    private readonly Border _dropdown =
        new() { BorderThickness = new Thickness(1), Padding = new Thickness(4), CornerRadius = new CornerRadius(4) };
    private readonly Popup _popup =
        new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, Focusable = false };
    private readonly Button _customChoice = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(7, 5, 7, 5),
        Cursor = Cursors.Hand,
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _empty =
        new() { Text = Loc.T("No matches"), Margin = new Thickness(7), Visibility = Visibility.Collapsed };
    private bool _removingChip, _fieldWasOpenOnPress, _closedOnFieldPress, _synchronizing;

    internal TextBox Input { get; } = new()
    {
        Tag = "RelationTypeDraft",
        Width = 145,
        MinHeight = 28,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Margin = new Thickness(2)
    };
    internal IReadOnlyList<string> Choices => _options;
    internal IReadOnlyCollection<string> SelectedValues => _selected;
    internal bool IsOpen => _popup.IsOpen;
    internal event EventHandler? SelectionChanged;

    internal RelationTypeInput(IEnumerable<string> options, ObservableCollection<string> selected)
    {
        _selected = selected;
        AutomationProperties.SetName(Input, Loc.T("Relation type"));

        var layout = new StackPanel();
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            MinHeight = 40,
            Cursor = Cursors.IBeam
        };
        frame.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var content = new DockPanel();
        var arrow = CreateArrow(Loc.T("Relation type"));
        DockPanel.SetDock(arrow, Dock.Right);
        content.Children.Add(arrow);
        _row.Children.Add(Input);
        content.Children.Add(_row);
        frame.Child = content;

        _popup.PlacementTarget = frame;
        _popup.Child = _dropdown;
        _dropdown.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = frame });
        DropdownChrome.SetCompact(_dropdown, true);
        _dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        _dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        foreach (string option in options
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase))
            AddChoice(option);

        _customChoice.Click += (_, _) => AddCustom(Input.Text);
        _choiceRows.Children.Add(_customChoice);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _choiceRows.Children.Add(_empty);
        _dropdown.Child = new ScrollViewer
        {
            Content = _choiceRows,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        layout.Children.Add(frame);
        layout.Children.Add(_popup);
        Content = layout;

        Input.TextChanged += (_, _) => FilterChoices();
        Input.GotKeyboardFocus += (_, _) =>
        {
            FilterChoices();
            if (!_removingChip && Mouse.LeftButton != MouseButtonState.Pressed)
                _popup.IsOpen = true;
        };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Back && Input.Text.Length == 0 &&
                Keyboard.Modifiers == ModifierKeys.None && _selected.Count > 0)
            {
                Remove(_selected[^1]);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                string value = Input.Text.Trim();
                if (value.Length > 0)
                {
                    string? existing = _options.FirstOrDefault(option =>
                        string.Equals(option, value, StringComparison.CurrentCultureIgnoreCase));
                    if (existing is null) AddCustom(value);
                    else _checks[existing].IsChecked = true;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                var first = _checks.Values.FirstOrDefault(check => check.Visibility == Visibility.Visible);
                if (first is not null) first.Focus();
                else if (_customChoice.Visibility == Visibility.Visible) _customChoice.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseDropdown();
                e.Handled = true;
            }
        };

        bool FieldHit(DependencyObject? source) =>
            ReferenceEquals(frame, source) || DropdownChrome.Contains(Input, source);
        frame.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && FieldHit(e.OriginalSource as DependencyObject))
                _fieldWasOpenOnPress = IsOpen;
        }), true);
        frame.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || !FieldHit(e.OriginalSource as DependencyObject)) return;
            bool close = IsOpen || _fieldWasOpenOnPress || _closedOnFieldPress;
            _fieldWasOpenOnPress = _closedOnFieldPress = false;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Input.IsKeyboardFocusWithin) return;
                if (close) CloseDropdown();
                else
                {
                    FilterChoices();
                    _popup.IsOpen = true;
                }
            }), DispatcherPriority.Background);
        }), true);
        frame.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                DropdownChrome.Contains(arrow, e.OriginalSource as DependencyObject) ||
                _chips.Values.Any(chip => DropdownChrome.Contains(chip, e.OriginalSource as DependencyObject))) return;
            Input.Focus();
            Input.CaretIndex = Input.Text.Length;
            e.Handled = true;
        };

        bool arrowWasOpenOnPress = false, closedOnArrowPress = false;
        arrow.PreviewMouseLeftButtonDown += (_, _) => arrowWasOpenOnPress = IsOpen;
        _popup.Closed += (_, _) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) return;
            if (DropdownChrome.PointerWithin(arrow)) closedOnArrowPress = true;
            else if (DropdownChrome.PointerWithin(frame) &&
                     !_chips.Values.Any(DropdownChrome.PointerWithin))
                _closedOnFieldPress = true;
        };
        arrow.Click += (_, _) =>
        {
            bool close = IsOpen || arrowWasOpenOnPress || closedOnArrowPress;
            arrowWasOpenOnPress = closedOnArrowPress = false;
            if (close) CloseDropdown();
            else
            {
                Input.Focus();
                FilterChoices();
                _popup.IsOpen = true;
            }
        };

        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsKeyboardFocusWithin && !_dropdown.IsKeyboardFocusWithin &&
                !DropdownChrome.PointerWithin(_dropdown)) CloseDropdown();
        }), DispatcherPriority.Input);
        Unloaded += (_, _) => CloseDropdown();

        RenderChips();
        FilterChoices();
    }

    internal void Select(string value)
    {
        string clean = value.Trim();
        if (clean.Length == 0) return;
        string? existing = _options.FirstOrDefault(option =>
            string.Equals(option, clean, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null)
        {
            AddChoice(clean);
            existing = clean;
        }
        _checks[existing].IsChecked = true;
    }

    internal void CloseDropdown() => _popup.IsOpen = false;

    private void AddChoice(string value)
    {
        if (_checks.ContainsKey(value)) return;
        _options.Add(value);
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var check = new CheckBox
        {
            Tag = value,
            IsChecked = _selected.Contains(value, StringComparer.CurrentCultureIgnoreCase),
            Margin = new Thickness(3),
            Cursor = Cursors.Hand,
            Content = text
        };
        check.Checked += (_, _) =>
        {
            if (_synchronizing) return;
            if (!_selected.Contains(value, StringComparer.CurrentCultureIgnoreCase))
                _selected.Add(value);
            RenderChips();
            Input.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        check.Unchecked += (_, _) =>
        {
            if (_synchronizing) return;
            foreach (string match in _selected
                .Where(item => string.Equals(item, value, StringComparison.CurrentCultureIgnoreCase)).ToList())
                _selected.Remove(match);
            RenderChips();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _checks.Add(value, check);
        _choiceRows.Children.Insert(Math.Max(0, _choiceRows.Children.Count - 2), check);
    }

    private void AddCustom(string value)
    {
        string clean = value.Trim();
        if (clean.Length == 0) return;
        string? existing = _options.FirstOrDefault(option =>
            string.Equals(option, clean, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null)
        {
            AddChoice(clean);
            existing = clean;
        }
        _checks[existing].IsChecked = true;
        Input.Clear();
        FilterChoices();
        _popup.IsOpen = true;
    }

    private void Remove(string value)
    {
        if (_checks.TryGetValue(value, out var check)) check.IsChecked = false;
    }

    private void FilterChoices()
    {
        string query = Input.Text.Trim();
        foreach (string option in _options)
            _checks[option].Visibility =
                option.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        bool exact = _options.Any(option =>
            string.Equals(option, query, StringComparison.CurrentCultureIgnoreCase));
        _customChoice.Content = "＋ " + query;
        _customChoice.Visibility = query.Length > 0 && !exact
            ? Visibility.Visible : Visibility.Collapsed;
        bool any = _checks.Values.Any(check => check.Visibility == Visibility.Visible) ||
                   _customChoice.Visibility == Visibility.Visible;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderChips()
    {
        foreach (var old in _chips.Where(pair =>
            !_selected.Contains(pair.Key, StringComparer.CurrentCultureIgnoreCase)).ToList())
        {
            _row.Children.Remove(old.Value);
            _chips.Remove(old.Key);
        }
        foreach (string value in _selected.Where(value => !_chips.ContainsKey(value)).ToList())
        {
            if (!_checks.ContainsKey(value)) AddChoice(value);
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            var label = new TextBlock
            {
                Text = value,
                MaxWidth = 220,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = value
            };
            chipRow.Children.Add(label);
            var remove = new Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(5, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = value
            };
            AutomationProperties.SetName(remove, Loc.T("Remove selection") + ": " + value);
            remove.Click += (_, _) =>
            {
                _removingChip = true;
                Remove(value);
                CloseDropdown();
                _fieldWasOpenOnPress = _closedOnFieldPress = false;
                Dispatcher.BeginInvoke(new Action(() => _removingChip = false),
                    DispatcherPriority.ContextIdle);
            };
            chipRow.Children.Add(remove);
            var chip = new Border
            {
                Child = chipRow,
                Padding = new Thickness(7, 3, 4, 3),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(12)
            };
            chip.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            _chips.Add(value, chip);
            _row.Children.Insert(_row.Children.Count - 1, chip);
        }
    }

    private static Button CreateArrow(string automationName)
    {
        var arrow = new Button
        {
            Width = 24,
            Height = 28,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand
        };
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var glyph = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        glyph.SetValue(FrameworkElement.WidthProperty, 8d);
        glyph.SetValue(FrameworkElement.HeightProperty, 5d);
        glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetValue(System.Windows.Shapes.Path.DataProperty,
            Geometry.Parse("M 0 0 L 4 4 L 8 0 Z"));
        glyph.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "MutedBrush");
        surface.AppendChild(glyph);
        arrow.Template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        AutomationProperties.SetName(arrow, automationName);
        return arrow;
    }
}
