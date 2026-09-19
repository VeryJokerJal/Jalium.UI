using System.Reflection;

namespace Jalium.UI.Tests;

internal static class LifecycleRootDiagnostic
{
    internal static string Find(object? target, object? extra = null)
    {
        if (target == null) return "Collected";
        var queue = new Queue<(object Value, string Path, int Depth)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var types = new[] { typeof(UIElement), typeof(Visual), typeof(EventManager), typeof(DependencyProperty), typeof(FrameworkElement), typeof(DependencyObject),
            typeof(Jalium.UI.Media.CompositionTarget), typeof(Jalium.UI.Diagnostics.RoutedEventDiagnostics),
            typeof(Jalium.UI.Animation.AnimationManager), typeof(Jalium.UI.Threading.Dispatcher),
            typeof(Jalium.UI.Threading.DispatcherCore), typeof(Jalium.UI.Input.Mouse), typeof(Jalium.UI.Input.Keyboard) };
        if (extra != null) queue.Enqueue((extra, "window", 0));
        foreach (var type in types)
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                try { if (field.GetValue(null) is { } value) queue.Enqueue((value, type.Name + "." + field.Name, 0)); } catch { }
        while (queue.Count > 0 && seen.Count < 80000)
        {
            var (value, path, depth) = queue.Dequeue();
            if (ReferenceEquals(value, target)) return path;
            if (depth > 20 || !seen.Add(value)) continue;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string or Type or MemberInfo ||
                type.FullName?.Contains("Weak") == true || value is IntPtr or UIntPtr) continue;
            if (value is Array array)
            {
                if (array.Length > 20000) continue;
                int i = 0;
                foreach (var element in array)
                {
                    if (element != null) queue.Enqueue((element, path + "[" + i + "]", depth + 1));
                    i++;
                }
                continue;
            }
            for (Type? current = type; current != null; current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    try
                    {
                        if (!field.FieldType.IsPointer && field.GetValue(value) is { } child)
                            queue.Enqueue((child, path + "." + field.Name, depth + 1));
                    }
                    catch { }
        }
        return "No known strong path; visited=" + seen.Count;
    }
}
