using System.Collections.Concurrent;

namespace Jalium.UI;

/// <summary>
/// Global registry for AOT-safe property accessors. Registered accessors are used
/// by the data binding engine and Razor value resolver instead of reflection,
/// enabling property access on types whose members would otherwise be trimmed by the linker.
/// </summary>
public static class PropertyAccessorRegistry
{
    private static readonly ConcurrentDictionary<(Type, string), Func<object, object?>> Accessors = new();

    /// <summary>
    /// Registers a direct property accessor for a type+property combination.
    /// </summary>
    public static void Register(Type type, string propertyName, Func<object, object?> accessor)
    {
        if (type != null && !string.IsNullOrWhiteSpace(propertyName) && accessor != null)
            Accessors[(type, propertyName)] = accessor;
    }

    /// <summary>
    /// Registers property accessors for multiple properties on a type.
    /// </summary>
    public static void RegisterAll(Type type, IEnumerable<(string Name, Func<object, object?> Accessor)> accessors)
    {
        if (type == null || accessors == null) return;
        foreach (var (name, accessor) in accessors)
            Accessors[(type, name)] = accessor;
    }

    /// <summary>
    /// Tries to get a registered accessor for the given type and property.
    /// </summary>
    public static bool TryGet(Type type, string propertyName, out Func<object, object?> accessor)
    {
        return Accessors.TryGetValue((type, propertyName), out accessor!);
    }

    /// <summary>
    /// Tries to read a property value using registered accessor, falling back to reflection.
    /// </summary>
    public static bool TryReadProperty(object source, string propertyName, out object? value)
    {
        var type = source.GetType();
        if (TryGet(type, propertyName, out var accessor))
        {
            value = accessor(source);
            return true;
        }

        var prop = type.GetProperty(propertyName);
        if (prop != null)
        {
            value = prop.GetValue(source);
            return true;
        }

        value = null;
        return false;
    }
}
