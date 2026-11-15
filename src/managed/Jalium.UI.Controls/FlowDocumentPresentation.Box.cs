using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly record struct DocumentBox(Thickness Margin, Thickness Padding, Thickness Border, double Width)
    {
        internal double ContentWidth => Math.Max(0, Width - Padding.Left - Padding.Right - Border.Left - Border.Right);
    }

    private static Thickness DocumentEdges(DependencyObject source, bool margin, double containingWidth)
    {
        var property = CssDependencyPropertyLookup.Find(source.GetType(), margin ? "Margin" : "Padding");
        if (source is FlowDocument) property ??= margin ? FrameworkElement.MarginProperty : Control.PaddingProperty;
        var native = property is not null && source.GetValue(property) is Thickness thickness ? thickness : default;
        if (property is not null && source.HasLocalOrAnimatedValue(property)) return native;
        var state = CssNode.Get(source).CssLayout;
        if (margin ? state?.HasMargin != true : state?.HasPadding != true) return native;
        var edgeBasis = double.IsFinite(containingWidth) ? containingWidth : 0;
        double Edge(CssLayoutLength value) => margin ? value.Resolve(edgeBasis, 0) : Math.Max(0, value.Resolve(edgeBasis, 0));
        return margin ? new(Edge(state!.MarginLeft), Edge(state.MarginTop), Edge(state.MarginRight), Edge(state.MarginBottom))
            : new(Edge(state!.PaddingLeft), Edge(state.PaddingTop), Edge(state.PaddingRight), Edge(state.PaddingBottom));
    }

    private static double DocumentLimit(DependencyObject source, DependencyProperty property, CssLayoutLength length,
        double containingWidth, double fallback, double insets, bool contentBox,
        (double Min, double Max)? intrinsic = null, double automatic = double.NaN, double stretch = double.NaN)
    {
        var native = source.HasLocalOrAnimatedValue(property);
        length = DocumentSizeValue(source, property, length);
        if (!native && length.IsSizing && intrinsic is { } content)
            return ResolveDocumentSizing(length, containingWidth, fallback, insets, contentBox, content, automatic, stretch);
        var value = !native && length.IsSet ? length.Resolve(fallback == 0 ? CssIntrinsicPercentage.MinimumBasis(containingWidth) : containingWidth, fallback)
            : (double)source.GetValue(property)!;
        return double.IsFinite(value) ? Math.Max(0, value) + (contentBox && !native ? insets : 0) : fallback;
    }

    private DocumentBox ResolveDocumentBox(DependencyObject source, double containingWidth, Thickness border, bool collapsed = false,
        double preferredWidth = double.NaN)
    {
        var margin = DocumentEdges(source, true, containingWidth);
        var padding = collapsed ? default : DocumentEdges(source, false, containingWidth);
        var state = CssNode.Get(source).CssLayout;
        var insets = padding.Left + padding.Right + border.Left + border.Right;
        var contentBox = state is { BoxSizing: CssBoxSizing.ContentBox };
        var sizing = DocumentSizeValue(source, FrameworkElement.WidthProperty, state?.Width ?? default).IsSizing ||
            DocumentSizeValue(source, FrameworkElement.MinWidthProperty, state?.MinWidth ?? default).IsSizing ||
            DocumentSizeValue(source, FrameworkElement.MaxWidthProperty, state?.MaxWidth ?? default).IsSizing;
        var content = sizing ? DocumentContentWidths(source) : default;
        var fill = containingWidth - margin.Left - margin.Right;
        var automatic = double.IsFinite(preferredWidth) ? preferredWidth : fill;
        var specified = DocumentLimit(source, FrameworkElement.WidthProperty, state?.Width ?? default, containingWidth,
            double.NaN, insets, contentBox, content, automatic, fill);
        var minimum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MinWidthProperty, state?.MinWidth ?? default,
            containingWidth, 0, insets, contentBox, content, fill, fill));
        var maximum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MaxWidthProperty, state?.MaxWidth ?? default,
            containingWidth, double.PositiveInfinity, insets, contentBox, content, fill, fill));
        var width = Math.Max(minimum, Math.Min(maximum, double.IsFinite(preferredWidth) &&
            !DocumentSizeValue(source, FrameworkElement.WidthProperty, state?.Width ?? default).IsSizing ? preferredWidth :
            double.IsFinite(specified) ? specified : containingWidth - margin.Left - margin.Right));
        return ResizeDocumentBox(source, containingWidth, new(margin, padding, border, width), width);
    }

    private DocumentBox ResizeDocumentBox(DependencyObject source, double containingWidth, DocumentBox box, double width)
    {
        var margin = DocumentEdges(source, true, containingWidth);
        var state = CssNode.Get(source).CssLayout;
        var marginProperty = CssDependencyPropertyLookup.Find(source.GetType(), "Margin");
        if (source is FlowDocument) marginProperty ??= FrameworkElement.MarginProperty;
        var cssMargin = state is { HasMargin: true } && (marginProperty is null || !source.HasLocalOrAnimatedValue(marginProperty));
        var autoLeft = cssMargin && state!.MarginLeft.IsAuto; var autoRight = cssMargin && state!.MarginRight.IsAuto;
        var spare = containingWidth - width - margin.Left - margin.Right;
        if (spare >= 0 && (autoLeft || autoRight))
        {
            var share = spare / (autoLeft && autoRight ? 2 : 1);
            margin = new(autoLeft ? share : margin.Left, margin.Top, autoRight ? share : margin.Right, margin.Bottom);
        }
        else
        {
            // The containing block's direction chooses the sacrificed margin.
            // A child's own bidi direction does not change its normal-flow placement.
            var direction = source is FrameworkContentElement { Parent: TextElement parent }
                ? (FlowDirection)parent.GetValue(Block.FlowDirectionProperty)! : _document?.FlowDirection ?? FlowDirection.LeftToRight;
            margin = direction == FlowDirection.RightToLeft
                ? new(margin.Left + spare, margin.Top, margin.Right, margin.Bottom)
                : new(margin.Left, margin.Top, margin.Right + spare, margin.Bottom);
        }
        return box with { Margin = margin, Width = width };
    }

    private static (double Min, double Max) DocumentIntrinsicWidths(DependencyObject source, double min, double max, Thickness border, bool collapsed)
    {
        // A table cell's intrinsic width is cyclic with its descendants' percentages.
        // Percent edges contribute zero until the cell has a definite content width.
        var margin = DocumentEdges(source, true, double.NaN);
        var padding = collapsed ? default : DocumentEdges(source, false, double.NaN);
        var insets = padding.Left + padding.Right + border.Left + border.Right;
        var state = CssNode.Get(source).CssLayout; var contentBox = state is { BoxSizing: CssBoxSizing.ContentBox };
        var content = (min, max);
        var minimum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MinWidthProperty, state?.MinWidth ?? default, double.NaN, 0, insets, contentBox, content));
        var maximum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MaxWidthProperty, state?.MaxWidth ?? default, double.NaN, double.PositiveInfinity, insets, contentBox, content));
        double Used(double intrinsic)
        {
            var width = DocumentLimit(source, FrameworkElement.WidthProperty, state?.Width ?? default, double.NaN, double.NaN,
                insets, contentBox, content, intrinsic + insets, intrinsic + insets);
            return Math.Max(minimum, Math.Min(maximum, double.IsFinite(width) ? width : intrinsic + insets)) + margin.Left + margin.Right;
        }
        return (Used(min), Used(max));
    }
}
