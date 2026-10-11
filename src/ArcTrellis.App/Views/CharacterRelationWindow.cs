using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App.Views;

internal sealed class CharacterTargetInput : UserControl
{
    private readonly List<StoryEntity> _options;
    private readonly StackPanel _layout = new();
    private readonly Border _frame = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(4), MinHeight = 40 };
    private readonly ListBox _suggestions = new() { MaxHeight = 210, BorderThickness = new Thickness(0), DisplayMemberPath = nameof(StoryEntity.Name) };
    private readonly Border _dropdown;
    private readonly Popup _popup = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, Focusable = false };
    private bool _committing;

    internal TextBox Input { get; } = new()
    {
        Tag = "RelationTargetDraft",
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(5, 4, 5, 4),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    internal StoryEntity? SelectedCharacter { get; private set; }
    internal IReadOnlyList<StoryEntity> Choices => _options;
    internal bool IsOpen => _popup.IsOpen;
    internal void CloseDropdown() => _popup.IsOpen = false;
    internal event EventHandler? SelectionChanged;

    internal CharacterTargetInput(IEnumerable<StoryEntity> options)
    {
        _options = options.OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _frame.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _frame.Cursor = Cursors.IBeam;
        _frame.Child = Input;
        _layout.Children.Add(_frame);
        Content = _layout;

        _suggestions.SetResourceReference(BackgroundProperty, "ElevatedBrush");
        _suggestions.SetResourceReference(ForegroundProperty, "TextBrush");
        _dropdown = new Border
        {
            Child = _suggestions,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2)
        };
        _dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        _dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _dropdown.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = _frame });
        DropdownChrome.SetCompact(_dropdown, true);
        _popup.Child = _dropdown;
        _popup.PlacementTarget = _frame;
        _layout.Children.Add(_popup);

        AutomationProperties.SetName(Input, Loc.T("Target Character"));
        Input.TextChanged += (_, _) =>
        {
            if (_committing) return;
            if (SelectedCharacter is not null &&
                !string.Equals(SelectedCharacter.Name, Input.Text, StringComparison.CurrentCulture))
            {
                SelectedCharacter = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            RefreshSuggestions();
        };
        Input.GotKeyboardFocus += (_, _) => RefreshSuggestions();
        Input.PreviewKeyDown += InputKeyDown;
        _frame.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Input.Focus();
            Input.CaretIndex = Input.Text.Length;
            e.Handled = true;
        };
        _frame.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                (!DropdownChrome.Contains(Input, e.OriginalSource as DependencyObject) && !ReferenceEquals(_frame, e.OriginalSource))) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Input.IsKeyboardFocusWithin) RefreshSuggestions();
            }), DispatcherPriority.Background);
        }), true);
        _suggestions.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_suggestions, source) is ListBoxItem { Content: StoryEntity character })
            {
                Select(character);
                e.Handled = true;
            }
        };
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsKeyboardFocusWithin && !_suggestions.IsKeyboardFocusWithin &&
                !DropdownChrome.PointerWithin(_dropdown)) _popup.IsOpen = false;
        }), DispatcherPriority.Input);
        Unloaded += (_, _) => _popup.IsOpen = false;
    }

    internal void Select(StoryEntity character)
    {
        if (!_options.Contains(character)) return;
        _committing = true;
        Input.Text = character.Name;
        Input.CaretIndex = Input.Text.Length;
        _committing = false;
        SelectedCharacter = character;
        _popup.IsOpen = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSuggestions()
    {
        string query = Input.Text.Trim();
        var matches = _options
            .Where(character => character.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(character => character.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _suggestions.ItemsSource = matches;
        _suggestions.SelectedIndex = -1;
        _popup.IsOpen = IsLoaded && Input.IsKeyboardFocusWithin && matches.Count > 0;
    }

    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var match = _suggestions.SelectedItem as StoryEntity ??
                _suggestions.Items.Cast<StoryEntity>().FirstOrDefault();
            if (match is not null) Select(match);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _popup.IsOpen = false;
            e.Handled = true;
        }
        else if (_popup.IsOpen && e.Key is Key.Down or Key.Up)
        {
            int direction = e.Key == Key.Down ? 1 : -1;
            _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + direction, 0, _suggestions.Items.Count - 1);
            _suggestions.ScrollIntoView(_suggestions.SelectedItem);
            e.Handled = true;
        }
    }
}

public sealed class CharacterRelationWindow : Window
{
    public static IReadOnlyList<string> DefaultRelationTypes { get; } =
    [
        "Family", "Parent", "Child", "Sibling", "Spouse", "Relative",
        "Friend", "Best friend", "Acquaintance", "Neighbor",
        "Romantic partner", "Love interest", "Crush", "Former partner",
        "Ally", "Mentor", "Student", "Colleague", "Employer", "Employee",
        "Rival", "Enemy", "Nemesis", "Guardian", "Ward", "Leader", "Follower"
    ];

    private readonly ObservableCollection<string> _selectedTypeLabels = [];
    private readonly Dictionary<string, string> _typeCodesByLabel = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly StoryProject _typeSuggestions = new();

    internal CharacterTargetInput TargetInput { get; }
    internal RelationTypeInput RelationTypesInput { get; }
    internal TextBox DescriptionInput { get; } = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 110,
        VerticalContentAlignment = VerticalAlignment.Top,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    internal Button SaveButton { get; }

    public StoryEntity? TargetCharacter => TargetInput.SelectedCharacter;
    public IReadOnlyList<string> RelationTypes => _selectedTypeLabels
        .Select(label => _typeCodesByLabel.TryGetValue(label, out string? code) ? code : label)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    public string RelationDescription => DescriptionInput.Text.Trim();

    public CharacterRelationWindow(StoryEntity source, IEnumerable<StoryEntity> targets, IEnumerable<string> existingTypes)
    {
        Title = Loc.T("Add relation");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Target Character"), Margin = new Thickness(0, 0, 0, 4) });
        TargetInput = new CharacterTargetInput(targets.Where(character => character.Id != source.Id));
        TargetInput.SelectionChanged += (_, _) => UpdateSaveButton();
        panel.Children.Add(TargetInput);

        panel.Children.Add(new TextBlock { Text = Loc.T("Relation type"), Margin = new Thickness(0, 12, 0, 4) });
        _typeSuggestions.Tags.Clear();
        foreach (string type in DefaultRelationTypes.Concat(existingTypes)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            string label = DefaultRelationTypes.Contains(type, StringComparer.OrdinalIgnoreCase) ? Loc.T(type) : type;
            if (_typeSuggestions.Tags.Contains(label, StringComparer.CurrentCultureIgnoreCase)) continue;
            _typeSuggestions.Tags.Add(label);
            _typeCodesByLabel[label] = type;
        }
        RelationTypesInput = new RelationTypeInput(_typeSuggestions.Tags, _selectedTypeLabels);
        RelationTypesInput.SelectionChanged += (_, _) => UpdateSaveButton();
        panel.Children.Add(RelationTypesInput);

        panel.Children.Add(new TextBlock { Text = Loc.T("Description"), Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(DescriptionInput);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        SaveButton = new Button { Content = Loc.T("Add"), MinWidth = 86, IsDefault = true, IsEnabled = false };
        SaveButton.Click += (_, _) =>
        {
            if (TargetCharacter is null || RelationTypes.Count == 0) return;
            DialogResult = true;
        };
        var cancel = new Button { Content = Loc.T("Cancel"), MinWidth = 86, IsCancel = true };
        actions.Children.Add(SaveButton);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        Content = panel;

        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            TargetInput.CloseDropdown();
            Keyboard.ClearFocus();
        }), DispatcherPriority.ContextIdle);
        SourceInitialized += (_, _) =>
        {
            ThemeChrome.Apply(this, Application.Current.Resources["PageBrush"] is SolidColorBrush brush && brush.Color.R < 64);
            ThemeChrome.HideIcon(this);
        };
    }

    internal void SelectTargetForTest(StoryEntity target) => TargetInput.Select(target);

    internal void AddTypeForTest(string type)
    {
        string label = DefaultRelationTypes.Contains(type, StringComparer.OrdinalIgnoreCase) ? Loc.T(type) : type;
        _typeCodesByLabel[label] = type;
        RelationTypesInput.Select(label);
    }

    private void UpdateSaveButton() => SaveButton.IsEnabled =
        TargetCharacter is not null && _selectedTypeLabels.Count > 0;
}
