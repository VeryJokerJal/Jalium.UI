namespace Jalium.UI.Styling;

internal sealed class CssContextTransformValue(string text, CssLength[] relativeLengths) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var element = context.Element;
        foreach (var length in relativeLengths) length.ObserveContainerDependencies(context.Lengths);
        var state = CssEngine.EnsureState(element);
        if (!state.ObservesOwnSize)
        {
            state.ObservesOwnSize = true;
            element.SizeChanged += (_, _) => CssEvaluationScheduler.InvalidateElement(element);
        }
        var size = new Size(element.ActualWidth, element.ActualHeight);
        if (state.TransformSource != text || state.TransformReferenceSize != size || !state.TransformLengths.Equals(context.Lengths))
        {
            var reader = new CssTokenReader(text, new CssNumericReadContext(context.Lengths));
            if (!CssTransformParser.TryParseTransformList(ref reader, out var transform, context.Lengths, size)) return false;
            if (transform?.CanFreeze == true) transform.Freeze();
            state.ContextTransform = transform;
            state.TransformSource = text;
            state.TransformReferenceSize = size;
            state.TransformLengths = context.Lengths;
        }
        sink.Set(UIElement.RenderTransformProperty, state.ContextTransform);
        return true;
    }
}
