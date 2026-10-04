using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Interpolates the thumb and track colors of a CSS scrollbar together.</summary>
internal sealed class CssScrollBarColorsAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(CssScrollBarColors),
            typeof(CssScrollBarColorsAnimation), new PropertyMetadata(null));

    internal static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(CssScrollBarColors),
            typeof(CssScrollBarColorsAnimation), new PropertyMetadata(null));

    internal static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction),
            typeof(CssScrollBarColorsAnimation), new PropertyMetadata(null));

    internal CssScrollBarColors? From
    {
        get => (CssScrollBarColors?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    internal CssScrollBarColors? To
    {
        get => (CssScrollBarColors?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    internal IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(CssScrollBarColors);

    public override object GetCurrentValue(object defaultOriginValue,
        object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var from = From ?? defaultOriginValue as CssScrollBarColors;
        var to = To ?? defaultDestinationValue as CssScrollBarColors;
        if (from is null || to is null)
            return defaultDestinationValue;

        var progress = Math.Clamp(EasingFunction?.Ease(animationClock.CurrentProgress) ??
            animationClock.CurrentProgress, 0, 1);
        if (progress <= 0) return from;
        if (progress >= 1) return to;

        return new CssScrollBarColors(
            Interpolate(from.ThumbData, to.ThumbData, progress),
            Interpolate(from.TrackData, to.TrackData, progress));
    }

    private static CssColorParser.CssColorData Interpolate(
        CssColorParser.CssColorData from, CssColorParser.CssColorData to,
        double progress)
    {
        // CSS Color 4 retains legacy sRGB interpolation for two legacy colors;
        // otherwise a host syntax without a declared space defaults to Oklab.
        var space = from.Legacy && to.Legacy
            ? CssColorParser.CssInterpolationSpace.Srgb
            : CssColorParser.CssInterpolationSpace.Oklab;
        return CssColorParser.InterpolateData(from, to,
            new CssColorParser.CssInterpolationMethod(space), progress);
    }

    protected override Freezable CreateInstanceCore() => new CssScrollBarColorsAnimation();
}
