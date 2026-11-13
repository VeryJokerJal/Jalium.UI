using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

internal sealed record CssFontSizeAdjust(byte Metric, double Aspect)
{
    internal static readonly CssFontSizeAdjust None = new(0, 0);
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssFontSizeAdjust", typeof(CssFontSizeAdjust), typeof(CssFontSizeAdjust),
        new FrameworkPropertyMetadata(None, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));
    private static readonly string[] Metrics = ["none", "ex-height", "cap-height", "ch-width", "ic-width", "ic-height"];

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        byte metric = 1;
        var probe = reader;
        if (probe.TryReadIdent(out var word))
        {
            string keyword = word.ToString().ToLowerInvariant();
            if (keyword == "none")
            { if (!probe.AtEnd) return null; reader = probe; return new CssImmediateValue(ValueProperty, None); }
            int index = Array.IndexOf(Metrics, keyword);
            if (index > 0) { metric = (byte)index; reader = probe; }
        }
        probe = reader;
        if (probe.TryReadIdent(out word) && word.Equals("from-font", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return new FromFontValue(metric); }
        if (!reader.TryReadNumber(out var value, out var unit) || unit != CssUnit.None || !reader.AtEnd ||
            !double.IsFinite(value) || value < 0 && !reader.NumberWasCalculated) return null;
        return new CssImmediateValue(ValueProperty, new CssFontSizeAdjust(metric, Math.Max(0, value)));
    }

    private sealed class FromFontValue(byte metric) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var fonts = context.Lengths.Fonts ?? CssFontContext.Initial;
            fonts.Dependency?.Observe();
            double aspect = fonts.Element.Aspect(metric, context.Lengths.ElementFontSize, fonts.Dependency);
            sink.Set(ValueProperty, aspect > 0 && double.IsFinite(aspect) ? new CssFontSizeAdjust(metric, aspect) : None);
            return true;
        }
    }

    internal string ToCss() => Metric == 0 ? "none" : (Metric == 1 ? "" : Metrics[Metric] + " ") + Aspect.ToString("R", CultureInfo.InvariantCulture);
    internal static AnimationTimeline? Animation(DependencyProperty property, object? from, object? to)
        => property == ValueProperty && from is CssFontSizeAdjust { Metric: > 0 } a &&
            to is CssFontSizeAdjust b && a.Metric == b.Metric ? new CssFontSizeAdjustAnimation { From = a, To = b } : null;
    internal CssFontSizeAdjust Interpolate(CssFontSizeAdjust other, double progress)
    {
        double scale = Math.Max(Aspect, other.Aspect);
        double value = progress == 0 ? Aspect : progress == 1 ? other.Aspect : scale == 0 ? 0 :
            Math.Clamp((Aspect / scale * (1 - progress) + other.Aspect / scale * progress) * scale, 0, double.MaxValue);
        return new(Metric, value);
    }
}

internal sealed class CssFontSizeAdjustAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssFontSizeAdjustAnimation), new PropertyMetadata(null));
    internal CssFontSizeAdjust From = new(1, 0), To = new(1, 0);
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => typeof(CssFontSizeAdjust);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        => From.Interpolate(To, EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress);
    protected override Freezable CreateInstanceCore() => new CssFontSizeAdjustAnimation { From = From, To = To };
}
