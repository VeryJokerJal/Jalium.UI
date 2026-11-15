using Jalium.UI.Controls;
using Jalium.UI.Media.Animation;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

internal readonly record struct CssInitialLetter(double Size, double Sink)
{
    internal bool Active => Size >= 1;
    internal CssInitialLetter Interpolate(CssInitialLetter to, double progress)
        => new(Math.Max(1, Blend(Size, to.Size, progress)), Math.Max(1, Math.Floor(Blend(Sink, to.Sink, progress) + .5)));
    private static double Blend(double from, double to, double progress)
    {
        if (progress == 0) return from;
        if (progress == 1) return to;
        var scale = Math.Max(from, to);
        return Math.Clamp((from / scale * (1 - progress) + to / scale * progress) * scale, 1, double.MaxValue);
    }
}

internal enum CssInitialLetterAlignment { Alphabetic, Ideographic, Hanging, Leading }
internal readonly record struct CssInitialLetterAlign(CssInitialLetterAlignment Alignment, bool BorderBox = false);
internal enum CssInitialLetterWrapKind { None, First, All, Grid, Offset }
internal readonly record struct CssInitialLetterWrap(CssInitialLetterWrapKind Kind, CssLayoutLength Offset = default)
{
    public bool Equals(CssInitialLetterWrap other) => Kind == other.Kind &&
        (Kind != CssInitialLetterWrapKind.Offset || CssSizeStyleQuery.Equal(Offset, other.Offset));
    public override int GetHashCode() => Kind.GetHashCode();
}

internal static class CssInitialLetterProperties
{
    // CSS Inline 3 §7.4.1 defines language and script defaults at UA origin.
    // Match normal selectors so language changes use the existing dependency
    // tracking, and authored declarations retain their ordinary cascade rank.
    private static readonly Lazy<CssStyleSheetCollection> s_languageDefaults = new(static () => new()
    {
        // A locally assigned native Language is the markup language attribute.
        // Restrict defaults to language declarations so inherited language does
        // not override an ancestor's authored initial-letter-align.
        CssStyleSheet.Parse(":is([lang],[Language]):lang(zh,ja,ko,ii){initial-letter-align:ideographic;}" +
            ":is([lang],[Language]):lang(hi,mr,ne,pi,kok,brx,mai,sd,sa){initial-letter-align:hanging;}" +
            ":is([lang],[Language]):lang(\"*-Latn\",\"*-Cyrl\"){initial-letter-align:alphabetic;}" +
            ":is([lang],[Language]):lang(\"*-Hani\",\"*-Hant\",\"*-Hans\"){initial-letter-align:ideographic;}")
    });
    internal static CssStyleSheetCollection LanguageDefaults => s_languageDefaults.Value;
    internal sealed record UsedMetrics(double Height, double Baseline, double InlineOffset = 0);
    private static readonly ConditionalWeakTable<DependencyObject, UsedMetrics> s_metrics = new();
    internal static UsedMetrics? Metrics(DependencyObject target) => s_metrics.TryGetValue(target, out var value) ? value : null;
    internal static bool SetMetrics(DependencyObject target, UsedMetrics? value)
    {
        if (Metrics(target) == value) return false;
        s_metrics.Remove(target); if (value is not null) s_metrics.Add(target, value);
        return true;
    }
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssInitialLetter", typeof(CssInitialLetter), typeof(CssInitialLetterProperties), new PropertyMetadata(default(CssInitialLetter), Changed));
    internal static readonly DependencyProperty AlignProperty = DependencyProperty.RegisterAttached(
        "CssInitialLetterAlign", typeof(CssInitialLetterAlign), typeof(CssInitialLetterProperties),
        new PropertyMetadata(default(CssInitialLetterAlign), Changed, null, inherits: true));
    internal static readonly DependencyProperty WrapProperty = DependencyProperty.RegisterAttached(
        "CssInitialLetterWrap", typeof(CssInitialLetterWrap), typeof(CssInitialLetterProperties),
        new PropertyMetadata(default(CssInitialLetterWrap), Changed, null, inherits: true));

    internal static void Register()
    {
        CssPropertyRegistry.Register(new() { Name = "initial-letter", Kind = CssPropertyKind.Longhand, StorageProperty = ValueProperty,
            TransitionTargetDpName = ValueProperty.Name,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => Parse(ref reader) });
        CssPropertyRegistry.Register(new() { Name = "initial-letter-align", Kind = CssPropertyKind.Longhand, StorageProperty = AlignProperty,
            TransitionTargetDpName = AlignProperty.Name,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => ParseAlign(ref reader) });
        CssPropertyRegistry.Register(new() { Name = "initial-letter-wrap", Kind = CssPropertyKind.Longhand, StorageProperty = WrapProperty,
            TransitionTargetDpName = WrapProperty.Name,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) => ParseWrap(ref reader) });
    }

    private static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var keyword))
        {
            var word = keyword.ToString().ToLowerInvariant();
            if (word == "normal" && probe.AtEnd)
            { reader = probe; return new CssImmediateValue(ValueProperty, default(CssInitialLetter)); }
            if (word is not ("drop" or "raise") || !Number(ref probe, out var first) || !probe.AtEnd) return null;
            reader = probe;
            return new CssImmediateValue(ValueProperty, new CssInitialLetter(first, word == "raise" ? 1 : Math.Floor(first)));
        }
        probe = reader;
        if (!Number(ref probe, out var size)) return null;
        var sink = Math.Floor(size);
        if (!probe.AtEnd)
        {
            var second = probe;
            if (second.TryReadIdent(out keyword))
            {
                var word = keyword.ToString().ToLowerInvariant();
                if (word is not ("drop" or "raise") || !second.AtEnd) return null;
                sink = word == "raise" ? 1 : Math.Floor(size); probe = second;
            }
            else
            {
                second = probe; var before = second.Remaining;
                if (!Number(ref second, out sink) || !second.AtEnd) return null;
                if (second.NumberWasCalculated) sink = Math.Floor(sink + .5);
                else
                {
                    var token = before[..(before.Length - second.Remaining.Length)];
                    if (token.Contains('.') || token.Contains('e') || token.Contains('E') || sink != Math.Truncate(sink)) return null;
                }
                probe = second;
            }
        }
        reader = probe;
        return new CssImmediateValue(ValueProperty, new CssInitialLetter(size, sink));
    }

    private static bool Number(ref CssTokenReader reader, out double value)
    {
        if (!reader.TryReadNumber(out value, out var unit) || unit != CssUnit.None || !double.IsFinite(value)) return false;
        if (value < 1 && !reader.NumberWasCalculated) return false;
        value = Math.Max(1, value); return true;
    }

    private static CssCompiledValue? ParseAlign(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var word)) return null;
        var border = word.Equals("border-box", StringComparison.OrdinalIgnoreCase);
        var kind = CssInitialLetterAlignment.Alphabetic;
        if (!border || !reader.AtEnd)
        {
            if (border && !reader.TryReadIdent(out word)) return null;
            CssInitialLetterAlignment? alignment = word.ToString().ToLowerInvariant() switch
            {
                "alphabetic" => CssInitialLetterAlignment.Alphabetic, "ideographic" => CssInitialLetterAlignment.Ideographic,
                "hanging" => CssInitialLetterAlignment.Hanging, "leading" => CssInitialLetterAlignment.Leading, _ => null,
            };
            if (alignment is null) return null;
            kind = alignment.Value;
        }
        return reader.AtEnd ? new CssImmediateValue(AlignProperty, new CssInitialLetterAlign(kind, border)) : null;
    }

    private static CssCompiledValue? ParseWrap(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var word) && probe.AtEnd)
        {
            CssInitialLetterWrapKind? kind = word.ToString().ToLowerInvariant() switch
            {
                "none" => CssInitialLetterWrapKind.None, "first" => CssInitialLetterWrapKind.First,
                "all" => CssInitialLetterWrapKind.All, "grid" => CssInitialLetterWrapKind.Grid, _ => null,
            };
            if (kind is not { } value) return null;
            reader = probe; return new CssImmediateValue(WrapProperty, new CssInitialLetterWrap(value));
        }
        return reader.TryReadLength(out var length) && reader.AtEnd ? new WrapValue(length) : null;
    }

    private sealed class WrapValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var value = length.Expression is { } expression ? CssLayoutLength.Math(expression, context.Lengths)
                : length.Unit == CssUnit.Percent ? CssLayoutLength.Percent(length.Value / 100)
                : length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ? CssLayoutLength.Px(pixels) : default;
            sink.Set(WrapProperty, new CssInitialLetterWrap(CssInitialLetterWrapKind.Offset, value));
            return true;
        }
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is Documents.TextElement text) text.NotifyTextContentChanged();
        else if (target is FrameworkElement element)
        { element.InvalidateMeasure(); element.InvalidateVisual(); CssFlowProperties.InvalidateInheritedTextLayout(element); }
    }

    internal static AnimationTimeline? Animation(DependencyProperty property, object? from, object? to)
        => property == ValueProperty && from is CssInitialLetter { Active: true } a && to is CssInitialLetter { Active: true } b
            ? new CssInitialLetterAnimation { From = a, To = b }
            : property == WrapProperty && from is CssInitialLetterWrap { Kind: CssInitialLetterWrapKind.Offset } x &&
                to is CssInitialLetterWrap { Kind: CssInitialLetterWrapKind.Offset } y
                ? new CssInitialLetterWrapAnimation { From = x.Offset, To = y.Offset } : null;
}

internal sealed class CssInitialLetterWrapAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssInitialLetterWrapAnimation), new PropertyMetadata(null));
    internal CssLayoutLength From, To;
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => typeof(CssInitialLetterWrap);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        var value = CssTextSpacing.Interpolate(From, To, EasingFunction?.Ease(clock.CurrentProgress) ?? clock.CurrentProgress);
        return new CssInitialLetterWrap(CssInitialLetterWrapKind.Offset, value is double pixels ? CssLayoutLength.Px(pixels) : (CssLayoutLength)value);
    }
    protected override Freezable CreateInstanceCore() => new CssInitialLetterWrapAnimation { From = From, To = To };
}

internal sealed class CssInitialLetterAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssInitialLetterAnimation), new PropertyMetadata(null));
    internal CssInitialLetter From, To;
    internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public override Type TargetPropertyType => typeof(CssInitialLetter);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
        => From.Interpolate(To, EasingFunction?.Ease(clock.CurrentProgress) ?? clock.CurrentProgress);
    protected override Freezable CreateInstanceCore() => new CssInitialLetterAnimation { From = From, To = To };
}
