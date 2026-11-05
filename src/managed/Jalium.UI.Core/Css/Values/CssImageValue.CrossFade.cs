using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed record CssImageColor(Color Color, bool Current, string? Expression)
{
    internal static bool TryRead(ref CssTokenReader reader, out CssImageColor? color)
    {
        color = null;
        if (!CssColorParser.TryParseContextual(ref reader, out var value, out var current, out var expression)) return false;
        color = new(value, current, expression); return true;
    }
    internal SolidColorBrush Brush(in CssApplyContext context)
    {
        var value = Color;
        if (Current || Expression is not null)
        {
            CssColorBrushObserver.Observe(context.Element, context.CurrentColor as SolidColorBrush);
            if (Current) value = context.CurrentColorValue;
            else if (!CssColorParser.TryResolveCurrentColor(Expression!, context.CurrentColorValue, out value)) value = Colors.Transparent;
        }
        return new(value);
    }
}

internal sealed partial class CssImageValue
{
    private readonly CssImageColor? _color;
    private readonly CrossFadeOption[]? _crossFade;
    private sealed record CrossFadeOption(CssImageValue? Image, CssImageColor? Color, double? Percentage, CssMathExpression? Expression);
    private CssImageValue(CssImageColor color) => _color = color;
    private CssImageValue(CrossFadeOption[] options) => _crossFade = options;

    private static bool TryReadCrossFade(ref CssTokenReader args, CssCompileContext context,
        bool insideSet, int depth, out CssImageValue? image)
    {
        image = null; var options = new List<CrossFadeOption>();
        do
        {
            if (!args.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty) return false;
            var item = new CssTokenReader(segment, args.NumericContext);
            var weightReader = item;
            var weight = TryReadWeight(ref weightReader, out var percentage, out var expression);
            if (weight) item = weightReader;
            var sourceReader = item;
            CssImageValue? source = null; CssImageColor? color = null;
            if (TryRead(ref sourceReader, context, out source, insideSet, depth)) item = sourceReader;
            else if (!CssImageColor.TryRead(ref item, out color)) return false;
            if (!weight)
            {
                weightReader = item;
                if (TryReadWeight(ref weightReader, out percentage, out expression)) item = weightReader;
            }
            if (!item.AtEnd) return false;
            options.Add(new(source, color, percentage, expression));
            if (!args.TryReadComma()) break;
            if (args.AtEnd) return false;
        } while (true);
        if (!args.AtEnd || options.Count == 0) return false;
        image = new(options.ToArray()); return true;
    }

    private static bool TryReadWeight(ref CssTokenReader reader, out double? percentage, out CssMathExpression? expression)
    {
        percentage = null; expression = null; var probe = reader;
        if (probe.TryReadFunction(out var name, out var args))
        {
            var math = CssMathExpression.Parse(name.ToString(), args.Remaining);
            if (math?.Kind != CssNumericKind.Percent || math.Type.PercentHint is not null) return false;
            expression = math; reader = probe; return true;
        }
        probe = reader;
        if (probe.TryReadNumber(out var value, out var unit))
        {
            if (unit != CssUnit.Percent || value < 0 || value > 100 || !double.IsFinite(value)) return false;
            percentage = value / 100; reader = probe; return true;
        }
        return false;
    }

    private bool Flatten(in CssApplyContext context, double multiplier,
        List<(CssImageValue? Image, CssImageColor? Color, double Weight)> leaves)
    {
        if (_crossFade is not { } options) return false;
        var weights = new double[options.Length]; var sum = 0d; var omitted = 0;
        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index];
            if (option.Expression is { } expression)
            {
                expression.ObserveContainerDependencies(context.Lengths);
                if (!expression.TryEvaluate(context.Lengths, 1, out var value) || !double.IsFinite(value)) return false;
                weights[index] = Math.Clamp(value, 0, 1);
            }
            else if (option.Percentage is { } percentage) weights[index] = percentage;
            else { omitted++; continue; }
            sum += weights[index];
        }
        var remainder = omitted > 0 ? Math.Max(0, 1 - sum) / omitted : 0;
        var total = sum + remainder * omitted; var normalizer = total > 1 ? total : 1;
        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index];
            var weight = (option.Percentage is null && option.Expression is null ? remainder : weights[index]) / normalizer * multiplier;
            if (weight <= 0) continue;
            if (option.Image is { _crossFade: not null } nested)
            {
                if (!nested.Flatten(context, weight, leaves)) return false;
                continue;
            }
            var previous = leaves.FindIndex(leaf => Equals(leaf.Image, option.Image) && Equals(leaf.Color, option.Color));
            if (previous >= 0) leaves[previous] = (option.Image, option.Color, leaves[previous].Weight + weight);
            else leaves.Add((option.Image, option.Color, weight));
        }
        return true;
    }
}
