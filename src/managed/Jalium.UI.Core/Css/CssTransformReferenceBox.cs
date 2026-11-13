namespace Jalium.UI.Styling;

internal enum CssTransformBox : byte
{
    ContentBox,
    BorderBox,
    FillBox,
    StrokeBox,
    ViewBox,
}

/// <summary>Resolves the CSS transform reference rectangle in border-box coordinates.</summary>
internal static class CssTransformReferenceBox
{
    // Resolve only current values; this collector is never written to.
    private static readonly CssEngine.CssSetterCollector s_currentValues = new();
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssTransformBox", typeof(CssTransformBox), typeof(CssTransformReferenceBox),
        new PropertyMetadata(CssTransformBox.ViewBox, static (target, _) =>
        {
            if (target is FrameworkElement element)
            {
                CssEvaluationScheduler.InvalidateElement(CssNode.Get(element));
                RefreshConsumers(element);
            }
        }));

    internal static Point InitialOrigin(UIElement element)
    {
        if (element is not FrameworkElement framework ||
            element.GetValue(ValueProperty) is not (CssTransformBox.ContentBox or CssTransformBox.FillBox))
            return new Point(.5, .5);
        var node = CssNode.Get(framework);
        ObserveInsets(node);
        var box = Resolve(node, s_currentValues, useCurrentStyle: true);
        var size = element.RenderSize;
        return new Point(size.Width == 0 ? .5 : (box.X + box.Width / 2) / size.Width,
            size.Height == 0 ? .5 : (box.Y + box.Height / 2) / size.Height);
    }

    internal static Rect Current(DependencyObject element, Size size)
    {
        var node = CssNode.Get(element);
        ObserveInsets(node);
        return Resolve(node, s_currentValues, useCurrentStyle: true, size);
    }

    private static void RefreshConsumers(UIElement element)
    {
        element.RefreshCssTransformOrigin();
        // An explicit offset-anchor can move while transform-origin stays the
        // same. Native default origins also stay zero when their CSS anchor moves.
        CssMotionPath.RefreshTransformOrigin(element);
    }

    internal static CssTransformBox UsedBox(CssNode element, CssEngine.CssSetterCollector setters,
        bool useCurrentStyle = false)
    {
        if (!useCurrentStyle &&
            (element.HasLocalOrAnimatedValue(ValueProperty) || element.Target.HasCssAnimatedValue(ValueProperty)) &&
            element.GetValue(ValueProperty) is CssTransformBox animated)
            return animated;
        if (setters.Values.TryGetValue(ValueProperty, out var applied) && applied.Value is CssTransformBox css)
            return css;
        return useCurrentStyle && element.GetValue(ValueProperty) is CssTransformBox current
            ? current : CssTransformBox.ViewBox;
    }

    internal static Rect Resolve(CssNode element, CssEngine.CssSetterCollector setters, bool useCurrentStyle = false, Size? size = null)
    {
        var outer = size ?? new Size(element.ActualWidth, element.ActualHeight);
        var box = UsedBox(element, setters, useCurrentStyle);
        // CSS layout boxes use the content box for fill-box, and the border box
        // for stroke-box and view-box (CSS Transforms 1, transform-box).
        if (box is not (CssTransformBox.ContentBox or CssTransformBox.FillBox))
            return new Rect(0, 0, outer.Width, outer.Height);

        var paddingProperty = CssDependencyPropertyLookup.Find(element.GetType(), "Padding");
        var paddingLayout = setters.LayoutState ?? (useCurrentStyle ? element.CssLayout : null);
        var padding = paddingProperty is not null
            ? ReadThickness(element, setters, paddingProperty, useCurrentStyle,
                paddingLayout is { HasPadding: true } layout ? ResolveLayoutPadding(element, layout) : null)
            : ResolveLayoutPadding(element, paddingLayout);
        var borderProperty = CssDependencyPropertyLookup.Find(element.GetType(), "BorderThickness");
        var border = borderProperty is not null
            ? ReadThickness(element, setters, borderProperty, useCurrentStyle)
            : ReadThickness(element, setters, CssBorderUsedThicknessProperties.ValueProperty, useCurrentStyle);
        var insets = new Thickness(border.Left + padding.Left, border.Top + padding.Top,
            border.Right + padding.Right, border.Bottom + padding.Bottom);
        var inner = CssBoxMetrics.InnerSize(outer, insets);
        return new Rect(insets.Left, insets.Top, inner.Width, inner.Height);
    }

    private static Thickness ReadThickness(CssNode element, CssEngine.CssSetterCollector setters,
        DependencyProperty property, bool useCurrentStyle, Thickness? layoutPadding = null)
    {
        if ((element.HasLocalOrAnimatedValue(property) || element.Target.HasCssAnimatedValue(property)) &&
            element.GetValue(property) is Thickness native)
            return native;
        var hasCss = setters.Values.TryGetValue(property, out var applied);
        if (useCurrentStyle && !hasCss && layoutPadding is null && element.GetValue(property) is Thickness current)
            return current;

        var target = element.Target;
        static bool TryLayer(DependencyObject target, DependencyProperty property,
            DependencyObject.LayerValueSource layer, out Thickness thickness)
        {
            if (target.TryGetLayerValue(property, layer, out var value) && value is Thickness found)
            {
                thickness = found;
                return true;
            }
            thickness = default;
            return false;
        }

        if (TryLayer(target, property, DependencyObject.LayerValueSource.ParentTemplateTrigger, out var higher) ||
            TryLayer(target, property, DependencyObject.LayerValueSource.ParentTemplate, out higher))
            return higher;
        if (hasCss && applied.Layer == DependencyObject.LayerValueSource.CssState && applied.Value is Thickness stateCss)
            return stateCss;
        if (TryLayer(target, property, DependencyObject.LayerValueSource.StyleTrigger, out var trigger) ||
            TryLayer(target, property, DependencyObject.LayerValueSource.TemplateTrigger, out trigger))
            return trigger;
        if (layoutPadding is { } resolvedPadding) return resolvedPadding;
        if (hasCss && applied.Value is Thickness baseCss) return baseCss;
        if (TryLayer(target, property, DependencyObject.LayerValueSource.StyleSetter, out var styled))
            return styled;
        // The old CSS layer may still be installed while this style pass runs.
        // An absent new declaration reveals the property's native default.
        if (!useCurrentStyle && target.GetEffectiveValueLayer(property) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            return property.GetEffectiveDefaultValue(element.GetType()) is Thickness fallback ? fallback : default;
        return element.GetValue(property) is Thickness existing ? existing : default;
    }

    private static Thickness ResolveLayoutPadding(CssNode element, CssLayoutState? layout)
    {
        if (layout is not { HasPadding: true }) return default;
        var basis = layout.ContainingWidthCache;
        if (!double.IsFinite(basis)) basis = element.CssLayout?.ContainingWidthCache ?? double.NaN;
        if (!double.IsFinite(basis)) basis = element.FrameworkParent?.ActualWidth ?? element.ActualWidth;
        return new Thickness(
            Math.Max(0, layout.PaddingLeft.Resolve(basis, 0)),
            Math.Max(0, layout.PaddingTop.Resolve(basis, 0)),
            Math.Max(0, layout.PaddingRight.Resolve(basis, 0)),
            Math.Max(0, layout.PaddingBottom.Resolve(basis, 0)));
    }

    internal static void ObserveInsets(CssNode element)
    {
        var state = CssEngine.EnsureState(element);
        if (state.ObservesTransformInsets) return;
        state.ObservesTransformInsets = true;
        element.PropertyChangedInternal += (property, _, _) =>
        {
            if (property.Name is "Padding" or "BorderThickness" or "CssBorderUsedThickness")
            {
                CssEvaluationScheduler.InvalidateElement(element);
                if (element.Target is UIElement visual) RefreshConsumers(visual);
            }
        };
        element.SizeChanged += (_, _) =>
        {
            if (element.Target is UIElement visual) RefreshConsumers(visual);
        };
    }
}
