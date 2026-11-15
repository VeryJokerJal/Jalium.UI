using Jalium.UI.Documents;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

// Absolute doubles remain valid for existing compiled CSS and native writes.
// Percentages retain their computed value until the actual text's font is known.
internal static class CssTextSpacing
{
    internal static double Letter(DependencyObject owner)
    {
        var value = owner.GetValue(CssFlowProperties.LetterSpacingProperty);
        double used = value is CssLayoutLength length
            ? length.Resolve((double)owner.GetValue(TextElement.FontSizeProperty)!, 0)
            : value is double pixels ? pixels : 0;
        return double.IsFinite(used) ? used : 0;
    }

    internal static bool Length(object? value, out CssLayoutLength length)
    {
        length = value is CssLayoutLength typed ? typed : value is double pixels && double.IsFinite(pixels)
            ? CssLayoutLength.Px(pixels) : default;
        return length.Kind is CssLayoutLength.KindPx or CssLayoutLength.KindPercent or CssLayoutLength.KindExpression;
    }

    internal static CssTextSpacingAnimation? Animation(DependencyProperty property, object? from, object? to)
        => property == CssFlowProperties.LetterSpacingProperty && Length(from, out var before) && Length(to, out var after)
            ? new() { From = before, To = after } : null;

    internal static object Interpolate(CssLayoutLength from, CssLayoutLength to, double progress)
    {
        if (progress == 0) return Box(from);
        if (progress == 1) return Box(to);
        // Canonicalize linear terms before composing. Reversing an interrupted
        // transition must not grow an ever deeper calculation tree.
        if (CssSizeStyleQuery.Linear(from, out var ax, out var ap) && CssSizeStyleQuery.Linear(to, out var bx, out var bp))
        {
            double px = ax + (bx - ax) * progress, percent = ap + (bp - ap) * progress;
            if (!from.IsPercent && !to.IsPercent) return px;
            if (px == 0) return CssLayoutLength.Percent(percent);
            return CssLayoutLength.Math(new("+", Mixed, default,
                [Literal(px, CssUnit.Px, CssNumericKind.Length), Literal(percent * 100, CssUnit.Percent, CssNumericKind.Percent)]), default);
        }
        var before = Freeze(from); var after = Freeze(to);
        return CssLayoutLength.Math(new("+", Mixed, default, [Scale(before, 1 - progress), Scale(after, progress)]), default);
    }

    private static object Box(CssLayoutLength length) => length.Kind == CssLayoutLength.KindPx ? length.Value : length;
    private static readonly CssNumericType Mixed = new(Length: 1, PercentHint: CssNumericKind.Length);
    private static CssMathExpression Literal(double value, CssUnit unit, CssNumericKind kind)
        => new("value", CssNumericType.Of(kind), new(value, unit), []);
    private static CssMathExpression Scale(CssMathExpression value, double factor)
        => new("*", value.Type, default, [value, Literal(factor, CssUnit.None, CssNumericKind.Number)]);

    private static CssMathExpression Freeze(CssLayoutLength value)
        => value.Kind == CssLayoutLength.KindPx ? Literal(value.Value, CssUnit.Px, CssNumericKind.Length)
            : value.Kind == CssLayoutLength.KindPercent ? Literal(value.Value * 100, CssUnit.Percent, CssNumericKind.Percent)
            : Freeze(value.Expression!, value.Context);

    private static CssMathExpression Freeze(CssMathExpression expression, CssLengthContext context)
    {
        // A computed em/viewport/container unit keeps the declaring element's
        // context, even when the interpolation is inherited by a different font.
        if (expression.Operation == "value" && expression.Kind == CssNumericKind.Length &&
            expression.Literal.TryResolve(context, CssPercentBasis.NotSupported, out var pixels))
            return Literal(pixels, CssUnit.Px, CssNumericKind.Length);
        if (expression.Operation is "sibling-index" or "sibling-count" && expression.TryEvaluate(context, 0, out var number))
            return Literal(number, CssUnit.None, CssNumericKind.Number);
        return expression with { Arguments = expression.Arguments.Select(argument => Freeze(argument, context)).ToArray() };
    }
}

internal sealed class CssTextSpacingAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssTextSpacingAnimation), new PropertyMetadata(null));
    internal CssLayoutLength From, To;
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => typeof(object);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        => CssTextSpacing.Interpolate(From, To, EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress);
    protected override Freezable CreateInstanceCore() => new CssTextSpacingAnimation { From = From, To = To };
}
