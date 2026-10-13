using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Implements a data structure for describing a property as a path below another property,
/// or below an owning type.
/// </summary>
[TypeConverter(typeof(PropertyPathConverter))]
public sealed class PropertyPath
{
    private string _path;
    private readonly Collection<object> _pathParameters;
    private string[]? _pathSegments;
    private string[]? _parsedSegments;

    /// <summary>
    /// Initializes a new instance of the PropertyPath class.
    /// </summary>
    /// <param name="path">A string that describes the path.</param>
    public PropertyPath(string path)
    {
        _path = path ?? string.Empty;
        _pathParameters = new Collection<object>();
    }

    /// <summary>
    /// Initializes a new instance of the PropertyPath class with the specified path and parameters.
    /// </summary>
    /// <param name="path">A string that describes the path.</param>
    /// <param name="pathParameters">An array of parameters for the path.</param>
    public PropertyPath(string path, params object[] pathParameters)
    {
        _path = path ?? string.Empty;
        _pathParameters = new Collection<object>(
            (pathParameters ?? Array.Empty<object>()).ToList());
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
            _pathParameters = new Collection<object> { parameter };
        }
        else if (parameter is string str)
        {
            _path = str;
            _pathParameters = new Collection<object>();
        }
        else
        {
            _path = parameter?.ToString() ?? string.Empty;
            _pathParameters = new Collection<object>();
        }
    }

    /// <summary>
    /// Gets the path string.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_path, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _path = normalized;
            _pathSegments = null;
            _parsedSegments = null;
        }
    }

    /// <summary>
    /// Gets the collection of parameters to use when the path refers to indexed parameters.
    /// </summary>
    public Collection<object> PathParameters => _pathParameters;

    /// <summary>
    /// Gets the path segments (simple dot-separated split).
    /// </summary>
    public string[] PathSegments
    {
        get
        {
            var segments = CachedPathSegments;
            return segments.Length == 0 ? segments : (string[])segments.Clone();
        }
    }

    /// <summary>
    /// Allocation-free segment view for framework binding consumers. Callers
    /// must treat the returned array as immutable.
    /// </summary>
    internal string[] CachedPathSegments =>
        _pathSegments ??= string.IsNullOrEmpty(_path) ? Array.Empty<string>() : _path.Split('.');

    // BindingExpression uses the same bracket-aware split as ResolveValue. A dot inside
    // a dictionary key is part of that key, rather than a property separator.
    internal string[] CachedParsedSegments => GetParsedSegments();

    internal static string BindingPropertyName(string segment)
    {
        var bracket = segment.IndexOf('[');
        return bracket < 0 ? segment : bracket == 0 ? "Item[]" : segment[..bracket];
    }

    internal static bool TryGetIndexedSegment(string segment, out string propertyName, out string index)
    {
        propertyName = "";
        index = "";
        var bracket = segment.IndexOf('[');
        if (bracket < 0 || !segment.EndsWith(']') || segment.IndexOf('[', bracket + 1) >= 0
            || segment.IndexOf(']') != segment.Length - 1 || bracket == segment.Length - 2) return false;
        propertyName = segment[..bracket];
        index = segment[(bracket + 1)..^1];
        return true;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Binding path segments use PropertyAccessorRegistry and public indexer reflection when no typed accessor exists.")]
    internal bool TryReadBindingSegment(object current, string segment, out object? value)
    {
        if (current is DependencyObject source && TryGetAttachedDependencyProperty(segment) is { } property)
        {
            value = source.GetValue(property);
            return true;
        }
        if (TryGetIndexedSegment(segment, out var propertyName, out var index))
        {
            object? indexedSource = current;
            if (propertyName.Length > 0 && !TryReadBindingSegment(current, propertyName, out indexedSource))
            {
                value = null;
                return false;
            }
            if (indexedSource != null) return TryReadIndexer(indexedSource, index, out value);
            value = null;
            return false;
        }
        if (segment.IndexOfAny(['[', ']', '(', ')']) >= 0)
        {
            value = null;
            return false;
        }
        return PropertyAccessorRegistry.TryReadProperty(current, segment, out value);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Owner-qualified property paths use the existing registered type resolver, with FindType reflection fallback.")]
    internal DependencyProperty? TryGetAttachedDependencyProperty(string segment)
    {
        if (!segment.StartsWith('(') || !segment.EndsWith(')')) return null;
        var name = segment[1..^1];
        if (int.TryParse(name, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index))
            return index < _pathParameters.Count ? _pathParameters[index] as DependencyProperty : null;

        // The single-DependencyProperty constructor retains its descriptive path,
        // while the supplied object provides an unambiguous, reflection-free owner.
        foreach (var parameter in _pathParameters)
            if (parameter is DependencyProperty property &&
                (name == $"{property.OwnerType.Name}.{property.Name}" ||
                 name == $"{property.OwnerType.FullName}.{property.Name}"))
                return property;

        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return null;
        var owner = FindType(name[..dot]);
        return owner == null ? null : DependencyProperty.FromName(owner, name[(dot + 1)..]);
    }

    /// <summary>
    /// Resolves the value at this path starting from the specified source.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <returns>The resolved value, or null if resolution fails.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("PropertyPath walks user object graphs via reflection when no DependencyProperty exists for a segment.")]
    internal object? ResolveValue(object source)
    {
        if (source == null || string.IsNullOrEmpty(_path))
            return null;

        var current = source;
        var segments = GetParsedSegments();

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
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("PropertyPath walks user object graphs via reflection when no DependencyProperty exists for a segment.")]
    internal bool SetValue(object source, object? value)
    {
        if (source == null || string.IsNullOrEmpty(_path))
            return false;

        var segments = GetParsedSegments();
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

    private string[] GetParsedSegments() => _parsedSegments ??= ParsePath(_path);

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("PropertyPath segment resolution may walk user types via reflection.")]
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

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Walks user object graphs via reflection to resolve property names.")]
    private static object? ResolveProperty(object current, string propertyName)
    {
        // Check for DependencyProperty via the AOT-safe registry (no reflection).
        if (current is DependencyObject depObj)
        {
            var dp = DependencyProperty.FromName(current.GetType(), propertyName);
            if (dp != null)
                return depObj.GetValue(dp);
        }

        // Regular CLR property — the [DynamicallyAccessedMembers] annotation on
        // 'current' guarantees the trimmer keeps PublicProperties on the runtime type.
        var property = current.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(current);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Walks user object graphs via reflection to resolve indexers.")]
    private static object? ResolveIndexer(object current, string indexStr)
    {
        return TryReadIndexer(current, indexStr, out var value) ? value : null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Public Item indexers are resolved through reflection when a collection interface is unavailable.")]
    private static bool TryReadIndexer(object current, string indexStr, out object? value)
    {
        value = null;
        var type = current.GetType();
        if (int.TryParse(indexStr, out var intIndex))
        {
            if (type.GetProperty("Item", new[] { typeof(int) }) is { CanRead: true } numeric)
            {
                try { value = numeric.GetValue(current, [intIndex]); return true; }
                catch (TargetInvocationException error) when (error.InnerException is ArgumentOutOfRangeException or IndexOutOfRangeException) { return false; }
            }
            if (current is System.Collections.IList list)
            {
                if (intIndex < 0 || intIndex >= list.Count) return false;
                value = list[intIndex];
                return true;
            }
        }
        if (type.GetProperty("Item", new[] { typeof(string) }) is { CanRead: true } keyed)
        {
            try { value = keyed.GetValue(current, [indexStr]); return true; }
            catch (TargetInvocationException error) when (error.InnerException is KeyNotFoundException) { return false; }
        }
        if (current is System.Collections.IDictionary map)
        {
            if (!map.Contains(indexStr)) return false;
            value = map[indexStr];
            return true;
        }
        return false;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Falls back to Assembly.GetType(string) inside FindType for trimmed types.")]
    private object? ResolveAttachedProperty(object current, string attachedProperty)
    {
        return current is DependencyObject source &&
            TryGetAttachedDependencyProperty($"({attachedProperty})") is { } property
            ? source.GetValue(property) : null;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("PropertyPath segment write may walk user types via reflection.")]
    private bool SetSegmentValue(object current, string segment, object? value)
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

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Walks user object graphs via reflection to resolve property names.")]
    private static bool SetPropertyValue(object current, string propertyName, object? value)
    {
        // Check for DependencyProperty via the AOT-safe registry.
        if (current is DependencyObject depObj)
        {
            var dp = DependencyProperty.FromName(current.GetType(), propertyName);
            if (dp != null)
            {
                depObj.SetValue(dp, value);
                return true;
            }
        }

        // Regular CLR property — DAM annotation on 'current' keeps PublicProperties.
        var property = current.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.CanWrite)
        {
            property.SetValue(current, value);
            return true;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Walks user object graphs via reflection to resolve indexers.")]
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

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Falls back to Assembly.GetType(string) inside FindType for trimmed types.")]
    private bool SetAttachedPropertyValue(object current, string attachedProperty, object? value)
    {
        if (current is DependencyObject source &&
            TryGetAttachedDependencyProperty($"({attachedProperty})") is { } property)
        {
            source.SetValue(property, value);
            return true;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Falls back to Assembly.GetType(string) which carries the RUC contract for trimmed types.")]
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
