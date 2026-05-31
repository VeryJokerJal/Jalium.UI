using System.Globalization;
using Jalium.UI.Controls;

namespace Jalium.UI.Markup;

internal static class GridDefinitionParser
{
    public static RowDefinitionCollection ParseRowDefinitions(string value) =>
        ParseDefinitions<RowDefinitionCollection, RowDefinition>(value, "Height", "Width");

    public static ColumnDefinitionCollection ParseColumnDefinitions(string value) =>
        ParseDefinitions<ColumnDefinitionCollection, ColumnDefinition>(value, "Width", "Height");

    public static bool TryResolveRowReference(Grid grid, string reference, out int index) =>
        TryResolveReference(grid.RowDefinitions, reference, out index);

    public static bool TryResolveColumnReference(Grid grid, string reference, out int index) =>
        TryResolveReference(grid.ColumnDefinitions, reference, out index);

    private static TCollection ParseDefinitions<TCollection, TDefinition>(
        string value,
        string primaryLengthProperty,
        string alternateLengthProperty)
        where TCollection : IList<TDefinition>, new()
        where TDefinition : DefinitionBase, new()
    {
        var collection = new TCollection();

        foreach (var entry in SplitDefinitions(value))
        {
            var definition = new TDefinition();
            ApplyEntry(definition, entry, primaryLengthProperty, alternateLengthProperty);
            collection.Add(definition);
        }

        return collection;
    }

    private static void ApplyEntry<TDefinition>(
        TDefinition definition,
        string entry,
        string primaryLengthProperty,
        string alternateLengthProperty)
        where TDefinition : DefinitionBase
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var trimmed = entry.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            foreach (var (name, value) in ParsePropertyBag(trimmed[1..^1]))
            {
                var propertyName = NormalizePropertyName(name, primaryLengthProperty, alternateLengthProperty);
                SetProperty(definition, propertyName, value);
            }

            return;
        }

        SetProperty(definition, primaryLengthProperty, trimmed);
    }

    private static string NormalizePropertyName(string propertyName, string primaryLengthProperty, string alternateLengthProperty)
    {
        if (propertyName.Equals("Size", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals(alternateLengthProperty, StringComparison.OrdinalIgnoreCase))
        {
            return primaryLengthProperty;
        }

        return propertyName;
    }

    /// <summary>
    /// Sets the named property on a Row/ColumnDefinition without reflection by going through the
    /// AOT-safe <see cref="DependencyProperty.FromName"/> registry. Only the well-known
    /// length/min/max/name properties of the two DefinitionBase subclasses are supported here —
    /// any unknown property name throws, matching the previous reflective behavior.
    /// </summary>
    private static void SetProperty(DefinitionBase instance, string propertyName, string value)
    {
        if (propertyName.Equals(nameof(DefinitionBase.Name), StringComparison.OrdinalIgnoreCase))
        {
            instance.Name = value;
            return;
        }

        var dp = DependencyProperty.FromName(instance.GetType(), propertyName);
        if (dp == null)
        {
            throw new FormatException($"Unknown grid definition property '{propertyName}'.");
        }

        object? convertedValue = dp.PropertyType == typeof(string)
            ? value
            : TypeConverterRegistry.ConvertValue(value, dp.PropertyType);

        if (convertedValue == null)
        {
            throw new FormatException(
                $"Cannot convert '{value}' to '{dp.PropertyType.Name}' for grid definition property '{propertyName}'.");
        }

        instance.SetValue(dp, convertedValue);
    }

    private static IEnumerable<(string Name, string Value)> ParsePropertyBag(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                yield break;
            }

            var nameStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '=')
            {
                index++;
            }

            var name = text[nameStart..index];
            SkipWhitespace(text, ref index);

            if (index >= text.Length || text[index] != '=')
            {
                throw new FormatException($"Invalid grid definition property assignment near '{name}'.");
            }

            index++;
            SkipWhitespace(text, ref index);

            if (index >= text.Length)
            {
                throw new FormatException($"Missing value for grid definition property '{name}'.");
            }

            string value;
            if (text[index] == '"' || text[index] == '\'')
            {
                var quote = text[index++];
                var valueStart = index;
                while (index < text.Length && text[index] != quote)
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    throw new FormatException($"Unterminated quoted value for grid definition property '{name}'.");
                }

                value = text[valueStart..index];
                index++;
            }
            else
            {
                var valueStart = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                value = text[valueStart..index];
            }

            yield return (name, value);
        }
    }

    private static IEnumerable<string> SplitDefinitions(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var start = 0;
        var depth = 0;
        var quote = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                continue;
            }

            if (ch == ',' && depth == 0)
            {
                var segment = value[start..index].Trim();
                if (!string.IsNullOrEmpty(segment))
                {
                    yield return segment;
                }

                start = index + 1;
            }
        }

        var tail = value[start..].Trim();
        if (!string.IsNullOrEmpty(tail))
        {
            yield return tail;
        }
    }

    private static bool TryResolveReference<TDefinition>(IReadOnlyList<TDefinition> definitions, string reference, out int index)
        where TDefinition : DefinitionBase
    {
        if (int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return true;
        }

        for (var i = 0; i < definitions.Count; i++)
        {
            if (string.Equals(definitions[i].Name, reference, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = 0;
        return false;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }
}
