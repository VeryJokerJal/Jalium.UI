using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Jalium.Extensions.Configuration;

/// <summary>
/// Reflection-based binder that materializes a configuration tree into a POCO graph.
/// Signatures and behavior mirror <c>Microsoft.Extensions.Configuration.ConfigurationBinder</c>.
/// </summary>
public static class ConfigurationBinder
{
    [RequiresUnreferencedCode("Uses reflection. Preserve target types when trimming.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code.")]
    public static void Bind(this IConfiguration configuration, object? instance)
        => Bind(configuration, instance, _ => { });

    [RequiresUnreferencedCode("Uses reflection. Preserve target types when trimming.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code.")]
    public static void Bind(this IConfiguration configuration, string key, object? instance)
        => configuration.GetSection(key).Bind(instance);

    [RequiresUnreferencedCode("Uses reflection. Preserve target types when trimming.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code.")]
    public static void Bind(this IConfiguration configuration, object? instance, Action<BinderOptions> configureOptions)
    {
        if (instance == null) return;
        var options = new BinderOptions();
        configureOptions(options);
        BindInstance(instance.GetType(), instance, configuration, options);
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static T? Get<T>(this IConfiguration configuration) => (T?)configuration.Get(typeof(T));

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static T? Get<T>(this IConfiguration configuration, Action<BinderOptions> configureOptions)
        => (T?)configuration.Get(typeof(T), configureOptions);

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static object? Get(this IConfiguration configuration, Type type)
        => configuration.Get(type, _ => { });

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static object? Get(this IConfiguration configuration, Type type, Action<BinderOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(type);
        var options = new BinderOptions();
        configureOptions(options);
        return BindInstance(type, instance: null, configuration, options);
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static T GetValue<T>(this IConfiguration configuration, string key, T defaultValue = default!)
    {
        var v = configuration[key];
        if (v == null) return defaultValue;
        return (T)ConvertValue(typeof(T), v, key)!;
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static object? GetValue(this IConfiguration configuration, Type type, string key) => GetValue(configuration, type, key, defaultValue: null);

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    public static object? GetValue(this IConfiguration configuration, Type type, string key, object? defaultValue)
    {
        var v = configuration[key];
        if (v == null) return defaultValue;
        return ConvertValue(type, v, key);
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    private static object? BindInstance(Type type, object? instance, IConfiguration config, BinderOptions options)
    {
        // Direct primitive section?
        if (config is IConfigurationSection section && section.Value != null && IsPrimitive(type))
        {
            return ConvertValue(type, section.Value, section.Path);
        }

        instance ??= CreateInstance(type);
        if (instance == null) return null;

        if (instance is IDictionary dict)
        {
            BindDictionary(dict, type, config, options);
            return instance;
        }

        if (instance is IList list && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            BindList(list, type, config, options);
            return instance;
        }

        // Plain POCO — walk public read/write properties and fields.
        var members = GetBindableMembers(type);
        foreach (var (name, getter, setter, memberType) in members)
        {
            var child = config.GetSection(name);
            var sectionValue = (child as IConfigurationSection)?.Value;
            if (sectionValue != null && IsPrimitive(memberType))
            {
                setter(instance, ConvertValue(memberType, sectionValue, child.GetSection(name).Path));
            }
            else if (HasChildren(child))
            {
                var current = getter(instance);
                var bound = BindInstance(memberType, current, child, options);
                if (bound != null) setter(instance, bound);
            }
            else if (sectionValue != null)
            {
                setter(instance, ConvertValue(memberType, sectionValue, name));
            }
        }
        return instance;
    }

    private static bool HasChildren(IConfiguration section)
    {
        foreach (var _ in section.GetChildren()) return true;
        return false;
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    private static void BindDictionary(IDictionary dict, Type dictType, IConfiguration config, BinderOptions options)
    {
        var args = dictType.GetGenericArguments();
        if (args.Length != 2) return;
        var keyType = args[0];
        var valueType = args[1];
        foreach (var child in config.GetChildren())
        {
            var key = ConvertValue(keyType, child.Key, child.Path)!;
            object? value;
            if (IsPrimitive(valueType)) value = ConvertValue(valueType, child.Value ?? string.Empty, child.Path);
            else value = BindInstance(valueType, null, child, options);
            dict[key] = value;
        }
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    private static void BindList(IList list, Type listType, IConfiguration config, BinderOptions options)
    {
        var elementType = listType.GetGenericArguments()[0];
        foreach (var child in config.GetChildren())
        {
            object? value;
            if (IsPrimitive(elementType)) value = ConvertValue(elementType, child.Value ?? string.Empty, child.Path);
            else value = BindInstance(elementType, null, child, options);
            list.Add(value);
        }
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    private static object? CreateInstance(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
        {
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(IList<>) || def == typeof(ICollection<>) || def == typeof(IEnumerable<>))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()));
                if (def == typeof(IDictionary<,>))
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()));
            }
            return null;
        }
        try { return Activator.CreateInstance(type); }
        catch { return null; }
    }

    [RequiresUnreferencedCode("Uses reflection.")]
    [RequiresDynamicCode("Uses reflection.")]
    private static List<(string Name, Func<object, object?> Getter, Action<object, object?> Setter, Type MemberType)> GetBindableMembers(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] Type type)
    {
        var result = new List<(string, Func<object, object?>, Action<object, object?>, Type)>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (!p.CanRead) continue;
            // Allow read-only complex properties (collections/POCOs we bind in place).
            var setter = p.CanWrite
                ? (Action<object, object?>)((obj, v) => p.SetValue(obj, v))
                : (obj, v) => { /* read-only: rely on in-place mutation */ };
            result.Add((p.Name, (obj) => p.GetValue(obj), setter, p.PropertyType));
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            result.Add((f.Name, (obj) => f.GetValue(obj), (obj, v) => f.SetValue(obj, v), f.FieldType));
        }
        return result;
    }

    private static bool IsPrimitive(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum
            || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(Uri) || t == typeof(DateOnly) || t == typeof(TimeOnly);
    }

    private static object? ConvertValue(Type type, string value, string? path)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (target == typeof(string)) return value;
            if (target.IsEnum) return Enum.Parse(target, value, ignoreCase: true);
            if (target == typeof(bool)) return bool.Parse(value);
            if (target == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(decimal)) return decimal.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(short)) return short.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(byte)) return byte.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(uint)) return uint.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(ulong)) return ulong.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(ushort)) return ushort.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(sbyte)) return sbyte.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(TimeSpan)) return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(Guid)) return Guid.Parse(value);
            if (target == typeof(Uri)) return new Uri(value, UriKind.RelativeOrAbsolute);
            if (target == typeof(DateOnly)) return DateOnly.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(TimeOnly)) return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
            var converter = TypeDescriptor.GetConverter(target);
            if (converter.CanConvertFrom(typeof(string))) return converter.ConvertFromInvariantString(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to convert configuration value at '{path}' to type '{type.FullName}'.", ex);
        }
        throw new InvalidOperationException($"No converter for type '{type.FullName}'.");
    }
}

public sealed class BinderOptions
{
    public bool BindNonPublicProperties { get; set; }
    public bool ErrorOnUnknownConfiguration { get; set; }
}

/// <summary><c>IConfiguration.GetSection</c>-style helpers that are not on <see cref="ConfigurationBinder"/>.</summary>
public static class ConfigurationExtensions
{
    public static IConfigurationSection GetSection(this IConfigurationRoot root, string key) => ((IConfiguration)root).GetSection(key);
    public static IEnumerable<IConfigurationSection> GetChildren(this IConfigurationRoot root) => ((IConfiguration)root).GetChildren();
    public static string? GetConnectionString(this IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection("ConnectionStrings")[name];
    }
    public static bool Exists(this IConfigurationSection section)
    {
        if (section == null) return false;
        if (section.Value != null) return true;
        foreach (var _ in section.GetChildren()) return true;
        return false;
    }

    /// <summary>Flatten the tree into <c>(key,value)</c> pairs, depth-first.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> AsEnumerable(this IConfiguration configuration) => AsEnumerable(configuration, makePathsRelative: false);

    public static IEnumerable<KeyValuePair<string, string?>> AsEnumerable(this IConfiguration configuration, bool makePathsRelative)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var stack = new Stack<IConfiguration>();
        stack.Push(configuration);
        string? rootPrefix = null;
        if (makePathsRelative && configuration is IConfigurationSection rootSection)
            rootPrefix = rootSection.Path + ConfigurationPath.KeyDelimiter;

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is IConfigurationSection cs && cs.Value != null)
            {
                var key = cs.Path;
                if (rootPrefix != null && key.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    key = key.Substring(rootPrefix.Length);
                yield return new KeyValuePair<string, string?>(key, cs.Value);
            }
            foreach (var child in current.GetChildren()) stack.Push(child);
        }
    }
}
