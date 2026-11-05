using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Basic-shape exclusions are layout inputs, independent of the element's paint clip.</summary>
internal static class CssShapeOutsideProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssShapeOutside", typeof(object), typeof(CssShapeOutsideProperties),
        new PropertyMetadata(null, Changed));
    internal static readonly DependencyProperty MarginProperty = DependencyProperty.RegisterAttached(
        "CssShapeMargin", typeof(CssLayoutLength), typeof(CssShapeOutsideProperties),
        new PropertyMetadata(CssLayoutLength.Px(0), CssFloatProperties.Changed));
    internal static readonly DependencyProperty ThresholdProperty = DependencyProperty.RegisterAttached(
        "CssShapeImageThreshold", typeof(double), typeof(CssShapeOutsideProperties),
        new PropertyMetadata(0d, CssFloatProperties.Changed));
    private static readonly ConditionalWeakTable<UIElement, object> Observed = new();

    internal static void Register()
    {
        CssPropertyRegistry.Register(new()
        {
            Name = "shape-outside", Kind = CssPropertyKind.Longhand, StorageProperty = ValueProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext context) =>
                CssShapeImageValue.Parse(ref reader, context) ??
                CssCoreProperties.ParseBasicShape(ref reader, context, ValueProperty, CssClipBox.Margin),
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "shape-image-threshold", Kind = CssPropertyKind.Longhand, StorageProperty = ThresholdProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext context) =>
                CssShapeImageValue.ParseThreshold(ref reader, context),
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "shape-margin", Kind = CssPropertyKind.Longhand, StorageProperty = MarginProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
                reader.TryReadLength(out var length) && reader.AtEnd &&
                    (length.Expression is not null || length.Value >= 0) ? new MarginValue(length) : null,
        });
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (target is UIElement consumer && consumer.GetValue(ValueProperty) is CssShapeImageValue image)
            foreach (var resource in image.Resources) resource.Subscribe(consumer);
        if (target is UIElement element && !Observed.TryGetValue(element, out _))
        {
            Observed.Add(element, new object());
            // Corner radii normally affect paint only. A shape-box also uses
            // them for line placement, including native/local CornerRadius.
            element.CssCornerRadiusPresentationChanged += () =>
            {
                if (element.GetValue(ValueProperty) is CssClipPathValue { Shape: CssClipShape.Box })
                    CssFloatProperties.Changed(element, default);
            };
            element.PropertyChangedInternal += (property, _, _) =>
            {
                if (property.PropertyType == typeof(CornerRadius) &&
                    element.GetValue(ValueProperty) is CssClipPathValue { Shape: CssClipShape.Box })
                    CssFloatProperties.Changed(element, default);
                if ((property == CssImageObjectProperties.FitProperty || property == CssImageObjectProperties.PositionProperty) &&
                    element.GetValue(ValueProperty) is CssShapeImageValue)
                    CssFloatProperties.Changed(element, default);
            };
        }
        CssFloatProperties.Changed(target, change);
    }

    internal static CssFloatShape? Resolve(UIElement element, Size borderSize, Thickness margin,
        double containingWidth)
    {
        if (!double.IsFinite(borderSize.Width) || !double.IsFinite(borderSize.Height)) return null;
        var distance = ((CssLayoutLength)element.GetValue(MarginProperty)!).Resolve(containingWidth, 0);
        if (!double.IsFinite(distance)) distance = 0;
        if (element.GetValue(ValueProperty) is CssShapeImageValue image)
            return image.Resolve(element, borderSize, margin, containingWidth, Math.Max(0, distance),
                (double)element.GetValue(ThresholdProperty)!);
        if (element.GetValue(ValueProperty) is not CssClipPathValue { Shape: not CssClipShape.None } value) return null;
        var geometry = value.Resolve(element, borderSize, containingWidth);
        if (geometry is null) return null;
        return CssFloatShape.Create(geometry, margin.Left, margin.Top,
            Math.Max(0, borderSize.Width + margin.Left + margin.Right),
            Math.Max(0, borderSize.Height + margin.Top + margin.Bottom), Math.Max(0, distance));
    }

    private sealed class MarginValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            length.ObserveContainerDependencies(context.Lengths);
            CssLayoutLength value;
            if (length.Expression is { } expression) value = CssLayoutLength.Math(expression, context.Lengths);
            else if (length.Unit == CssUnit.Percent) value = CssLayoutLength.Percent(length.Value / 100);
            else
            {
                if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px)) return false;
                value = CssLayoutLength.Px(px);
            }
            // CSS range restriction clamps math at used-value time.
            sink.Set(MarginProperty, value);
            return true;
        }
    }
}
