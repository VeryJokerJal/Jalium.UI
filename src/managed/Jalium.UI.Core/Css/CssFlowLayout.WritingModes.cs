using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssFlowLayout
{
    private CssWritingAxes Axes => new(CssWritingModeProperties.GetWritingMode(owner));
    private CssWritingMode? _writingMode;
    private double PreferredBlockSize => Axes.Vertical
        ? owner.CssPreferredWidth(owner.CssParentBox?.ContainingBlock.Width ?? owner.PreviousAvailableSize.Width)
        : owner.CssPreferredHeight(owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height);
    private bool HasIntrinsicBlockSize => owner.CssHasIntrinsicPreferredSize(Axes.Vertical);
    private (double Min, double Max) BlockBounds(double basis) => Axes.Vertical ? owner.CssWidthBounds(basis) : owner.CssHeightBounds(basis);
    private double PreferredInlineSize(FrameworkElement element, double basis) => Axes.Vertical ? element.CssPreferredHeight(basis) : element.CssPreferredWidth(basis);
    private (double Min, double Max) InlineBounds(FrameworkElement element, double basis) => Axes.Vertical ? element.CssHeightBounds(basis) : element.CssWidthBounds(basis);
    private Size Desired(CssLayoutBox box) => Axes.Logical(box.Element.DesiredSize);
    private bool LocalInlineAlignment(FrameworkElement element) => Axes.Vertical
        ? element.HasLocalOrAnimatedValue(FrameworkElement.VerticalAlignmentProperty) && element.VerticalAlignment != VerticalAlignment.Stretch
        : element.HasLocalOrAnimatedValue(FrameworkElement.HorizontalAlignmentProperty) && element.HorizontalAlignment != HorizontalAlignment.Stretch;
    private void MeasureFlowBox(CssLayoutBox box, Size available, Size containing, CssFloatEnvironment? floats = null)
        => box.MeasureBorderBox(Axes.Physical(available), Axes.Physical(containing), floats, Axes.Mode);
    private (double Height, double Baseline, double Ascent, double Descent) FlowLineMetrics()
    {
        var metrics = CssFlowProperties.LineMetrics(owner);
        return Axes.Vertical && Axes.Mode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
            CssWritingModeProperties.GetTextOrientation(owner) != CssTextOrientation.Sideways
            ? (metrics.Height, metrics.Height / 2, metrics.Height / 2, metrics.Height / 2) : metrics;
    }
    private void MeasureFlowIntrinsic(CssLayoutBox box, Size containing, CssFloatEnvironment? floats)
    {
        if (!Axes.Vertical) { box.MeasureIntrinsicWidth(containing, floats); return; }
        var element = box.Element as FrameworkElement;
        var previous = element?.CssIntrinsicMeasure ?? false;
        if (element is not null) { element.CssIntrinsicMeasure = true; element.InvalidateCssAllocation(); }
        try
        {
            Probe(new(0, double.PositiveInfinity)); box.MinContent = Desired(box);
            Probe(new(double.PositiveInfinity, double.PositiveInfinity)); box.MaxContent = Desired(box);
        }
        finally { if (element is not null) element.CssIntrinsicMeasure = previous; }
        void Probe(Size available)
        {
            using var contribution = element is null ? null : new CssIntrinsicPercentage.Scope(element, false, available.Width == 0);
            for (int pass = 0; pass < 8; pass++)
            {
                MeasureFlowBox(box, available, containing, floats);
                if (!CssFirstLine.HasPending(box.Element.Dispatcher)) break;
                CssFirstLine.FlushLayout(box.Element.Dispatcher);
                if (box.Element.IsMeasureValid) break;
            }
        }
    }
}
