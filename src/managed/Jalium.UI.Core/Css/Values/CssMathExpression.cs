namespace Jalium.UI.Styling;

internal enum CssNumericKind { Number, Length, Percent, LengthPercent, Angle, Time, Resolution, Frequency, Flex, Compound }

/// <summary>CSS arithmetic retains dimensions and percentages until its evaluation context is known.</summary>
internal sealed record CssMathExpression(
    string Operation, CssNumericType Type, CssLength Literal, CssMathExpression[] Arguments, string? Option = null)
{
    public CssNumericKind Kind => Type.Kind;
    public bool Equals(CssMathExpression? other) => other is not null && Operation == other.Operation &&
        Type == other.Type && Option == other.Option && Literal.Unit == other.Literal.Unit &&
        Literal.Value.Equals(other.Literal.Value) && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Operation); hash.Add(Type); hash.Add(Option); hash.Add(Literal.Unit); hash.Add(Literal.Value);
        foreach (var argument in Arguments) hash.Add(argument);
        return hash.ToHashCode();
    }

    public bool UsesPercent => Literal.Unit == CssUnit.Percent || Type.PercentHint is not null || Arguments.Any(a => a.UsesPercent);
    internal bool RequiresElementContext => Operation == "value"
        ? Kind == CssNumericKind.Length && !Literal.IsAbsolute
        : Arguments.Any(a => a.RequiresElementContext);
    public bool IsAbsolute => Operation is "value" or "none"
        ? Literal.IsAbsolute || Kind is CssNumericKind.Angle or CssNumericKind.Time or CssNumericKind.Resolution or CssNumericKind.Frequency or CssNumericKind.Flex
        : Arguments.All(a => a.IsAbsolute);

    internal void ObserveContainerDependencies(in CssLengthContext context)
    {
        Literal.ObserveContainerDependencies(context);
        foreach (var argument in Arguments) argument.ObserveContainerDependencies(context);
    }

    // IEEE special values are retained inside the tree and censored only at its
    // outer boundary. Missing layout context is distinct from arithmetic NaN.
    public bool TryEvaluate(in CssLengthContext context, double percentBasis, out double value)
    {
        if (!TryEvaluateCore(context, percentBasis, out value)) return false;
        value = Censor(value);
        return true;
    }

    internal static double Censor(double value) => double.IsNaN(value) || value == 0 ? 0
        : double.IsPositiveInfinity(value) ? double.MaxValue
        : double.IsNegativeInfinity(value) ? -double.MaxValue : value;

    internal bool TryEvaluateCore(in CssLengthContext context, double percentBasis, out double value)
    {
        value = double.NaN;
        if (Operation == "none") { value = Literal.Value; return true; }
        if (Operation == "value")
        {
            if (Literal.Unit == CssUnit.Percent)
            {
                if (!double.IsFinite(percentBasis)) return false;
                value = Literal.Value * (percentBasis / 100);
            }
            else if (Kind is CssNumericKind.Number or CssNumericKind.Flex) value = Literal.Value;
            else if (Kind == CssNumericKind.Angle) CssUnitConversion.TryToDegrees(Literal.Value, Literal.Unit, out value);
            else if (Kind == CssNumericKind.Time) CssUnitConversion.TryToMilliseconds(Literal.Value, Literal.Unit, out value);
            else if (Kind == CssNumericKind.Resolution) CssUnitConversion.TryToDppx(Literal.Value, Literal.Unit, out value);
            else if (Kind == CssNumericKind.Frequency) value = Literal.Value * (Literal.Unit == CssUnit.Khz ? 1000 : 1);
            else if (!Literal.TryResolve(context, CssPercentBasis.NotSupported, out value)) return false;
            return true;
        }
        Span<double> values = Arguments.Length <= 32 ? stackalloc double[Arguments.Length] : new double[Arguments.Length];
        var nan = false;
        for (var i = 0; i < Arguments.Length; i++)
        {
            if (!Arguments[i].TryEvaluateCore(context, percentBasis, out values[i])) return false;
            nan |= double.IsNaN(values[i]);
        }
        if (nan) return true;
        value = Operation switch
        {
            "+" => values[0] + values[1], "-" => values[0] - values[1],
            "*" => values[0] * values[1], "/" => values[0] / values[1],
            "negate" => -values[0], "calc" => values[0],
            "min" => Minimum(values), "max" => Maximum(values),
            "clamp" => Math.Max(values[0], Math.Min(values[1], values[2])),
            "round" => Round(values[0], values[1], Option!),
            "mod" => Mod(values[0], values[1]), "rem" => values[0] % values[1],
            "abs" => Math.Abs(values[0]), "sign" => values[0] == 0 ? values[0] : Math.CopySign(1, values[0]),
            "sin" => Trig("sin", values[0], Arguments[0].Kind),
            "cos" => Trig("cos", values[0], Arguments[0].Kind),
            "tan" => Trig("tan", values[0], Arguments[0].Kind),
            "asin" => Math.Asin(values[0]) * (180 / Math.PI),
            "acos" => Math.Acos(values[0]) * (180 / Math.PI),
            "atan" => Math.Atan(values[0]) * (180 / Math.PI),
            "atan2" => Math.Atan2(values[0], values[1]) * (180 / Math.PI),
            "pow" => Math.Abs(values[0]) == 1 && double.IsInfinity(values[1]) ? double.NaN : Math.Pow(values[0], values[1]),
            "sqrt" => Math.Sqrt(values[0]), "hypot" => Hypot(values),
            "log" => values.Length == 1 ? Math.Log(values[0]) : values[1] == 1 || values[1] < 0
                ? double.NaN : Math.Log(values[0]) / Math.Log(values[1]),
            "exp" => Math.Exp(values[0]), _ => double.NaN,
        };
        return true;
    }

    private static double Minimum(ReadOnlySpan<double> values)
    { var result = values[0]; foreach (var value in values) result = Math.Min(result, value); return result; }
    private static double Maximum(ReadOnlySpan<double> values)
    { var result = values[0]; foreach (var value in values) result = Math.Max(result, value); return result; }

    private static double Trig(string operation, double value, CssNumericKind kind)
    {
        if (double.IsInfinity(value)) return double.NaN;
        if (kind == CssNumericKind.Angle)
        {
            value %= 360;
            if (operation == "sin" && value % 180 == 0) return Math.CopySign(0, value);
            if (operation == "cos" && Math.Abs(value) % 180 == 90) return 0;
            if (operation == "tan")
            {
                if (value % 180 == 0) return Math.CopySign(0, value);
                if (value is 90 or -270) return double.PositiveInfinity;
                if (value is -90 or 270) return double.NegativeInfinity;
            }
            value *= Math.PI / 180;
        }
        return operation == "sin" ? Math.Sin(value) : operation == "cos" ? Math.Cos(value) : Math.Tan(value);
    }

    private static double Hypot(ReadOnlySpan<double> values)
    {
        var maximum = 0d;
        foreach (var value in values) maximum = Math.Max(maximum, Math.Abs(value));
        if (maximum == 0 || double.IsInfinity(maximum)) return maximum;
        var sum = 0d;
        foreach (var value in values) { var scaled = value / maximum; sum += scaled * scaled; }
        return maximum * Math.Sqrt(sum);
    }

    private static double Round(double value, double interval, string strategy)
    {
        interval = Math.Abs(interval);
        if (interval == 0 || double.IsInfinity(value) && double.IsInfinity(interval)) return double.NaN;
        if (double.IsInfinity(value)) return value;
        if (double.IsInfinity(interval))
        {
            if (strategy == "up" && value > 0) return double.PositiveInfinity;
            if (strategy == "down" && value < 0) return double.NegativeInfinity;
            return Math.CopySign(0, value);
        }
        var remainder = value % interval;
        if (remainder == 0) return value;
        var quotient = value / interval;
        if (double.IsInfinity(quotient)) return value;
        var lower = Math.Floor(quotient) * interval;
        var upper = Math.Ceiling(quotient) * interval;
        if (lower == 0) lower = 0d;
        if (upper == 0) upper = -0d;
        return strategy switch
        {
            "up" => upper, "down" => lower, "to-zero" => value < 0 ? upper : lower,
            _ => value - lower < upper - value ? lower : upper,
        };
    }

    private static double Mod(double value, double interval)
    {
        if (interval == 0 || double.IsInfinity(value)) return double.NaN;
        if (double.IsInfinity(interval))
            return double.IsNegative(value) == double.IsNegative(interval) ? value : double.NaN;
        var remainder = value % interval;
        if (remainder == 0) return Math.CopySign(0, interval);
        return double.IsNegative(remainder) == double.IsNegative(interval) ? remainder : remainder + interval;
    }

    internal static bool IsFunction(string name) => name is "calc" or "min" or "max" or "clamp"
        or "round" or "mod" or "rem" or "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "atan2"
        or "pow" or "sqrt" or "hypot" or "log" or "exp" or "abs" or "sign";

    public static CssMathExpression? Parse(string name, ReadOnlySpan<char> arguments, int depth = 0)
        => Parse(name, arguments, depth, new ParseBudget());

    private sealed class ParseBudget { internal int Terms; }

    private static CssMathExpression? Parse(string name, ReadOnlySpan<char> arguments, int depth, ParseBudget budget)
    {
        name = name.ToLowerInvariant();
        if (depth > 64 || !IsFunction(name)) return null;
        var parser = new Parser(arguments, depth, budget);
        string? option = null;
        if (name == "round")
        {
            option = "nearest";
            var probe = parser;
            if (probe.Identifier() is { } strategy && strategy is "nearest" or "up" or "down" or "to-zero")
            {
                if (!probe.Consume(',')) return null;
                option = strategy; parser = probe;
            }
        }
        var values = new List<CssMathExpression>();
        while (true)
        {
            CssMathExpression? expression = null;
            if (name == "clamp" && values.Count is 0 or 2)
            {
                var probe = parser;
                if (probe.Identifier() == "none")
                {
                    expression = new("none", default, new(values.Count == 0 ? double.NegativeInfinity : double.PositiveInfinity, CssUnit.None), []);
                    parser = probe;
                }
            }
            expression ??= parser.Sum();
            if (expression is null) return null;
            values.Add(expression);
            if (values.Count > 4096) return null;
            if (!parser.Consume(',')) break;
        }
        if (!parser.AtEnd) return null;
        var count = values.Count;
        if (name is "calc" or "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "sqrt" or "exp" or "abs" or "sign")
        { if (count != 1) return null; }
        else if (name == "clamp") { if (count != 3) return null; }
        else if (name is "mod" or "rem" or "atan2" or "pow") { if (count != 2) return null; }
        else if (name is "round" or "log") { if (count is < 1 or > 2) return null; }

        if (name == "clamp")
            for (var i = 0; i < 3; i += 2)
                if (values[i].Operation == "none") values[i] = values[i] with { Type = values[1].Type };

        var type = values[0].Type;
        if (name is "sin" or "cos" or "tan")
        {
            if (type.Kind is not (CssNumericKind.Number or CssNumericKind.Angle)) return null;
            type = type.Result(CssNumericKind.Number);
        }
        else if (name is "asin" or "acos" or "atan" or "pow" or "sqrt" or "log" or "exp")
        {
            foreach (var value in values) if (value.Kind != CssNumericKind.Number) return null;
            foreach (var value in values)
            {
                if (CssNumericType.Add(type, value.Type) is not { } common) return null;
                type = common;
            }
            type = type.Result(name is "asin" or "acos" or "atan" ? CssNumericKind.Angle : CssNumericKind.Number);
        }
        else if (name == "sign") type = type.Result(CssNumericKind.Number);
        else
        {
            foreach (var value in values)
            {
                if (CssNumericType.Add(type, value.Type) is not { } common) return null;
                type = common;
            }
            if (name == "atan2") type = type.Result(CssNumericKind.Angle);
        }
        if (name == "round" && count == 1)
        {
            if (type.Kind != CssNumericKind.Number) return null;
            values.Add(Number(1));
        }
        return new(name, type, default, values.ToArray(), option);
    }

    private static CssMathExpression Number(double value) => new("value", default, new(value, CssUnit.None), []);

    private ref struct Parser(ReadOnlySpan<char> text, int depth, ParseBudget budget)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private readonly int _depth = depth;
        private readonly ParseBudget _budget = budget;
        private int _position;

        private bool Whitespace()
        {
            var found = false;
            while (_position < _text.Length)
            {
                if (CssTokenReader.IsCssWhitespace(_text[_position])) { found = true; _position++; }
                else if (_text[_position] == '/' && _position + 1 < _text.Length && _text[_position + 1] == '*')
                    _position = CssTokenReader.SkipComment(_text, _position);
                else break;
            }
            return found;
        }
        public bool AtEnd { get { Whitespace(); return _position == _text.Length; } }
        public bool Consume(char c)
        { Whitespace(); if (_position >= _text.Length || _text[_position] != c) return false; _position++; return true; }
        public string? Identifier()
        {
            var reader = new CssTokenReader(_text[_position..]);
            if (!reader.TryReadIdent(out var ident)) return null;
            _position += reader.Position; return ident.ToString().ToLowerInvariant();
        }

        public CssMathExpression? Sum()
        {
            var result = Product();
            while (result is not null)
            {
                var whitespace = Whitespace();
                if (_position >= _text.Length || _text[_position] is not ('+' or '-')) break;
                if (!whitespace) return null;
                var operation = _text[_position++].ToString();
                if (!Whitespace()) return null;
                var right = Product();
                if (right is null || CssNumericType.Add(result.Type, right.Type) is not { } type) return null;
                result = new(operation, type, default, [result, right]);
            }
            return result;
        }

        private CssMathExpression? Product()
        {
            var result = Value();
            while (result is not null)
            {
                var previous = _position;
                Whitespace();
                if (_position >= _text.Length || _text[_position] is not ('*' or '/')) { _position = previous; break; }
                var operation = _text[_position++].ToString();
                var right = Value();
                if (right is null || CssNumericType.Multiply(result.Type, right.Type, operation == "/") is not { } type) return null;
                result = new(operation, type, default, [result, right]);
            }
            return result;
        }

        private CssMathExpression? Value()
        {
            Whitespace();
            if (_position >= _text.Length || _depth > 64 || ++_budget.Terms > 512) return null;
            if (_text[_position] == '(')
            {
                var reader = new CssTokenReader("calc" + _text[_position..].ToString());
                if (!reader.TryReadFunction(out _, out var args)) return null;
                _position += reader.Position - 4;
                return Parse("calc", args.Remaining, _depth + 1, _budget);
            }
            var token = new CssTokenReader(_text[_position..]);
            if (token.TryReadFunction(out var name, out var arguments))
            {
                var expression = Parse(name.ToString(), arguments.Remaining, _depth + 1, _budget);
                _position += token.Position;
                return expression;
            }
            token = new CssTokenReader(_text[_position..]);
            if (token.TryReadIdent(out var keyword))
            {
                var constant = keyword.ToString().ToLowerInvariant() switch
                {
                    "e" => Math.E, "pi" => Math.PI, "infinity" => double.PositiveInfinity,
                    "-infinity" => double.NegativeInfinity, "nan" => double.NaN, _ => (double?)null,
                };
                if (constant is null) return null;
                _position += token.Position; return Number(constant.Value);
            }
            token = new CssTokenReader(_text[_position..]);
            if (!token.TryReadNumber(out var value, out var unit)) return null;
            _position += token.Position;
            var kind = unit switch
            {
                CssUnit.None => CssNumericKind.Number, CssUnit.Percent => CssNumericKind.Percent,
                CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn => CssNumericKind.Angle,
                CssUnit.S or CssUnit.Ms => CssNumericKind.Time,
                CssUnit.Dpi or CssUnit.Dpcm or CssUnit.Dppx => CssNumericKind.Resolution,
                CssUnit.Hz or CssUnit.Khz => CssNumericKind.Frequency, CssUnit.Fr => CssNumericKind.Flex,
                _ => CssNumericKind.Length,
            };
            return new("value", CssNumericType.Of(kind), new(value == 0 ? 0 : value, unit), []);
        }
    }
}
