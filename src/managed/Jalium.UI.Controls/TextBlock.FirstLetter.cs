using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _findingFirstLetter;
    private int _firstLetterRevision = -1;
    private FirstLetterPlan? _firstLetterPlan;
    private sealed record FirstLetterPlan(int Start, int End, CssNode[] Origins);
    internal bool HasCssFirstLetter => GetFirstLetterPlan() is not null;
    internal bool CssFirstLetterHasContent
    {
        get
        {
            var breaks = GetExplicitLineBreaks(); var hidden = FirstLetterHiddenMask(breaks);
            var mode = (CssWhiteSpaceCollapse)GetValue(CssFlowProperties.WhiteSpaceProperty)!;
            for (var index = 0; index < _displayText.Length; index++)
            {
                if (hidden?[index] == true) continue;
                var value = _displayText[index];
                if (breaks?[index] == true || value is not (' ' or '\t' or '\r' or '\n' or '\f' or '\uFEFF' or '\u00AD')) return true;
                if (mode is CssWhiteSpaceCollapse.Preserve or CssWhiteSpaceCollapse.BreakSpaces ||
                    mode == CssWhiteSpaceCollapse.PreserveBreaks && value is '\r' or '\n' or '\f') return true;
            }
            return false;
        }
    }

    private FirstLetterPlan? GetFirstLetterPlan()
    {
        // These private leaves already carry their projected style and must not
        // recursively manufacture another first-letter formatting subtree.
        if (this is InlineSourceText || _displayText.Length == 0 || _findingFirstLetter) return null;
        var revision = CssFirstLetter.Revision;
        if (_firstLetterRevision == revision) return _firstLetterPlan;
        _findingFirstLetter = true;
        try
        {
            var origins = CssFirstLetter.Origins(this);
            FirstLetterPlan? plan = null;
            if (origins.Length > 0)
            {
                var mode = (CssWhiteSpaceCollapse)GetValue(CssFlowProperties.WhiteSpaceProperty)!;
                var preserve = mode is CssWhiteSpaceCollapse.Preserve or
                    CssWhiteSpaceCollapse.PreserveBreaks or CssWhiteSpaceCollapse.BreakSpaces;
                var breaks = GetExplicitLineBreaks();
                var range = CssFirstLetter.Select(_displayText, Language.IetfLanguageTag, preserve,
                    breaks, FirstLetterHiddenMask(breaks), mode is not CssWhiteSpaceCollapse.Preserve and not CssWhiteSpaceCollapse.BreakSpaces);
                if (range.End > range.Start) plan = new(range.Start, range.End, origins);
            }
            if (_firstLetterPlan?.Start != plan?.Start || _firstLetterPlan?.End != plan?.End ||
                !(_firstLetterPlan?.Origins ?? []).SequenceEqual(plan?.Origins ?? []))
            {
                // A previously native leaf may now need a CSS formatting body
                // after an earlier sibling disappears. Its old measure and
                // paragraph allocation cannot survive that change of formatter.
                InvalidateCaches(); InvalidateMeasure(); InvalidateVisual();
            }
            _firstLetterPlan = plan; _firstLetterRevision = revision;
            return plan;
        }
        finally { _findingFirstLetter = false; }
    }

    private bool[]? FirstLetterHiddenMask(bool[]? breaks)
    {
        var hidden = InlineDisplayMask();
        if (GetWhiteSpaceTrimMask(breaks) is { } trim)
        {
            hidden ??= new bool[trim.Length];
            for (var i = 0; i < trim.Length; i++) hidden[i] |= trim[i];
        }
        return hidden;
    }
}
