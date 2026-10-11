using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArcTrellis.App.Views;

internal sealed record RelationTypeChoice(string Group, string Value);

/// <summary>A searchable, grouped single-select that also accepts a custom relation type.</summary>
internal sealed class RelationTypeInput : UserControl
{
    private readonly List<RelationTypeChoice> _options = [];
    private readonly Dictionary<string, Button> _buttons =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, StackPanel> _groupPanels =
        new(StringComparer.CurrentCultureIgnoreCase);
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
        Padding = new Thickness(7, 6, 7, 6),
        Cursor = Cursors.Hand,
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _empty =
        new() { Text = Loc.T("No matches"), Margin = new Thickness(7), Visibility = Visibility.Collapsed };
    private readonly Button _clear;
    private bool _committing, _fieldWasOpenOnPress, _closedOnFieldPress;

    internal TextBox Input { get; } = new()
    {
        Tag = "RelationTypeDraft",
        MaxLength = 15,
        MinHeight = 28,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Margin = new Thickness(2)
    };
    internal IReadOnlyList<string> Choices => _options.Select(option => option.Value).ToList();
    internal IReadOnlyList<string> Groups => _options.Select(option => option.Group)
        .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
    internal IReadOnlyCollection<string> SelectedValues =>
        SelectedValue is null ? [] : [SelectedValue];
    internal string? SelectedValue { get; private set; }
    internal bool IsOpen => _popup.IsOpen;
    internal event EventHandler? SelectionChanged;

    internal RelationTypeInput(IEnumerable<RelationTypeChoice> options)
    {
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
        _clear = CreateClear();
        DockPanel.SetDock(_clear, Dock.Right);
        content.Children.Add(_clear);
        content.Children.Add(Input);
        frame.Child = content;

        _customChoice.Click += (_, _) => SelectCustom();
        _choiceRows.Children.Add(_customChoice);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _choiceRows.Children.Add(_empty);

        foreach (var option in options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => new RelationTypeChoice(option.Group, option.Value.Trim()))
            .DistinctBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase))
            AddChoice(option);

        _popup.PlacementTarget = frame;
        _popup.Child = _dropdown;
        _dropdown.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = frame });
        DropdownChrome.SetCompact(_dropdown, true);
        _dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        _dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _dropdown.Child = new ScrollViewer
        {
            Content = _choiceRows,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        layout.Children.Add(frame);
        layout.Children.Add(_popup);
        Content = layout;

        Input.TextChanged += (_, _) =>
        {
            if (_committing) return;
            if (SelectedValue is not null &&
                !string.Equals(SelectedValue, Input.Text, StringComparison.CurrentCulture))
            {
                SelectedValue = null;
                UpdateClear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            FilterChoices(Input.Text);
            if (IsLoaded && Input.IsKeyboardFocusWithin) _popup.IsOpen = true;
        };
        Input.GotKeyboardFocus += (_, _) =>
        {
            FilterChoices(SelectedValue is null ? Input.Text : "");
            if (Mouse.LeftButton != MouseButtonState.Pressed) _popup.IsOpen = true;
        };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                string value = Input.Text.Trim();
                if (value.Length > 0)
                {
                    string? exact = _options.Select(option => option.Value).FirstOrDefault(option =>
                        string.Equals(option, value, StringComparison.CurrentCultureIgnoreCase));
                    if (exact is not null) Select(exact);
                    else SelectCustom();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                var first = _buttons.Values.FirstOrDefault(button => button.Visibility == Visibility.Visible);
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
                else OpenDropdown();
            }), DispatcherPriority.Background);
        }), true);
        frame.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                DropdownChrome.Contains(arrow, e.OriginalSource as DependencyObject) ||
                DropdownChrome.Contains(_clear, e.OriginalSource as DependencyObject)) return;
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
            else if (DropdownChrome.PointerWithin(frame)) _closedOnFieldPress = true;
        };
        arrow.Click += (_, _) =>
        {
            bool close = IsOpen || arrowWasOpenOnPress || closedOnArrowPress;
            arrowWasOpenOnPress = closedOnArrowPress = false;
            if (close) CloseDropdown();
            else
            {
                Input.Focus();
                OpenDropdown();
            }
        };
        _clear.Click += (_, _) =>
        {
            ClearSelection();
            CloseDropdown();
        };
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsKeyboardFocusWithin && !_dropdown.IsKeyboardFocusWithin &&
                !DropdownChrome.PointerWithin(_dropdown)) CloseDropdown();
        }), DispatcherPriority.Input);
        Unloaded += (_, _) => CloseDropdown();

        FilterChoices("");
        UpdateClear();
    }

    internal void Select(string value)
    {
        string clean = value.Trim();
        if (clean.Length == 0) return;
        string? existing = _options.Select(option => option.Value).FirstOrDefault(option =>
            string.Equals(option, clean, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null)
        {
            AddChoice(new RelationTypeChoice("CUSTOM", clean));
            existing = clean;
        }
        _committing = true;
        Input.Text = existing;
        Input.CaretIndex = Input.Text.Length;
        Input.SelectionLength = 0;
        _committing = false;
        SelectedValue = existing;
        UpdateClear();
        CloseDropdown();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OpenForTest() => OpenDropdown();
    internal void CloseDropdown() => _popup.IsOpen = false;

    private void OpenDropdown()
    {
        FilterChoices("");
        _popup.IsOpen = IsLoaded;
    }

    private void SelectCustom()
    {
        string clean = Input.Text.Trim();
        if (clean.Length == 0) return;
        Select(clean);
    }

    private void ClearSelection()
    {
        _committing = true;
        Input.Clear();
        _committing = false;
        SelectedValue = null;
        UpdateClear();
        FilterChoices("");
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddChoice(RelationTypeChoice option)
    {
        if (_buttons.ContainsKey(option.Value)) return;
        _options.Add(option);
        var group = EnsureGroup(option.Group);
        var text = new TextBlock
        {
            Text = option.Value,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 430
        };
        var button = new Button
        {
            Tag = option.Value,
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => Select(option.Value);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            Select(option.Value);
            e.Handled = true;
        };
        _buttons.Add(option.Value, button);
        group.Children.Add(button);
    }

    private StackPanel EnsureGroup(string name)
    {
        if (_groupPanels.TryGetValue(name, out var existing)) return existing;
        var panel = new StackPanel();
        var header = new TextBlock
        {
            Text = name.ToUpperInvariant(),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 9, 7, 3)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(header);
        _groupPanels.Add(name, panel);
        _choiceRows.Children.Insert(Math.Max(0, _choiceRows.Children.Count - 2), panel);
        return panel;
    }

    private void FilterChoices(string text)
    {
        string query = text.Trim();
        foreach (var option in _options)
            _buttons[option.Value].Visibility =
                option.Value.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in _groupPanels)
            pair.Value.Visibility = pair.Value.Children.OfType<Button>()
                .Any(button => button.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
        bool exact = _options.Any(option =>
            string.Equals(option.Value, query, StringComparison.CurrentCultureIgnoreCase));
        _customChoice.Content = "＋ Create \"" + query + "\"";
        _customChoice.Visibility = query.Length > 0 && !exact
            ? Visibility.Visible : Visibility.Collapsed;
        bool any = _groupPanels.Values.Any(panel => panel.Visibility == Visibility.Visible) ||
                   _customChoice.Visibility == Visibility.Visible;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateClear() =>
        _clear.Visibility = SelectedValue is null ? Visibility.Collapsed : Visibility.Visible;

    private static Button CreateClear()
    {
        var clear = new Button
        {
            Content = "×",
            Width = 24,
            Height = 28,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(clear, Loc.T("Clear"));
        return clear;
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
