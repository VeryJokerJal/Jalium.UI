using System.Globalization;
using System.Reflection;
using System.Text;
using Jalium.UI.Markup;

namespace Jalium.UI.Xaml;

/// <summary>
/// Provides a static Save method that can be used for limited XAML serialization of provided objects into XAML markup.
/// </summary>
public static class XamlWriter
{
    /// <summary>
    /// Returns a XAML string that serializes the provided object.
    /// </summary>
    public static string Save(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        Save(obj, writer, 0);
        return sb.ToString();
    }

    /// <summary>
    /// Saves XAML information as the source for a provided text writer.
    /// </summary>
    public static void Save(object obj, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(writer);
        Save(obj, writer, 0);
    }

    /// <summary>
    /// Saves XAML information into a specified stream as the source.
    /// </summary>
    public static void Save(object obj, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        Save(obj, writer, 0);
        writer.Flush();
    }

    private static void Save(object obj, TextWriter writer, int indent)
    {
        var type = obj.GetType();
        var typeName = type.Name;
        var indentStr = new string(' ', indent);

        // Get properties that are different from defaults
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => !IsExcludedProperty(p.Name))
            .ToList();

        var simpleProps = new List<(string Name, string Value)>();
        var complexProps = new List<(string Name, object Value)>();

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                if (value == null) continue;

                if (IsSimpleType(prop.PropertyType))
                {
                    var str = ConvertToString(value);
                    if (str != null)
                        simpleProps.Add((prop.Name, str));
                }
                else if (value is System.Collections.IEnumerable enumerable && prop.PropertyType != typeof(string))
                {
                    var items = new List<object>();
                    foreach (var item in enumerable) items.Add(item);
                    if (items.Count > 0)
                        complexProps.Add((prop.Name, items));
                }
                else
                {
                    complexProps.Add((prop.Name, value));
                }
            }
            catch
            {
                // Skip properties that throw on read
            }
        }

        // Write opening tag
        writer.Write($"{indentStr}<{typeName}");
        foreach (var (name, value) in simpleProps)
        {
            writer.Write($" {name}=\"{EscapeXml(value)}\"");
        }

        if (complexProps.Count == 0)
        {
            writer.WriteLine(" />");
        }
        else
        {
            writer.WriteLine(">");

            foreach (var (name, value) in complexProps)
            {
                if (value is List<object> items)
                {
                    writer.WriteLine($"{indentStr}  <{typeName}.{name}>");
                    foreach (var item in items)
                    {
                        Save(item, writer, indent + 4);
                    }
                    writer.WriteLine($"{indentStr}  </{typeName}.{name}>");
                }
                else
                {
                    writer.WriteLine($"{indentStr}  <{typeName}.{name}>");
                    Save(value, writer, indent + 4);
                    writer.WriteLine($"{indentStr}  </{typeName}.{name}>");
                }
            }

            writer.WriteLine($"{indentStr}</{typeName}>");
        }
    }

    private static bool IsSimpleType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid) || type == typeof(TimeSpan) ||
               type.IsEnum;
    }

    private static string? ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            bool b => b ? "True" : "False",
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            Enum e => e.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()!
        };
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static bool IsExcludedProperty(string name)
    {
        // Exclude common infrastructure properties that shouldn't be serialized
        return name is "Parent" or "TemplatedParent" or "DataContext" or "Dispatcher" or
               "DependencyObjectType" or "IsSealed" or "NativeHandle";
    }
}
