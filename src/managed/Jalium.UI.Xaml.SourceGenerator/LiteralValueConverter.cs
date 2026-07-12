using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Compile-time analogue of the runtime <c>TypeConverterRegistry</c>. Given a target
/// property type symbol and a raw jalxaml attribute string, attempts to produce a C#
/// expression that yields the converted value. Returns null when the value cannot be
/// converted statically (markup extension, unrecognised type, malformed literal); the
/// caller then falls back to the runtime <c>XamlBuilder.SetProperty</c> path.
/// </summary>
internal static class LiteralValueConverter
{
    /// <summary>
    /// Attempt to convert <paramref name="value"/> to a C# expression of type
    /// <paramref name="targetType"/>. Returns null when the conversion is not safe to
    /// inline at compile time. The caller is responsible for emitting the assignment
    /// (<c>{var}.{Prop} = {expr};</c>) or the property setter call.
    /// </summary>
    public static string? TryConvert(string value, ITypeSymbol targetType)
    {
        if (value == null)
            return "null";

        // Markup extensions / Razor escapes / dynamic resources go through the runtime
        // pipeline. Detection is lightweight — anything that starts with `{` and isn't
        // the `{}` escape-for-literal-brace falls back to runtime.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '{' && !trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return null;
        }

        // Strip the literal-brace escape (`{}#FFF` → `#FFF`).
        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            value = trimmed.Substring(2);
        }

        // Razor expressions / interpolation / code-block tokens — never safe inline.
        if (value.IndexOf('@') >= 0 && !value.StartsWith("@@", StringComparison.Ordinal))
        {
            return null;
        }

        // Unwrap nullable<T> — convert against the underlying value type, then reuse.
        var underlyingNullable = TryUnwrapNullable(targetType);
        if (underlyingNullable != null)
        {
            return TryConvert(value, underlyingNullable);
        }

        // Special types: System.String / System.Object — always emit as string literal.
        if (targetType.SpecialType == SpecialType.System_String ||
            targetType.SpecialType == SpecialType.System_Object)
        {
            return EscapeStringLiteral(value);
        }

        switch (targetType.SpecialType)
        {
            case SpecialType.System_Boolean:
                return TryConvertBool(value);
            case SpecialType.System_Int32:
                return TryConvertInt32(value);
            case SpecialType.System_Int64:
                return TryConvertInt64(value);
            case SpecialType.System_UInt32:
                return TryConvertUInt32(value);
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return TryConvertSmallIntegral(value, targetType.SpecialType);
            case SpecialType.System_Single:
                return TryConvertSingle(value);
            case SpecialType.System_Double:
                return TryConvertDouble(value);
            case SpecialType.System_Char:
                return TryConvertChar(value);
        }

        if (targetType.TypeKind == TypeKind.Enum)
        {
            return TryConvertEnum(value, (INamedTypeSymbol)targetType);
        }

        // Common framework structs / classes — match by full name. Each helper returns null
        // when the string does not parse cleanly, and we fall back to runtime conversion.
        var fullName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (fullName)
        {
            case "global::Jalium.UI.Thickness":
                return TryConvertThickness(value);
            case "global::Jalium.UI.CornerRadius":
                return TryConvertCornerRadius(value);
            case "global::Jalium.UI.GridLength":
                return TryConvertGridLength(value);
            case "global::Jalium.UI.Media.Color":
                return TryConvertColor(value);
            case "global::Jalium.UI.FontWeight":
                return TryConvertNamedStaticMember(value, "global::Jalium.UI.FontWeights");
            case "global::Jalium.UI.FontStyle":
                return TryConvertNamedStaticMember(value, "global::Jalium.UI.FontStyles");
            case "global::Jalium.UI.FontStretch":
                return TryConvertNamedStaticMember(value, "global::Jalium.UI.FontStretches");
        }

        // Brush hierarchy: anything assignable to Jalium.UI.Media.Brush gets the named-brush
        // / hex-color → SolidColorBrush treatment. Walks the base chain so SolidColorBrush
        // / GradientBrush properties also benefit when the value is a static-resource alias.
        if (TypeIsAssignableToBrush(targetType))
        {
            return TryConvertBrush(value);
        }

        return null;
    }

    private static bool TypeIsAssignableToBrush(ITypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var fn = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fn == "global::Jalium.UI.Media.Brush")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Lower a literal <c>Source="..."</c> on <see cref="Jalium.UI.ResourceDictionary"/>
    /// (and other Uri-typed properties) into a <c>new Uri(..., UriKind.RelativeOrAbsolute)</c>
    /// expression. Without this fast path the runtime <c>SetProperty(..., "Source", ...)</c>
    /// roundtrip pays a markup-extension parse + reflective <c>PropertyInfo.SetValue</c> +
    /// runtime type conversion; on Generic.jalxaml's 27 nested ResourceDictionary entries
    /// that's 27 × ~3-5ms during cold-start theme load. Strongly-typed setter shaves the
    /// per-call cost down to a single property assignment.
    /// </summary>
    private static string? TryConvertUri(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // Validate at compile time so we don't hand the C# compiler an Uri that the runtime
        // would reject. RelativeOrAbsolute matches the runtime UriValueConverter contract.
        if (!Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out _))
            return null;

        // EscapeStringLiteral is on JalxamlCodeGenerator — inline a minimal escape here so
        // LiteralValueConverter stays self-contained.
        var sb = new System.Text.StringBuilder(trimmed.Length + 16);
        sb.Append("new global::System.Uri(\"");
        foreach (var ch in trimmed)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append("\", global::System.UriKind.RelativeOrAbsolute)");
        return sb.ToString();
    }

    private static string? TryConvertBrush(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // `#XXX` / `#XXXX` / `#XXXXXX` / `#XXXXXXXX` → SolidColorBrush(Color.FromArgb(...)).
        if (trimmed[0] == '#')
        {
            var color = TryParseHexColor(trimmed);
            if (color == null)
                return null;
            return $"new global::Jalium.UI.Media.SolidColorBrush({color})";
        }

        // Named brush — match against the curated list. Using Brushes.X directly keeps the
        // single shared instance behaviour the runtime had (avoids one allocation per
        // SetProperty call) and is AOT-safe.
        if (NamedColorIndex.Contains(trimmed))
        {
            return $"global::Jalium.UI.Media.Brushes.{NormalizeNamedColor(trimmed)}";
        }

        return null;
    }

    private static string? TryConvertColor(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (trimmed[0] == '#')
        {
            return TryParseHexColor(trimmed);
        }

        if (NamedColorIndex.Contains(trimmed))
        {
            return $"global::Jalium.UI.Media.Colors.{NormalizeNamedColor(trimmed)}";
        }

        return null;
    }

    private static string? TryParseHexColor(string trimmed)
    {
        var hex = trimmed.Substring(1);
        byte a, r, g, b;
        switch (hex.Length)
        {
            case 3: // RGB short
                if (!TryHex4(hex[0], out var rr) || !TryHex4(hex[1], out var gg) || !TryHex4(hex[2], out var bb))
                    return null;
                a = 0xFF;
                r = (byte)((rr << 4) | rr);
                g = (byte)((gg << 4) | gg);
                b = (byte)((bb << 4) | bb);
                break;
            case 4: // ARGB short
                if (!TryHex4(hex[0], out var aa4) || !TryHex4(hex[1], out var rr4) || !TryHex4(hex[2], out var gg4) || !TryHex4(hex[3], out var bb4))
                    return null;
                a = (byte)((aa4 << 4) | aa4);
                r = (byte)((rr4 << 4) | rr4);
                g = (byte)((gg4 << 4) | gg4);
                b = (byte)((bb4 << 4) | bb4);
                break;
            case 6: // RRGGBB
                if (!TryHex8(hex.Substring(0, 2), out r) || !TryHex8(hex.Substring(2, 2), out g) || !TryHex8(hex.Substring(4, 2), out b))
                    return null;
                a = 0xFF;
                break;
            case 8: // AARRGGBB
                if (!TryHex8(hex.Substring(0, 2), out a) || !TryHex8(hex.Substring(2, 2), out r) || !TryHex8(hex.Substring(4, 2), out g) || !TryHex8(hex.Substring(6, 2), out b))
                    return null;
                break;
            default:
                return null;
        }
        return $"global::Jalium.UI.Media.Color.FromArgb((byte){a:D}, (byte){r:D}, (byte){g:D}, (byte){b:D})";
    }

    private static bool TryHex4(char c, out byte value)
    {
        if (c >= '0' && c <= '9') { value = (byte)(c - '0'); return true; }
        if (c >= 'a' && c <= 'f') { value = (byte)(c - 'a' + 10); return true; }
        if (c >= 'A' && c <= 'F') { value = (byte)(c - 'A' + 10); return true; }
        value = 0;
        return false;
    }

    private static bool TryHex8(string twoChars, out byte value)
    {
        return byte.TryParse(twoChars, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string? TryConvertNamedStaticMember(string value, string staticHostFullName)
    {
        // Token like `Bold` / `Italic` — emit `staticHost.Token` so the C# compiler resolves
        // it to the static property/field. We don't validate against a specific symbol here
        // because the fallback path will bind it; if the name is bogus the C# compile will
        // fail with a clear error rather than the runtime swallowing it silently.
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed) || !IsValidIdentifier(trimmed))
            return null;
        return $"{staticHostFullName}.{trimmed}";
    }

    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        for (var i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return false;
        }
        return true;
    }

    private static string NormalizeNamedColor(string name)
    {
        // Sanity-check the name preserves casing; Brushes / Colors property names use
        // PascalCase so we need to map "transparent" → "Transparent". We accept any
        // casing matching the curated list and emit the canonical form.
        return NamedColorIndex.GetCanonical(name) ?? name;
    }

    private static ITypeSymbol? TryUnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }
        return null;
    }

    private static string? TryConvertBool(string value)
    {
        if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)) return "true";
        if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase)) return "false";
        return null;
    }

    private static string? TryConvertInt32(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    private static string? TryConvertInt64(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v.ToString(CultureInfo.InvariantCulture) + "L";
        return null;
    }

    private static string? TryConvertUInt32(string value)
    {
        if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v.ToString(CultureInfo.InvariantCulture) + "u";
        return null;
    }

    private static string? TryConvertSmallIntegral(string value, SpecialType target)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return null;

        // Cast the int literal so the C# compiler treats it as the right small integral.
        var literal = v.ToString(CultureInfo.InvariantCulture);
        return target switch
        {
            SpecialType.System_Int16 => $"(short){literal}",
            SpecialType.System_UInt16 => $"(ushort){literal}",
            SpecialType.System_Byte => $"(byte){literal}",
            SpecialType.System_SByte => $"(sbyte){literal}",
            _ => null,
        };
    }

    private static string? TryConvertSingle(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsInfinity(v) && !float.IsNaN(v))
            return v.ToString("R", CultureInfo.InvariantCulture) + "f";
        return null;
    }

    private static string? TryConvertDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsInfinity(v) && !double.IsNaN(v))
            return v.ToString("R", CultureInfo.InvariantCulture) + "d";

        // XAML accepts "Auto" / "*" for some numeric types via TypeConverterRegistry, but
        // those should not reach a System.Double-typed property — they're for GridLength.
        return null;
    }

    private static string? TryConvertChar(string value)
    {
        if (value.Length == 1)
        {
            return $"'{EscapeChar(value[0])}'";
        }
        return null;
    }

    private static string? TryConvertEnum(string value, INamedTypeSymbol enumType)
    {
        // Comma-separated flags — XAML allows "Bold,Italic" on FontWeight enums in WPF
        // territory; we accept the same syntax. Leading/trailing whitespace tolerated.
        var enumName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var pieces = value.Split(',');
        var resolved = new string[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i].Trim();
            if (string.IsNullOrEmpty(piece))
                return null;

            var memberName = ResolveEnumMember(enumType, piece);
            if (memberName == null)
                return null;
            resolved[i] = $"{enumName}.{memberName}";
        }

        return resolved.Length == 1 ? resolved[0] : string.Join(" | ", resolved);
    }

    private static string? ResolveEnumMember(INamedTypeSymbol enumType, string memberName)
    {
        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol field &&
                field.IsConst &&
                string.Equals(field.Name, memberName, StringComparison.Ordinal))
            {
                return field.Name;
            }
        }
        return null;
    }

    private static string? TryConvertThickness(string value)
    {
        // "1" → uniform; "1,2" → horizontal/vertical; "1,2,3,4" → left,top,right,bottom.
        var parts = value.Split(',');
        if (parts.Length != 1 && parts.Length != 2 && parts.Length != 4)
            return null;

        var doubles = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out doubles[i]))
                return null;
        }

        return parts.Length switch
        {
            1 => $"new global::Jalium.UI.Thickness({Format(doubles[0])})",
            2 => $"new global::Jalium.UI.Thickness({Format(doubles[0])}, {Format(doubles[1])}, {Format(doubles[0])}, {Format(doubles[1])})",
            4 => $"new global::Jalium.UI.Thickness({Format(doubles[0])}, {Format(doubles[1])}, {Format(doubles[2])}, {Format(doubles[3])})",
            _ => null,
        };
    }

    private static string? TryConvertCornerRadius(string value)
    {
        var parts = value.Split(',');
        if (parts.Length != 1 && parts.Length != 4)
            return null;

        var doubles = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out doubles[i]))
                return null;
        }

        return parts.Length switch
        {
            1 => $"new global::Jalium.UI.CornerRadius({Format(doubles[0])})",
            4 => $"new global::Jalium.UI.CornerRadius({Format(doubles[0])}, {Format(doubles[1])}, {Format(doubles[2])}, {Format(doubles[3])})",
            _ => null,
        };
    }

    private static string? TryConvertGridLength(string value)
    {
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
            return "global::Jalium.UI.GridLength.Auto";
        if (string.Equals(trimmed, "*", StringComparison.Ordinal))
            return "new global::Jalium.UI.GridLength(1, global::Jalium.UI.GridUnitType.Star)";

        if (trimmed.EndsWith("*", StringComparison.Ordinal))
        {
            var head = trimmed.Substring(0, trimmed.Length - 1);
            if (string.IsNullOrEmpty(head))
                return "new global::Jalium.UI.GridLength(1, global::Jalium.UI.GridUnitType.Star)";
            if (double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
                return $"new global::Jalium.UI.GridLength({Format(weight)}, global::Jalium.UI.GridUnitType.Star)";
            return null;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return $"new global::Jalium.UI.GridLength({Format(px)}, global::Jalium.UI.GridUnitType.Pixel)";

        return null;
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }

    private static string EscapeChar(char ch)
    {
        return ch switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            '\0' => "\\0",
            _ => ch.ToString(),
        };
    }

    /// <summary>
    /// Same string-escaping logic the codegen uses for runtime SetProperty values, exposed
    /// here so System.String / System.Object can share a single implementation.
    /// </summary>
    public static string EscapeStringLiteral(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
