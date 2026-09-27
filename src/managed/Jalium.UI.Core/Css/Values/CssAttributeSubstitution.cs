using System.Globalization;
using System.Text;

namespace Jalium.UI.Styling;

/// <summary>Computed-value attribute substitution on the styled element or query container.</summary>
internal static class CssAttributeSubstitution
{
    internal static bool TrySubstitute(string source, CssNode node, CssLengthContext lengths,
        out string result, int depth = 0, bool inUrl = false,
        CssNamespaceContext? namespaces = null)
    {
        result = string.Empty;
        if (depth >= 32 || source.Length > 1_048_576) return false;
        var output = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length;)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = CssTokenReader.SkipComment(source, i);
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            if (source[i] is '\'' or '"')
            {
                var end = CssTokenReader.SkipString(source, i);
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            if (CssSyntax.IsNameStart(source, i))
            {
                var reader = new CssTokenReader(source.AsSpan(i));
                if (reader.TryReadFunction(out var name, out var arguments))
                {
                    string replacement;
                    if (name.Equals("first-valid", StringComparison.OrdinalIgnoreCase))
                    {
                        // A candidate is selected by property grammar before its attr()
                        // functions are substituted; unused candidates stay untouched.
                        output.Append(source.AsSpan(i, reader.Position));
                    }
                    else if (name.Equals("attr", StringComparison.OrdinalIgnoreCase))
                    {
                        // Attribute values may not become any part of a URL.
                        if (inUrl) return false;
                        if (!Resolve(arguments.Remaining.ToString(), node, lengths, out replacement,
                                depth + 1, namespaces)) return false;
                        output.Append(' ').Append(replacement).Append(' ');
                    }
                    else
                    {
                        var url = name.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                                  name.Equals("src", StringComparison.OrdinalIgnoreCase);
                        if (!TrySubstitute(arguments.Remaining.ToString(), node, lengths,
                                out replacement, depth + 1, inUrl || url, namespaces)) return false;
                        output.Append(name).Append('(').Append(replacement).Append(')');
                    }
                    i += reader.Position;
                    continue;
                }
                var end = i;
                if (CssSyntax.ReadIdentifier(source, ref end, out _))
                { output.Append(source.AsSpan(i, end - i)); i = end; continue; }
            }
            if (source[i] == '\\')
            {
                var end = i;
                if (!CssSyntax.ReadEscape(source, ref end, out _)) return false;
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            output.Append(source[i++]);
            if (output.Length > 1_048_576) return false;
        }
        result = output.ToString();
        return true;
    }

    private static bool Resolve(string arguments, CssNode node, CssLengthContext lengths, out string result,
        int depth, CssNamespaceContext? namespaces)
    {
        result = string.Empty;
        var reader = new CssTokenReader(arguments);
        if (!reader.TryReadUntilTopLevelComma(out var first)) return false;
        string? fallback = null;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadComma()) return false;
            fallback = reader.Remaining.ToString();
            var check = new CssTokenReader(fallback);
            if (check.TryReadUntilTopLevelComma(out _) && !check.AtEnd) return false;
        }

        string? type = null;
        if (CssCustomProperties.TrySubstitute(first.ToString(), node, out var expandedFirst) &&
            TrySubstitute(expandedFirst, node, lengths, out expandedFirst, depth, namespaces: namespaces) &&
            TryReadDescriptor(expandedFirst, namespaces, out var attributeName, out type))
        {
            var attribute = Attribute(node, attributeName);
            if (attribute is not null && Convert(attribute, type, node, lengths, out result,
                    depth, namespaces)) return true;
        }

        // The first argument is parsed after substitution. A failed substitution,
        // name, or type parse selects the same fallback as a missing attribute.
        if (fallback is null)
        {
            if (type is null) { result = "\"\""; return true; }
            return false;
        }
        return CssCustomProperties.TrySubstitute(fallback, node, out fallback) &&
            TrySubstitute(fallback, node, lengths, out result, depth, namespaces: namespaces);
    }

    private static bool TryReadDescriptor(string text, CssNamespaceContext? namespaces,
        out CssExpandedName attributeName, out string? type)
    {
        attributeName = default;
        type = null;
        var leading = new CssTokenReader(text);
        var source = leading.Remaining;
        var position = 0;
        string? prefix = null;
        var localName = string.Empty;
        if (!source.IsEmpty && source[0] == '|')
        {
            prefix = string.Empty;
            position++;
        }
        else
        {
            if (!CssSyntax.ReadIdentifier(source, ref position, out var first)) return false;
            var separator = position;
            SkipComments(source, ref separator);
            if (separator < source.Length && source[separator] == '|')
            {
                prefix = first.ToString();
                position = separator + 1;
            }
            else localName = first.ToString();
        }
        if (prefix is not null)
        {
            SkipComments(source, ref position);
            if (!CssSyntax.ReadIdentifier(source, ref position, out var local)) return false;
            localName = local.ToString();
        }
        var namespaceUri = string.Empty;
        if (prefix is { Length: > 0 } &&
            (namespaces is null || !namespaces.TryResolve(prefix, out namespaceUri))) return false;
        attributeName = new(namespaceUri, localName);
        var descriptor = new CssTokenReader(source[position..]);
        if (!descriptor.AtEnd)
        {
            if (descriptor.TryReadFunction(out var function, out var syntax) &&
                function.Equals("type", StringComparison.OrdinalIgnoreCase) && descriptor.AtEnd)
                type = "type(" + syntax.Remaining.ToString() + ")";
            else if (descriptor.TryReadDelimiter('%') && descriptor.AtEnd)
                type = "%";
            else if (descriptor.TryReadIdent(out var keyword) && descriptor.AtEnd)
                type = keyword.ToString().ToLowerInvariant();
            else return false;
        }
        // An unknown attr-unit is valid syntax. It selects the fallback when the
        // attribute is evaluated, even when that attribute is present.
        if (type is not null && type.StartsWith("type(", StringComparison.Ordinal) &&
            type[5..^1].Trim() != "<frequency>" &&
            !(CssPropertySyntax.Parse(type[5..^1]) is { } parsedSyntax &&
              parsedSyntax.Components.All(component => component.Name != "url"))) return false;
        return true;
    }

    private static void SkipComments(ReadOnlySpan<char> source, ref int position)
    {
        while (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
            position = CssTokenReader.SkipComment(source, position);
    }

    private static bool Convert(string attribute, string? type, CssNode node, CssLengthContext lengths,
        out string result, int depth, CssNamespaceContext? namespaces)
    {
        result = string.Empty;
        if (type is null or "raw-string")
        { result = CssDeclarationValue.String(attribute); return true; }
        if (type is "number" or "%" || CssUnitConversion.TryMapUnit(type, out var unit) && unit != CssUnit.None)
        {
            var reader = new CssTokenReader(attribute);
            if (!reader.TryReadNumber(out var number, out var actualUnit) || actualUnit != CssUnit.None ||
                reader.NumberWasCalculated || !reader.AtEnd || !double.IsFinite(number)) return false;
            result = CssPropertySyntax.Number(number) + (type == "number" ? string.Empty : type);
            return true;
        }
        if (!type.StartsWith("type(", StringComparison.Ordinal)) return false;
        if (!CssCustomProperties.TrySubstitute(attribute, node, out var substituted) ||
            !TrySubstitute(substituted, node, lengths, out substituted, depth,
                namespaces: namespaces)) return false;
        if (type[5..^1].Trim() == "<frequency>")
        {
            var reader = new CssTokenReader(substituted, new CssNumericReadContext(lengths));
            if (!reader.TryReadNumber(out var frequency, out var frequencyUnit) || !reader.AtEnd ||
                frequencyUnit is not (CssUnit.Hz or CssUnit.Khz) || !double.IsFinite(frequency)) return false;
            result = CssPropertySyntax.Number(frequency * (frequencyUnit == CssUnit.Khz ? 1000 : 1)) + "Hz";
            return true;
        }
        var syntax = CssPropertySyntax.Parse(type[5..^1]);
        if (syntax is null || !syntax.TryCompute(substituted, new CssPropertyValueContext(lengths), out var value)) return false;
        // type(<image>) can parse url(...), although type(<url>) itself was rejected above.
        if (CssCustomProperties.ContainsUrlFunction(value!.Text)) return false;
        result = value!.Text;
        return true;
    }

    private static string? Attribute(CssNode node, CssExpandedName name)
    {
        foreach (var item in Css.AttributeValues(node.Target, name.NamespaceUri, name.LocalName)) return item.Value;
        foreach (var item in CssXmlIdentity.Attributes(node.Target, name.NamespaceUri, name.LocalName))
            if (item.Value is not null) return item.Value;
        if (name.NamespaceUri.Length > 0) return null;
        if (name.LocalName == "id") return string.IsNullOrEmpty(node.Name) ? null : node.Name;
        if (name.LocalName == "class")
        {
            var classes = Css.GetClass(node.Target);
            return string.IsNullOrEmpty(classes) ? null : classes;
        }
        if (CssDependencyPropertyLookup.Find(node.GetType(), name.LocalName) is { } property && node.HasLocalValue(property))
            return System.Convert.ToString(node.GetValue(property), CultureInfo.InvariantCulture);
        return null;
    }
}
