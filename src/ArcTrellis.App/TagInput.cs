using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTrellis.Core.Models;
using ArcTrellis.Core.Services;

namespace ArcTrellis.App;

public sealed class TagEditEventArgs(string tag, bool remove) : RoutedEventArgs(TagInput.TagEditRequestedEvent)
{
    public string Tag { get; } = tag;
    public bool Remove { get; } = remove;
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        => ((EventHandler<TagEditEventArgs>)genericHandler)(genericTarget, this);
}

public sealed class TagInput : UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(nameof(Project), typeof(StoryProject), typeof(TagInput));
    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(nameof(Tags), typeof(ObservableCollection<string>), typeof(TagInput), new PropertyMetadata(null, TagsChanged));
    public static readonly RoutedEvent TagEditRequestedEvent = EventManager.RegisterRoutedEvent("TagEditRequested", RoutingStrategy.Bubble, typeof(EventHandler<TagEditEventArgs>), typeof(TagInput));
    public bool ExistingOnly { get; set; }
    public StoryProject? Project { get => (StoryProject?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }
    public ObservableCollection<string>? Tags { get => (ObservableCollection<string>?)GetValue(TagsProperty); set => SetValue(TagsProperty, value); }
    private readonly WrapPanel _row = new() { Orientation = Orientation.Horizontal };
    internal TextBox Input { get; } = new() { Tag = "TagDraft", MinWidth = 110, Width = 150, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(5, 4, 5, 4), Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _frame = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(4), MinHeight = 40 };
    private readonly ListBox _suggestions = new() { MaxHeight = 210, MinWidth = 180, BorderThickness = new Thickness(0) };
    private readonly Popup _popup = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Custom };
    internal bool IsSuggestionsMouseOver => _suggestions.IsMouseOver;
    internal bool SuggestionsOpen => _popup.IsOpen;
    internal IEnumerable<string> Suggestions => _suggestions.Items.Cast<string>();

    public TagInput()
    {
        _frame.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _frame.Child = _row; _row.Children.Add(Input); Content = _frame;
        _suggestions.SetResourceReference(BackgroundProperty, "ElevatedBrush");
        _suggestions.SetResourceReference(ForegroundProperty, "TextBrush");
        var dropdown = new Border { Child = _suggestions, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2) };
        dropdown.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        dropdown.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _popup.Child = dropdown; _popup.PlacementTarget = _frame;
        _popup.CustomPopupPlacementCallback = (_, target, _) => [new CustomPopupPlacement(new Point(0, target.Height + 2), PopupPrimaryAxis.Vertical)];
        AutomationProperties.SetName(Input, Loc.T("Tags"));
        Input.TextChanged += (_, _) => RefreshSuggestions();
        Input.GotKeyboardFocus += (_, _) => RefreshSuggestions();
        Input.PreviewKeyDown += InputKeyDown;
        _suggestions.PreviewKeyDown += InputKeyDown;
        _suggestions.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_suggestions, source) is ListBoxItem { Content: string tag })
            { Commit(tag); e.Handled = true; }
        };
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        { if (!IsKeyboardFocusWithin && !_suggestions.IsKeyboardFocusWithin) _popup.IsOpen = false; }), DispatcherPriority.Input);
        Loaded += (_, _) => { Subscribe(); RenderTags(); };
        Unloaded += (_, _) => { if (Tags is not null) Tags.CollectionChanged -= CollectionChanged; _popup.IsOpen = false; Input.Clear(); };
    }
    private static void TagsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (TagInput)sender;
        if (e.OldValue is ObservableCollection<string> old) old.CollectionChanged -= control.CollectionChanged;
        control.Input.Clear(); control._popup.IsOpen = false;
        if (control.IsLoaded) control.Subscribe();
        control.RenderTags();
    }
    private void Subscribe()
    {
        if (Tags is null) return;
        Tags.CollectionChanged -= CollectionChanged; Tags.CollectionChanged += CollectionChanged;
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { RenderTags(); RefreshSuggestions(); }
    private void RenderTags()
    {
        while (_row.Children.Count > 1) _row.Children.RemoveAt(0);
        foreach (string tag in Tags ?? [])
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 7, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
            label.SetBinding(TextBlock.TextProperty, new Binding { Source = tag });
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var remove = new Button { Content = "×", Tag = tag, Width = 20, Height = 20, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = Loc.T("Remove tag") };
            AutomationProperties.SetName(remove, Loc.T("Remove tag") + ": " + tag);
            var circle = new FrameworkElementFactory(typeof(Border)); circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); circle.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); circle.AppendChild(content);
            remove.Template = new ControlTemplate(typeof(Button)) { VisualTree = circle };
            remove.Click += (_, _) => { Request(tag, true); Input.Focus(); };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal }; chipRow.Children.Add(label); chipRow.Children.Add(remove);
            var chip = new Border { Child = chipRow, CornerRadius = new CornerRadius(15), Padding = new Thickness(8, 4, 5, 4), Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            chip.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
            _row.Children.Insert(_row.Children.Count - 1, chip);
        }
    }
    private void RefreshSuggestions()
    {
        IReadOnlyList<string> matches = Project is null || Tags is null ? [] : TagService.Suggest(Project, Tags, Input.Text);
        _suggestions.ItemsSource = matches; _suggestions.SelectedIndex = -1;
        _popup.IsOpen = IsLoaded && Input.IsKeyboardFocusWithin && matches.Count > 0;
    }
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && ReferenceEquals(sender, Input) && Input.Text.Length == 0 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (Tags is { Count: > 0 }) Request(Tags[^1], true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { Commit(_popup.IsOpen ? _suggestions.SelectedItem as string ?? Input.Text : Input.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { _popup.IsOpen = false; Input.Focus(); e.Handled = true; }
        else if (_popup.IsOpen && e.Key is Key.Down or Key.Up)
        {
            int direction = e.Key == Key.Down ? 1 : -1;
            _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + direction, 0, _suggestions.Items.Count - 1);
            _suggestions.ScrollIntoView(_suggestions.SelectedItem); e.Handled = true;
        }
    }
    private void Commit(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Request(value.Trim(), false); Input.Clear(); _popup.IsOpen = false; Input.Focus();
    }
    private void Request(string value, bool remove)
    {
        if (Tags is null || Project is null) return;
        if (ExistingOnly)
        {
            if (remove)
            {
                foreach (string tag in Tags.Where(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)).ToList()) Tags.Remove(tag);
            }
            else if (TagService.Existing(Project).FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)) is { } existing
                && !Tags.Contains(existing, StringComparer.OrdinalIgnoreCase)) Tags.Add(existing);
            return;
        }
        RaiseEvent(new TagEditEventArgs(value, remove) { Source = this });
    }
}


