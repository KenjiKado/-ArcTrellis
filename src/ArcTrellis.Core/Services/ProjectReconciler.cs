using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using ArcTrellis.Core.Models;

namespace ArcTrellis.Core.Services;

/// <summary>Applies a history snapshot without replacing surviving bound objects or collections.</summary>
public static class ProjectReconciler
{
    public static void Apply(StoryProject target, StoryProject snapshot) => Copy(target, snapshot);

    private static void Copy(object target, object snapshot)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0
                || property.IsDefined(typeof(JsonIgnoreAttribute))) continue;
            var current = property.GetValue(target);
            var restored = property.GetValue(snapshot);
            if (current is IList items && restored is IList desired) Synchronize(items, desired);
            else if (!Equals(current, restored)) property.SetValue(target, restored);
        }
    }

    private static void Synchronize(IList items, IList desired)
    {
        for (int index = 0; index < desired.Count; index++)
        {
            object next = desired[index]!;
            var idProperty = next.GetType().GetProperty("Id");
            int found = -1;
            for (int candidate = index; candidate < items.Count; candidate++)
            {
                bool matches = idProperty is not null
                    ? Equals(idProperty.GetValue(items[candidate]), idProperty.GetValue(next))
                    : next is ObservableObject ? candidate == index : Equals(items[candidate], next);
                if (matches) { found = candidate; break; }
            }
            if (found < 0) items.Insert(index, next);
            else
            {
                // ObservableCollection.Move preserves item containers during reordering.
                if (found != index) items.GetType().GetMethod("Move")!.Invoke(items, [found, index]);
                if (next is ObservableObject) Copy(items[index]!, next);
            }
        }
        while (items.Count > desired.Count) items.RemoveAt(items.Count - 1);
    }
}
