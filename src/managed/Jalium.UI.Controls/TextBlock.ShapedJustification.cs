using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool WantsShapedJustification => OperatingSystem.IsMacOS() &&
        (_cssRubyJustify || CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, false) == TextAlignment.Justify ||
         CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, true) == TextAlignment.Justify);

    private sealed class ShapingJustification(double width)
    {
        internal readonly double Width = width;
        internal JustificationGap[] Gaps = [];
        internal double Expansion;
    }

    private static bool SuppressJustificationLigatures(string text, IEnumerable<DependencyObject> owners)
    {
        bool cjk = text.EnumerateRunes().Any(IsCjkLineBreakRune);
        return owners.Any(owner => (CssTextJustification)owner.GetValue(CssFlowProperties.TextJustifyProperty)! is var method &&
            (method == CssTextJustification.InterCharacter || cjk && method == CssTextJustification.Auto));
    }

    private static string JustificationFont(string family, DependencyObject owner)
    {
        FontFeature[] suppressed = [new(FontFeature.TagOf("liga"), 0), new(FontFeature.TagOf("clig"), 0),
            new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)];
        var explicitFeatures = ((CssFontFeatureSettings)owner.GetValue(CssFontFeatureSettings.SettingsProperty)!).Features;
        return FontFeatureRenderingSource.Wrap(family, FontFeatureRenderingSource.Merge(suppressed, explicitFeatures));
    }

    private static void ApplyShapedJustification(NativeTextParagraph natural, NativeMethods.ParagraphAdvanceUnit[] units,
        AutospaceUnit[] metadata, DependencyObject fallback, ShapingJustification request)
    {
        var visual = Enumerable.Range(0, units.Length).Where(i => (units[i].Flags & 1) == 0).Select(i =>
        {
            double start = natural.Caret(0, (int)units[i].TextPosition, false).X;
            double end = natural.Caret(0, (int)(units[i].TextPosition + units[i].Length), true).X;
            return (Index: i, Left: Math.Min(start, end), Right: Math.Max(start, end), Rtl: end < start);
        }).OrderBy(cell => cell.Left).ToArray();
        var opportunities = new List<(int Left, int Right, bool Separator)>();
        for (int i = 0; i < visual.Length; i++)
        {
            int index = visual[i].Index; var rune = metadata[index].Rune;
            var own = (CssTextJustification)metadata[index].Owner.GetValue(CssFlowProperties.TextJustifyProperty)!;
            if (i > 0 && i + 1 < visual.Length && IsWordSeparator(rune) && visual[i].Right - visual[i].Left > .000001 &&
                own is CssTextJustification.Auto or CssTextJustification.InterWord)
                opportunities.Add((i, i, true));
            if (i == 0) continue;
            int before = visual[i - 1].Index; var previous = metadata[before].Rune;
            if (IsProhibitedLineEnd(previous) || IsProhibitedLineStart(rune)) continue;
            var owner = CommonAutospaceOwner(metadata[before].Owner, metadata[index].Owner, fallback);
            var method = (CssTextJustification)owner.GetValue(CssFlowProperties.TextJustifyProperty)!;
            if (method == CssTextJustification.InterCharacter && !IsCursiveJoiningPair(previous, rune) ||
                method == CssTextJustification.Auto && !Rune.IsWhiteSpace(previous) && !Rune.IsWhiteSpace(rune) &&
                (IsCjkLineBreakRune(previous) || IsCjkLineBreakRune(rune)) && RubyAutoOpportunity(previous, rune, fallback)) opportunities.Add((i - 1, i, false));
        }
        double slack = request.Width - natural.Lines[0].Metrics.Line.Width;
        request.Gaps = []; request.Expansion = 0;
        if (opportunities.Count == 0 || !double.IsFinite(slack) || slack <= .001) return;
        double extra = slack / opportunities.Count;
        if (extra > 1000000) return;
        var gaps = new List<JustificationGap>();
        foreach (var opportunity in opportunities)
        {
            var left = visual[opportunity.Left]; var right = visual[opportunity.Right];
            if (opportunity.Separator)
            {
                units[left.Index].Leading += (float)(extra / 2); units[left.Index].Trailing += (float)(extra / 2);
                gaps.Add(new((int)units[left.Index].TextPosition, (int)(units[left.Index].TextPosition + units[left.Index].Length)));
            }
            else
            {
                if (left.Rtl) units[left.Index].Leading += (float)(extra / 2); else units[left.Index].Trailing += (float)(extra / 2);
                if (right.Rtl) units[right.Index].Trailing += (float)(extra / 2); else units[right.Index].Leading += (float)(extra / 2);
                int position = (int)units[right.Index].TextPosition; gaps.Add(new(position, position));
            }
        }
        request.Gaps = gaps.ToArray(); request.Expansion = extra;
    }

    private bool TryBuildShapedJustification(in TextLayoutLine line, int visibleEnd, double width, double tabOrigin,
        out JustifiedLine justified)
    {
        justified = default;
        if (line.PaintWrap is not null || line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true } || line.HangingStart != 0 || line.HangingEnd != 0) return false;
        var request = new ShapingJustification(width);
        if (!TryShapedSpacing(line.StartIndex, line.StartIndex + line.Length, line.Height, out var paragraph, visibleEnd, request)) return false;
        justified = new(line.StartIndex, visibleEnd, paragraph.Lines[0].Metrics.Line.Width, request.Expansion,
            request.Gaps, [], [], TabOrigin: tabOrigin, Shaped: paragraph);
        return true;
    }
}
