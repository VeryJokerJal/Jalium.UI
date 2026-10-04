using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private double _groupAlignmentCacheWidth = double.NaN;
    private CssTextGroupAlignment _groupAlignmentCacheMode;
    private double _groupAlignmentCachePadding;

    private double GetGroupAlignmentPadding(double contentWidth)
    {
        var mode = (CssTextGroupAlignment)GetValue(CssFlowProperties.TextGroupAlignProperty)!;
        if (mode == CssTextGroupAlignment.None || !double.IsFinite(contentWidth) ||
            contentWidth <= 0) return 0;
        if (_groupAlignmentCacheWidth == contentWidth &&
            _groupAlignmentCacheMode == mode) return _groupAlignmentCachePadding;

        var shortestRemaining = double.PositiveInfinity;
        foreach (var line in _layoutLines)
        {
            var indent = ResolveTextIndent(line.IndentApplies, contentWidth);
            shortestRemaining = Math.Min(shortestRemaining,
                Math.Max(0, contentWidth - indent - line.Width -
                    line.StartPadding - line.EndPadding));
            if (shortestRemaining <= 0) break;
        }

        _groupAlignmentCacheWidth = contentWidth;
        _groupAlignmentCacheMode = mode;
        _groupAlignmentCachePadding = double.IsFinite(shortestRemaining)
            ? shortestRemaining : 0;
        return _groupAlignmentCachePadding;
    }
}
