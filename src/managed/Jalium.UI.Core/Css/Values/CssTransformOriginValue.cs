using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Resolves CSS transform-origin lengths against the element's used size.</summary>
internal sealed class CssTransformOriginValue(CssBackgroundPosition position) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var element = context.Element;
        position.ObserveDependencies(context.Lengths);
        var state = CssEngine.EnsureState(element);
        if (!state.ObservesOwnSize)
        {
            state.ObservesOwnSize = true;
            element.SizeChanged += (_, _) => CssEvaluationScheduler.InvalidateElement(element);
        }

        var box = context.Slots.TransformReferenceBox;
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var x = box.X + position.X.Resolve(box.Width, context.Lengths);
        var y = box.Y + position.Y.Resolve(box.Height, context.Lengths);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        sink.Set(UIElement.RenderTransformOriginProperty,
            new Point(width == 0 ? ZeroSizeOrigin(position.X, context.Lengths) : x / width,
                height == 0 ? ZeroSizeOrigin(position.Y, context.Lengths) : y / height));
        return true;
    }

    private static double ZeroSizeOrigin(CssBackgroundPositionAxis axis, in CssLengthContext lengths)
        => axis.Offset.Unit == CssUnit.Percent && axis.Offset.Expression is null
            ? axis.Resolve(1, lengths) : 0;
}
