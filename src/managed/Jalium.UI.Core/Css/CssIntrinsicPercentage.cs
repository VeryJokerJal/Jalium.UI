using Jalium.UI.Media;

namespace Jalium.UI
{
    public partial class FrameworkElement
    {
        internal Styling.CssIntrinsicPercentageContext? CssContribution;
    }
}

namespace Jalium.UI.Styling
{
    internal readonly record struct CssIntrinsicPercentageContext(byte Active, byte Cyclic, byte Minimum);

    /// <summary>CSS Sizing 3 §5.2.1: contribution-time percentages precede used-size resolution.</summary>
    internal static class CssIntrinsicPercentage
    {
        private static byte Axis(bool horizontal) => (byte)(horizontal ? 1 : 2);
        internal static bool Cyclic(FrameworkElement element, bool horizontal)
            => ((element.CssContribution?.Cyclic ?? 0) & Axis(horizontal)) != 0;
        internal static bool CyclicSize(FrameworkElement element, CssLayoutLength value, bool horizontal)
            => Cyclic(element, horizontal) && (value.Kind == CssLayoutLength.KindPercent ||
                value.Kind == CssLayoutLength.KindExpression && value.IsPercent || value.Kind == CssLayoutLength.KindStretch);
        internal static double EdgeBasis(FrameworkElement element, double basis)
            => Cyclic(element, true) || !double.IsFinite(basis) ? 0 : basis;

        // An unresolved percentage in a minimum contributes zero; the rest of
        // a length-percentage expression still participates in content sizing.
        internal static double MinimumBasis(double basis) => double.IsFinite(basis) ? basis : 0;

        internal static double Resolve(FrameworkElement element, CssLayoutLength value, bool horizontal,
            double fallback, bool minimumBound = false)
        {
            var minimum = ((element.CssContribution?.Minimum ?? 0) & Axis(horizontal)) != 0;
            if (minimumBound) return Math.Max(0, value.Resolve(0, 0));
            if (!minimum || !CssFlexItemSizing.IsReplaced(element)) return fallback;
            return Math.Max(Math.Max(0, value.Resolve(0, 0)), TransferredMinimum(element, horizontal));
        }

        private static double TransferredMinimum(FrameworkElement element, bool horizontal)
        {
            var image = CssReplacedContent.Image(element) ?? (element as CssGeneratedText)?.ReplacementImage;
            var naturalRatio = image is { HasNaturalRatio: true } ? image.Intrinsic.Ratio :
                element is Controls.Image native ? native.CssNaturalRatio :
                element is Controls.MediaElement { NaturalVideoHeight: > 0 } video ? (double)video.NaturalVideoWidth / video.NaturalVideoHeight : 0;
            var layout = element.CssLayout;
            var authored = layout is { AspectRatio: > 0 } && (!layout.AspectRatioAuto || !(naturalRatio > 0));
            var ratio = authored ? layout!.AspectRatio : naturalRatio;
            if (!(ratio > 0) || !double.IsFinite(ratio)) return 0;
            var property = horizontal ? FrameworkElement.MinHeightProperty : FrameworkElement.MinWidthProperty;
            var local = element.HasLocalOrAnimatedValue(property);
            var limit = horizontal ? layout?.MinHeight : layout?.MinWidth;
            var minimum = element.GetValue(property) is double number ? number : 0;
            if (!local && limit is { IsSet: true } length)
                minimum = length.IsSizing ? 0 : Math.Max(0, length.Resolve(0, 0));
            var edges = CssBoxMetrics.ContentInsets(element, layout?.ContainingWidthCache ?? double.PositiveInfinity);
            var mainChrome = horizontal ? edges.Left + edges.Right : edges.Top + edges.Bottom;
            var otherChrome = horizontal ? edges.Top + edges.Bottom : edges.Left + edges.Right;
            var contentBox = layout?.BoxSizing == CssBoxSizing.ContentBox;
            if (contentBox && local) minimum = Math.Max(0, minimum - otherChrome);
            else if (!contentBox && !authored) minimum = Math.Max(0, minimum - otherChrome);
            var transferred = horizontal ? minimum * ratio : minimum / ratio;
            return transferred + (!contentBox && !authored ? mainChrome : 0);
        }

        internal sealed class Scope : IDisposable
        {
            private readonly List<(FrameworkElement Element, CssIntrinsicPercentageContext? Context, bool Intrinsic)> _saved = [];
            internal Scope(FrameworkElement root, bool horizontal, bool minimum,
                bool cyclicRoot = false, bool naturalRoot = false)
            {
                var axis = Axis(horizontal); var previous = root.CssContribution ?? default;
                var active = (byte)(previous.Active | axis);
                var cyclic = (byte)(previous.Cyclic | (cyclicRoot ? axis : 0));
                var minima = (byte)((previous.Minimum & ~axis) | (minimum ? axis : 0));
                try { Visit(root, new(active, cyclic, minima), true); }
                catch { Dispose(); throw; }

                void Visit(Visual visual, CssIntrinsicPercentageContext context, bool first)
                {
                    var children = context;
                    if (visual is FrameworkElement element)
                    {
                        _saved.Add((element, element.CssContribution, element.CssIntrinsicMeasure));
                        element.CssContribution = context; element.CssIntrinsicMeasure = true;
                        element.InvalidateCssAllocation();
                        byte dependent = 0;
                        for (var dimension = 0; dimension < 2; dimension++)
                        {
                            var horizontalAxis = dimension == 0;
                            var bit = Axis(horizontalAxis);
                            if ((context.Active & bit) == 0) continue;
                            if (first && naturalRoot && bit == axis ||
                                ContentDependent(element, horizontalAxis, context, first)) dependent |= bit;
                        }
                        children = context with { Cyclic = dependent };
                    }
                    for (var i = 0; i < visual.InternalVisualChildrenCount; i++)
                        if (visual.InternalGetVisualChild(i) is { } child) Visit(child, children, false);
                }
            }
            public void Dispose()
            {
                foreach (var (element, context, intrinsic) in _saved)
                {
                    element.CssContribution = context; element.CssIntrinsicMeasure = intrinsic;
                    element.InvalidateCssAllocation();
                }
            }
        }

        private static bool ContentDependent(FrameworkElement element, bool horizontal,
            CssIntrinsicPercentageContext context, bool root)
        {
            var property = horizontal ? FrameworkElement.WidthProperty : FrameworkElement.HeightProperty;
            CssLayoutLength? value = element.CssPresentedSize(horizontal ? CssLayoutSlotField.Width : CssLayoutSlotField.Height).SizingBasis;
            if (element.HasLocalOrAnimatedValue(property)) value = null;
            var intrinsic = value is { IsSizing: true, Kind: not CssLayoutLength.KindStretch };
            if (!root && (context.Cyclic & Axis(horizontal)) == 0 && !intrinsic) return false;
            if (value is { IsSet: true } length)
                return length.IsAuto || intrinsic || CyclicSize(element, length, horizontal);
            return element.GetValue(property) is double number && double.IsNaN(number);
        }
    }
}
