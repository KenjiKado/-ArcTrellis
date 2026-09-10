using System.Text.Json.Nodes;
namespace ArcTrellis.Core.Services;

// Property-level patches prevent an undo in one editor from restoring unrelated data.
public sealed class ScopedHistory
{
    private sealed record Change(string[] Path, JsonNode? Before, JsonNode? After);
    private sealed record Entry(List<Change> Changes);
    private readonly Dictionary<string, (Stack<Entry> Undo, Stack<Entry> Redo)> _scopes = new();
    private (Stack<Entry> Undo, Stack<Entry> Redo) Stacks(string scope)
    {
        if (!_scopes.TryGetValue(scope, out var stacks)) _scopes[scope] = stacks = (new(), new());
        return stacks;
    }
    public void Clear() => _scopes.Clear();
    public bool CanUndo(string scope) => Stacks(scope).Undo.Count > 0;
    public bool CanRedo(string scope) => Stacks(scope).Redo.Count > 0;
    public void Record(string scope, string before, string after)
    {
        var changes = new List<Change>();
        Diff(JsonNode.Parse(before), JsonNode.Parse(after), [], changes);
        if (changes.Count == 0) return;
        var stacks = Stacks(scope);
        stacks.Undo.Push(new(changes)); stacks.Redo.Clear();
        if (stacks.Undo.Count > 100)
        {
            var recent = stacks.Undo.Take(100).Reverse().ToArray(); stacks.Undo.Clear();
            foreach (var entry in recent) stacks.Undo.Push(entry);
        }
    }
    public string? Apply(string scope, string current, bool redo, Func<string, bool>? validate = null)
    {
        var stacks = Stacks(scope);
        var source = redo ? stacks.Redo : stacks.Undo;
        if (!source.TryPeek(out var entry)) return null;
        var root = JsonNode.Parse(current)!;
        foreach (var change in entry.Changes)
        {
            if (!TryParent(root, change.Path, out var parent)) return null;
            if (!JsonNode.DeepEquals(Read(parent!, change.Path[^1]), redo ? change.Before : change.After)) return null;
        }
        foreach (var change in entry.Changes)
        {
            TryParent(root, change.Path, out var parent);
            Write(parent!, change.Path[^1], (redo ? change.After : change.Before)?.DeepClone());
        }
        string result = root.ToJsonString();
        if (validate is not null && !validate(result)) return null;
        source.Pop(); (redo ? stacks.Undo : stacks.Redo).Push(entry);
        return result;
    }
    private static void Diff(JsonNode? a, JsonNode? b, string[] path, List<Change> result)
    {
        if (JsonNode.DeepEquals(a, b)) return;
        if (a is JsonObject ao && b is JsonObject bo)
        {
            foreach (var key in ao.Select(p => p.Key).Union(bo.Select(p => p.Key)))
                if (key != "ModifiedUtc") Diff(ao[key], bo[key], [..path, key], result);
        }
        else if (a is JsonArray aa && b is JsonArray ba && aa.Concat(ba).All(x => x is JsonObject o && o["Id"] is not null))
        {
            var am = aa.ToDictionary(x => x!["Id"]!.ToString());
            var bm = ba.ToDictionary(x => x!["Id"]!.ToString());
            foreach (var id in am.Keys.Union(bm.Keys)) Diff(am.GetValueOrDefault(id), bm.GetValueOrDefault(id), [..path, "@" + id], result);
        }
        else result.Add(new(path, a?.DeepClone(), b?.DeepClone()));
    }
    private static bool TryParent(JsonNode root, string[] path, out JsonNode? parent)
    {
        parent = root;
        foreach (var key in path[..^1]) { parent = Read(parent, key); if (parent is null) return false; }
        return true;
    }
    private static JsonNode? Read(JsonNode? parent, string key) => parent switch
    {
        JsonObject obj => obj[key],
        JsonArray array when key.StartsWith('@') => array.FirstOrDefault(x => x?["Id"]?.ToString() == key[1..]),
        _ => null
    };
    private static void Write(JsonNode parent, string key, JsonNode? value)
    {
        if (parent is JsonObject obj) { obj[key] = value; return; }
        var array = (JsonArray)parent;
        var old = Read(array, key);
        if (old is null) { if (value is not null) array.Add(value); }
        else if (value is null) array.Remove(old);
        else array[array.IndexOf(old)] = value;
    }
}
