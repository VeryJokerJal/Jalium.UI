using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _usesAutospaceShaping;
    private NaturalShapeCache? _autospaceNaturals;
    private readonly record struct AutospaceUnit(DependencyObject Owner, Rune Rune, double Word, bool Replacement);
    private sealed record VisualAutoGap(int Left, int Right, int Replacement, double Width, DependencyObject Owner);

    // Natural geometry is independent of spacing/paint. Keep it across repeated
    // caret and range queries; the final immutable line is cached by its advances.
    private sealed class NaturalShapeCache
    {
        private sealed record Entry(NativeTextParagraph.Span[] Spans, NativeMethods.ParagraphAdvanceUnit[] Units,
            string Family, double Size, double Height, FlowDirection Direction, NativeTextParagraph Paragraph);
        private readonly List<Entry> _entries = [];
        internal NativeTextParagraph? Get(NativeTextParagraph.Span[] spans, NativeMethods.ParagraphAdvanceUnit[] units,
            string family, double size, double height, FlowDirection direction, bool preserveAdvances = false)
        {
            var zero = preserveAdvances ? units.ToArray() : units.Select(unit => new NativeMethods.ParagraphAdvanceUnit
                { TextPosition = unit.TextPosition, Length = unit.Length, Flags = unit.Flags & 3 }).ToArray();
            var found = _entries.FirstOrDefault(entry => entry.Family == family && entry.Size == size &&
                entry.Height == height && entry.Direction == direction && entry.Spans.SequenceEqual(spans) && entry.Units.SequenceEqual(zero));
            if (found is not null) return found.Paragraph;
            var paragraph = NativeTextParagraph.TryCreateAdvanced(spans, family, size, Math.Max(1000000, size * spans.Sum(span => span.Text.Length) * 4),
                height, direction, zero, contextPaint: true);
            if (paragraph?.Lines.Length != 1) { paragraph?.Dispose(); return null; }
            if (_entries.Count >= 128) { _entries[0].Paragraph.Dispose(); _entries.RemoveAt(0); }
            _entries.Add(new(spans, zero, family, size, height, direction, paragraph)); return paragraph;
        }
        internal void Clear() { foreach (var entry in _entries) entry.Paragraph.Dispose(); _entries.Clear(); }
    }

    private static DependencyObject AutospaceStyleSource(DependencyObject value)
    {
        if (value is InlineSourceText projected)
            return projected.ProjectedStyleSource ?? InlineRunAt(projected.SourceOwner.GetInlineTextRanges(), projected.SourceOffset) ?? (DependencyObject)projected.SourceOwner;
        if (value is ICssAnonymousTextBox anonymous && !ReferenceEquals(value, anonymous.Owner))
            return AutospaceStyleSource(anonymous.Owner);
        return value is ICssStyledBox styled && !ReferenceEquals(value, styled.StyleSource)
            ? AutospaceStyleSource(styled.StyleSource) : value;
    }

    private static DependencyObject CommonAutospaceOwner(DependencyObject first, DependencyObject last, DependencyObject fallback)
    {
        first = AutospaceStyleSource(first); last = AutospaceStyleSource(last);
        for (var a = CssNode.Get(first); a is not null; a = a.FrameworkParent)
            for (var b = CssNode.Get(last); b is not null; b = b.FrameworkParent)
                if (ReferenceEquals(a.Target, b.Target)) return a.Target;
        return AutospaceStyleSource(fallback);
    }

    private static bool ContainsAutospaceOwner(DependencyObject element, DependencyObject owner)
    {
        element = AutospaceStyleSource(element);
        for (var node = CssNode.Get(AutospaceStyleSource(owner)); node is not null; node = node.FrameworkParent)
            if (ReferenceEquals(node.Target, element)) return true;
        return false;
    }

    private bool CouldShapeAutospace()
    {
        bool ideograph = false, alpha = false, numeric = false, punctuation = false;
        foreach (var rune in _visualText.EnumerateRunes())
        {
            ideograph |= IsAutospaceIdeograph(rune);
            punctuation |= rune.Value is ';' or '!' or '?' or ':' or 0x00ab or 0x00bb;
            var category = Rune.GetUnicodeCategory(rune);
            numeric |= category == UnicodeCategory.DecimalDigitNumber && !CssUnicodeLineBreakData.IsEastAsianFullwidth(rune);
            alpha |= !IsEastAsianWideLetter(rune) && category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
        }
        bool Enabled(DependencyObject owner)
        {
            var mode = (CssTextAutospace)owner.GetValue(CssFlowProperties.TextAutospaceProperty)!;
            return ideograph && (alpha && (mode & CssTextAutospace.IdeographAlpha) != 0 || numeric && (mode & CssTextAutospace.IdeographNumeric) != 0) ||
                punctuation && (mode & CssTextAutospace.Punctuation) != 0 && UsesFrenchPunctuation(owner);
        }
        var ranges = GetInlineTextRanges();
        if (!Enabled(this) && !ranges.Any(range => Enabled(range.Run))) return false;
        if (_autospaceReplacementWidths.Count > 0) return true;
        // Non-ASCII non-wide text and explicit directional controls may expose
        // different visual neighbors. Plain text without an eligible boundary
        // keeps its existing paint path (including document markers).
        if (_visualText.EnumerateRunes().Any(rune => rune.Value > 127 &&
            (!IsEastAsianWideLetter(rune) && Rune.IsLetterOrDigit(rune) ||
             rune.Value is 0x061c or 0x200e or 0x200f or >= 0x202a and <= 0x202e or >= 0x2066 and <= 0x2069))) return true;
        int previous = -1;
        var boundaries = GraphemeClusters.GetBoundaries(_visualText);
        foreach (int position in boundaries)
        {
            if (position >= _visualText.Length || !Rune.TryGetRuneAt(_visualText, position, out var rune) ||
                Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) continue;
            if (previous >= 0 && AutospaceAtBoundary(previous, position, InlineRunAt(ranges, previous), InlineRunAt(ranges, position)).Width > 0) return true;
            previous = position;
        }
        return false;
    }

    private string RestoreShapedAutospaceSpaces(string value, int start)
    {
        char[]? result = null;
        foreach (int position in _autospaceReplacementWidths.Keys)
            if (position >= start && position - start < value.Length && _displayText[position] == ' ')
            {
                result ??= value.ToCharArray(); result[position - start] = ' ';
            }
        // The compatibility formatter marks replacements as NBSP/NNBSP. The
        // retained path decides their use after bidi, from the original space.
        return result is null ? value : new string(result);
    }

    private AutospaceUnit AutospaceMetadata(int position, Rune rune)
    {
        var run = InlineRunAt(GetInlineTextRanges(), position);
        bool replacement = _autospaceReplacementWidths.ContainsKey(position);
        if (_textTransformClassification?.TryGetValue(position, out var classification) == true) rune = classification;
        DependencyObject owner = this is InlineSourceText projected
            ? InlineRunAt(projected.SourceOwner.GetInlineTextRanges(), projected.SourceOffset + position) ?? (DependencyObject)projected.SourceOwner
            : run ?? (DependencyObject)this;
        return new(owner, rune,
            IsWordSeparator(rune) || replacement ? ComputedWordSpacingAt(position) : 0, replacement);
    }

    private bool ApplyShapedAutospace(NativeTextParagraph natural, NativeMethods.ParagraphAdvanceUnit[] units,
        AutospaceUnit[] metadata, DependencyObject fallback, out VisualAutoGap[] gaps)
    {
        var ordered = new List<(int Index, double Left, double Right, bool Rtl)>();
        for (int i = 0; i < units.Length; i++)
        {
            if ((units[i].Flags & 1) != 0) continue;
            double first = natural.Caret(0, (int)units[i].TextPosition, false).X;
            double last = natural.Caret(0, (int)(units[i].TextPosition + units[i].Length), true).X;
            ordered.Add((i, Math.Min(first, last), Math.Max(first, last), last < first));
        }
        return ApplyVisualAutospace(ordered.OrderBy(cell => cell.Left).ToList(), units, metadata, fallback, out gaps);
    }

    private bool ApplyVisualAutospace(List<(int Index, double Left, double Right, bool Rtl)> ordered,
        NativeMethods.ParagraphAdvanceUnit[] units, AutospaceUnit[] metadata,
        DependencyObject fallback, out VisualAutoGap[] gaps)
    {
        var result = new List<VisualAutoGap>();
        Rune VisualRune(int index) => ordered[index].Rtl ? metadata[ordered[index].Index].Rune.Value switch
        { 0x00ab => new Rune(0x00bb), 0x00bb => new Rune(0x00ab), _ => metadata[ordered[index].Index].Rune } : metadata[ordered[index].Index].Rune;
        for (int i = 0; i < ordered.Count; i++)
        {
            int index = ordered[i].Index; var source = metadata[index];
            if (source.Rune.Value != ' ' && !source.Replacement) continue;
            // Replacements retain their source offsets, but only boundaries on
            // this used line may contribute the virtual spacing.
            if (i == 0 || i + 1 == ordered.Count)
            {
                if (source.Replacement)
                {
                    double delta = -(ordered[i].Right - ordered[i].Left);
                    units[index].Leading = units[index].Trailing = (float)(delta / 2);
                    units[index].Flags &= ~4u;
                }
                continue;
            }
            int left = ordered[i - 1].Index, right = ordered[i + 1].Index;
            var owner = CommonAutospaceOwner(metadata[left].Owner, metadata[right].Owner, fallback);
            var spacing = AutospaceBetween(VisualRune(i - 1), VisualRune(i + 1), owner, requireReplace: true);
            if (spacing.Width <= 0) continue;
            if (!double.IsFinite(spacing.Width) || spacing.Width > 1000000) { gaps = []; return false; }
            double adjustment = spacing.Width - (ordered[i].Right - ordered[i].Left);
            units[index].Leading += (float)(adjustment / 2); units[index].Trailing += (float)(adjustment / 2);
            units[index].Flags &= ~4u;
            result.Add(new(left, right, index, spacing.Width, owner));
        }
        for (int i = 1; i < ordered.Count; i++)
        {
            int left = ordered[i - 1].Index, right = ordered[i].Index;
            var owner = CommonAutospaceOwner(metadata[left].Owner, metadata[right].Owner, fallback);
            double width = AutospaceBetween(VisualRune(i - 1), VisualRune(i), owner).Width;
            if (width <= 0) continue;
            if (!double.IsFinite(width) || width > 1000000) { gaps = []; return false; }
            if (ordered[i - 1].Rtl) units[left].Leading += (float)(width / 2); else units[left].Trailing += (float)(width / 2);
            if (ordered[i].Rtl) units[right].Trailing += (float)(width / 2); else units[right].Leading += (float)(width / 2);
            result.Add(new(left, right, -1, width, owner));
        }
        gaps = result.ToArray(); return true;
    }
}
