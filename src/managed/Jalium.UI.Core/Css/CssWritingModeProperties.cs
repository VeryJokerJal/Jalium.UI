using Jalium.UI.Documents;
using System.Globalization;
using System.Text;

namespace Jalium.UI.Styling;

/// <summary>CSS Writing Modes Level 4 <c>writing-mode</c> values.</summary>
public enum CssWritingMode : byte
{
    HorizontalTb = 0,
    VerticalRl = 1,
    VerticalLr = 2,
    SidewaysRl = 3,
    SidewaysLr = 4,
}

/// <summary>CSS Writing Modes Level 4 <c>text-orientation</c> values.</summary>
public enum CssTextOrientation : byte
{
    Mixed = 0,
    Upright = 1,
    Sideways = 2,
}

/// <summary>CSS Writing Modes Level 4 <c>text-combine-upright</c> mode.</summary>
public enum CssTextCombineUprightKind : byte
{
    None = 0,
    All = 1,
    Digits = 2,
}

/// <summary>Represents a parsed <c>text-combine-upright</c> value (tate-chu-yoko).</summary>
public readonly record struct CssTextCombineUpright(CssTextCombineUprightKind Kind, int MaxDigits = 2)
{
    public static readonly CssTextCombineUpright None = new(CssTextCombineUprightKind.None, 0);
    public static readonly CssTextCombineUpright All = new(CssTextCombineUprightKind.All, 0);

    public bool IsEnabled => Kind != CssTextCombineUprightKind.None;

    public override string ToString() => Kind switch
    {
        CssTextCombineUprightKind.All => "all",
        CssTextCombineUprightKind.Digits => MaxDigits == 2 ? "digits" : $"digits {MaxDigits}",
        _ => "none",
    };
}

/// <summary>CSS Ruby Layout Level 1 <c>ruby-position</c> values.</summary>
public enum CssRubyPosition : byte
{
    Over = 0,
    Under = 1,
    Alternate = 2,
    InterCharacter = 3,
    AlternateUnder = 4,
}

/// <summary>CSS Ruby Layout Level 1 <c>ruby-align</c> values.</summary>
public enum CssRubyAlign : byte
{
    SpaceAround = 0,
    Start = 1,
    Center = 2,
    SpaceBetween = 3,
}

/// <summary>CSS Ruby Layout Level 1 <c>ruby-merge</c> values.</summary>
public enum CssRubyMerge : byte
{
    Separate = 0,
    Merge = 1,
    // Retain the original C# enum value for binary/source compatibility.
    Collapse = Merge,
    Auto = 2,
}

/// <summary>Segment produced by tate-chu-yoko (<c>text-combine-upright</c>) analysis.</summary>
public readonly record struct CssTateChuYokoSegment(
    string Text,
    int StartIndex,
    int Length,
    bool IsCombined,
    double HorizontalCompressionScale);

/// <summary>Input pair of ruby base text and ruby annotation text (<c>&lt;rb&gt;</c> / <c>&lt;rt&gt;</c>).</summary>
public readonly record struct CssRubyAnnotationPair(string BaseText, string AnnotationText);

/// <summary>Computed layout geometry for a ruby base and its annotation run.</summary>
public readonly record struct CssRubyPlacement(
    string BaseText,
    string AnnotationText,
    Rect BaseRect,
    Rect AnnotationRect,
    CssRubyPosition EffectivePosition,
    bool IsMerged);

/// <summary>
/// CSS Writing Modes Level 4 (<c>writing-mode</c>, <c>text-orientation</c>, <c>text-combine-upright</c>)
/// and CSS Ruby Layout Level 1 (<c>ruby-position</c>, <c>ruby-align</c>, <c>ruby-merge</c>) properties
/// and layout geometry helpers.
/// </summary>
public static class CssWritingModeProperties
{
    public static readonly DependencyProperty WritingModeProperty = DependencyProperty.RegisterAttached(
        "WritingMode",
        typeof(CssWritingMode),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssWritingMode.HorizontalTb, OnWritingModeChanged, CssRubyWritingMode.Coerce, inherits: true));

    public static readonly DependencyProperty TextOrientationProperty = DependencyProperty.RegisterAttached(
        "TextOrientation",
        typeof(CssTextOrientation),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssTextOrientation.Mixed, OnWritingModeChanged, null, inherits: true));

    public static readonly DependencyProperty TextCombineUprightProperty = DependencyProperty.RegisterAttached(
        "TextCombineUpright",
        typeof(CssTextCombineUpright),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssTextCombineUpright.None, OnWritingModeChanged, null, inherits: true));

    public static readonly DependencyProperty RubyPositionProperty = DependencyProperty.RegisterAttached(
        "RubyPosition",
        typeof(CssRubyPosition),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssRubyPosition.Alternate, OnWritingModeChanged, null, inherits: true));

    public static readonly DependencyProperty RubyAlignProperty = DependencyProperty.RegisterAttached(
        "RubyAlign",
        typeof(CssRubyAlign),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssRubyAlign.SpaceAround, OnWritingModeChanged, null, inherits: true));

    public static readonly DependencyProperty RubyMergeProperty = DependencyProperty.RegisterAttached(
        "RubyMerge",
        typeof(CssRubyMerge),
        typeof(CssWritingModeProperties),
        new PropertyMetadata(CssRubyMerge.Separate, OnWritingModeChanged, null, inherits: true));

    private static void OnWritingModeChanged(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (change.Property == RubyPositionProperty) CssRubyWritingMode.ObservePosition(change.NewValue);
        if (target is FlowDocument document)
        {
            document.NotifyPaginationStyleChanged();
        }
        else if (target is TextElement textElement)
        {
            textElement.NotifyTextContentChanged();
        }
        else if (target is UIElement element)
        {
            // Inherited values are read lazily. Descendant text can otherwise
            // keep a shaped paragraph and retained drawing from the prior mode.
            CssFlowProperties.InvalidateInheritedTextLayout(element);
            element.InvalidateArrange();
        }
    }

    public static CssWritingMode GetWritingMode(DependencyObject element)
        => element.GetValue(WritingModeProperty) is CssWritingMode mode ? mode : CssWritingMode.HorizontalTb;

    public static void SetWritingMode(DependencyObject element, CssWritingMode value)
        => element.SetValue(WritingModeProperty, value);

    public static CssTextOrientation GetTextOrientation(DependencyObject element)
        => element.GetValue(TextOrientationProperty) is CssTextOrientation orientation ? orientation : CssTextOrientation.Mixed;

    public static void SetTextOrientation(DependencyObject element, CssTextOrientation value)
        => element.SetValue(TextOrientationProperty, value);

    public static CssTextCombineUpright GetTextCombineUpright(DependencyObject element)
        => element.GetValue(TextCombineUprightProperty) is CssTextCombineUpright combine ? combine : CssTextCombineUpright.None;

    public static void SetTextCombineUpright(DependencyObject element, CssTextCombineUpright value)
        => element.SetValue(TextCombineUprightProperty, value);

    public static CssRubyPosition GetRubyPosition(DependencyObject element)
        => element.GetValue(RubyPositionProperty) is CssRubyPosition position ? position : CssRubyPosition.Alternate;

    public static void SetRubyPosition(DependencyObject element, CssRubyPosition value)
        => element.SetValue(RubyPositionProperty, value);

    public static CssRubyAlign GetRubyAlign(DependencyObject element)
        => element.GetValue(RubyAlignProperty) is CssRubyAlign align ? align : CssRubyAlign.SpaceAround;

    public static void SetRubyAlign(DependencyObject element, CssRubyAlign value)
        => element.SetValue(RubyAlignProperty, value);

    public static CssRubyMerge GetRubyMerge(DependencyObject element)
        => element.GetValue(RubyMergeProperty) is CssRubyMerge merge ? merge : CssRubyMerge.Separate;

    public static void SetRubyMerge(DependencyObject element, CssRubyMerge value)
        => element.SetValue(RubyMergeProperty, value);

    /// <summary>Resolves the used text direction without replacing the inherited computed value.</summary>
    public static FlowDirection GetUsedFlowDirection(DependencyObject element)
        => GetWritingMode(element) is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
           GetTextOrientation(element) == CssTextOrientation.Upright
            ? FlowDirection.LeftToRight
            : element.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection direction
                ? direction : FlowDirection.LeftToRight;

    /// <summary>Returns true when the inline progression axis is vertical.</summary>
    public static bool IsVertical(CssWritingMode mode)
        => mode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr or CssWritingMode.SidewaysRl or CssWritingMode.SidewaysLr;

    /// <summary>Returns true when block flow progresses from right to left (<c>vertical-rl</c> or <c>sideways-rl</c>).</summary>
    public static bool IsBlockRightToLeft(CssWritingMode mode)
        => mode is CssWritingMode.VerticalRl or CssWritingMode.SidewaysRl;

    /// <summary>Returns true when inline progression Along the vertical axis runs bottom-to-top.</summary>
    public static bool IsInlineBottomToTop(CssWritingMode mode, bool rightToLeft = false)
        => mode switch
        {
            CssWritingMode.SidewaysLr => !rightToLeft,
            CssWritingMode.VerticalRl or CssWritingMode.VerticalLr or CssWritingMode.SidewaysRl => rightToLeft,
            _ => false,
        };

    /// <summary>Converts a physical <see cref="Size"/> into <c>(InlineSize, BlockSize)</c> for the given <paramref name="mode"/>.</summary>
    public static (double InlineSize, double BlockSize) ToLogicalSize(Size physicalSize, CssWritingMode mode)
        => IsVertical(mode)
            ? (physicalSize.Height, physicalSize.Width)
            : (physicalSize.Width, physicalSize.Height);

    /// <summary>Converts logical <c>(inlineSize, blockSize)</c> into a physical <see cref="Size"/> for the given <paramref name="mode"/>.</summary>
    public static Size ToPhysicalSize(double inlineSize, double blockSize, CssWritingMode mode)
        => IsVertical(mode)
            ? new Size(blockSize, inlineSize)
            : new Size(inlineSize, blockSize);

    /// <summary>
    /// Converts logical offsets and dimensions into a physical <see cref="Rect"/> inside <paramref name="containerPhysicalSize"/>.
    /// </summary>
    public static Rect ToPhysicalRect(
        double inlineOffset,
        double blockOffset,
        double inlineSize,
        double blockSize,
        Size containerPhysicalSize,
        CssWritingMode mode,
        bool rightToLeft = false)
    {
        var safeInline = Math.Max(0, inlineSize);
        var safeBlock = Math.Max(0, blockSize);
        if (!IsVertical(mode))
        {
            var x = rightToLeft
                ? Math.Max(0, containerPhysicalSize.Width - inlineOffset - safeInline)
                : inlineOffset;
            return new Rect(x, blockOffset, safeInline, safeBlock);
        }

        var physicalX = IsBlockRightToLeft(mode)
            ? Math.Max(0, containerPhysicalSize.Width - blockOffset - safeBlock)
            : blockOffset;
        var physicalY = IsInlineBottomToTop(mode, rightToLeft)
            ? Math.Max(0, containerPhysicalSize.Height - inlineOffset - safeInline)
            : inlineOffset;
        return new Rect(physicalX, physicalY, safeBlock, safeInline);
    }

    /// <summary>
    /// Returns the clockwise glyph rotation in degrees for <paramref name="codePoint"/> under <paramref name="mode"/> and <paramref name="orientation"/>.
    /// </summary>
    public static double GetGlyphRotationDegrees(int codePoint, CssWritingMode mode, CssTextOrientation orientation)
    {
        if (!IsVertical(mode))
        {
            return 0d;
        }

        if (mode == CssWritingMode.SidewaysLr)
        {
            return -90d;
        }

        if (mode == CssWritingMode.SidewaysRl)
        {
            return 90d;
        }

        return orientation switch
        {
            CssTextOrientation.Upright => 0d,
            CssTextOrientation.Sideways => 90d,
            _ => IsNaturallyUprightInVerticalMixed(codePoint) ? 0d : 90d,
        };
    }

    public static bool IsNaturallyUprightInVerticalMixed(int codePoint)
        => CssUnicodeVerticalOrientationData.Property(codePoint) != 0;

    /// <summary>Returns the mixed orientation of one grapheme cluster (UAX #50).</summary>
    public static bool IsNaturallyUprightInVerticalMixed(ReadOnlySpan<char> grapheme)
    {
        bool upright = false, first = true;
        foreach (var scalar in grapheme.EnumerateRunes())
        {
            if (first) upright = IsNaturallyUprightInVerticalMixed(scalar.Value);
            if (CssUnicodeVerticalOrientationData.Enclosing(scalar.Value)) return true;
            first = false;
        }
        return upright;
    }

    /// <summary>
    /// Segments <paramref name="text"/> into vertical runs and horizontal-in-vertical (tate-chu-yoko) combined runs.
    /// The compression scale is a legacy font-independent hint. Actual layout
    /// must measure shaped glyphs and available width variants, as TextBlock does.
    /// </summary>
    public static IReadOnlyList<CssTateChuYokoSegment> SegmentTateChuYoko(
        string text,
        CssWritingMode mode,
        CssTextCombineUpright combine)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (!IsVertical(mode) || mode is CssWritingMode.SidewaysRl or CssWritingMode.SidewaysLr || !combine.IsEnabled)
        {
            return [new CssTateChuYokoSegment(text, 0, text.Length, false, 1.0)];
        }

        if (combine.Kind == CssTextCombineUprightKind.All)
        {
            var hintText = string.Concat(text.Where(static ch => ch is not ('\r' or '\n' or '\u2028' or '\u2029'))).Trim();
            var scale = Math.Min(1.0, 1.0 / Math.Max(1, hintText.Length * 0.55));
            // A whitespace-only composition still occupies a 1em square,
            // including when its horizontal whitespace is entirely trimmed.
            return [new CssTateChuYokoSegment(text, 0, text.Length, true, scale)];
        }

        var maxDigits = Math.Clamp(combine.MaxDigits, 2, 4);
        var segments = new List<CssTateChuYokoSegment>();
        var index = 0;
        var normalStart = 0;
        var boundaries = StringInfo.ParseCombiningCharacters(text);

        while (index < text.Length)
        {
            if (IsAsciiDigit(text[index]))
            {
                var runStart = index;
                while (index < text.Length && IsAsciiDigit(text[index]))
                {
                    index++;
                }

                var digitCount = index - runStart;
                if (digitCount >= 1 && digitCount <= maxDigits && Array.BinarySearch(boundaries, runStart) >= 0 &&
                    (index == text.Length || Array.BinarySearch(boundaries, index) >= 0))
                {
                    if (runStart > normalStart)
                    {
                        segments.Add(new CssTateChuYokoSegment(
                            text[normalStart..runStart],
                            normalStart,
                            runStart - normalStart,
                            false,
                            1.0));
                    }

                    var scale = digitCount <= 1 ? 1.0 : Math.Min(1.0, 1.0 / (digitCount * 0.52));
                    segments.Add(new CssTateChuYokoSegment(
                        text[runStart..index],
                        runStart,
                        digitCount,
                        true,
                        scale));
                    normalStart = index;
                }
            }
            else
            {
                index++;
            }
        }

        if (normalStart < text.Length)
        {
            segments.Add(new CssTateChuYokoSegment(
                text[normalStart..],
                normalStart,
                text.Length - normalStart,
                false,
                1.0));
        }

        return segments;
    }

    private static bool IsAsciiDigit(char ch) => ch is >= '0' and <= '9';

    /// <summary>
    /// Computes base and ruby annotation rectangles according to <c>writing-mode</c>,
    /// <c>ruby-position</c>, <c>ruby-align</c>, and <c>ruby-merge</c>.
    /// </summary>
    public static IReadOnlyList<CssRubyPlacement> ComputeRubyPlacements(
        IReadOnlyList<CssRubyAnnotationPair> pairs,
        double baseEmSize,
        CssWritingMode writingMode,
        CssRubyPosition rubyPosition,
        CssRubyAlign rubyAlign,
        CssRubyMerge rubyMerge,
        double rubyScale = 0.5)
    {
        if (pairs.Count == 0)
        {
            return [];
        }

        var effectiveBaseEm = Math.Max(1.0, baseEmSize);
        var effectiveRubyEm = Math.Max(1.0, effectiveBaseEm * Math.Clamp(rubyScale, 0.25, 1.0));
        var isVertical = IsVertical(writingMode);

        // This helper retains its approximate em-based API. Real layout uses
        // measured annotations and re-evaluates merging for each broken line.
        var shouldMerge = rubyMerge == CssRubyMerge.Merge ||
            (rubyMerge == CssRubyMerge.Auto && pairs.Count > 1 && ShouldAutoMergeRuby(pairs, effectiveBaseEm, effectiveRubyEm));

        var effectivePairs = shouldMerge && pairs.Count > 1
            ? [new CssRubyAnnotationPair(
                string.Concat(pairs.Select(static p => p.BaseText)),
                string.Concat(pairs.Select(static p => p.AnnotationText)))]
            : pairs;

        var placements = new List<CssRubyPlacement>(effectivePairs.Count);
        var inlineCursor = 0.0;

        for (var i = 0; i < effectivePairs.Count; i++)
        {
            var pair = effectivePairs[i];
            var baseInlineSpan = Math.Max(1, pair.BaseText.Length) * effectiveBaseEm;
            var rtInlineSpan = Math.Max(1, pair.AnnotationText.Length) * effectiveRubyEm;
            var cellInlineSpan = Math.Max(baseInlineSpan, rtInlineSpan);

            var baseInlineStart = inlineCursor + AlignRunOffset(cellInlineSpan, baseInlineSpan, pair.BaseText.Length, rubyAlign);
            var rtInlineStart = inlineCursor + AlignRunOffset(cellInlineSpan, rtInlineSpan, pair.AnnotationText.Length, rubyAlign);

            var effectivePos = rubyPosition switch
            {
                CssRubyPosition.Alternate => i % 2 == 0 ? CssRubyPosition.Over : CssRubyPosition.Under,
                CssRubyPosition.AlternateUnder => i % 2 == 0 ? CssRubyPosition.Under : CssRubyPosition.Over,
                _ => rubyPosition,
            };

            Rect baseRect;
            Rect rtRect;

            if (!isVertical)
            {
                if (effectivePos == CssRubyPosition.InterCharacter)
                {
                    // Inter-character in horizontal acts as inline right column next to base
                    baseRect = new Rect(baseInlineStart, effectiveRubyEm, baseInlineSpan, effectiveBaseEm);
                    rtRect = new Rect(baseInlineStart + baseInlineSpan, effectiveRubyEm, rtInlineSpan, effectiveBaseEm);
                    cellInlineSpan = baseInlineSpan + rtInlineSpan;
                }
                else if (effectivePos == CssRubyPosition.Under)
                {
                    baseRect = new Rect(baseInlineStart, 0, baseInlineSpan, effectiveBaseEm);
                    rtRect = new Rect(rtInlineStart, effectiveBaseEm, rtInlineSpan, effectiveRubyEm);
                }
                else
                {
                    // Over (above the base text in horizontal-tb)
                    rtRect = new Rect(rtInlineStart, 0, rtInlineSpan, effectiveRubyEm);
                    baseRect = new Rect(baseInlineStart, effectiveRubyEm, baseInlineSpan, effectiveBaseEm);
                }
            }
            else
            {
                // In vertical writing modes, "over" is on the right side of the vertical base column,
                // and "under" is on the left side of the vertical base column.
                var overOnRight = true;
                var placeRight = effectivePos switch
                {
                    CssRubyPosition.Under => !overOnRight,
                    _ => overOnRight,
                };

                if (placeRight)
                {
                    baseRect = new Rect(0, baseInlineStart, effectiveBaseEm, baseInlineSpan);
                    rtRect = new Rect(effectiveBaseEm, rtInlineStart, effectiveRubyEm, rtInlineSpan);
                }
                else
                {
                    rtRect = new Rect(0, rtInlineStart, effectiveRubyEm, rtInlineSpan);
                    baseRect = new Rect(effectiveRubyEm, baseInlineStart, effectiveBaseEm, baseInlineSpan);
                }
            }

            placements.Add(new CssRubyPlacement(
                pair.BaseText,
                pair.AnnotationText,
                baseRect,
                rtRect,
                effectivePos,
                shouldMerge && pairs.Count > 1));

            inlineCursor += cellInlineSpan;
        }

        return placements;
    }

    private static bool ShouldAutoMergeRuby(
        IReadOnlyList<CssRubyAnnotationPair> pairs,
        double baseEm,
        double rubyEm)
    {
        var anyOverflowsIndividually = false;
        var totalBase = 0.0;
        var totalRuby = 0.0;
        for (var i = 0; i < pairs.Count; i++)
        {
            var b = Math.Max(1, pairs[i].BaseText.Length) * baseEm;
            var r = Math.Max(1, pairs[i].AnnotationText.Length) * rubyEm;
            if (r > b)
            {
                anyOverflowsIndividually = true;
            }

            totalBase += b;
            totalRuby += r;
        }

        return anyOverflowsIndividually && totalRuby <= totalBase * 1.25;
    }

    private static double AlignRunOffset(
        double cellInlineSpan,
        double runInlineSpan,
        int glyphCount,
        CssRubyAlign align)
    {
        var slack = Math.Max(0.0, cellInlineSpan - runInlineSpan);
        if (slack <= 0.0)
        {
            return 0.0;
        }

        return align switch
        {
            CssRubyAlign.Start => 0.0,
            CssRubyAlign.Center => slack * 0.5,
            CssRubyAlign.SpaceBetween => glyphCount <= 1 ? slack * 0.5 : 0.0,
            _ => slack / (Math.Max(1, glyphCount) * 2.0), // space-around leaves half-space at edges
        };
    }

    internal static void Register()
    {
        RegisterWritingMode();
        RegisterTextOrientation();
        RegisterTextCombineUpright();
        RegisterRubyPosition();
        RegisterRubyAlign();
        RegisterRubyMerge();
    }

    private static void RegisterWritingMode()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "writing-mode",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = WritingModeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                CssWritingMode? mode = ident.ToString().ToLowerInvariant() switch
                {
                    "horizontal-tb" or "lr" or "lr-tb" or "rl" or "rl-tb" => CssWritingMode.HorizontalTb,
                    "vertical-rl" or "tb" or "tb-rl" => CssWritingMode.VerticalRl,
                    "vertical-lr" or "tb-lr" => CssWritingMode.VerticalLr,
                    "sideways-rl" => CssWritingMode.SidewaysRl,
                    "sideways-lr" => CssWritingMode.SidewaysLr,
                    _ => null,
                };

                return mode is { } value ? new CssImmediateValue(WritingModeProperty, value) : null;
            },
        });

    private static void RegisterTextOrientation()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-orientation",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = TextOrientationProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                CssTextOrientation? orientation = ident.ToString().ToLowerInvariant() switch
                {
                    "mixed" => CssTextOrientation.Mixed,
                    "upright" => CssTextOrientation.Upright,
                    "sideways" or "sideways-right" => CssTextOrientation.Sideways,
                    _ => null,
                };

                return orientation is { } value ? new CssImmediateValue(TextOrientationProperty, value) : null;
            },
        });

    private static void RegisterTextCombineUpright()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "text-combine-upright",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = TextCombineUprightProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident))
                {
                    return null;
                }

                var keyword = ident.ToString().ToLowerInvariant();
                if (keyword == "none" && reader.AtEnd)
                {
                    return new CssImmediateValue(TextCombineUprightProperty, CssTextCombineUpright.None);
                }

                if (keyword == "all" && reader.AtEnd)
                {
                    return new CssImmediateValue(TextCombineUprightProperty, CssTextCombineUpright.All);
                }

                if (keyword == "digits")
                {
                    if (reader.AtEnd)
                    {
                        return new CssImmediateValue(
                            TextCombineUprightProperty,
                            new CssTextCombineUpright(CssTextCombineUprightKind.Digits, 2));
                    }

                    if (reader.TryReadInteger(out var digits) && reader.AtEnd && digits is >= 2 and <= 4)
                    {
                        return new CssImmediateValue(
                            TextCombineUprightProperty,
                            new CssTextCombineUpright(CssTextCombineUprightKind.Digits, digits));
                    }
                }

                return null;
            },
        });

    private static void RegisterRubyPosition()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "ruby-position",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = RubyPositionProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var first))
                {
                    return null;
                }

                var firstKeyword = first.ToString().ToLowerInvariant();
                if (reader.AtEnd)
                {
                    CssRubyPosition? single = firstKeyword switch
                    {
                        "over" => CssRubyPosition.Over,
                        "under" => CssRubyPosition.Under,
                        "alternate" => CssRubyPosition.Alternate,
                        "inter-character" => CssRubyPosition.InterCharacter,
                        _ => null,
                    };
                    return single is { } s ? new CssImmediateValue(RubyPositionProperty, s) : null;
                }

                if (!reader.TryReadIdent(out var second) || !reader.AtEnd)
                {
                    return null;
                }

                var secondKeyword = second.ToString().ToLowerInvariant();
                var isAlternatePair =
                    (firstKeyword == "alternate" && secondKeyword is "over" or "under") ||
                    (secondKeyword == "alternate" && firstKeyword is "over" or "under");

                return isAlternatePair
                    ? new CssImmediateValue(RubyPositionProperty,
                        firstKeyword == "under" || secondKeyword == "under" ? CssRubyPosition.AlternateUnder : CssRubyPosition.Alternate)
                    : null;
            },
        });

    private static void RegisterRubyAlign()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "ruby-align",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = RubyAlignProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                CssRubyAlign? align = ident.ToString().ToLowerInvariant() switch
                {
                    "space-around" => CssRubyAlign.SpaceAround,
                    "start" => CssRubyAlign.Start,
                    "center" => CssRubyAlign.Center,
                    "space-between" => CssRubyAlign.SpaceBetween,
                    _ => null,
                };

                return align is { } value ? new CssImmediateValue(RubyAlignProperty, value) : null;
            },
        });

    private static void RegisterRubyMerge()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "ruby-merge",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = RubyMergeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                CssRubyMerge? merge = ident.ToString().ToLowerInvariant() switch
                {
                    "separate" => CssRubyMerge.Separate,
                    "merge" => CssRubyMerge.Merge,
                    "auto" => CssRubyMerge.Auto,
                    _ => null,
                };

                return merge is { } value ? new CssImmediateValue(RubyMergeProperty, value) : null;
            },
        });
}
