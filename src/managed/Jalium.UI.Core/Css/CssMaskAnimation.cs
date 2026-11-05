using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

/// <summary>Repeatable-list interpolation, sharing computed CSS length arithmetic with text spacing.</summary>
internal sealed class CssMaskAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssMaskAnimation), new PropertyMetadata(null));
    internal object From = null!, To = null!;
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => From.GetType();
    protected override Freezable CreateInstanceCore() => new CssMaskAnimation { From = From, To = To };

    internal static CssMaskAnimation? Create(DependencyProperty property, object? from, object? to)
    {
        if (property == CssMaskProperties.PositionsProperty && from is CssMaskList<CssBackgroundPosition> a && to is CssMaskList<CssBackgroundPosition> b && Count(a.Values.Length, b.Values.Length) > 0)
            return new() { From = a, To = b };
        if (property == CssMaskProperties.SizesProperty && from is CssMaskList<CssBackgroundSize> c && to is CssMaskList<CssBackgroundSize> d)
        {
            var count = Count(c.Values.Length, d.Values.Length);
            if (count > 0 && Enumerable.Range(0, count).All(i => Compatible(c.At(i), d.At(i)))) return new() { From = c, To = d };
        }
        if ((property == CssMaskBorderProperties.SliceProperty || property == CssMaskBorderProperties.WidthProperty ||
            property == CssMaskBorderProperties.OutsetProperty) && from is CssBorderImageMeasures first && to is CssBorderImageMeasures second &&
            first.Fill == second.Fill && first.Sides.Length == second.Sides.Length &&
            first.Sides.Zip(second.Sides).All(pair => pair.First.Auto == pair.Second.Auto &&
                (pair.First.Auto || pair.First.Number == pair.Second.Number))) return new() { From = first, To = second };
        return null;
    }

    private static int Count(int a, int b)
    {
        int x = a, y = b; while (y != 0) { int next = x % y; x = y; y = next; }
        long count = x == 0 ? 0 : (long)a / x * b;
        return count <= 65536 ? (int)count : 0;
    }
    private static bool Compatible(CssBackgroundSize a, CssBackgroundSize b)
    {
        bool aKeyword = a.Mode is CssBackgroundSizeMode.Contain or CssBackgroundSizeMode.Cover;
        bool bKeyword = b.Mode is CssBackgroundSizeMode.Contain or CssBackgroundSizeMode.Cover;
        return aKeyword || bKeyword ? a.Mode == b.Mode : (a.Width is null) == (b.Width is null) && (a.Height is null) == (b.Height is null);
    }

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        double progress = EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress;
        if (progress == 0) return From; if (progress == 1) return To;
        if (From is CssBorderImageMeasures first && To is CssBorderImageMeasures second)
        {
            var values = new CssBorderImageMeasure[first.Sides.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var a = first.Sides[i]; var b = second.Sides[i];
                values[i] = a.Auto ? a : a.Number
                    ? new(new(a.Resolve(first.Context, 0, 1, 0) * (1 - progress) + b.Resolve(second.Context, 0, 1, 0) * progress, CssUnit.None), Number: true)
                    : new(Interpolate(CssMaskStyleQuery.Computed(a.Length, first.Context), CssMaskStyleQuery.Computed(b.Length, second.Context), progress));
            }
            return new CssBorderImageMeasures(values, default, first.Fill);
        }
        if (From is CssMaskList<CssBackgroundPosition> positions && To is CssMaskList<CssBackgroundPosition> other)
        {
            var result = new CssBackgroundPosition[Count(positions.Values.Length, other.Values.Length)];
            for (var i = 0; i < result.Length; i++)
            {
                var a = positions.At(i); var b = other.At(i);
                result[i] = new(new(Interpolate(CssMaskStyleQuery.Position(a.X, a.Lengths), CssMaskStyleQuery.Position(b.X, b.Lengths), progress)),
                    new(Interpolate(CssMaskStyleQuery.Position(a.Y, a.Lengths), CssMaskStyleQuery.Position(b.Y, b.Lengths), progress)), default);
            }
            return new CssMaskList<CssBackgroundPosition>(result);
        }
        var from = (CssMaskList<CssBackgroundSize>)From; var to = (CssMaskList<CssBackgroundSize>)To;
        var sizes = new CssBackgroundSize[Count(from.Values.Length, to.Values.Length)];
        for (var i = 0; i < sizes.Length; i++)
        {
            var a = from.At(i); var b = to.At(i);
            sizes[i] = a.Mode is CssBackgroundSizeMode.Contain or CssBackgroundSizeMode.Cover ? a : new(
                a.Width is { } aw && b.Width is { } bw ? Interpolate(CssMaskStyleQuery.Computed(aw, a.Lengths), CssMaskStyleQuery.Computed(bw, b.Lengths), progress) : null,
                a.Height is { } ah && b.Height is { } bh ? Interpolate(CssMaskStyleQuery.Computed(ah, a.Lengths), CssMaskStyleQuery.Computed(bh, b.Lengths), progress) : null);
        }
        return new CssMaskList<CssBackgroundSize>(sizes);
    }

    private static CssLength Interpolate(CssLayoutLength from, CssLayoutLength to, double progress)
    {
        var value = CssTextSpacing.Interpolate(from, to, progress);
        return value is double pixels ? new(pixels, CssUnit.Px) : value is CssLayoutLength { Expression: { } expression } ? new(expression)
            : value is CssLayoutLength length && length.IsPercent ? new(length.Value * 100, CssUnit.Percent)
            : new(((CssLayoutLength)value).Value, CssUnit.Px);
    }
}
