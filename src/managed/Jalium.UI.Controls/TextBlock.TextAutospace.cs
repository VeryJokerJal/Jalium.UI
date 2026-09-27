using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly Dictionary<int, AutospaceReplacement> _autospaceReplacementWidths = [];
    private readonly Dictionary<(DependencyObject Owner, char Space), double>
        _autospacePunctuationWidths = [];

    private readonly record struct AutospaceReplacement(
        int LeftStart, int RightStart, double Width, double UsedWidth = 0);

    private readonly record struct AutospaceSpacing(double Width, char Character = '\0');

    private bool IsEdgeAutospaceReplacement(int index, int start, int end)
        => _autospaceReplacementWidths.TryGetValue(index, out var replacement) &&
           (replacement.LeftStart < start || replacement.RightStart >= end);

    private double EdgeAutospaceReplacementWidth(int start, int end)
    {
        var width = 0.0;
        foreach (var (index, replacement) in _autospaceReplacementWidths)
            if (index >= start && index < end &&
                (replacement.LeftStart < start || replacement.RightStart >= end))
                width += replacement.UsedWidth;
        return width;
    }

    private void RebuildAutospaceReplacement()
    {
        _autospaceReplacementWidths.Clear();
        _autospacePunctuationWidths.Clear();
        if (_visualText.Length < 3 || !_visualText.Contains(' ')) return;
        var ranges = GetInlineTextRanges();
        var blockMode = (CssTextAutospace)GetValue(CssFlowProperties.TextAutospaceProperty)!;
        if ((blockMode & CssTextAutospace.Replace) == 0 && !ranges.Any(static range =>
                ((CssTextAutospace)range.Run.GetValue(
                    CssFlowProperties.TextAutospaceProperty)! & CssTextAutospace.Replace) != 0))
            return;

        char[]? visual = null;
        char[]? paint = null;
        for (var index = 1; index + 1 < _visualText.Length; index++)
        {
            if (_displayText[index] != ' ' || _visualText[index] != ' ' ||
                !GraphemeClusters.IsBoundary(_visualText, index) ||
                !GraphemeClusters.IsBoundary(_visualText, index + 1) ||
                PaintSlice(index, 1) != " ")
                continue;
            var previousEnd = index;
            var previousStart = GraphemeClusters.PreviousBoundary(_visualText, previousEnd);
            while (previousStart < previousEnd &&
                   PaintSlice(previousStart, previousEnd - previousStart).Length == 0)
            {
                previousEnd = previousStart;
                if (previousEnd == 0) { previousStart = -1; break; }
                previousStart = GraphemeClusters.PreviousBoundary(_visualText, previousEnd);
            }
            var nextStart = index + 1;
            while (nextStart < _visualText.Length)
            {
                var nextEnd = GraphemeClusters.NextBoundary(_visualText, nextStart);
                if (PaintSlice(nextStart, nextEnd - nextStart).Length > 0) break;
                nextStart = nextEnd;
            }
            if (previousStart < 0 || nextStart >= _visualText.Length) continue;
            var spacing = AutospaceAtBoundary(previousStart, nextStart,
                InlineRunAt(ranges, previousStart), InlineRunAt(ranges, nextStart),
                requireReplace: true);
            if (spacing.Width <= 0) continue;

            visual ??= _visualText.ToCharArray();
            paint ??= _paintText.ToCharArray();
            visual[index] = spacing.Character == '\0' ? '\u00A0' : spacing.Character;
            paint[_paintOffsets?[index] ?? index] = visual[index];
            _autospaceReplacementWidths[index] = new AutospaceReplacement(
                previousStart, nextStart, spacing.Width);
        }
        if (visual is null) return;
        _visualText = new string(visual);
        _paintText = new string(paint!);
        _hasPaintTextChanges = true;
    }

    private static Run? InlineRunAt(List<InlineTextRange> ranges, int position)
    {
        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var range = ranges[middle];
            if (position < range.Start) high = middle - 1;
            else if (position >= range.End) low = middle + 1;
            else return range.Run;
        }
        return null;
    }

    private bool HasAutospaceCandidate()
    {
        foreach (var rune in _visualText.EnumerateRunes())
            if (IsAutospaceIdeograph(rune) ||
                rune.Value is ';' or '!' or '?' or ':' or 0x00AB or 0x00BB)
                return true;
        return false;
    }

    private AutospaceSpacing AutospaceAtBoundary(int previousStart, int currentStart,
        Run? previousRun, Run? currentRun, bool requireReplace = false)
    {
        if (previousStart < 0 || currentStart <= previousStart ||
            !Rune.TryGetRuneAt(_visualText, previousStart, out var previous) ||
            !Rune.TryGetRuneAt(_visualText, currentStart, out var current))
            return default;

        if (_textTransformClassification?.TryGetValue(previousStart, out var previousPaint) == true)
            previous = previousPaint;
        if (_textTransformClassification?.TryGetValue(currentStart, out var currentPaint) == true)
            current = currentPaint;

        var owner = AutospaceOwner(previousRun, currentRun);
        var mode = (CssTextAutospace)owner.GetValue(CssFlowProperties.TextAutospaceProperty)!;
        if (requireReplace && (mode & CssTextAutospace.Replace) == 0) return default;

        var frenchSpace = FrenchPunctuationSpace(previous, current);
        if (frenchSpace != '\0' && (mode & CssTextAutospace.Punctuation) != 0 &&
            UsesFrenchPunctuation(owner))
            return new AutospaceSpacing(MeasureAutospacePunctuation(owner, frenchSpace),
                frenchSpace);

        var previousIdeograph = IsAutospaceIdeograph(previous);
        var currentIdeograph = IsAutospaceIdeograph(current);
        if (previousIdeograph == currentIdeograph) return default;
        var other = previousIdeograph ? current : previous;
        var category = Rune.GetUnicodeCategory(other);
        var numeric = category == UnicodeCategory.DecimalDigitNumber &&
            !CssUnicodeLineBreakData.IsEastAsianFullwidth(other);
        var alpha = (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) &&
            !IsEastAsianWideLetter(other);
        if (!alpha && !numeric) return default;
        if (numeric ? (mode & CssTextAutospace.IdeographNumeric) == 0
            : (mode & CssTextAutospace.IdeographAlpha) == 0)
            return default;
        var advance = CssTextDecorationPainter.GetSourceFontMetrics(owner, FontSize)
            .IdeographicAdvance;
        return new AutospaceSpacing(double.IsFinite(advance) && advance > 0
            ? advance / 8 : 0);
    }

    private static char FrenchPunctuationSpace(Rune previous, Rune current)
    {
        static bool IsTextNeighbor(Rune rune) => Rune.GetUnicodeCategory(rune) is not
            (UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or
             UnicodeCategory.ParagraphSeparator or UnicodeCategory.Control or
             UnicodeCategory.Format);
        if (!IsTextNeighbor(previous) || !IsTextNeighbor(current)) return '\0';
        if (current.Value is ';' or '!' or '?') return '\u202F';
        if (current.Value is ':' or 0x00BB || previous.Value == 0x00AB) return '\u00A0';
        return '\0';
    }

    private static bool UsesFrenchPunctuation(DependencyObject owner)
    {
        var tag = (owner.GetValue(FrameworkElement.LanguageProperty) as XmlLanguage)?
            .IetfLanguageTag;
        return tag is not null && (tag.Equals("fr", StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith("fr-", StringComparison.OrdinalIgnoreCase));
    }

    private double MeasureAutospacePunctuation(DependencyObject owner, char space)
    {
        var key = (owner, space);
        if (_autospacePunctuationWidths.TryGetValue(key, out var cached)) return cached;
        var type = owner.GetType();
        var size = CssDependencyPropertyLookup.Find(type, "FontSize") is { } sizeProperty &&
            owner.GetValue(sizeProperty) is double fontSize && double.IsFinite(fontSize) && fontSize > 0
                ? fontSize : FontSize;
        var family = CssDependencyPropertyLookup.Find(type, "FontFamily") is { } familyProperty
            ? owner.GetValue(familyProperty) as FontFamily : null;
        var weight = CssDependencyPropertyLookup.Find(type, "FontWeight") is { } weightProperty &&
            owner.GetValue(weightProperty) is FontWeight fontWeight
                ? fontWeight.ToOpenTypeWeight() : FontWeight.ToOpenTypeWeight();
        var style = CssDependencyPropertyLookup.Find(type, "FontStyle") is { } styleProperty &&
            owner.GetValue(styleProperty) is FontStyle fontStyle
                ? fontStyle.ToOpenTypeStyle() : FontStyle.ToOpenTypeStyle();
        var stretch = CssDependencyPropertyLookup.Find(type, "FontStretch") is { } stretchProperty &&
            owner.GetValue(stretchProperty) is FontStretch fontStretch
                ? fontStretch.ToOpenTypeStretch() : FontStretch.ToOpenTypeStretch();
        var formatted = new FormattedText(space.ToString(),
            family?.GetRenderingSource(owner) ?? FontFamily.GetRenderingSource(this), size)
        {
            FontWeight = weight, FontStyle = style, FontStretch = stretch,
        };
        if (TextMeasurement.MeasureText(formatted) && formatted.IsMeasured &&
            double.IsFinite(formatted.WidthIncludingTrailingWhitespace) &&
            formatted.WidthIncludingTrailingWhitespace > 0)
            return _autospacePunctuationWidths[key] =
                formatted.WidthIncludingTrailingWhitespace;
        return _autospacePunctuationWidths[key] = EstimateTextWidth(space.ToString(), size);
    }

    private DependencyObject AutospaceOwner(Run? previousRun, Run? currentRun)
    {
        if (previousRun is null || currentRun is null) return this;
        if (ReferenceEquals(previousRun, currentRun)) return previousRun;
        for (DependencyObject? ancestor = previousRun; ancestor is not null;
             ancestor = ancestor is FrameworkContentElement content ? content.Parent : null)
        {
            for (DependencyObject? other = currentRun; other is not null;
                 other = other is FrameworkContentElement content ? content.Parent : null)
                if (ReferenceEquals(ancestor, other)) return ancestor;
            if (ReferenceEquals(ancestor, this)) break;
        }
        return this;
    }

    private static bool IsAutospaceIdeograph(Rune rune)
    {
        var value = rune.Value;
        if (value is >= 0x3041 and <= 0x30FF &&
            Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation))
            return true;
        return value is >= 0x31C0 and <= 0x31FF ||
            CssUnicodeLineBreakData.IsHanExtendedScript(rune);
    }

    private static bool IsEastAsianWideLetter(Rune rune)
    {
        return IsAutospaceIdeograph(rune) ||
            CssUnicodeLineBreakData.IsEastAsianWideOrFullwidth(rune);
    }
}
