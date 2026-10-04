using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>A parsed container condition; selection precedes evaluation of all its features.</summary>
internal sealed class CssContainerQuery
{
    private enum Result { False, True, Unknown }
    private enum QueryMode { Size, Style, ScrollState }
    private sealed record Condition(string? Name, Expression? Query);
    private readonly Condition[] _conditions;
    private readonly CssNamespaceContext? _namespaces;
    private CssContainerQuery(Condition[] conditions, CssNamespaceContext? namespaces)
    { _conditions = conditions; _namespaces = namespaces; }

    internal static CssContainerQuery? Parse(string text, CssNamespaceContext? namespaces = null)
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
                expression = ParseExpression(ref conditionReader, QueryMode.Size, 0);
                if (expression is null || !conditionReader.AtEnd) return null;
            }
            else if (name is null) return null;
            conditions.Add(new(name, expression));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma() || reader.AtEnd) return null;
        } while (true);
        return new(conditions.ToArray(), namespaces);
    }

    internal bool Evaluate(CssNode element)
    {
        foreach (var condition in _conditions)
        {
            // An unknown feature makes this condition ineligible, even under `not` or `or`.
            if (condition.Query is { Supported: false }) continue;
            var features = condition.Query?.Features ?? CssContainerDependency.None;
            var selected = CssContainerQueries.Find(element, features, condition.Name,
                condition.Query is { DynamicFeatures: true } query ? query.FeaturesFor : null);
            if (selected is null) continue;
            var container = CssContainerQueries.Container(selected);
            CssContainerQueries.Dependent(element).Observe(container, features);
            if (condition.Query is null || condition.Query.Evaluate(new(selected, element, container, _namespaces)) == Result.True) return true;
        }
        return false;
    }

    internal static CssQueryResult EvaluateStyleOnElement(string body, CssNode? element,
        Func<string, string?> resolve, Func<string, bool>? isActive = null,
        Action<string>? onQueried = null)
    {
        if (element is null) return CssQueryResult.Unknown;
        var expression = ParseBody(body, QueryMode.Style, 0);
        if (expression is null || !expression.Supported) return CssQueryResult.Unknown;
        var context = new Evaluation(element, element, CssContainerQueries.Container(element),
            null, resolve, isActive, true, onQueried);
        return expression.Evaluate(context) switch
        {
            Result.True => CssQueryResult.True,
            Result.False => CssQueryResult.False,
            _ => CssQueryResult.Unknown,
        };
    }

    private sealed class Evaluation(CssNode container, CssNode dependent, CssQueryContainer metrics,
        CssNamespaceContext? namespaces, Func<string, string?>? resolve = null,
        Func<string, bool>? isActive = null, bool selfQuery = false,
        Action<string>? onQueried = null)
    {
        internal readonly CssNode Container = container;
        internal readonly CssQueryContainer Metrics = metrics;
        internal readonly CssNamespaceContext? Namespaces = namespaces;
        internal readonly Func<string, string?>? Resolve = resolve;
        internal readonly Func<string, bool>? IsActive = isActive;
        internal readonly Action<string>? OnQueried = onQueried;
        private CssLengthContext? _lengths;
        internal CssLengthContext Lengths => _lengths ??= CssEngine.BuildLengthContext(Container, dependent);

        internal bool Substitute(string source, out string result)
        {
            if (Resolve is null) return CssContainerQuery.Substitute(source, Container, out result);
            if (!CssCustomProperties.Substitute(source, Resolve, out result, element: Container,
                    isActive: IsActive)) return false;
            result = CssParser.StripComments(result).Trim().ToString();
            return true;
        }

        internal void ObserveStyleAncestors()
        {
            if (selfQuery) return;
            for (var parent = CssMatcher.CssAncestor(Container); parent is not null;
                 parent = CssMatcher.CssAncestor(parent))
                CssContainerQueries.Dependent(dependent).Observe(
                    CssContainerQueries.Container(parent), CssContainerDependency.Style);
        }
    }

    private abstract record Expression
    {
        internal abstract CssContainerDependency Features { get; }
        internal virtual bool DynamicFeatures => false;
        internal virtual CssContainerDependency FeaturesFor(CssNode candidate) => Features;
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
        internal override bool DynamicFeatures => Left.DynamicFeatures || (Right?.DynamicFeatures ?? false);
        internal override CssContainerDependency FeaturesFor(CssNode candidate)
            => Left.FeaturesFor(candidate) | (Right?.FeaturesFor(candidate) ?? CssContainerDependency.None);
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
        internal override bool Supported => Name.StartsWith("--", StringComparison.Ordinal) ||
            CssOrdinaryStyleQuery.IsSupported(Name);
        internal override Result Evaluate(Evaluation context)
        {
            context.OnQueried?.Invoke(Name);
            if (context.IsActive?.Invoke(Name) == true) return Result.False;
            if (!Name.StartsWith("--", StringComparison.Ordinal))
            {
                if (Name.Equals("display", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("visibility", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("color", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("background-color", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-weight", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-size", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-style", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-family", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-stretch", StringComparison.OrdinalIgnoreCase) ||
                    Name.Equals("font-width", StringComparison.OrdinalIgnoreCase))
                    context.ObserveStyleAncestors();
                if (Value is null)
                    return CssOrdinaryStyleQuery.Matches(Name, null, context.Container, context.Lengths)
                        ? Result.True : Result.False;
                if (!context.Substitute(Value, out var ordinaryExpected) ||
                    !CssAttributeSubstitution.TrySubstitute(ordinaryExpected, context.Container, context.Lengths, out ordinaryExpected,
                        namespaces: context.Namespaces))
                    return Result.False;
                return CssOrdinaryStyleQuery.Matches(Name, ordinaryExpected, context.Container, context.Lengths)
                    ? Result.True : Result.False;
            }
            var properties = context.Container.CssRuntimeState?.CustomProperties;
            var actual = context.Resolve is not null ? context.Resolve(Name) :
                properties is not null && properties.TryGetValue(Name, out var text) ? text : null;
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
            if (!context.Substitute(Value, out var expected) ||
                !CssAttributeSubstitution.TrySubstitute(expected, context.Container, context.Lengths, out expected,
                    namespaces: context.Namespaces))
                return Result.False;
            switch (CssPropertyMetadata.WideKeyword(expected) ?? expected.ToLowerInvariant())
            {
                case "revert": case "revert-layer": case "revert-rule": return Result.False;
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

    private sealed record StyleRange(string[] Values, string[] Operators) : Expression
    {
        internal override CssContainerDependency Features => CssContainerDependency.Style;

        internal override Result Evaluate(Evaluation context)
        {
            var values = new (CssNumericKind Kind, double Number)[Values.Length];
            for (var i = 0; i < Values.Length; i++)
                if (!ReadValue(Values[i], context, out values[i])) return Result.False;

            for (var i = 0; i < Operators.Length; i++)
            {
                var left = values[i];
                var right = values[i + 1];
                if (left.Kind != right.Kind)
                {
                    // Only a unitless zero may compare as a length; other dimensions stay distinct.
                    if (left.Kind == CssNumericKind.Number && left.Number == 0 && right.Kind == CssNumericKind.Length)
                        left.Kind = CssNumericKind.Length;
                    else if (right.Kind == CssNumericKind.Number && right.Number == 0 && left.Kind == CssNumericKind.Length)
                        right.Kind = CssNumericKind.Length;
                    else return Result.False;
                }
                if (Operators[i] == "=" ? left.Number != right.Number : !Compare(left.Number, Operators[i], right.Number))
                    return Result.False;
            }
            return Result.True;
        }

        private static bool ReadValue(string source, Evaluation context, out (CssNumericKind Kind, double Number) value)
        {
            value = default;
            var name = Identifier(source);
            if (name is { Length: > 2 } && name.StartsWith("--", StringComparison.Ordinal))
            {
                context.OnQueried?.Invoke(name);
                source = "var(" + CssDeclarationValue.Identifier(name) + ")";
            }
            if (!context.Substitute(source, out var text)) return false;
            if (!CssAttributeSubstitution.TrySubstitute(text, context.Container, context.Lengths, out text,
                    namespaces: context.Namespaces)) return false;

            var valueContext = new CssPropertyValueContext(context.Lengths);
            var reader = new CssTokenReader(text, new CssNumericReadContext(registered: valueContext));
            if (reader.TryReadNumber(out var number, out var unit) && reader.AtEnd && double.IsFinite(number))
            {
                switch (unit)
                {
                    case CssUnit.None: value = (CssNumericKind.Number, number); return true;
                    case CssUnit.Percent: value = (CssNumericKind.Percent, number); return true;
                    case CssUnit.Deg: case CssUnit.Rad: case CssUnit.Grad: case CssUnit.Turn:
                        if (CssUnitConversion.TryToDegrees(number, unit, out var degrees))
                        { value = (CssNumericKind.Angle, degrees); return true; }
                        return false;
                    case CssUnit.S: case CssUnit.Ms:
                        if (CssUnitConversion.TryToMilliseconds(number, unit, out var milliseconds))
                        { value = (CssNumericKind.Time, milliseconds); return true; }
                        return false;
                    case CssUnit.Dpi: case CssUnit.Dpcm: case CssUnit.Dppx:
                        if (CssUnitConversion.TryToDppx(number, unit, out var dppx))
                        { value = (CssNumericKind.Resolution, dppx); return true; }
                        return false;
                    case CssUnit.Hz: case CssUnit.Khz:
                        value = (CssNumericKind.Frequency, number * (unit == CssUnit.Khz ? 1000 : 1)); return true;
                }
                var length = new CssLength(number, unit);
                if (length.IsLengthUnit && valueContext.Resolve(length, out var pixels))
                { value = (CssNumericKind.Length, pixels); return true; }
                return false;
            }

            reader = new CssTokenReader(text, new CssNumericReadContext(registered: valueContext));
            if (!reader.TryReadLength(out var expression) || !reader.AtEnd || expression.UsesPercent ||
                expression.Unit == CssUnit.None && expression.Value != 0 ||
                !valueContext.Resolve(expression, out var resolved)) return false;
            value = (CssNumericKind.Length, resolved);
            return true;
        }
    }

    private sealed record ScrollStateFeature(string Name, string? Value, bool Variable = false) : Expression
    {
        internal override bool DynamicFeatures => Variable;
        internal override CssContainerDependency Features => (Variable ? CssContainerDependency.Style : CssContainerDependency.None) |
            (Variable ? CssContainerDependency.ScrollAny : Axis(Value));

        internal override CssContainerDependency FeaturesFor(CssNode candidate)
            => !Variable ? Features : CssContainerDependency.Style |
                (TryValue(candidate, out var value) ? Axis(value) : CssContainerDependency.ScrollAny);

        private static CssContainerDependency Axis(string? value) => value switch
        {
            "left" or "right" or "inline-start" or "inline-end" or "x" or "inline" => CssContainerDependency.ScrollX,
            "top" or "bottom" or "block-start" or "block-end" or "y" or "block" => CssContainerDependency.ScrollY,
            _ => CssContainerDependency.ScrollAny,
        };

        private bool TryValue(CssNode candidate, out string? value)
        {
            value = Value;
            if (!Variable) return true;
            if (!Substitute(Value!, candidate, out var substituted)) return false;
            value = Identifier(substituted)?.ToLowerInvariant();
            return IsScrollDirection(value);
        }

        internal override Result Evaluate(Evaluation context)
        {
            if (!context.Metrics.HasBox || context.Container.Target is not Jalium.UI.Controls.ScrollViewer viewer)
                return Result.Unknown;
            if (!TryValue(context.Container, out var value)) return Result.Unknown;
            var directions = Name == "scrolled"
                ? context.Metrics.ScrolledDirection : context.Metrics.ScrollableDirections;
            if (value is null) return directions == CssScrollableDirection.None ? Result.False : Result.True;
            if (value == "none") return directions == CssScrollableDirection.None ? Result.True : Result.False;
            var rtl = viewer.FlowDirection == FlowDirection.RightToLeft;
            var edge = value switch
            {
                "left" => CssScrollableDirection.Left,
                "right" => CssScrollableDirection.Right,
                "top" or "block-start" => CssScrollableDirection.Top,
                "bottom" or "block-end" => CssScrollableDirection.Bottom,
                "inline-start" => rtl ? CssScrollableDirection.Right : CssScrollableDirection.Left,
                "inline-end" => rtl ? CssScrollableDirection.Left : CssScrollableDirection.Right,
                "x" or "inline" => CssScrollableDirection.Left | CssScrollableDirection.Right,
                "y" or "block" => CssScrollableDirection.Top | CssScrollableDirection.Bottom,
                _ => CssScrollableDirection.None,
            };
            return (directions & edge) != 0 ? Result.True : Result.False;
        }
    }

    private static Expression? ParseExpression(ref CssTokenReader reader, QueryMode mode, int depth)
    {
        if (depth > 64) return null;
        var probe = reader;
        if (probe.TryReadIdent(out var not) && not.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            var operand = ParseAtom(ref reader, mode, depth + 1);
            return operand is not null && reader.AtEnd ? new Boolean("not", operand) : null;
        }
        var left = ParseAtom(ref reader, mode, depth + 1);
        if (left is null) return null;
        string? operation = null;
        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var ident)) return null;
            var current = ident.ToString().ToLowerInvariant();
            if (current is not ("and" or "or") || operation is not null && operation != current) return null;
            operation = current;
            var right = ParseAtom(ref reader, mode, depth + 1);
            if (right is null) return null;
            left = new Boolean(operation, left, right);
        }
        return left;
    }

    private static Expression? ParseAtom(ref CssTokenReader reader, QueryMode mode, int depth)
    {
        if (depth > 64) return null;
        if (reader.TryReadFunction(out var function, out var arguments))
        {
            if (mode != QueryMode.Size) return new Unknown();
            if (function.Equals("style", StringComparison.OrdinalIgnoreCase))
                return ParseBody(arguments.Remaining.ToString(), QueryMode.Style, depth + 1);
            if (function.Equals("scroll-state", StringComparison.OrdinalIgnoreCase))
                return ParseBody(arguments.Remaining.ToString(), QueryMode.ScrollState, depth + 1);
            return new Unknown();
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
        return ParseBody(body, mode, depth + 1);
    }

    private static Expression? ParseBody(string body, QueryMode mode, int depth)
    {
        var reader = new CssTokenReader(body);
        var probe = reader;
        if (reader.TryPeekChar(out var first) && first == '(' ||
            probe.TryReadIdent(out var ident) && ident.Equals("not", StringComparison.OrdinalIgnoreCase) ||
            mode == QueryMode.Size && probe.TryReadFunction(out _, out _))
            return ParseExpression(ref reader, mode, depth + 1);
        return ParseFeature(body, mode);
    }

    private static Expression? ParseFeature(string body, QueryMode mode)
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
        if (mode == QueryMode.Style)
        {
            if (!validDeclaration) return new Unknown();
            if (operations.Count is 1 or 2 && operations.All(operation => operation != ":") &&
                parts.All(part => part.Length > 0) &&
                (operations.Count == 1 || operations[0][0] == operations[1][0] && operations[0] != "="))
                return new StyleRange(parts.ToArray(), operations.ToArray());
            var name = Identifier(parts[0]);
            if (name is null || name == "--") return new Unknown();
            if (operations.Count == 0) return new StyleFeature(name, null);
            return operations.All(operation => operation == ":")
                ? new StyleFeature(name, body[(firstOperator + 1)..].Trim()) : new Unknown();
        }
        if (mode == QueryMode.ScrollState)
        {
            var name = Identifier(parts[0])?.ToLowerInvariant();
            if (!validDeclaration || name is not ("scrollable" or "scrolled"))
                return new Unknown();
            if (operations.Count == 0) return new ScrollStateFeature(name, null);
            if (operations.Count != 1 || operations[0] != ":") return new Unknown();
            var value = Identifier(parts[1])?.ToLowerInvariant();
            if (IsScrollDirection(value)) return new ScrollStateFeature(name, value);
            return CssCustomProperties.ContainsVariable(parts[1])
                ? new ScrollStateFeature(name, parts[1], Variable: true) : new Unknown();
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
    private static bool IsScrollDirection(string? value) => value is "none" or "top" or "right" or "bottom" or "left" or
        "block-start" or "inline-start" or "block-end" or "inline-end" or "x" or "y" or "block" or "inline";
    private static string? Identifier(string value)
    {
        var reader = new CssTokenReader(value);
        return reader.TryReadIdent(out var name) && reader.AtEnd ? name.ToString() : null;
    }
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
