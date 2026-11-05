using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        internal CssWritingMode WritingMode => CssWritingModeProperties.GetWritingMode(owner);
        private FlowDirection UsedFlowDirection => CssWritingAxes.UsedDirection(owner);
        private CssWritingAxes Axes => new(WritingMode);
        private void MeasureInlineBox(UIElement element, Size available, Size? containing = null, CssFloatEnvironment? floats = null)
            => new CssLayoutBox(element, 0).MeasureBorderBox(Axes.Physical(available),
                Axes.Physical(containing ?? _containing), floats, WritingMode);
        private (double Height, double Baseline, double Ascent, double Descent) InlineLineMetrics(FrameworkElement element)
        {
            var metrics = CssFlowProperties.LineMetrics(element);
            return WritingMode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
                CssWritingModeProperties.GetTextOrientation(owner) != CssTextOrientation.Sideways
                ? (metrics.Height, metrics.Height / 2, metrics.Height / 2, metrics.Height / 2) : metrics;
        }
        private CssInlineBoxFragment PhysicalFragment(Rect logical, Rect allocation, bool left, bool right)
            => new(Axes.Local(logical, allocation), left, right, WritingMode: WritingMode);
    }
}
