using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class FlexPanel
{
    // Flexbox §9.9.1.2/3 and §9.9.3: web-compatible contributions, including
    // the item's preferred size and the limits imposed by its flexibility.
    private double IntrinsicMainSize(FlexAxis axis, Size containingBlock, bool minimum, double gap)
    {
        double result = 0;
        var active = 0;
        for (var i = 0; i < _itemCount; i++)
        {
            ref readonly var item = ref _items![i];
            if (item.Collapsed) continue;
            var replaced = item.Element is FrameworkElement replacement && CssFlexItemSizing.IsReplaced(replacement);
            var constraint = minimum && axis.Horizontal && !replaced ? 0 : double.PositiveInfinity;
            var contribution = axis.Main(CssFlexItemSizing.Measure(item.Element,
                axis.Size(constraint, axis.Cross(containingBlock)), containingBlock,
                new(axis.Horizontal, true, true), intrinsic: true, intrinsicMinimum: minimum));
            if (item.Element is FrameworkElement element)
            {
                var preferred = axis.Horizontal ? element.CssPreferredWidth(containingBlock.Width)
                    : element.CssPreferredHeight(containingBlock.Height);
                if (double.IsFinite(preferred)) contribution = Math.Max(contribution, preferred + item.MainMargin);
            }
            if (item.Grow == 0) contribution = Math.Min(contribution, item.FlexBaseMain);
            if (item.Shrink == 0) contribution = Math.Max(contribution, item.FlexBaseMain);
            contribution = Math.Clamp(contribution, item.MinMain, item.MaxMain);
            if (minimum && Wrap != FlexWrap.NoWrap) result = Math.Max(result, contribution);
            else result += contribution;
            active++;
        }
        if (!(minimum && Wrap != FlexWrap.NoWrap)) result += gap * Math.Max(0, active - 1);
        return Math.Max(0, result);
    }
}
