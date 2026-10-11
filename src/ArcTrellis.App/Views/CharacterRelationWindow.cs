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
    public sealed record RelationTypeGroup(string Name, IReadOnlyList<string> Types);

    public static IReadOnlyList<RelationTypeGroup> DefaultRelationTypeGroups { get; } =
    [
        new("FAMILY",
        [
            "Parent", "Child", "Sibling", "Spouse", "Twin", "Grandparent", "Grandchild",
            "Uncle", "Aunt", "Nephew", "Niece", "Cousin", "In-Law", "Step-Parent",
            "Step-Child", "Step-Sibling", "Adopted", "Adoptive", "Foster Parent",
            "Foster Child", "Blood Heir", "Blood Parent", "Clan Elder", "Clan Member",
            "Ancestor", "Descendant", "Bloodline", "Dynasty", "Half-Sibling", "Bastard",
            "Orphan Of"
        ]),
        new("ROMANTIC",
        [
            "Lover", "Love Interest", "Crush", "Admirer", "Beloved", "Soulmate",
            "Fiancé", "Betrothed", "Consort", "Suitor", "Courting", "Ex-Lover",
            "Ex-Partner", "Ex-Spouse", "Secret Lover", "Forbidden Love",
            "Unrequited Love", "Arranged Match", "Paramour", "Affair", "Bonded",
            "Fated Pair", "Mated", "Chosen One", "Destined", "Blood Bond",
            "Pact Bound", "Flirtation", "Infatuation", "Obsession"
        ]),
        new("SOCIAL",
        [
            "Friend", "Best Friend", "Acquaintance", "Stranger", "Neighbor", "Roommate",
            "Colleague", "Coworker", "Classmate", "Business Partner", "Client",
            "Service Provider", "Confidant", "Companion", "Trusted Friend",
            "Childhood Friend", "Pen Pal", "Sworn Friend", "Blood Brother", "Oath Sworn",
            "Spirit Friend", "Familiar", "Summoner", "Summoned", "Party Member",
            "Crew Member", "Tribe Member", "Guild Member", "Order Member", "Coven Member"
        ]),
        new("HIERARCHY",
        [
            "Mentor", "Student", "Teacher", "Pupil", "Master", "Apprentice", "Disciple",
            "Sensei", "Employer", "Employee", "Boss", "Subordinate", "Superior",
            "Inferior", "Leader", "Follower", "Commander", "Soldier", "Captain", "Crew",
            "Advisor", "Advisee", "Counselor", "Counseled", "King/Queen", "Subject",
            "Lord/Lady", "Vassal", "Servant", "Squire", "Knight", "Herald", "Liege",
            "Priest", "Devotee", "Prophet", "Believer"
        ]),
        new("CONFLICT",
        [
            "Enemy", "Rival", "Nemesis", "Adversary", "Opponent", "Foe", "Betrayer",
            "Betrayed", "Traitor", "Deceiver", "Deceived", "Bully", "Victim", "Oppressor",
            "Oppressed", "Tyrant", "Rebel", "Captor", "Captive", "Competitor",
            "Challenger", "Defender", "Usurper", "Usurped", "Avenger", "Target", "Hunter",
            "Hunted", "Stalker", "Stalked", "Cursed By", "Cursed", "Bound Enemy",
            "Blood Feud", "Sworn Enemy"
        ]),
        new("ALLIANCE",
        [
            "Ally", "Partner", "Teammate", "Comrade", "Supporter", "Supported", "Guardian",
            "Ward", "Protector", "Protected", "Bodyguard", "Charge", "Champion", "Patron",
            "Sponsor", "Sponsored", "Benefactor", "Beneficiary", "Faction Ally",
            "Faction Leader", "Faction Member", "Oath Bound", "Sworn Ally", "Shield Mate",
            "Battle Sworn", "Pack Member", "Pack Alpha", "Pack Beta", "Hive Mind", "Linked",
            "Contract"
        ]),
        new("SUPERNATURAL",
        [
            "Deity", "Worshipper", "Avatar", "God", "Chosen", "Chooser", "Blessed By",
            "Blessed", "Demon", "Host", "Possessed", "Possessor", "Corrupted", "Corruptor",
            "Bound Spirit", "Binder", "Vessel", "Contained", "Enchanted", "Enchanter",
            "Sire", "Progeny", "Maker", "Created", "Necromancer", "Raised", "Dragon Rider",
            "Dragon", "Rider", "Mount", "Shapeshifter", "Doppelganger", "Original",
            "Reincarnation", "Past Life"
        ]),
        new("PROFESSIONAL",
        [
            "Accomplice", "Informant", "Handler", "Crime Boss", "Underling", "Hitman",
            "Fixer", "General", "Lieutenant", "Recruit", "Trainer", "War Buddy",
            "Brothers in Arms", "Doctor", "Patient", "Healer", "Healed", "Muse", "Artist",
            "Writer", "Biographer", "Ambassador", "Diplomat", "Spy", "Double Agent",
            "Assassin", "Merchant", "Customer", "Debtor", "Creditor", "Smuggler", "Contact"
        ])
    ];

    public static IReadOnlyList<string> DefaultRelationTypes { get; } =
        DefaultRelationTypeGroups.SelectMany(group => group.Types).ToList();

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
    public string RelationType => RelationTypesInput.SelectedValue ?? "";
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
        var typeChoices = DefaultRelationTypeGroups
            .SelectMany(group => group.Types.Select(type => new RelationTypeChoice(group.Name, type)))
            .ToList();
        foreach (string type in existingTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Where(type => !DefaultRelationTypes.Contains(type, StringComparer.CurrentCultureIgnoreCase)))
            typeChoices.Add(new RelationTypeChoice("CUSTOM", type));
        RelationTypesInput = new RelationTypeInput(typeChoices);
        RelationTypesInput.SelectionChanged += (_, _) => UpdateSaveButton();
        panel.Children.Add(RelationTypesInput);
        var relationHint = new TextBlock
        {
            Text = Loc.T("Max 15 chars. Type to search or create custom."),
            FontSize = 11,
            Margin = new Thickness(3, 3, 0, 0)
        };
        relationHint.SetResourceReference(ForegroundProperty, "MutedBrush");
        panel.Children.Add(relationHint);

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
            if (TargetCharacter is null || string.IsNullOrWhiteSpace(RelationType)) return;
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

    internal void AddTypeForTest(string type) => RelationTypesInput.Select(type);

    private void UpdateSaveButton() => SaveButton.IsEnabled =
        TargetCharacter is not null && !string.IsNullOrWhiteSpace(RelationType);
}
