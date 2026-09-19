using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>A parsed container condition; selection precedes evaluation of all its features.</summary>
internal sealed class CssContainerQuery
{
    private enum Result { False, True, Unknown }
    private sealed record Condition(string? Name, Expression? Query);
    private readonly Condition[] _conditions;
    private CssContainerQuery(Condition[] conditions) => _conditions = conditions;

    internal static CssContainerQuery? Parse(string text)
    {
        var reader = new CssTokenReader(CssParser.StripComments(text));
        var conditions = new List<Condition>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var part) || part.IsEmpty) return null;
            var conditionReader = new CssTokenReader(part);
            var probe = conditionReader;
            string? name = null;
            if (probe.TryReadIdent(out var ident) && !ident.Equals("not", StringComparison.OrdinalIgnoreCase))
            {
                name = ident.ToString();
                if (!CssContainerProperties.IsName(name)) return null;
                conditionReader = probe;
            }
            Expression? expression = null;
            if (!conditionReader.AtEnd)
            {
                expression = ParseExpression(ref conditionReader, false, 0);
                if (expression is null || !conditionReader.AtEnd) return null;
            }
            else if (name is null) return null;
            conditions.Add(new(name, expression));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma() || reader.AtEnd) return null;
        } while (true);
        return new(conditions.ToArray());
    }

    internal bool Evaluate(CssNode element)
    {
        foreach (var condition in _conditions)
        {
            // An unknown feature makes this condition ineligible, even under `not` or `or`.
            if (condition.Query is { Supported: false }) continue;
            var features = condition.Query?.Features ?? CssContainerDependency.None;
            var selected = CssContainerQueries.Find(element, features, condition.Name);
            if (selected is null) continue;
            var container = CssContainerQueries.Container(selected);
            CssContainerQueries.Dependent(element).Observe(container, features);
            if (condition.Query is null || condition.Query.Evaluate(new(selected, element, container)) == Result.True) return true;
        }
        return false;
    }

    private sealed class Evaluation(CssNode container, CssNode dependent, CssQueryContainer metrics)
    {
        internal readonly CssNode Container = container;
        internal readonly CssQueryContainer Metrics = metrics;
        private CssLengthContext? _lengths;
        internal CssLengthContext Lengths => _lengths ??= CssEngine.BuildLengthContext(Container, dependent);
    }

    private abstract record Expression
    {
        internal abstract CssContainerDependency Features { get; }
        internal virtual bool Supported => true;
        internal abstract Result Evaluate(Evaluation context);
    }

    private sealed record Unknown : Expression
    {
        internal override CssContainerDependency Features => CssContainerDependency.None;
        internal override bool Supported => false;
        internal override Result Evaluate(Evaluation context) => Result.Unknown;
    }

    private sealed record Boolean(string Operator, Expression Left, Expression? Right = null) : Expression
    {
        internal override CssContainerDependency Features => Left.Features | (Right?.Features ?? CssContainerDependency.None);
        internal override bool Supported => Left.Supported && (Right?.Supported ?? true);
        internal override Result Evaluate(Evaluation context)
        {
            var left = Left.Evaluate(context);
            if (Operator == "not") return left == Result.Unknown ? left : left == Result.True ? Result.False : Result.True;
            var right = Right!.Evaluate(context);
            if (Operator == "and") return left == Result.False || right == Result.False ? Result.False
                : left == Result.Unknown || right == Result.Unknown ? Result.Unknown : Result.True;
            return left == Result.True || right == Result.True ? Result.True
                : left == Result.Unknown || right == Result.Unknown ? Result.Unknown : Result.False;
        }
    }

    private sealed record SizeFeature(string Name, string? Operator = null, string? Value = null,
        string? SecondOperator = null, string? SecondValue = null) : Expression
    {
        internal override CssContainerDependency Features => Name switch
        {
            "width" or "inline-size" => CssContainerDependency.Width,
            "height" or "block-size" => CssContainerDependency.Height, _ => CssContainerDependency.Both,
        };
        internal override Result Evaluate(Evaluation context)
        {
            if (!context.Metrics.HasBox) return Result.Unknown;
            var size = context.Metrics.ContentSize;
            if (Name == "orientation")
            {
                if (Value is null) return Result.True;
                if (!Substitute(Value, context.Container, out var orientation)) return Result.Unknown;
                orientation = orientation.ToLowerInvariant();
                if (orientation is not ("portrait" or "landscape")) return Result.Unknown;
                return (orientation == "landscape" ? size.Width > size.Height : size.Height >= size.Width) ? Result.True : Result.False;
            }
            var actual = Name switch
            {
                "width" or "inline-size" => size.Width, "height" or "block-size" => size.Height,
                _ => size.Height == 0 ? size.Width == 0 ? 1 : double.PositiveInfinity : size.Width / size.Height,
            };
            if (Operator is null) return actual == 0 ? Result.False : Result.True;
            if (!ReadTarget(Value!, context, out var target)) return Result.Unknown;
            var matches = Compare(actual, Operator, target);
            if (SecondOperator is not null)
            {
                if (!ReadTarget(SecondValue!, context, out target)) return Result.Unknown;
                matches &= Compare(actual, SecondOperator, target);
            }
            return matches ? Result.True : Result.False;
        }

        private bool ReadTarget(string value, Evaluation context, out double target)
        {
            target = 0;
            if (!Substitute(value, context.Container, out value)) return false;
            var reader = new CssTokenReader(value);
            if (Name == "aspect-ratio")
            {
                if (!reader.TryReadNumber(out var numerator, out var unit) || unit != CssUnit.None || numerator < 0) return false;
                var denominator = 1d;
                if (!reader.AtEnd && (!reader.TryReadSlash() || !reader.TryReadNumber(out denominator, out unit) || unit != CssUnit.None || denominator < 0)) return false;
                target = denominator == 0 ? numerator == 0 ? 1 : double.PositiveInfinity : numerator / denominator;
                return reader.AtEnd;
            }
            return reader.TryReadLength(out var length) && reader.AtEnd && length.IsLengthUnit && !length.UsesPercent &&
                (length.Unit != CssUnit.None || length.Value == 0) && length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out target);
        }
    }

    private sealed record StyleFeature(string Name, string? Value) : Expression
    {
        internal override CssContainerDependency Features => CssContainerDependency.Style;
        internal override Result Evaluate(Evaluation context)
        {
            var properties = context.Container.CssRuntimeState?.CustomProperties;
            var actual = properties is not null && properties.TryGetValue(Name, out var text) ? text : null;
            var registrations = context.Container.CssRuntimeState?.RegisteredProperties;
            var registration = registrations is not null && registrations.TryGetValue(Name, out var registered) ? registered : null;
            var valueContext = new CssPropertyValueContext(context.Lengths, baseUri: registration?.BaseUri)
            {
                CurrentColor = () => CssDependencyPropertyLookup.Find(context.Container.GetType(), "Foreground") is { } property &&
                    context.Container.GetValue(property) is SolidColorBrush brush ? brush.Color : Colors.Black,
            };
            var initial = registration?.InitialValue is { } initialText && registration.Syntax.TryCompute(initialText, valueContext, out var initialValue)
                ? initialValue!.Text : null;
            if (Value is null) return Equal(actual, initial) ? Result.False : Result.True;
            if (!Substitute(Value, context.Container, out var expected)) return Result.False;
            switch (CssPropertyMetadata.WideKeyword(expected) ?? expected.ToLowerInvariant())
            {
                case "revert": case "revert-layer": return Result.False;
                case "initial": return Equal(actual, initial) ? Result.True : Result.False;
                case "inherit": case "unset":
                    if (CssPropertyMetadata.WideKeyword(expected) == "unset" && registration?.Inherits == false)
                        return Equal(actual, initial) ? Result.True : Result.False;
                    var parentProperties = CssMatcher.CssAncestor(context.Container)?.CssRuntimeState?.CustomProperties;
                    expected = parentProperties is not null && parentProperties.TryGetValue(Name, out var parentValue) ? parentValue : initial;
                    return Equal(actual, expected) ? Result.True : Result.False;
            }
            if (registration is not null)
            {
                if (!registration.Syntax.TryCompute(expected, valueContext, out var typed)) return Result.False;
                expected = typed!.Text;
            }
            return Equal(actual, expected) ? Result.True : Result.False;
        }

        private static bool Equal(string? a, string? b) => a is null || b is null ? a == b : Tokens(a).SequenceEqual(Tokens(b), StringComparer.Ordinal);
    }

    private static Expression? ParseExpression(ref CssTokenReader reader, bool style, int depth)
    {
        if (depth > 64) return null;
        var probe = reader;
        if (probe.TryReadIdent(out var not) && not.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            var operand = ParseAtom(ref reader, style, depth + 1);
            return operand is not null && reader.AtEnd ? new Boolean("not", operand) : null;
        }
        var left = ParseAtom(ref reader, style, depth + 1);
        if (left is null) return null;
        string? operation = null;
        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var ident)) return null;
            var current = ident.ToString().ToLowerInvariant();
            if (current is not ("and" or "or") || operation is not null && operation != current) return null;
            operation = current;
            var right = ParseAtom(ref reader, style, depth + 1);
            if (right is null) return null;
            left = new Boolean(operation, left, right);
        }
        return left;
    }

    private static Expression? ParseAtom(ref CssTokenReader reader, bool style, int depth)
    {
        if (depth > 64) return null;
        if (reader.TryReadFunction(out var function, out var arguments))
        {
            if (style || !function.Equals("style", StringComparison.OrdinalIgnoreCase)) return new Unknown();
            return ParseBody(arguments.Remaining.ToString(), true, depth + 1);
        }
        var rest = reader.Remaining;
        if (rest.IsEmpty || rest[0] != '(') return null;
        var nesting = 1;
        var pos = 1;
        for (; pos < rest.Length; pos++)
        {
            if (rest[pos] is '\'' or '"') { pos = CssTokenReader.SkipString(rest, pos) - 1; continue; }
            if (rest[pos] == '\\')
            { if (!CssSyntax.ReadEscape(rest, ref pos, out _)) return null; pos--; continue; }
            if (rest[pos] == '(') nesting++;
            else if (rest[pos] == ')' && --nesting == 0) break;
        }
        if (nesting != 0) return null;
        var body = rest[1..pos].ToString();
        reader = new CssTokenReader(rest[(pos + 1)..]);
        return ParseBody(body, style, depth + 1);
    }

    private static Expression? ParseBody(string body, bool style, int depth)
    {
        var reader = new CssTokenReader(body);
        var probe = reader;
        if (reader.TryPeekChar(out var first) && first == '(' ||
            probe.TryReadIdent(out var ident) && ident.Equals("not", StringComparison.OrdinalIgnoreCase) ||
            probe.TryReadFunction(out _, out _))
            return ParseExpression(ref reader, style, depth + 1);
        return ParseFeature(body, style);
    }

    private static Expression? ParseFeature(string body, bool style)
    {
        var parts = new List<string>();
        var operations = new List<string>();
        var start = 0; var depth = 0;
        var firstOperator = -1;
        var validDeclaration = true;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c is '\'' or '"') { i = CssTokenReader.SkipString(body, i) - 1; continue; }
            if (c == '\\')
            { if (!CssSyntax.ReadEscape(body, ref i, out _)) return null; i--; continue; }
            if (c is '(' or '[' or '{') { depth++; continue; }
            if (c is ')' or ']' or '}') { if (--depth < 0) return null; continue; }
            if (depth == 0 && c is '!' or ';') validDeclaration = false;
            if (depth != 0 || c is not (':' or '<' or '>' or '=')) continue;
            if (firstOperator < 0) firstOperator = i;
            parts.Add(body[start..i].Trim());
            var operation = c.ToString();
            if (c is '<' or '>' && i + 1 < body.Length && body[i + 1] == '=') { operation += '='; i++; }
            operations.Add(operation); start = i + 1;
        }
        if (depth != 0) return null;
        parts.Add(body[start..].Trim());
        if (parts[0].Length == 0) return null;
        static string? Identifier(string value)
        {
            var reader = new CssTokenReader(value);
            return reader.TryReadIdent(out var name) && reader.AtEnd ? name.ToString() : null;
        }
        if (style)
        {
            var name = Identifier(parts[0]);
            if (name is null || !name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2) return new Unknown();
            if (operations.Count == 0) return new StyleFeature(name, null);
            return validDeclaration && operations.All(operation => operation == ":")
                ? new StyleFeature(name, body[(firstOperator + 1)..].Trim()) : new Unknown();
        }
        var feature = Identifier(parts[0])?.ToLowerInvariant();
        var reversed = false;
        if (feature is null && parts.Count > 1) { feature = Identifier(parts[1])?.ToLowerInvariant(); reversed = true; }
        if (feature is null) return new Unknown();
        if (operations.Count == 1 && operations[0] == ":")
        {
            if (reversed) return null;
            var operation = "=";
            if (feature.StartsWith("min-", StringComparison.Ordinal)) { feature = feature[4..]; operation = ">="; }
            else if (feature.StartsWith("max-", StringComparison.Ordinal)) { feature = feature[4..]; operation = "<="; }
            return IsSizeFeature(feature) && (feature != "orientation" || operation == "=")
                ? new SizeFeature(feature, operation, parts[1]) : new Unknown();
        }
        if (!IsSizeFeature(feature)) return new Unknown();
        if (operations.Count == 0) return new SizeFeature(feature);
        if (feature == "orientation" || operations.Contains(":")) return new Unknown();
        if (operations.Count == 1 && parts[1].Length > 0)
            return new SizeFeature(feature, reversed ? Reverse(operations[0]) : operations[0], parts[reversed ? 0 : 1]);
        if (operations.Count == 2 && reversed && operations[0][0] == operations[1][0] && operations[0] != "=" && parts[2].Length > 0)
            return new SizeFeature(feature, Reverse(operations[0]), parts[0], operations[1], parts[2]);
        return null;
    }

    private static bool IsSizeFeature(string name) => name is "width" or "height" or "inline-size" or "block-size" or "aspect-ratio" or "orientation";
    private static string Reverse(string operation) => operation switch { ">" => "<", ">=" => "<=", "<" => ">", "<=" => ">=", _ => operation };
    private static bool Compare(double actual, string operation, double expected) => operation switch
    {
        ">" => actual > expected, ">=" => actual >= expected, "<" => actual < expected, "<=" => actual <= expected,
        "=" => actual == expected || Math.Abs(actual - expected) < 1e-9, _ => false,
    };
    private static bool Substitute(string value, CssNode node, out string result)
    {
        if (!CssCustomProperties.TrySubstitute(value, node, out result)) return false;
        result = CssParser.StripComments(result).Trim().ToString();
        return true;
    }

    private static List<string> Tokens(string value)
    {
        var reader = new CssTokenReader(CssParser.StripComments(value));
        var result = new List<string>();
        while (!reader.AtEnd)
        {
            var text = reader.Remaining;
            if (reader.TryReadString(out var quoted)) { result.Add("string:" + quoted); continue; }
            if ((char.IsAsciiDigit(text[0]) || text[0] is '+' or '-' or '.') && reader.TryReadNumber(out var number, out var unit))
            { result.Add("number:" + number.ToString("R", CultureInfo.InvariantCulture) + ":" + unit); continue; }
            var position = 0;
            if (CssSyntax.ReadIdentifier(text, ref position, out var ident))
            {
                var function = position < text.Length && text[position] == '(';
                result.Add((function ? "function:" : "ident:") + ident.ToString());
                reader = new CssTokenReader(text[(position + (function ? 1 : 0))..]);
                continue;
            }
            result.Add("token:" + text[0]); reader = new CssTokenReader(text[1..]);
        }
        return result;
    }
}
