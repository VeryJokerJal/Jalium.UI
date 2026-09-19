using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>A computed registration value, serialized only when substituted into another declaration.</summary>
internal sealed record CssTypedValue(string Type, string Text, double Number = double.NaN, CssTypedValue[]? Items = null);

internal sealed class CssPropertyValueContext(
    CssLengthContext lengths, Func<double>? fontSize = null, bool isRoot = false, bool initial = false, Uri? baseUri = null,
    Func<double>? lineHeight = null, Func<CssFontInfo>? font = null)
{
    internal readonly CssLengthContext Lengths = lengths;
    internal readonly bool Initial = initial;
    internal readonly Uri? BaseUri = baseUri;
    internal Func<Color>? CurrentColor;

    internal bool Resolve(CssLength length, out double value)
    {
        value = 0;
        if (Initial && (length.IsFontRelative || length.Unit is >= CssUnit.Cqw and <= CssUnit.Cqmax)) return false;
        var context = Lengths;
        if (length.Unit is CssUnit.Em or CssUnit.Ex or CssUnit.Cap or CssUnit.Ch or CssUnit.Ic or CssUnit.Lh && fontSize is not null)
            context = context.WithElementFontSize(fontSize());
        if (length.Unit is CssUnit.Rem or CssUnit.Rex or CssUnit.Rcap or CssUnit.Rch or CssUnit.Ric or CssUnit.Rlh && isRoot && fontSize is not null)
            context = context.WithRootFontSize(fontSize());
        if ((length.Unit is CssUnit.Ex or CssUnit.Cap or CssUnit.Ch or CssUnit.Ic ||
             isRoot && length.Unit is CssUnit.Rex or CssUnit.Rcap or CssUnit.Rch or CssUnit.Ric) && font is not null)
        {
            var fonts = context.Fonts ?? CssFontContext.Initial;
            var computed = font();
            fonts = fonts with { Element = computed, Root = isRoot ? computed : fonts.Root };
            context = context.WithFonts(fonts);
        }
        if ((length.Unit == CssUnit.Lh || isRoot && length.Unit == CssUnit.Rlh) && lineHeight is not null)
        {
            var fonts = context.Fonts ?? CssFontContext.Initial;
            var computed = new CssLineHeight(CssLineHeightKind.Pixels, lineHeight());
            context = context.WithFonts(fonts with { ElementLine = computed, RootLine = isRoot ? computed : fonts.RootLine });
        }
        return length.TryResolve(context, CssPercentBasis.NotSupported, out value) && double.IsFinite(value);
    }
}

/// <summary>The intentionally restricted syntax-string grammar defined by Properties and Values API 1.</summary>
internal sealed class CssPropertySyntax
{
    internal sealed record Component(string Name, bool Type, char Multiplier);
    private readonly Component[] _components;
    internal IReadOnlyList<Component> Components => _components;
    internal bool Universal { get; }
    internal CssPropertySyntax(Component[] components, bool universal = false) { _components = components; Universal = universal; }

    internal static CssPropertySyntax? Parse(string syntax)
    {
        var text = syntax.AsSpan();
        while (!text.IsEmpty && CssTokenReader.IsCssWhitespace(text[0])) text = text[1..];
        while (!text.IsEmpty && CssTokenReader.IsCssWhitespace(text[^1])) text = text[..^1];
        if (text.SequenceEqual("*")) return new([], true);
        var components = new List<Component>();
        var position = 0;
        while (position < text.Length)
        {
            var type = text[position] == '<';
            string name;
            if (type)
            {
                var start = ++position;
                while (position < text.Length && CssSyntax.IsNameCharacter(text[position])) position++;
                name = text[start..position].ToString();
            }
            else
            {
                if (!CssSyntax.ReadIdentifier(text, ref position, out var identifier)) return null;
                name = identifier.ToString();
            }
            if (type)
            {
                if (position >= text.Length || text[position++] != '>' || name is not ("length" or "number" or "percentage" or "length-percentage" or "color"
                    or "image" or "url" or "integer" or "angle" or "time" or "resolution" or "transform-function" or "transform-list" or "custom-ident")) return null;
            }
            else if (CssPropertyMetadata.IsWideKeyword(name) || name.Equals("default", StringComparison.OrdinalIgnoreCase)) return null;
            var multiplier = '\0';
            if (position < text.Length && text[position] is '+' or '#') multiplier = text[position++];
            if (name == "transform-list" && multiplier != '\0') return null;
            components.Add(new(name, type, multiplier));
            while (position < text.Length && CssTokenReader.IsCssWhitespace(text[position])) position++;
            if (position == text.Length) break;
            if (text[position++] != '|') return null;
            while (position < text.Length && CssTokenReader.IsCssWhitespace(text[position])) position++;
            if (position == text.Length) return null;
        }
        return components.Count == 0 ? null : new(components.ToArray());
    }

    internal bool TryCompute(string text, CssPropertyValueContext context, out CssTypedValue? value)
    {
        value = null;
        text = CssParser.StripComments(text).Trim().ToString();
        if (!CssDeclarationValue.IsValid(text)) return false;
        if (Universal) { value = new("*", text); return true; }
        if (CssPropertyMetadata.IsWideKeyword(text)) return false;
        foreach (var component in _components)
        {
            var reader = new CssTokenReader(text, new CssNumericReadContext(registered: context));
            if (!ReadComponent(component, ref reader, context, out value) || !reader.AtEnd) continue;
            return true;
        }
        value = null;
        return false;
    }

    private static bool ReadComponent(Component component, ref CssTokenReader reader, CssPropertyValueContext context, out CssTypedValue? value)
    {
        value = null;
        if (component.Multiplier == '\0') return ReadSingle(component, ref reader, context, out value);
        var values = new List<CssTypedValue>();
        do
        {
            if (!ReadSingle(component, ref reader, context, out var item)) return false;
            values.Add(item!);
            if (reader.AtEnd) break;
            if (component.Multiplier == '#' && (!reader.TryReadComma() || reader.AtEnd)) return false;
        } while (values.Count < 4096);
        if (!reader.AtEnd || values.Count == 0) return false;
        value = new(component.Name + component.Multiplier, string.Join(component.Multiplier == '#' ? ", " : " ", values.Select(v => v.Text)), Items: values.ToArray());
        return true;
    }

    private static bool ReadSingle(Component component, ref CssTokenReader reader, CssPropertyValueContext context, out CssTypedValue? value)
    {
        value = null;
        var before = reader.Remaining;
        if (!component.Type || component.Name == "custom-ident")
        {
            if (!reader.TryReadIdent(out var ident) || CssPropertyMetadata.IsWideKeyword(ident.ToString()) || ident.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                !component.Type && !ident.SequenceEqual(component.Name)) return false;
            value = new("ident", CssDeclarationValue.Identifier(ident.ToString()));
            return true;
        }
        if (component.Name is "length" or "length-percentage")
        {
            if (!reader.TryReadLength(out var length) || length.Unit == CssUnit.None && length.Value != 0 ||
                component.Name == "length" && length.UsesPercent) return false;
            if (!ComputeLength(length, context, out var text, out var number, out var percent)) return false;
            value = new(percent ? "length-percentage" : "length", text, number);
            return true;
        }
        if (component.Name is "number" or "integer" or "percentage" or "angle" or "time" or "resolution")
        {
            var probe = reader;
            var isMath = probe.TryReadFunction(out _, out _);
            if (!reader.TryReadNumber(out var number, out var unit) || !double.IsFinite(number)) return false;
            var suffix = string.Empty;
            switch (component.Name)
            {
                case "number": if (unit != CssUnit.None) return false; break;
                case "integer":
                    if (unit != CssUnit.None) return false;
                    if (isMath) number = Math.Floor(number + .5);
                    else
                    {
                        var consumed = before[..(before.Length - reader.Remaining.Length)];
                        if (number != Math.Truncate(number) || consumed.Contains('.') || consumed.Contains('e') || consumed.Contains('E')) return false;
                    }
                    break;
                case "percentage": if (unit != CssUnit.Percent) return false; suffix = "%"; break;
                case "angle":
                    if (unit is not (CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn) || !CssUnitConversion.TryToDegrees(number, unit, out number)) return false;
                    suffix = "deg"; break;
                case "time":
                    if (unit is not (CssUnit.S or CssUnit.Ms) || !CssUnitConversion.TryToMilliseconds(number, unit, out number)) return false;
                    number /= 1000; suffix = "s"; break;
                case "resolution":
                    if (!CssUnitConversion.TryToDppx(number, unit, out number)) return false;
                    suffix = "dppx"; break;
            }
            value = new(component.Name, Number(number) + suffix, number);
            return true;
        }
        if (component.Name == "color")
        {
            if (!CssColorParser.TryParse(ref reader, out var color, out var current)) return false;
            if (current)
            {
                if (context.Initial) return false;
                color = context.CurrentColor?.Invoke() ?? Colors.Black;
            }
            value = new("color", ColorText(color));
            return true;
        }
        if (component.Name is "url" or "image")
        {
            if (!reader.TryReadFunction(out var function, out var arguments)) return false;
            if (function.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                string reference;
                if (arguments.TryReadString(out var quoted)) { if (!arguments.AtEnd) return false; reference = quoted; }
                else
                {
                    var urlText = arguments.Remaining.Trim();
                    var decoded = new System.Text.StringBuilder();
                    for (var position = 0; position < urlText.Length;)
                    {
                        if (urlText[position] == '\\')
                        {
                            if (!CssSyntax.ReadEscape(urlText, ref position, out var escape)) return false;
                            decoded.Append(escape); continue;
                        }
                        var c = urlText[position++];
                        if (char.IsWhiteSpace(c) || c < 0x20 || c is '(' or ')' or '\'' or '"') return false;
                        decoded.Append(c);
                    }
                    reference = decoded.ToString();
                }
                try
                {
                    if (context.BaseUri is { } baseUri) reference = CssStyleSheet.ResolveReference(baseUri, reference).ToString();
                    value = new(component.Name, "url(" + CssDeclarationValue.String(reference) + ")");
                    return true;
                }
                catch (UriFormatException) { return false; }
            }
            if (component.Name != "image" || !CssGradientParser.TryParseGradientFunction(function, ref arguments, out _)) return false;
            var raw = before[..(before.Length - reader.Remaining.Length)].ToString();
            if (!NormalizeEmbeddedLengths(raw, context, out var normalized)) return false;
            value = new("image", normalized);
            return true;
        }
        if (component.Name is "transform-function" or "transform-list")
        {
            var functions = new List<CssTypedValue>();
            do
            {
                var probe = reader;
                if (!probe.TryReadFunction(out var function, out var args)) break;
                var source = function.ToString() + "(" + args.Remaining.ToString() + ")";
                if (!NormalizeEmbeddedLengths(source, context, out source)) return false;
                var validation = new CssTokenReader(source);
                if (!CssTransformParser.TryParseTransformList(ref validation, out _)) return false;
                functions.Add(new("transform-function", source)); reader = probe;
                if (component.Name == "transform-function") break;
            } while (functions.Count < 4096);
            if (functions.Count == 0) return false;
            value = component.Name == "transform-function" ? functions[0]
                : new("transform-list", string.Join(" ", functions.Select(f => f.Text)), Items: functions.ToArray());
            return true;
        }
        return false;
    }

    internal static bool ComputeLength(CssLength length, CssPropertyValueContext context, out string text, out double number, out bool percent)
    {
        text = string.Empty; number = double.NaN; percent = length.UsesPercent;
        if (length.Expression is { } expression)
        {
            var normalized = Normalize(expression, context);
            if (normalized is null) return false;
            if (normalized.Kind == CssNumericKind.Percent && normalized.TryEvaluate(context.Lengths, 100, out number))
            { text = Number(number) + "%"; return true; }
            if (!normalized.UsesPercent && normalized.TryEvaluate(context.Lengths, double.NaN, out number))
            { text = Number(number) + "px"; return true; }
            if (Linear(normalized, out var pixels, out var percentage))
            {
                text = pixels == 0 ? Number(percentage) + "%" : percentage == 0 ? Number(pixels) + "px"
                    : "calc(" + Number(percentage) + "% " + (pixels < 0 ? "- " : "+ ") + Number(Math.Abs(pixels)) + "px)";
                return true;
            }
            text = Serialize(normalized);
            return true;
        }
        if (length.Unit == CssUnit.Percent) { number = length.Value; text = Number(number) + "%"; return true; }
        if (!context.Resolve(length, out number)) return false;
        text = Number(number) + "px";
        return true;
    }

    internal static CssMathExpression? Normalize(CssMathExpression expression, CssPropertyValueContext context)
    {
        if (expression.Operation == "value")
        {
            if (expression.Kind != CssNumericKind.Length) return expression;
            if (!context.Resolve(expression.Literal, out var value)) return null;
            return expression with { Literal = new(value, CssUnit.Px) };
        }
        var arguments = new List<CssMathExpression>();
        foreach (var argument in expression.Arguments)
        {
            var normalized = Normalize(argument, context);
            if (normalized is null) return null;
            arguments.Add(normalized);
        }
        return expression with { Arguments = arguments.ToArray() };
    }

    private static bool Linear(CssMathExpression expression, out double pixels, out double percent)
    {
        pixels = percent = 0;
        if (expression.Operation == "value")
        {
            if (expression.Kind == CssNumericKind.Percent) percent = expression.Literal.Value;
            else if (expression.Kind == CssNumericKind.Length) pixels = expression.Literal.Value;
            else return false;
            return true;
        }
        if (expression.Operation is "calc" or "negate")
        {
            if (!Linear(expression.Arguments[0], out pixels, out percent)) return false;
            if (expression.Operation == "negate") { pixels = -pixels; percent = -percent; }
            return true;
        }
        if (expression.Operation is "+" or "-")
        {
            if (!Linear(expression.Arguments[0], out var a, out var b) || !Linear(expression.Arguments[1], out var c, out var d)) return false;
            var sign = expression.Operation == "+" ? 1 : -1;
            pixels = a + sign * c; percent = b + sign * d; return true;
        }
        if (expression.Operation is "*" or "/")
        {
            var scalar = expression.Arguments[1]; var length = expression.Arguments[0];
            if (expression.Operation == "*" && expression.Arguments[0].Kind == CssNumericKind.Number) (scalar, length) = (length, scalar);
            if (scalar.Kind != CssNumericKind.Number || !scalar.TryEvaluateCore(CssLengthContext.Default, double.NaN, out var factor) || !double.IsFinite(factor) ||
                !Linear(length, out pixels, out percent) || expression.Operation == "/" && factor == 0) return false;
            if (expression.Operation == "/") factor = 1 / factor;
            pixels *= factor; percent *= factor; return true;
        }
        return false;
    }

    private static string Serialize(CssMathExpression expression)
    {
        if (expression.Operation == "none") return "none";
        if (expression.Operation == "value")
        {
            var suffix = expression.Kind switch
            {
                CssNumericKind.Percent => "%", CssNumericKind.Length => "px", CssNumericKind.Angle => "deg",
                CssNumericKind.Time => "ms", CssNumericKind.Resolution => "dppx", CssNumericKind.Frequency => "Hz",
                CssNumericKind.Flex => "fr", _ => ""
            };
            var number = expression.Literal.Value;
            if (expression.Kind == CssNumericKind.Angle) CssUnitConversion.TryToDegrees(number, expression.Literal.Unit, out number);
            if (expression.Kind == CssNumericKind.Time) CssUnitConversion.TryToMilliseconds(number, expression.Literal.Unit, out number);
            if (expression.Kind == CssNumericKind.Resolution) CssUnitConversion.TryToDppx(number, expression.Literal.Unit, out number);
            if (expression.Literal.Unit == CssUnit.Khz) number *= 1000;
            if (!double.IsFinite(number)) return "calc(" + (double.IsNaN(number) ? "NaN" : number > 0 ? "infinity" : "-infinity") +
                (suffix.Length == 0 ? "" : " * 1" + suffix) + ")";
            return Number(number) + suffix;
        }
        if (expression.Operation is "+" or "-" or "*" or "/")
            return "(" + Serialize(expression.Arguments[0]) + " " + expression.Operation + " " + Serialize(expression.Arguments[1]) + ")";
        if (expression.Operation == "negate") return "(-1 * " + Serialize(expression.Arguments[0]) + ")";
        return expression.Operation + "(" + (expression.Option is null ? "" : expression.Option + ", ") +
            string.Join(", ", expression.Arguments.Select(Serialize)) + ")";
    }

    private static bool NormalizeEmbeddedLengths(string text, CssPropertyValueContext context, out string normalized, int depth = 0)
    {
        normalized = string.Empty;
        if (depth > 64) return false;
        var output = new System.Text.StringBuilder(); var reader = new CssTokenReader(text);
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (probe.TryReadLength(out var length) && length.Unit != CssUnit.None)
            {
                if (!ComputeLength(length, context, out var computed, out _, out _)) return false;
                output.Append(computed).Append(' '); reader = probe; continue;
            }
            probe = reader;
            if (probe.TryReadFunction(out var function, out var arguments))
            {
                if (!NormalizeEmbeddedLengths(arguments.Remaining.ToString(), context, out var args, depth + 1)) return false;
                output.Append(function).Append('(').Append(args).Append(") "); reader = probe; continue;
            }
            if (reader.TryReadIdent(out var ident))
            {
                if (ident.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    if (context.Initial) return false;
                    output.Append(ColorText(context.CurrentColor?.Invoke() ?? Colors.Black)).Append(' ');
                }
                else output.Append(CssDeclarationValue.Identifier(ident.ToString())).Append(' ');
                continue;
            }
            var rest = reader.Remaining;
            // Non-length numeric tokens (angles/numbers) must remain intact.
            probe = reader;
            if (probe.TryReadNumber(out _, out _))
            { output.Append(rest[..(rest.Length - probe.Remaining.Length)]).Append(' '); reader = probe; continue; }
            output.Append(rest[0]); reader = new(rest[1..]);
        }
        normalized = output.ToString().Trim(); return true;
    }

    internal static string Number(double number) => (number == 0 ? 0 : number).ToString("R", CultureInfo.InvariantCulture);
    internal static string ColorText(Color color) => color.A == 255 ? $"rgb({color.R}, {color.G}, {color.B})"
        : $"rgba({color.R}, {color.G}, {color.B}, {Number(color.A / 255d)})";
}

internal static class CssDeclarationValue
{
    internal static bool IsValid(string text)
    {
        var stack = new Stack<char>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\'' or '"')
            {
                var reader = new CssTokenReader(text.AsSpan(i));
                if (!reader.TryReadString(out _)) return false;
                i += reader.Position - 1; continue;
            }
            if (c == '\\') { if (!CssSyntax.ReadEscape(text, ref i, out _)) return false; i--; continue; }
            if (c is '(' or '[' or '{') stack.Push(c);
            else if (c is ')' or ']' or '}')
            {
                if (!stack.TryPop(out var open) || c != (open == '(' ? ')' : open == '[' ? ']' : '}')) return false;
            }
            else if (stack.Count == 0 && c is '!' or ';') return false;
        }
        return stack.Count == 0;
    }

    internal static string String(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\a ", StringComparison.Ordinal).Replace("\r", "\\d ", StringComparison.Ordinal) + "\"";

    internal static string Identifier(string value)
    {
        var output = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (CssSyntax.IsNameCharacter(c) && !(char.IsAsciiDigit(c) && (i == 0 || i == 1 && value[0] == '-'))) output.Append(c);
            else output.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
        }
        return output.ToString();
    }
}
