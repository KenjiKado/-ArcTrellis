using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.Core.Models;

namespace ArcTrellis.App;

public sealed class CharacterEditEventArgs(Guid? characterId, string? characterName, bool remove) : RoutedEventArgs(CharacterInput.CharacterEditRequestedEvent)
{
    public Guid? CharacterId { get; } = characterId;
    public string? CharacterName { get; } = characterName;
    public bool Remove { get; } = remove;
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        => ((EventHandler<CharacterEditEventArgs>)genericHandler)(genericTarget, this);
}

public sealed class CharacterChipClickEventArgs(Guid characterId) : RoutedEventArgs(CharacterInput.CharacterChipClickedEvent)
{
    public Guid CharacterId { get; } = characterId;
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        => ((EventHandler<CharacterChipClickEventArgs>)genericHandler)(genericTarget, this);
}

public sealed class CharacterInput : UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(nameof(Project), typeof(StoryProject), typeof(CharacterInput), new PropertyMetadata(null, ProjectChanged));
    public static readonly DependencyProperty CharacterIdsProperty = DependencyProperty.Register(nameof(CharacterIds), typeof(ObservableCollection<Guid>), typeof(CharacterInput), new PropertyMetadata(null, CharacterIdsChanged));
    public static readonly RoutedEvent CharacterEditRequestedEvent = EventManager.RegisterRoutedEvent("CharacterEditRequested", RoutingStrategy.Bubble, typeof(EventHandler<CharacterEditEventArgs>), typeof(CharacterInput));
    public static readonly RoutedEvent CharacterChipClickedEvent = EventManager.RegisterRoutedEvent("CharacterChipClicked", RoutingStrategy.Bubble, typeof(EventHandler<CharacterChipClickEventArgs>), typeof(CharacterInput));

    private readonly WrapPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Border _frame = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(4), MinHeight = 40 };
    private readonly ListBox _suggestions = new() { MaxHeight = 210, MinWidth = 220, BorderThickness = new Thickness(0) };
    private readonly Popup _popup = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Custom };
    private readonly Border _dropdown;
    private StoryProject? _subscribedProject;
    private ObservableCollection<Guid>? _subscribedCharacterIds;

    internal TextBox Input { get; } = new()
    {
        Tag = "CharacterDraft", MinWidth = 130, Width = 180, BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Padding = new Thickness(5, 4, 5, 4), Margin = new Thickness(2),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    public StoryProject? Project { get => (StoryProject?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }
    public ObservableCollection<Guid>? CharacterIds { get => (ObservableCollection<Guid>?)GetValue(CharacterIdsProperty); set => SetValue(CharacterIdsProperty, value); }
    internal bool SuggestionsOpen => _popup.IsOpen;
    internal IReadOnlyList<StoryEntity> Suggestions => _suggestions.Items.Cast<StoryEntity>().ToList();

    public CharacterInput()
    {
        _frame.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _frame.Child = _row; _row.Children.Add(Input); Content = _frame;
        _suggestions.SetResourceReference(BackgroundProperty, "ElevatedBrush");
        _suggestions.SetResourceReference(ForegroundProperty, "TextBrush");
        var itemText = new FrameworkElementFactory(typeof(TextBlock));
        itemText.SetBinding(TextBlock.TextProperty, new Binding(nameof(StoryEntity.Name)));
        itemText.SetValue(TextBlock.PaddingProperty, new Thickness(8, 5, 8, 5));
        _suggestions.ItemTemplate = new DataTemplate { VisualTree = itemText };
        _dropdown = new Border { Child = _suggestions, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2) };
        _dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        _dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _popup.Child = _dropdown; _popup.PlacementTarget = _frame;
        _popup.CustomPopupPlacementCallback = (_, target, _) => [new CustomPopupPlacement(new Point(0, target.Height + 2), PopupPrimaryAxis.Vertical)];
        AutomationProperties.SetName(Input, Loc.T("Characters"));
        Input.TextChanged += (_, _) => RefreshSuggestions();
        Input.GotKeyboardFocus += (_, _) => RefreshSuggestions();
        Input.PreviewKeyDown += InputKeyDown;
        _suggestions.PreviewKeyDown += InputKeyDown;
        _suggestions.PreviewMouseLeftButtonUp += SuggestionsMouseUp;
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!IsKeyboardFocusWithin && !_suggestions.IsKeyboardFocusWithin) CloseSuggestions(); }), DispatcherPriority.Input);
        Loaded += (_, _) => { Subscribe(); RenderCharacters(); };
        Unloaded += (_, _) => { Unsubscribe(); CloseSuggestions(); Input.Clear(); };
    }

    private static void ProjectChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (CharacterInput)sender; control.UnsubscribeProject(); control.SubscribeProject(); control.RenderCharacters(); control.RefreshSuggestions();
    }
    private static void CharacterIdsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (CharacterInput)sender; control.UnsubscribeCharacterIds(); control.SubscribeCharacterIds(); control.RenderCharacters(); control.RefreshSuggestions();
    }
    private void Subscribe() { SubscribeProject(); SubscribeCharacterIds(); }
    private void SubscribeProject()
    {
        if (ReferenceEquals(_subscribedProject, Project)) return;
        UnsubscribeProject();
        if (Project is not null) { Project.Characters.CollectionChanged += CharactersChanged; _subscribedProject = Project; }
    }
    private void SubscribeCharacterIds()
    {
        if (ReferenceEquals(_subscribedCharacterIds, CharacterIds)) return;
        UnsubscribeCharacterIds();
        if (CharacterIds is not null) { CharacterIds.CollectionChanged += CharacterIdsChanged; _subscribedCharacterIds = CharacterIds; }
    }
    private void Unsubscribe() { UnsubscribeProject(); UnsubscribeCharacterIds(); }
    private void UnsubscribeProject() { if (_subscribedProject is not null) _subscribedProject.Characters.CollectionChanged -= CharactersChanged; _subscribedProject = null; }
    private void UnsubscribeCharacterIds() { if (_subscribedCharacterIds is not null) _subscribedCharacterIds.CollectionChanged -= CharacterIdsChanged; _subscribedCharacterIds = null; }
    private void CharactersChanged(object? sender, NotifyCollectionChangedEventArgs e) { RenderCharacters(); RefreshSuggestions(); }
    private void CharacterIdsChanged(object? sender, NotifyCollectionChangedEventArgs e) { RenderCharacters(); RefreshSuggestions(); }

    private void RenderCharacters()
    {
        while (_row.Children.Count > 1) _row.Children.RemoveAt(0);
        foreach (Guid id in CharacterIds ?? [])
        {
            var character = Project?.Characters.FirstOrDefault(candidate => candidate.Id == id);
            if (character is null) continue;
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 7, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(StoryEntity.Name)) { Source = character });
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var remove = new Button { Content = "×", Tag = id, Width = 20, Height = 20, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = Loc.T("Remove character") };
            AutomationProperties.SetName(remove, Loc.T("Remove character") + ": " + character.Name);
            remove.Click += (_, _) => { Request(id, null, true); Input.Focus(); };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal }; chipRow.Children.Add(label); chipRow.Children.Add(remove);
            var chip = new Border { Child = chipRow, CornerRadius = new CornerRadius(15), Padding = new Thickness(8, 4, 5, 4), Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            chip.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            chip.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is Button) return; RaiseEvent(new CharacterChipClickEventArgs(id) { Source = this }); e.Handled = true; };
            _row.Children.Insert(_row.Children.Count - 1, chip);
        }
    }

    private void RefreshSuggestions()
    {
        string prefix = Input.Text.Trim();
        var matches = Project is null || CharacterIds is null || prefix.Length < 3
            ? []
            : Project.Characters.Where(character => !CharacterIds.Contains(character.Id) && character.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(character => character.Name).ToList();
        _suggestions.ItemsSource = matches; _suggestions.SelectedIndex = -1;
        _popup.IsOpen = IsLoaded && Input.IsKeyboardFocusWithin && matches.Count > 0;
    }

    private void SuggestionsMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_suggestions, source) is ListBoxItem { Content: StoryEntity character })
        { Commit(character, null); e.Handled = true; }
    }
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && ReferenceEquals(sender, Input) && Input.Text.Length == 0 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (CharacterIds is { Count: > 0 } ids) Request(ids[^1], null, true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { Commit(_popup.IsOpen ? _suggestions.SelectedItem as StoryEntity : null, Input.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseSuggestions(); Input.Focus(); e.Handled = true; }
        else if (_popup.IsOpen && e.Key is Key.Down or Key.Up)
        {
            int direction = e.Key == Key.Down ? 1 : -1;
            _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + direction, 0, _suggestions.Items.Count - 1);
            _suggestions.ScrollIntoView(_suggestions.SelectedItem); e.Handled = true;
        }
    }
    private void Commit(StoryEntity? character, string? typedName)
    {
        if (character is not null) Request(character.Id, null, false);
        else if (!string.IsNullOrWhiteSpace(typedName)) Request(null, typedName.Trim(), false);
        Input.Clear(); CloseSuggestions(); Input.Focus();
    }
    private void Request(Guid? characterId, string? characterName, bool remove)
    {
        if (Project is null || CharacterIds is null) return;
        RaiseEvent(new CharacterEditEventArgs(characterId, characterName, remove) { Source = this });
    }
    private void CloseSuggestions() => _popup.IsOpen = false;
}
