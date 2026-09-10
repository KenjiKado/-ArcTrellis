namespace ArcTrellis.Core.Services;

public sealed class TextEditHistory(string text)
{
    public sealed record State(string Text, int Start, int Length = 0);
    private sealed record Edit(State Before, State After);
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();
    private string _group = "";
    private DateTime _lastEdit;
    public string Current { get; private set; } = text;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void BreakGroup() => _group = "";
    public void Record(State before, State after, DateTime now, bool atomic = false)
    {
        if (before.Text == after.Text) return;
        if (Current != before.Text) { _undo.Clear(); _redo.Clear(); BreakGroup(); }
        int prefix = 0;
        while (prefix < before.Text.Length && prefix < after.Text.Length && before.Text[prefix] == after.Text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < before.Text.Length - prefix && suffix < after.Text.Length - prefix && before.Text[^(suffix + 1)] == after.Text[^(suffix + 1)]) suffix++;
        string removed = before.Text.Substring(prefix, before.Text.Length - prefix - suffix);
        string added = after.Text.Substring(prefix, after.Text.Length - prefix - suffix);
        string group = "";
        if (!atomic && before.Length == 0)
        {
            if (removed.Length == 0 && added.Length == 1) group = "insert/" + Kind(added[0]);
            else if (added.Length == 0 && removed.Length == 1) group = (prefix < before.Start ? "backspace/" : "delete/") + Kind(removed[0]);
        }
        if (group.Length > 0 && group == _group && now - _lastEdit < TimeSpan.FromSeconds(1.2)
            && _undo.TryPeek(out var last) && last.After.Text == before.Text && last.After.Start == before.Start && before.Length == 0)
        {
            _undo.Pop(); _undo.Push(new(last.Before, after));
        }
        else _undo.Push(new(before, after));
        Current = after.Text; _redo.Clear(); _lastEdit = now; _group = group;
        if (_undo.Count > 100)
        {
            var recent = _undo.Take(100).Reverse().ToArray(); _undo.Clear();
            foreach (var entry in recent) _undo.Push(entry);
        }
    }
    private static string Kind(char c) => char.IsWhiteSpace(c) ? "space" : char.IsLetterOrDigit(c) || c == '_' ? "word" : "punctuation";
    public State? Undo()
    {
        BreakGroup(); if (!_undo.TryPop(out var edit)) return null;
        _redo.Push(edit); Current = edit.Before.Text; return edit.Before;
    }
    public State? Redo()
    {
        BreakGroup(); if (!_redo.TryPop(out var edit)) return null;
        _undo.Push(edit); Current = edit.After.Text; return edit.After;
    }
}
