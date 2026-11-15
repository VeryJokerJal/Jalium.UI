using System.Globalization;
using System.Runtime.InteropServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct FontVariation(uint Tag, double Value);

// Computed coordinates remain unclamped. Each selected face clamps its own axes
// at use time, and unsupported axes never change face selection or synthesize ink.
internal sealed class CssFontVariationSettings : IEquatable<CssFontVariationSettings>
{
    internal const int MaximumCount = 4096;
    internal static readonly CssFontVariationSettings Normal = new([]);
    internal static readonly DependencyProperty SettingsProperty = DependencyProperty.RegisterAttached(
        "CssFontVariationSettings", typeof(CssFontVariationSettings), typeof(CssFontVariationSettings),
        new FrameworkPropertyMetadata(Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal FontVariation[] Axes { get; }
    internal CssFontVariationSettings(FontVariation[] axes) => Axes = axes;
    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
        => Read(ref reader) is { } value ? new CssImmediateValue(SettingsProperty, value) : null;

    internal static CssFontVariationSettings? Read(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var word) && word.Equals("normal", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return Normal; }
        var values = new Dictionary<uint, double>();
        while (reader.TryReadString(out var tag))
        {
            if (tag.Length != 4 || tag.Any(c => c is < ' ' or > '~') ||
                !reader.TryReadNumber(out var value, out var unit) || unit != CssUnit.None || !double.IsFinite(value)) return null;
            values[FontFeature.TagOf(tag)] = value == 0 ? 0 : value;
            if (values.Count > MaximumCount) return null;
            if (reader.AtEnd) return new(values.OrderBy(p => p.Key).Select(p => new FontVariation(p.Key, p.Value)).ToArray());
            if (!reader.TryReadComma() || reader.AtEnd) return null;
        }
        return null;
    }

    internal static FontVariation[] Merge(FontVariation[] first, FontVariation[] last)
    {
        if (first.Length == 0) return last;
        if (last.Length == 0) return first;
        var values = new Dictionary<uint, double>();
        foreach (var axis in first) values[axis.Tag] = axis.Value;
        foreach (var axis in last) values[axis.Tag] = axis.Value;
        return values.OrderBy(p => p.Key).Select(p => new FontVariation(p.Key, p.Value)).ToArray();
    }

    internal string ToCss() => Axes.Length == 0 ? "normal" : string.Join(", ", Axes.Select(axis =>
        "\"" + new string([(char)(axis.Tag >> 24), (char)(axis.Tag >> 16 & 255),
            (char)(axis.Tag >> 8 & 255), (char)(axis.Tag & 255)]).Replace("\\", "\\\\").Replace("\"", "\\\"") +
        "\" " + axis.Value.ToString("R", CultureInfo.InvariantCulture)));

    internal bool Like(CssFontVariationSettings other) => Axes.Select(a => a.Tag).SequenceEqual(other.Axes.Select(a => a.Tag));
    internal CssFontVariationSettings Interpolate(CssFontVariationSettings other, double progress)
        => new(Axes.Select((axis, i) => new FontVariation(axis.Tag, Coordinate(axis.Value, other.Axes[i].Value, progress))).ToArray());
    private static double Coordinate(double from, double to, double progress)
    {
        if (progress == 0) return from;
        if (progress == 1) return to;
        // Normalize before weighting so opposite large coordinates and easing
        // overshoot cannot overflow intermediate products or produce NaN.
        double scale = Math.Max(Math.Abs(from), Math.Abs(to));
        return scale == 0 ? 0 : double.Clamp((from / scale * (1 - progress) + to / scale * progress) * scale, -double.MaxValue, double.MaxValue);
    }
    internal static AnimationTimeline? Animation(DependencyProperty property, object? from, object? to)
        => property == SettingsProperty && from is CssFontVariationSettings a && to is CssFontVariationSettings b && a.Like(b)
            ? new CssFontVariationAnimation { From = a, To = b } : null;

    public bool Equals(CssFontVariationSettings? other) => other is not null && Axes.SequenceEqual(other.Axes);
    public override bool Equals(object? other) => other is CssFontVariationSettings value && Equals(value);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var axis in Axes) hash.Add(axis); return hash.ToHashCode(); }
}

internal sealed class CssFontVariationAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssFontVariationAnimation), new PropertyMetadata(null));
    internal CssFontVariationSettings From = CssFontVariationSettings.Normal, To = CssFontVariationSettings.Normal;
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => typeof(CssFontVariationSettings);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        => From.Interpolate(To, EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress);
    protected override Freezable CreateInstanceCore() => new CssFontVariationAnimation { From = From, To = To };
}
