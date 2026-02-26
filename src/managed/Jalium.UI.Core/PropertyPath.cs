using System.ComponentModel;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Implements a data structure for describing a property as a path below another property,
/// or below an owning type.
/// </summary>
[TypeConverter(typeof(PropertyPathConverter))]
public sealed class PropertyPath
{
    private readonly string _path;
    private readonly object[] _pathParameters;

    /// <summary>
    /// Initializes a new instance of the PropertyPath class.
    /// </summary>
    /// <param name="path">A string that describes the path.</param>
    public PropertyPath(string path)
    {
        _path = path ?? string.Empty;
        _pathParameters = Array.Empty<object>();
    }

    /// <summary>
    /// Initializes a new instance of the PropertyPath class with the specified path and parameters.
    /// </summary>
    /// <param name="path">A string that describes the path.</param>
    /// <param name="pathParameters">An array of parameters for the path.</param>
    public PropertyPath(string path, params object[] pathParameters)
    {
        _path = path ?? string.Empty;
        _pathParameters = pathParameters ?? Array.Empty<object>();
    }

    /// <summary>
    /// Initializes a new instance of the PropertyPath class for a single property.
    /// </summary>
    /// <param name="parameter">A DependencyProperty or property name.</param>
    public PropertyPath(object parameter)
    {
        if (parameter is DependencyProperty dp)
        {
            _path = $"({dp.OwnerType.Name}.{dp.Name})";
            _pathParameters = new[] { parameter };
        }
        else if (parameter is string str)
        {
            _path = str;
            _pathParameters = Array.Empty<object>();
        }
        else
        {
            _path = parameter?.ToString() ?? string.Empty;
            _pathParameters = Array.Empty<object>();
        }
    }

    /// <summary>
    /// Gets the path string.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Gets the collection of parameters to use when the path refers to indexed parameters.
    /// </summary>
    public IList<object> PathParameters => _pathParameters;

    /// <summary>
    /// Gets the path segments (simple dot-separated split).
    /// </summary>
    public string[] PathSegments => string.IsNullOrEmpty(_path) ? Array.Empty<string>() : _path.Split('.');

    /// <summary>
    /// Resolves the value at this path starting from the specified source.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <returns>The resolved value, or null if resolution fails.</returns>
    internal object? ResolveValue(object source)
    {
        if (source == null || string.IsNullOrEmpty(_path))
            return null;

        var current = source;
        var segments = ParsePath(_path);

        foreach (var segment in segments)
        {
            if (current == null)
                return null;

            current = ResolveSegment(current, segment);
        }

        return current;
    }

    /// <summary>
    /// Sets a value at this path on the specified source.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the value was set successfully; otherwise, false.</returns>
    internal bool SetValue(object source, object? value)
    {
        if (source == null || string.IsNullOrEmpty(_path))
            return false;

        var segments = ParsePath(_path);
        if (segments.Length == 0)
            return false;

        // Navigate to the parent of the last segment
        var current = source;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current == null)
                return false;
            current = ResolveSegment(current, segments[i]);
        }

        if (current == null)
            return false;

        // Set the value on the last segment
        return SetSegmentValue(current, segments[^1], value);
    }

    private static string[] ParsePath(string path)
    {
        // Simple parsing - split by '.' but handle indexers and parentheses
        var segments = new List<string>();
        var currentSegment = new System.Text.StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;

        foreach (var c in path)
        {
            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == '[') bracketDepth++;
            else if (c == ']') bracketDepth--;

            if (c == '.' && parenDepth == 0 && bracketDepth == 0)
            {
                if (currentSegment.Length > 0)
                {
                    segments.Add(currentSegment.ToString());
                    currentSegment.Clear();
                }
            }
            else
            {
                currentSegment.Append(c);
            }
        }

        if (currentSegment.Length > 0)
        {
            segments.Add(currentSegment.ToString());
        }

        return segments.ToArray();
    }

    private object? ResolveSegment(object current, string segment)
    {
        // Handle indexer [index]
        if (segment.StartsWith('[') && segment.EndsWith(']'))
        {
            var indexStr = segment[1..^1];
            return ResolveIndexer(current, indexStr);
        }

        // Handle attached property (Type.Property)
        if (segment.StartsWith('(') && segment.EndsWith(')'))
        {
            var attachedProperty = segment[1..^1];
            return ResolveAttachedProperty(current, attachedProperty);
        }

        // Handle property with indexer: Property[index]
        var bracketIndex = segment.IndexOf('[');
        if (bracketIndex > 0)
        {
            var propertyName = segment[..bracketIndex];
            var indexPart = segment[bracketIndex..];

            var propertyValue = ResolveProperty(current, propertyName);
            if (propertyValue == null)
                return null;

            return ResolveSegment(propertyValue, indexPart);
        }

        // Regular property
        return ResolveProperty(current, segment);
    }

    private static object? ResolveProperty(object current, string propertyName)
    {
        // Check for DependencyProperty
        if (current is DependencyObject depObj)
        {
            var dpField = current.GetType().GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty dp)
            {
                return depObj.GetValue(dp);
            }
        }

        // Regular property
        var property = current.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(current);
    }

    private static object? ResolveIndexer(object current, string indexStr)
    {
        var type = current.GetType();

        // Try integer indexer
        if (int.TryParse(indexStr, out var intIndex))
        {
            var indexer = type.GetProperty("Item", new[] { typeof(int) });
            if (indexer != null)
            {
                return indexer.GetValue(current, new object[] { intIndex });
            }

            // Try IList
            if (current is System.Collections.IList list)
            {
                return list[intIndex];
            }
        }

        // Try string indexer
        var stringIndexer = type.GetProperty("Item", new[] { typeof(string) });
        if (stringIndexer != null)
        {
            return stringIndexer.GetValue(current, new object[] { indexStr });
        }

        // Try IDictionary
        if (current is System.Collections.IDictionary dict)
        {
            return dict[indexStr];
        }

        return null;
    }

    private static object? ResolveAttachedProperty(object current, string attachedProperty)
    {
        // Parse Type.Property
        var dotIndex = attachedProperty.LastIndexOf('.');
        if (dotIndex < 0)
            return null;

        var typeName = attachedProperty[..dotIndex];
        var propertyName = attachedProperty[(dotIndex + 1)..];

        // Find the type
        var ownerType = FindType(typeName);
        if (ownerType == null)
            return null;

        // Find the DependencyProperty
        var dpField = ownerType.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static);
        if (dpField?.GetValue(null) is not DependencyProperty dp)
            return null;

        // Get the value
        if (current is DependencyObject depObj)
        {
            return depObj.GetValue(dp);
        }

        return null;
    }

    private static bool SetSegmentValue(object current, string segment, object? value)
    {
        // Handle indexer
        if (segment.StartsWith('[') && segment.EndsWith(']'))
        {
            var indexStr = segment[1..^1];
            return SetIndexerValue(current, indexStr, value);
        }

        // Handle attached property
        if (segment.StartsWith('(') && segment.EndsWith(')'))
        {
            var attachedProperty = segment[1..^1];
            return SetAttachedPropertyValue(current, attachedProperty, value);
        }

        // Regular property
        return SetPropertyValue(current, segment, value);
    }

    private static bool SetPropertyValue(object current, string propertyName, object? value)
    {
        // Check for DependencyProperty
        if (current is DependencyObject depObj)
        {
            var dpField = current.GetType().GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField?.GetValue(null) is DependencyProperty dp)
            {
                depObj.SetValue(dp, value);
                return true;
            }
        }

        // Regular property
        var property = current.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.CanWrite)
        {
            property.SetValue(current, value);
            return true;
        }

        return false;
    }

    private static bool SetIndexerValue(object current, string indexStr, object? value)
    {
        var type = current.GetType();

        if (int.TryParse(indexStr, out var intIndex))
        {
            var indexer = type.GetProperty("Item", new[] { typeof(int) });
            if (indexer != null && indexer.CanWrite)
            {
                indexer.SetValue(current, value, new object[] { intIndex });
                return true;
            }

            if (current is System.Collections.IList list)
            {
                list[intIndex] = value;
                return true;
            }
        }

        var stringIndexer = type.GetProperty("Item", new[] { typeof(string) });
        if (stringIndexer != null && stringIndexer.CanWrite)
        {
            stringIndexer.SetValue(current, value, new object[] { indexStr });
            return true;
        }

        if (current is System.Collections.IDictionary dict)
        {
            dict[indexStr] = value;
            return true;
        }

        return false;
    }

    private static bool SetAttachedPropertyValue(object current, string attachedProperty, object? value)
    {
        var dotIndex = attachedProperty.LastIndexOf('.');
        if (dotIndex < 0)
            return false;

        var typeName = attachedProperty[..dotIndex];
        var propertyName = attachedProperty[(dotIndex + 1)..];

        var ownerType = FindType(typeName);
        if (ownerType == null)
            return false;

        var dpField = ownerType.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static);
        if (dpField?.GetValue(null) is not DependencyProperty dp)
            return false;

        if (current is DependencyObject depObj)
        {
            depObj.SetValue(dp, value);
            return true;
        }

        return false;
    }

    private static Type? FindType(string typeName)
    {
        // AOT-safe: Use the registered type resolver (XamlTypeRegistry) first
        if (TypeResolver.ResolveTypeByName != null)
        {
            var type = TypeResolver.ResolveTypeByName(typeName);
            if (type != null)
                return type;
        }

        // Fallback: Search in loaded assemblies (works in non-AOT scenarios)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null)
                return type;

            type = assembly.GetType($"Jalium.UI.{typeName}");
            if (type != null)
                return type;

            type = assembly.GetType($"Jalium.UI.Controls.{typeName}");
            if (type != null)
                return type;
        }

        return null;
    }

    /// <summary>
    /// Returns the path string.
    /// </summary>
    public override string ToString() => _path;
}

/// <summary>
/// Provides a type converter for PropertyPath.
/// </summary>
public sealed class PropertyPathConverter : TypeConverter
{
    /// <summary>
    /// Determines whether this converter can convert from the specified source type.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <summary>
    /// Converts the specified value to a PropertyPath.
    /// </summary>
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return new PropertyPath(str);
        }
        return base.ConvertFrom(context, culture, value);
    }

    /// <summary>
    /// Converts the PropertyPath to the specified destination type.
    /// </summary>
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is PropertyPath path)
        {
            return path.Path;
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
