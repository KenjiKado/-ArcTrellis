using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcTrellis.Core.Services;
namespace ArcTrellis.App;

public sealed class TextUndoController
{
    private readonly Func<int> _tab;
    private readonly Dictionary<string, TextEditHistory> _histories = new();
    private readonly Dictionary<TextBox, TextEditHistory.State> _before = new();
    private readonly Dictionary<TextBox, string> _keys = new();
    private readonly HashSet<TextBox> _atomic = new();
    private bool _applying;
    public TextUndoController(Window window, Func<int> tab)
    {
        _tab = tab;
        window.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
        {
            if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
            if (Keyboard.FocusedElement is not TextBox box) return;
            Apply(box, e.Command == ApplicationCommands.Redo); e.Handled = true;
        }), true);
        window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        { if (e.NewFocus is TextBox box) { Prepare(box); History(box).BreakGroup(); } }), true);
        window.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        { if (e.OldFocus is TextBox box) History(box).BreakGroup(); }), true);
        window.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler((_, e) =>
        { if (e.OriginalSource is TextBox box) Prepare(box); }), true);
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.OriginalSource is not TextBox box) return;
            Prepare(box);
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) History(box).BreakGroup();
        }), true);
        window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) =>
        { if (Keyboard.FocusedElement is TextBox box) History(box).BreakGroup(); }), true);
        window.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler((_, e) =>
        { if (e.OriginalSource is TextBox box) { Prepare(box); _atomic.Add(box); } }), true);
        window.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, e) =>
        {
            if (_applying || e.OriginalSource is not TextBox box || !box.IsKeyboardFocusWithin) return;
            string key = Key(box);
            if (!_keys.TryGetValue(box, out var previousKey) || previousKey != key) { Prepare(box); return; }
            var history = History(box);
            var before = _before.GetValueOrDefault(box) ?? new(history.Current, 0);
            if (before.Text != history.Current) { Prepare(box); return; }
            int prefix = 0;
            while (prefix < before.Text.Length && prefix < box.Text.Length && before.Text[prefix] == box.Text[prefix]) prefix++;
            int suffix = 0;
            while (suffix < before.Text.Length - prefix && suffix < box.Text.Length - prefix && before.Text[^(suffix + 1)] == box.Text[^(suffix + 1)]) suffix++;
            var after = new TextEditHistory.State(box.Text, box.Text.Length - suffix);
            history.Record(before, after, DateTime.UtcNow, _atomic.Remove(box));
            _before[box] = after;
        }), true);
    }
    private string Key(TextBox box)
    {
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        object source = binding?.ResolvedSource ?? box;
        string identity = source.GetType().GetProperty("Id")?.GetValue(source)?.ToString() ?? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source).ToString();
        return $"{_tab()}/{source.GetType().FullName}/{identity}/{binding?.ResolvedSourcePropertyName}";
    }
    private TextEditHistory History(TextBox box)
    {
        string key = Key(box);
        if (!_histories.TryGetValue(key, out var history)) _histories[key] = history = new(box.Text);
        return history;
    }
    public void Prepare(TextBox box)
    {
        _keys[box] = Key(box);
        var history = History(box);
        if (history.Current != box.Text) _histories[Key(box)] = new(box.Text);
        _before[box] = new(box.Text, box.SelectionStart, box.SelectionLength);
    }
    public void Clear() { _histories.Clear(); _keys.Clear(); _before.Clear(); _atomic.Clear(); }
    public void Apply(TextBox box, bool redo)
    {
        Prepare(box);
        var state = redo ? History(box).Redo() : History(box).Undo();
        if (state is null) return;
        _applying = true;
        try
        {
            box.SetCurrentValue(TextBox.TextProperty, state.Text);
            int start = Math.Min(state.Start, box.Text.Length);
            box.Select(start, Math.Min(state.Length, box.Text.Length - start));
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); _before[box] = state;
        }
        finally { _applying = false; }
    }
}
