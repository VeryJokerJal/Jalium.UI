using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal readonly record struct CssComputedCorner(CssLayoutLength X, CssLayoutLength Y)
{
    internal static CssComputedCorner Create(CssLength x, CssLength y, in CssLengthContext context)
        => new(Compute(x, context), Compute(y, context));

    private static CssLayoutLength Compute(CssLength length, in CssLengthContext context)
    {
        length.ObserveContainerDependencies(context);
        if (length.Expression is { } expression) return CssLayoutLength.Math(expression, context);
        if (length.Unit == CssUnit.Percent) return CssLayoutLength.Percent(length.Value / 100);
        return CssLayoutLength.Px(length.TryResolve(context, CssPercentBasis.NotSupported, out var px) ? px : 0);
    }

    internal Size Resolve(Size size) => new(NonNegative(X.Resolve(size.Width, 0)), NonNegative(Y.Resolve(size.Height, 0)));
    private static double NonNegative(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, float.MaxValue);
}

// Computed lengths retain percentages until the border box is known. This is a
// separate CSS layer: the public, circular WPF CornerRadius and its bindings stay intact.
internal sealed record CssBorderRadiusValue(CssComputedCorner TopLeft, CssComputedCorner TopRight,
    CssComputedCorner BottomRight, CssComputedCorner BottomLeft)
{
    internal bool UsesPercent => TopLeft.X.IsPercent || TopLeft.Y.IsPercent || TopRight.X.IsPercent || TopRight.Y.IsPercent ||
        BottomRight.X.IsPercent || BottomRight.Y.IsPercent || BottomLeft.X.IsPercent || BottomLeft.Y.IsPercent;
    internal bool RequiresGeometry => UsesPercent || !TopLeft.X.Equals(TopLeft.Y) || !TopRight.X.Equals(TopRight.Y) ||
        !BottomRight.X.Equals(BottomRight.Y) || !BottomLeft.X.Equals(BottomLeft.Y);
    internal CssComputedCorner Corner(int index) => index switch { 0 => TopLeft, 1 => TopRight, 2 => BottomRight, _ => BottomLeft };
    internal CornerRadius Horizontal(Size size) => new(TopLeft.Resolve(size).Width, TopRight.Resolve(size).Width,
        BottomRight.Resolve(size).Width, BottomLeft.Resolve(size).Width);
    internal CssUsedBorderRadii Resolve(Size size)
        => new CssUsedBorderRadii(TopLeft.Resolve(size), TopRight.Resolve(size), BottomRight.Resolve(size), BottomLeft.Resolve(size)).Normalize(size);

    internal CssBorderRadiusValue ForTemplate(Size referenceSize)
    {
        CssComputedCorner Freeze(CssComputedCorner corner)
        {
            var value = corner.Resolve(referenceSize);
            return new(CssLayoutLength.Px(value.Width), CssLayoutLength.Px(value.Height));
        }
        return new(Freeze(TopLeft), Freeze(TopRight), Freeze(BottomRight), Freeze(BottomLeft));
    }
}

internal sealed record CssTemplateRadiusValue(CssBorderRadiusValue Radius, bool Clip, ClipEdges Edges);

internal static class CssBorderRadiusProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBorderRadius", typeof(CssBorderRadiusValue), typeof(CssBorderRadiusProperties),
        new PropertyMetadata(null, static (target, _) => target.NotifyCssCornerRadiusPresentationChanged()));

    internal static readonly DependencyProperty TemplateValueProperty = DependencyProperty.RegisterAttached(
        "CssTemplateRadius", typeof(CssTemplateRadiusValue), typeof(CssBorderRadiusProperties),
        new PropertyMetadata(null, static (target, _) => target.NotifyCssCornerRadiusPresentationChanged()));

    internal static CssBorderRadiusValue? Get(DependencyObject target)
    {
        var dp = CssDependencyPropertyLookup.Find(target.GetType(), "CornerRadius");
        if (dp is null)
            return target is FrameworkElement &&
                target.GetEffectiveValueLayer(ValueProperty) is
                    (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)
                ? target.GetValue(ValueProperty) as CssBorderRadiusValue : null;
        if (target.HasLocalOrAnimatedValue(dp)) return null;
        var value = target.GetEffectiveValueLayer(dp) switch
        {
            DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState => target.GetValue(ValueProperty) as CssBorderRadiusValue,
            DependencyValueStore.Layer.ParentTemplate => (target.GetValue(TemplateValueProperty) as CssTemplateRadiusValue)?.Radius,
            _ => null,
        };
        // SetCurrentValue and native coercion can change the projection without
        // creating a local value. They must not leave an older CSS shape active.
        var size = target is FrameworkElement element ? element.RenderSize : default;
        return value is not null && Equals(target.GetValue(dp), value.Horizontal(size)) ? value : null;
    }

    internal static bool IsTemplateRadiusBinding(DependencyProperty? source, DependencyProperty target)
        => source?.Name == "CornerRadius" && source.PropertyType == typeof(CornerRadius) &&
            target.Name == "CornerRadius" && target.PropertyType == typeof(CornerRadius);

    internal static bool IsTemplatePresentationInput(DependencyProperty property)
        => property == FrameworkElement.ActualWidthProperty || property == FrameworkElement.ActualHeightProperty ||
            property == UIElement.ClipToBoundsProperty || property == UIElement.ClipToBoundsEdgesProperty;

    internal static void TransferTemplate(FrameworkElement source, DependencyObject target)
    {
        if (Get(source) is { } value)
        {
            var clip = EffectiveClip(source);
            var presentation = new CssTemplateRadiusValue(value.ForTemplate(source.RenderSize), clip.Enabled, clip.Edges);
            target.SetLayerValue(TemplateValueProperty, presentation, DependencyObject.LayerValueSource.ParentTemplate, allowAutoTransition: false);
        }
        else ClearTemplate(target);
    }

    internal static void ClearTemplate(DependencyObject target)
        => target.ClearLayerValue(TemplateValueProperty, DependencyObject.LayerValueSource.ParentTemplate, allowAutoTransition: false);

    internal static (bool Enabled, ClipEdges Edges) EffectiveClip(UIElement target)
    {
        var enabled = target.ClipToBounds;
        var edges = target.ClipToBoundsEdges;
        if (Get(target) is not null && target.GetValue(TemplateValueProperty) is CssTemplateRadiusValue template)
        {
            // Explicit template/native clip values remain authoritative. The
            // forwarding channel does not write to or replace either property.
            if (target.GetEffectiveValueLayer(UIElement.ClipToBoundsProperty) is null) enabled = template.Clip;
            if (target.GetEffectiveValueLayer(UIElement.ClipToBoundsEdgesProperty) is null) edges = template.Edges;
        }
        return (enabled, edges);
    }

    internal static bool Inherit(string name, CssNode parent, CssSlotAccumulator slots)
    {
        if (!CssLogicalBoxEdges.IsCorner(name)) return false;
        var targetIndex = CssLogicalBoxEdges.Corner(name, slots.LogicalRightToLeft);
        var sourceIndex = CssLogicalBoxEdges.Corner(name,
            parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft);
        if (Get(parent.Target) is { } value) slots.SetCorner(targetIndex, value.Corner(sourceIndex));
        else if (CssDependencyPropertyLookup.Find(parent.GetType(), "CornerRadius") is { } dp && parent.GetValue(dp) is CornerRadius native)
        {
            var radius = sourceIndex switch { 0 => native.TopLeft, 1 => native.TopRight, 2 => native.BottomRight, _ => native.BottomLeft };
            slots.SetCorner(targetIndex, new(CssLayoutLength.Px(radius), CssLayoutLength.Px(radius)));
        }
        else return false;
        return true;
    }
}

internal readonly record struct CssUsedBorderRadii(Size TopLeft, Size TopRight, Size BottomRight, Size BottomLeft)
{
    internal bool IsCircular => TopLeft.Width == TopLeft.Height && TopRight.Width == TopRight.Height &&
        BottomRight.Width == BottomRight.Height && BottomLeft.Width == BottomLeft.Height;
    internal CornerRadius Circular => new(TopLeft.Width, TopRight.Width, BottomRight.Width, BottomLeft.Width);
    internal CssUsedBorderRadii Normalize(Size size)
    {
        var scale = 1.0;
        void Limit(double available, double sum) { if (sum > available && sum > 0) scale = Math.Min(scale, available / sum); }
        Limit(size.Width, TopLeft.Width + TopRight.Width);
        Limit(size.Width, BottomLeft.Width + BottomRight.Width);
        Limit(size.Height, TopLeft.Height + BottomLeft.Height);
        Limit(size.Height, TopRight.Height + BottomRight.Height);
        Size Scale(Size radius) => radius.Width == 0 || radius.Height == 0 ? default : new(radius.Width * scale, radius.Height * scale);
        return new(Scale(TopLeft), Scale(TopRight), Scale(BottomRight), Scale(BottomLeft));
    }

    internal CssUsedBorderRadii Inset(Thickness border)
    {
        static Size Subtract(Size radius, double x, double y) => new(Math.Max(0, radius.Width - x), Math.Max(0, radius.Height - y));
        return new(Subtract(TopLeft, border.Left, border.Top), Subtract(TopRight, border.Right, border.Top),
            Subtract(BottomRight, border.Right, border.Bottom), Subtract(BottomLeft, border.Left, border.Bottom));
    }

    internal CssUsedBorderRadii Offset(Thickness outsets, Size resultSize)
    {
        // CSS shadow-shape adjustment also defines overflow clip edge corners.
        // Work from the border-edge radii: insetting first loses a small corner
        // that can reappear when a positive clip margin partly cancels the inset.
        static double Radius(double radius, double outset)
        {
            if (outset <= 0) return Math.Max(0, radius + outset);
            var factor = radius < outset ? 1 + Math.Pow(radius / outset - 1, 3) : 1;
            return radius + outset * factor;
        }
        Size Corner(Size radius, double x, double y) => new(Radius(radius.Width, x), Radius(radius.Height, y));
        return new CssUsedBorderRadii(
            Corner(TopLeft, outsets.Left, outsets.Top),
            Corner(TopRight, outsets.Right, outsets.Top),
            Corner(BottomRight, outsets.Right, outsets.Bottom),
            Corner(BottomLeft, outsets.Left, outsets.Bottom)).Normalize(resultSize);
    }

    internal CssUsedBorderRadii Mask(ClipEdges edges)
    {
        Size Keep(Size radius, ClipEdges adjacent) => (edges & adjacent) == adjacent ? radius : default;
        return new(Keep(TopLeft, ClipEdges.Left | ClipEdges.Top), Keep(TopRight, ClipEdges.Right | ClipEdges.Top),
            Keep(BottomRight, ClipEdges.Right | ClipEdges.Bottom), Keep(BottomLeft, ClipEdges.Left | ClipEdges.Bottom));
    }
}

/// <summary>One shared elliptical contour for CSS background, border, clipping and input.</summary>
internal sealed class CssRoundedRectangleGeometry : Geometry
{
    internal Rect Rect { get; }
    internal CssUsedBorderRadii Radii { get; }
    internal Rect ClipReferenceRect { get; }
    internal ClipEdges ClipEdges { get; }
    internal PathGeometry Path { get; }
    public override Rect Bounds => Rect;

    internal CssRoundedRectangleGeometry(Rect rect, CssUsedBorderRadii radii, ClipEdges clipEdges = ClipEdges.All, Rect? clipReference = null)
    {
        Rect = rect;
        Radii = radii;
        ClipReferenceRect = clipReference ?? rect;
        ClipEdges = clipEdges;
        Path = new PathGeometry();
        if (rect.Width > 0 && rect.Height > 0) Path.Figures.Add(Figure(rect, radii));
        // Insets can put an inner ellipse's centre beyond the opposite edge.
        // Clip that contour; renormalizing it would change the outer curve's centre.
        if (rect.Width > 0 && rect.Height > 0 && (radii.TopLeft.Width + radii.TopRight.Width > rect.Width ||
            radii.BottomLeft.Width + radii.BottomRight.Width > rect.Width ||
            radii.TopLeft.Height + radii.BottomLeft.Height > rect.Height ||
            radii.TopRight.Height + radii.BottomRight.Height > rect.Height))
            Path = Geometry.Combine(Path, new RectangleGeometry(rect), GeometryCombineMode.Intersect, null);
        Path.Freeze();
    }

    internal static PathFigure Figure(Rect rect, CssUsedBorderRadii radii)
    {
        static Size SquareZero(Size radius) => radius.Width > 0 && radius.Height > 0 ? radius : default;
        var tl = SquareZero(radii.TopLeft); var tr = SquareZero(radii.TopRight);
        var br = SquareZero(radii.BottomRight); var bl = SquareZero(radii.BottomLeft);
        var figure = new PathFigure { StartPoint = new(rect.Left + tl.Width, rect.Top), IsClosed = true, IsFilled = true };
        void Line(double x, double y) => figure.Segments.Add(new LineSegment(new(x, y), true));
        void Arc(double x, double y, Size radius)
        {
            if (radius.Width > 0 && radius.Height > 0)
                figure.Segments.Add(new ArcSegment(new(x, y), radius, 0, false, SweepDirection.Clockwise, true));
        }
        Line(rect.Right - tr.Width, rect.Top); Arc(rect.Right, rect.Top + tr.Height, tr);
        Line(rect.Right, rect.Bottom - br.Height); Arc(rect.Right - br.Width, rect.Bottom, br);
        Line(rect.Left + bl.Width, rect.Bottom); Arc(rect.Left, rect.Bottom - bl.Height, bl);
        Line(rect.Left, rect.Top + tl.Height); Arc(rect.Left + tl.Width, rect.Top, tl);
        return figure;
    }

    internal double SignedDistance(Point point) => SignedDistance(Rect, Radii, point);

    internal static bool Contains(Rect rect, CssUsedBorderRadii radii, Point point)
        => rect.Width > 0 && rect.Height > 0 && SignedDistance(rect, radii, point) <= 0;

    private static double SignedDistance(Rect rect, CssUsedBorderRadii radii, Point point)
    {
        var distance = Math.Max(Math.Max(rect.Left - point.X, point.X - rect.Right), Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
        void Corner(Size radius, double dx, double dy)
        {
            if (radius.Width <= 0 || radius.Height <= 0 || dx >= radius.Width || dy >= radius.Height) return;
            var x = (dx - radius.Width) / radius.Width; var y = (dy - radius.Height) / radius.Height;
            var length = Math.Sqrt(x * x + y * y);
            var gradient = Math.Sqrt(x * x / (radius.Width * radius.Width) + y * y / (radius.Height * radius.Height));
            if (gradient > 0) distance = Math.Max(distance, length * (length - 1) / gradient);
        }
        Corner(radii.TopLeft, point.X - rect.Left, point.Y - rect.Top);
        Corner(radii.TopRight, rect.Right - point.X, point.Y - rect.Top);
        Corner(radii.BottomRight, rect.Right - point.X, rect.Bottom - point.Y);
        Corner(radii.BottomLeft, point.X - rect.Left, rect.Bottom - point.Y);
        return distance;
    }

    public override bool FillContains(Point point) => Rect.Width > 0 && Rect.Height > 0 && SignedDistance(point) <= 0;
    public override bool MayHaveCurves() => true;
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
        => Path.GetFlattenedPathGeometry(tolerance, toleranceType);
    protected override Freezable CreateInstanceCore() => new CssRoundedRectangleGeometry(Rect, Radii, ClipEdges, ClipReferenceRect);
}
