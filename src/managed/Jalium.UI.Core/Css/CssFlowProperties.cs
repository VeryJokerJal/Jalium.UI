using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssInlineAlignment { Baseline, Top, Bottom, TextTop, TextBottom, Offset, Middle, Sub, Super, Center }
internal readonly record struct CssInlineVerticalAlign(CssInlineAlignment Kind, CssLayoutLength Offset = default);
internal enum CssFlowTextAlignment { Start, End, Left, Right, Center, Justify, MatchParent }
internal enum CssFlowLastTextAlignment { Auto, Start, End, Left, Right, Center, Justify, MatchParent }
internal enum CssTextGroupAlignment { None, Start, End, Left, Right, Center }
internal enum CssTextJustification { Auto, None, InterWord, InterCharacter }
internal enum CssOverflowWrap { Normal, Anywhere, BreakWord }
internal enum CssWordBreak { Normal, KeepAll, BreakAll, BreakWord }
internal enum CssLineBreak { Auto, Loose, Normal, Strict, Anywhere }
[Flags]
internal enum CssTextAutospace
{
    None = 0,
    IdeographAlpha = 1,
    IdeographNumeric = 2,
    Punctuation = 4,
    Replace = 8,
    Normal = IdeographAlpha | IdeographNumeric,
}
[Flags]
internal enum CssHangingPunctuation
{
    None = 0,
    First = 1,
    ForceEnd = 2,
    AllowEnd = 4,
    Last = 8,
}
internal enum CssHyphens { None, Manual, Auto }
internal enum CssHyphenateLimitLast { None, Always, Column, Page, Spread }
internal readonly record struct CssHyphenateCharacter(string? Value);
internal readonly record struct CssHyphenateLimitLines(int? Maximum);
internal readonly record struct CssHyphenateLimitChars(int? Word, int? Before, int? After)
{
    internal int UsedWord => Word ?? 5;
    internal int UsedBefore => Before ?? 2;
    internal int UsedAfter => After ?? 2;
}
internal enum CssWhiteSpaceCollapse { Collapse, Discard, Preserve, PreserveBreaks, PreserveSpaces, BreakSpaces }
internal enum CssTextWrapMode { Wrap, NoWrap }
internal enum CssTextWrapStyle { Auto, Balance, Stable, Pretty, AvoidShortLastLine }
[Flags]
internal enum CssWordSpaceTransform { None = 0, Space = 1, IdeographicSpace = 2, AutoPhrase = 4 }
internal readonly record struct CssTabSize(double Value, bool IsLength)
{
    internal static CssTabSize Initial => new(8, false);
}
[Flags]
internal enum CssWhiteSpaceTrim { None = 0, DiscardBefore = 1, DiscardAfter = 2, DiscardInner = 4 }
[Flags]
internal enum CssTextTransformMode
{
    None = 0,
    Capitalize = 1,
    Uppercase = 2,
    Lowercase = 3,
    CaseMask = 3,
    FullWidth = 4,
    FullSizeKana = 8,
}
internal readonly record struct CssTextIndent(CssLayoutLength Length, bool Hanging, bool EachLine)
{
    internal double Resolve(double contentWidth)
        => Length.Resolve(contentWidth, 0);

    internal bool Applies(bool firstLine, bool afterForcedBreak)
        => (firstLine || EachLine && afterForcedBreak) != Hanging;
}

internal static class CssFlowProperties
{
    internal static readonly DependencyProperty VerticalAlignProperty = DependencyProperty.RegisterAttached(
        "CssVerticalAlign", typeof(CssInlineVerticalAlign), typeof(CssFlowProperties), new PropertyMetadata(default(CssInlineVerticalAlign), Changed));
    internal static readonly DependencyProperty TextAlignProperty = DependencyProperty.RegisterAttached(
        "CssTextAlign", typeof(CssFlowTextAlignment), typeof(CssFlowProperties),
        new PropertyMetadata(CssFlowTextAlignment.Start, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextAlignLastProperty = DependencyProperty.RegisterAttached(
        "CssTextAlignLast", typeof(CssFlowLastTextAlignment), typeof(CssFlowProperties),
        new PropertyMetadata(CssFlowLastTextAlignment.Auto, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextGroupAlignProperty = DependencyProperty.RegisterAttached(
        "CssTextGroupAlign", typeof(CssTextGroupAlignment), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextGroupAlignment.None, Changed));
    internal static readonly DependencyProperty LinePaddingProperty = DependencyProperty.RegisterAttached(
        "CssLinePadding", typeof(double), typeof(CssFlowProperties),
        new PropertyMetadata(0.0, Changed, null, inherits: true));

    internal static readonly DependencyProperty TextJustifyProperty = DependencyProperty.RegisterAttached(
        "CssTextJustify", typeof(CssTextJustification), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextJustification.Auto, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextIndentProperty = DependencyProperty.RegisterAttached(
        "CssTextIndent", typeof(CssTextIndent), typeof(CssFlowProperties),
        new PropertyMetadata(default(CssTextIndent), Changed, null, inherits: true));
    internal static readonly DependencyProperty OverflowWrapProperty = DependencyProperty.RegisterAttached(
        "CssOverflowWrap", typeof(CssOverflowWrap), typeof(CssFlowProperties),
        new PropertyMetadata(CssOverflowWrap.Normal, Changed, null, inherits: true));
    internal static readonly DependencyProperty WordBreakProperty = DependencyProperty.RegisterAttached(
        "CssWordBreak", typeof(CssWordBreak), typeof(CssFlowProperties),
        new PropertyMetadata(CssWordBreak.Normal, Changed, null, inherits: true));
    internal static readonly DependencyProperty LineBreakProperty = DependencyProperty.RegisterAttached(
        "CssLineBreak", typeof(CssLineBreak), typeof(CssFlowProperties),
        new PropertyMetadata(CssLineBreak.Auto, Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphensProperty = DependencyProperty.RegisterAttached(
        "CssHyphens", typeof(CssHyphens), typeof(CssFlowProperties),
        new PropertyMetadata(CssHyphens.Manual, Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphenateCharacterProperty = DependencyProperty.RegisterAttached(
        "CssHyphenateCharacter", typeof(CssHyphenateCharacter), typeof(CssFlowProperties),
        new PropertyMetadata(default(CssHyphenateCharacter), Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphenateLimitLinesProperty = DependencyProperty.RegisterAttached(
        "CssHyphenateLimitLines", typeof(CssHyphenateLimitLines), typeof(CssFlowProperties),
        new PropertyMetadata(default(CssHyphenateLimitLines), Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphenateLimitLastProperty = DependencyProperty.RegisterAttached(
        "CssHyphenateLimitLast", typeof(CssHyphenateLimitLast), typeof(CssFlowProperties),
        new PropertyMetadata(CssHyphenateLimitLast.None, Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphenateLimitZoneProperty = DependencyProperty.RegisterAttached(
        "CssHyphenateLimitZone", typeof(CssLayoutLength), typeof(CssFlowProperties),
        new PropertyMetadata(CssLayoutLength.Px(0), Changed, null, inherits: true));
    internal static readonly DependencyProperty HyphenateLimitCharsProperty = DependencyProperty.RegisterAttached(
        "CssHyphenateLimitChars", typeof(CssHyphenateLimitChars), typeof(CssFlowProperties),
        new PropertyMetadata(default(CssHyphenateLimitChars), Changed, null, inherits: true));
    internal static readonly DependencyProperty WordSpacingProperty = DependencyProperty.RegisterAttached(
        "CssWordSpacing", typeof(CssLayoutLength), typeof(CssFlowProperties),
        new PropertyMetadata(CssLayoutLength.Px(0), Changed, null, inherits: true));
    internal static readonly DependencyProperty LetterSpacingProperty = DependencyProperty.RegisterAttached(
        "CssLetterSpacing", typeof(double), typeof(CssFlowProperties),
        new PropertyMetadata(0.0, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextAutospaceProperty = DependencyProperty.RegisterAttached(
        "CssTextAutospace", typeof(CssTextAutospace), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextAutospace.Normal, Changed, null, inherits: true));
    internal static readonly DependencyProperty HangingPunctuationProperty = DependencyProperty.RegisterAttached(
        "CssHangingPunctuation", typeof(CssHangingPunctuation), typeof(CssFlowProperties),
        new PropertyMetadata(CssHangingPunctuation.None, Changed, null, inherits: true));
    internal static readonly DependencyProperty TabSizeProperty = DependencyProperty.RegisterAttached(
        "CssTabSize", typeof(CssTabSize), typeof(CssFlowProperties),
        new PropertyMetadata(CssTabSize.Initial, Changed, null, inherits: true));
    internal static readonly DependencyProperty WhiteSpaceProperty = DependencyProperty.RegisterAttached(
        "CssWhiteSpaceCollapse", typeof(CssWhiteSpaceCollapse), typeof(CssFlowProperties),
        new PropertyMetadata(CssWhiteSpaceCollapse.Collapse, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextWrapModeProperty = DependencyProperty.RegisterAttached(
        "CssTextWrapMode", typeof(CssTextWrapMode), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextWrapMode.Wrap, Changed, null, inherits: true));
    internal static readonly DependencyProperty TextWrapStyleProperty = DependencyProperty.RegisterAttached(
        "CssTextWrapStyle", typeof(CssTextWrapStyle), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextWrapStyle.Auto, Changed, null, inherits: true));
    internal static readonly DependencyProperty WhiteSpaceTrimProperty = DependencyProperty.RegisterAttached(
        "CssWhiteSpaceTrim", typeof(CssWhiteSpaceTrim), typeof(CssFlowProperties),
        new PropertyMetadata(CssWhiteSpaceTrim.None, Changed));
    internal static readonly DependencyProperty TextTransformProperty = DependencyProperty.RegisterAttached(
        "CssTextTransform", typeof(CssTextTransformMode), typeof(CssFlowProperties),
        new PropertyMetadata(CssTextTransformMode.None, Changed, null, inherits: true));
    internal static readonly DependencyProperty WordSpaceTransformProperty = DependencyProperty.RegisterAttached(
        "CssWordSpaceTransform", typeof(CssWordSpaceTransform), typeof(CssFlowProperties),
        new PropertyMetadata(CssWordSpaceTransform.None, Changed, null, inherits: true));

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (change.Property == WhiteSpaceProperty || change.Property == TextWrapModeProperty ||
            change.Property == TextWrapStyleProperty ||
            change.Property == WhiteSpaceTrimProperty)
        {
            InvalidateWhiteSpaceOrigin(target);
            return;
        }
        if (target is FrameworkContentElement content &&
            (change.Property == LinePaddingProperty ||
             change.Property == WordSpacingProperty || change.Property == LetterSpacingProperty ||
             change.Property == TextAutospaceProperty || change.Property == HangingPunctuationProperty ||
             change.Property == TextTransformProperty || change.Property == WordSpaceTransformProperty ||
             change.Property == TabSizeProperty ||
             change.Property == HyphensProperty || change.Property == HyphenateCharacterProperty ||
             change.Property == HyphenateLimitCharsProperty || change.Property == HyphenateLimitLinesProperty ||
             change.Property == HyphenateLimitLastProperty ||
             change.Property == HyphenateLimitZoneProperty))
        {
            for (DependencyObject? ancestor = content.Parent; ancestor is not null;
                 ancestor = ancestor is FrameworkContentElement contentParent ? contentParent.Parent : null)
            {
                if (ancestor is TextBlock owner)
                {
                    InvalidateInheritedTextLayout(owner);
                    break;
                }
            }
            return;
        }
        if (target is not UIElement element) return;
        if (change.Property == TextGroupAlignProperty || change.Property == LinePaddingProperty ||
            change.Property == TextIndentProperty ||
            change.Property == OverflowWrapProperty ||
            change.Property == WordBreakProperty || change.Property == LineBreakProperty ||
            change.Property == WordSpacingProperty || change.Property == LetterSpacingProperty ||
            change.Property == TextAutospaceProperty || change.Property == HangingPunctuationProperty ||
            change.Property == TextTransformProperty || change.Property == WordSpaceTransformProperty ||
            change.Property == TabSizeProperty ||
            change.Property == HyphensProperty || change.Property == HyphenateCharacterProperty ||
            change.Property == HyphenateLimitCharsProperty || change.Property == HyphenateLimitLinesProperty ||
            change.Property == HyphenateLimitLastProperty ||
            change.Property == HyphenateLimitZoneProperty)
            InvalidateInheritedTextLayout(element);
        else
        {
            element.InvalidateMeasure();
            element.InvalidateVisual();
        }
        if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
    }

    internal static void InvalidateWhiteSpaceOrigin(DependencyObject target)
    {
        if (target is FrameworkContentElement content)
        {
            for (DependencyObject? ancestor = content.Parent; ancestor is not null;
                 ancestor = ancestor is FrameworkContentElement contentParent ? contentParent.Parent : null)
            {
                if (ancestor is TextBlock owner)
                {
                    InvalidateInheritedTextLayout(owner);
                    break;
                }
            }
            return;
        }
        if (target is not UIElement element) return;
        InvalidateInheritedTextLayout(element);
        if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
    }

    private static void InvalidateInheritedTextLayout(Visual visual)
    {
        if (visual is TextBlock textBlock) textBlock.InvalidateCssTextLayout();
        if (visual is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateVisual();
        }
        for (var index = 0; index < visual.VisualChildrenCount; index++)
            if (visual.GetVisualChild(index) is { } child)
                InvalidateInheritedTextLayout(child);
    }

    internal static double GroupStartPadding(FrameworkElement element, double padding)
    {
        var mode = (CssTextGroupAlignment)element.GetValue(TextGroupAlignProperty)!;
        return GroupStartPadding(mode, element.FlowDirection == FlowDirection.RightToLeft, padding);
    }

    internal static double UsedLinePadding(DependencyObject element)
    {
        var value = (double)element.GetValue(LinePaddingProperty)!;
        return double.IsFinite(value) ? value : 0;
    }

    internal static double GroupStartPadding(CssTextGroupAlignment mode, bool rtl, double padding)
    {
        return mode switch
        {
            CssTextGroupAlignment.Center => padding / 2,
            CssTextGroupAlignment.Right => padding,
            CssTextGroupAlignment.End when !rtl => padding,
            CssTextGroupAlignment.Start when rtl => padding,
            _ => 0,
        };
    }

    internal static void Register() => CssPropertyRegistry.Register(new()
    {
        Name = "vertical-align", Kind = CssPropertyKind.Longhand, StorageProperty = VerticalAlignProperty,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            var probe = reader;
            if (probe.TryReadIdent(out var keyword) && probe.AtEnd)
            {
                CssInlineAlignment? alignment = keyword.ToString().ToLowerInvariant() switch
                {
                    "baseline" => CssInlineAlignment.Baseline, "top" => CssInlineAlignment.Top,
                    "bottom" => CssInlineAlignment.Bottom, "center" => CssInlineAlignment.Center,
                    "text-top" => CssInlineAlignment.TextTop,
                    "text-bottom" => CssInlineAlignment.TextBottom, "middle" => CssInlineAlignment.Middle,
                    "sub" => CssInlineAlignment.Sub, "super" => CssInlineAlignment.Super, _ => null,
                };
                return alignment is { } value ? new CssImmediateValue(VerticalAlignProperty, new CssInlineVerticalAlign(value)) : null;
            }
            return reader.TryReadLength(out var length) && reader.AtEnd ? new OffsetValue(length) : null;
        },
    });

    private sealed class OffsetValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var value = length.Expression is { } expression ? CssLayoutLength.Math(expression, context.Lengths)
                : length.Unit == CssUnit.Percent ? CssLayoutLength.Percent(length.Value / 100)
                : length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ? CssLayoutLength.Px(pixels) : default;
            sink.Set(VerticalAlignProperty, new CssInlineVerticalAlign(CssInlineAlignment.Offset, value));
            return true;
        }
    }

    internal static (double Height, double Baseline, double Ascent, double Descent) LineMetrics(FrameworkElement element)
    {
        var fontSize = (double)element.GetValue(TextElement.FontSizeProperty)!;
        var family = (FontFamily)element.GetValue(TextElement.FontFamilyProperty)!;
        var weight = (FontWeight)element.GetValue(TextElement.FontWeightProperty)!;
        var style = (FontStyle)element.GetValue(TextElement.FontStyleProperty)!;
        var metrics = TextMeasurement.GetFontMetrics(family.Source, fontSize, weight.ToOpenTypeWeight(), style.ToOpenTypeStyle());
        var lineHeight = (double)element.GetValue(TextBlock.LineHeightProperty)!;
        if (!double.IsFinite(lineHeight) || lineHeight < 0) lineHeight = Math.Max(metrics.LineHeight, metrics.Baseline + metrics.Descent);
        var natural = Math.Max(metrics.LineHeight, metrics.Baseline + metrics.Descent);
        return (lineHeight, metrics.Baseline + (lineHeight - natural) / 2, metrics.Ascent, metrics.Descent);
    }

    internal static double InlineShift(FrameworkElement parent, UIElement item,
        CssInlineVerticalAlign alignment, double baseline, double outerHeight,
        double parentHeight, double parentAscent, double parentDescent)
        => alignment.Kind switch
        {
            CssInlineAlignment.Offset => alignment.Offset.Resolve(item is FrameworkElement element
                ? LineMetrics(element).Height : parentHeight, 0),
            CssInlineAlignment.TextTop => parentAscent - baseline,
            CssInlineAlignment.TextBottom => outerHeight - parentDescent - baseline,
            // CSS 2.2 middle aligns the margin-box midpoint with the parent
            // baseline plus half its x-height. Positive shifts raise the box.
            CssInlineAlignment.Middle => outerHeight / 2 - baseline - ParentXHeight(parent) / 2,
            CssInlineAlignment.Sub => -ParentFontSize(parent) / 5,
            CssInlineAlignment.Super => ParentFontSize(parent) / 3,
            _ => 0,
        };

    private static double ParentFontSize(FrameworkElement parent)
        => (double)parent.GetValue(TextElement.FontSizeProperty)!;

    private static double ParentXHeight(FrameworkElement parent)
    {
        var fontSize = ParentFontSize(parent);
        var family = (FontFamily)parent.GetValue(TextElement.FontFamilyProperty)!;
        var weight = (FontWeight)parent.GetValue(TextElement.FontWeightProperty)!;
        var style = (FontStyle)parent.GetValue(TextElement.FontStyleProperty)!;
        var xHeight = TextMeasurement.GetFontUnitMetrics(family.Source, fontSize,
            weight.ToOpenTypeWeight(), style.ToOpenTypeStyle()).XHeight;
        return float.IsFinite(xHeight) && xHeight > 0 ? xHeight : fontSize * 0.5;
    }

    internal static CssFlowTextAlignment TextAlignment(FrameworkElement element,
        DependencyProperty? nativeProperty = null)
    {
        var native = nativeProperty ?? TextBlock.TextAlignmentProperty;
        var cssSource = element.GetValueSourceInternal(TextAlignProperty).BaseValueSource;
        if (element.HasLocalOrAnimatedValue(native) || cssSource == BaseValueSource.Default &&
            element.GetValueSourceInternal(native).BaseValueSource != BaseValueSource.Default)
            return (TextAlignment)element.GetValue(native)! switch
            {
                Jalium.UI.TextAlignment.Right => CssFlowTextAlignment.Right,
                Jalium.UI.TextAlignment.Center => CssFlowTextAlignment.Center,
                Jalium.UI.TextAlignment.Justify => CssFlowTextAlignment.Justify, _ => CssFlowTextAlignment.Left,
            };
        return (CssFlowTextAlignment)element.GetValue(TextAlignProperty)!;
    }

    internal static TextAlignment NativeTextAlignment(FrameworkElement element,
        DependencyProperty nativeProperty)
        => NativeLineTextAlignment(element, nativeProperty, false);

    internal static CssFlowTextAlignment LineAlignment(FrameworkElement element, bool isLast,
        DependencyProperty? nativeProperty = null)
    {
        var native = nativeProperty ?? TextBlock.TextAlignmentProperty;
        var all = TextAlignment(element, native);
        if (!isLast || element.HasLocalOrAnimatedValue(native)) return all;
        return (CssFlowLastTextAlignment)element.GetValue(TextAlignLastProperty)! switch
        {
            CssFlowLastTextAlignment.Auto when all == CssFlowTextAlignment.Justify => CssFlowTextAlignment.Start,
            CssFlowLastTextAlignment.Auto => all,
            CssFlowLastTextAlignment.Start => CssFlowTextAlignment.Start,
            CssFlowLastTextAlignment.End => CssFlowTextAlignment.End,
            CssFlowLastTextAlignment.Left => CssFlowTextAlignment.Left,
            CssFlowLastTextAlignment.Right => CssFlowTextAlignment.Right,
            CssFlowLastTextAlignment.Center => CssFlowTextAlignment.Center,
            CssFlowLastTextAlignment.Justify => CssFlowTextAlignment.Justify,
            _ => all,
        };
    }

    internal static TextAlignment NativeLineTextAlignment(FrameworkElement element,
        DependencyProperty nativeProperty, bool isLast)
    {
        if (element.GetValueSourceInternal(TextAlignProperty).BaseValueSource == BaseValueSource.Default &&
            element.GetValueSourceInternal(TextAlignLastProperty).BaseValueSource == BaseValueSource.Default)
            return (TextAlignment)element.GetValue(nativeProperty)!;
        return ToNativeTextAlignment(LineAlignment(element, isLast, nativeProperty),
            element.FlowDirection == FlowDirection.RightToLeft);
    }

    internal static bool JustifiesLastLine(FrameworkElement element, DependencyProperty nativeProperty)
        => !element.HasLocalOrAnimatedValue(nativeProperty) &&
           (CssFlowLastTextAlignment)element.GetValue(TextAlignLastProperty)! == CssFlowLastTextAlignment.Justify;

    internal static CssFlowTextAlignment MatchParentAlignment(CssNode element)
    {
        var parent = CssMatcher.CssAncestor(element);
        if (parent is null) return CssFlowTextAlignment.Start;
        var inherited = parent.Target is FrameworkElement frameworkParent
            ? TextAlignment(frameworkParent)
            : parent.GetValue(TextAlignProperty) is CssFlowTextAlignment value
                ? value : CssFlowTextAlignment.Start;
        var rtl = parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft;
        return inherited switch
        {
            CssFlowTextAlignment.Start => rtl ? CssFlowTextAlignment.Right : CssFlowTextAlignment.Left,
            CssFlowTextAlignment.End => rtl ? CssFlowTextAlignment.Left : CssFlowTextAlignment.Right,
            _ => inherited,
        };
    }

    internal static CssFlowLastTextAlignment MatchParentLastAlignment(CssNode element)
    {
        var parent = CssMatcher.CssAncestor(element);
        if (parent is null) return CssFlowLastTextAlignment.Start;
        var inherited = parent.GetValue(TextAlignLastProperty) is CssFlowLastTextAlignment value
            ? value : CssFlowLastTextAlignment.Auto;
        var rtl = parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft;
        return inherited switch
        {
            CssFlowLastTextAlignment.Start => rtl ? CssFlowLastTextAlignment.Right : CssFlowLastTextAlignment.Left,
            CssFlowLastTextAlignment.End => rtl ? CssFlowLastTextAlignment.Left : CssFlowLastTextAlignment.Right,
            _ => inherited,
        };
    }

    internal static TextAlignment ToNativeTextAlignment(CssFlowTextAlignment value, bool rightToLeft)
        => value switch
        {
            CssFlowTextAlignment.Start => rightToLeft ? Jalium.UI.TextAlignment.Right : Jalium.UI.TextAlignment.Left,
            CssFlowTextAlignment.End => rightToLeft ? Jalium.UI.TextAlignment.Left : Jalium.UI.TextAlignment.Right,
            CssFlowTextAlignment.Right => Jalium.UI.TextAlignment.Right,
            CssFlowTextAlignment.Center => Jalium.UI.TextAlignment.Center,
            CssFlowTextAlignment.Justify => Jalium.UI.TextAlignment.Justify,
            _ => Jalium.UI.TextAlignment.Left,
        };
}

internal sealed class CssFlowTextAlignmentValue(CssFlowTextAlignment value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var resolved = value == CssFlowTextAlignment.MatchParent
            ? CssFlowProperties.MatchParentAlignment(context.Element) : value;
        sink.Set(CssFlowProperties.TextAlignProperty, resolved);
        var native = CssFlowProperties.ToNativeTextAlignment(resolved, context.Slots.LogicalRightToLeft);
        return new CssNamedValue("text-align", "TextAlignment", native).TryApply(context, sink);
    }
}

internal sealed class CssFlowLastTextAlignmentValue(CssFlowLastTextAlignment value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssFlowProperties.TextAlignLastProperty,
            value == CssFlowLastTextAlignment.MatchParent
                ? CssFlowProperties.MatchParentLastAlignment(context.Element) : value);
        return true;
    }
}
