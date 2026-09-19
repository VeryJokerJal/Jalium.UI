using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Stroke style of the element outline. <see cref="Solid"/> is the default so that setting
/// only <c>OutlineBrush</c> + <c>OutlineThickness</c> shows a ring (mirroring the
/// BorderBrush/BorderThickness intuition); <see cref="None"/> exists for CSS
/// <c>outline-style: none</c>, which must disable the ring without touching the brush.
/// </summary>
public enum OutlineStyle
{
    Solid,
    Dashed,
    Dotted,
    None,
}

public partial class FrameworkElement
{
    /// <summary>
    /// CSS-style outline: a ring drawn OUTSIDE the element bounds that takes no layout
    /// space (Measure/Arrange are unaffected). Drawn after children and the element
    /// effect, outside the element's own layout clip; ancestor clips still apply,
    /// matching CSS overflow semantics.
    /// </summary>
    public static readonly DependencyProperty OutlineBrushProperty =
        DependencyProperty.Register(
            nameof(OutlineBrush), typeof(Brush), typeof(FrameworkElement),
            new PropertyMetadata(null, OnOutlinePropertyChanged));

    public static readonly DependencyProperty OutlineThicknessProperty =
        DependencyProperty.Register(
            nameof(OutlineThickness), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnOutlinePropertyChanged));

    public static readonly DependencyProperty OutlineStyleProperty =
        DependencyProperty.Register(
            nameof(OutlineStyle), typeof(OutlineStyle), typeof(FrameworkElement),
            new PropertyMetadata(OutlineStyle.Solid, OnOutlinePropertyChanged));

    /// <summary>Distance between the element edge and the ring; negative values pull it inward.</summary>
    public static readonly DependencyProperty OutlineOffsetProperty =
        DependencyProperty.Register(
            nameof(OutlineOffset), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnOutlinePropertyChanged));

    public Brush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty) as Brush;
        set => SetValue(OutlineBrushProperty, value);
    }

    public double OutlineThickness
    {
        get => (double)(GetValue(OutlineThicknessProperty) ?? 0.0);
        set => SetValue(OutlineThicknessProperty, value);
    }

    public OutlineStyle OutlineStyle
    {
        get => (OutlineStyle)(GetValue(OutlineStyleProperty) ?? OutlineStyle.Solid);
        set => SetValue(OutlineStyleProperty, value);
    }

    public double OutlineOffset
    {
        get => (double)(GetValue(OutlineOffsetProperty) ?? 0.0);
        set => SetValue(OutlineOffsetProperty, value);
    }

    // Per-frame render gate and dirty padding, mirrored on the UI thread by the DP
    // callback so the render walk and the background dirty-registration thread never
    // touch DP getters (same contract as Border._liquidGlassDirtyPadding).
    private volatile bool _hasOutline;
    private volatile int _outlineDirtyPadding;

    private Pen? _cachedOutlinePen;
    private Brush? _cachedOutlinePenBrush;
    private double _cachedOutlinePenThickness;
    private OutlineStyle _cachedOutlinePenStyle;
    private PathGeometry? _cachedOutlineGeometry;
    private Rect _cachedOutlineGeometryRect;
    private CornerRadius _cachedOutlineGeometryRadius;

    private static DashStyle? s_outlineDotStyle;

    internal override double GetExtraDirtyPadding() => _outlineDirtyPadding;

    private static void OnOutlinePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var hasOutline = element.OutlineBrush is not null &&
                         element.OutlineThickness > 0 &&
                         element.OutlineStyle != OutlineStyle.None;
        element._hasOutline = hasOutline;

        if (hasOutline)
        {
            // Monotonic max: shrinking the ring must keep the old, larger dirty extent
            // registered or the previous ring leaves residue (a few px of over-
            // invalidation is the accepted cost, same trade as LiquidGlass).
            var padding = (int)Math.Ceiling(Math.Max(0.0, element.OutlineOffset + element.OutlineThickness));
            element._outlineDirtyPadding = Math.Max(element._outlineDirtyPadding, padding);
        }

        if (ReferenceEquals(e.Property, OutlineBrushProperty) ||
            ReferenceEquals(e.Property, OutlineThicknessProperty) ||
            ReferenceEquals(e.Property, OutlineStyleProperty))
        {
            element._cachedOutlinePen = null;
        }

        element.InvalidateVisual();
    }

    internal override void RenderOutlineCore(DrawingContext drawingContext)
    {
        if (!_hasOutline)
        {
            return;
        }

        var brush = OutlineBrush;
        var thickness = OutlineThickness;
        var style = OutlineStyle;
        if (brush is null || thickness <= 0 || style == OutlineStyle.None)
        {
            return;
        }

        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // Stroke centreline sits offset + thickness/2 outside the element rect.
        var expand = OutlineOffset + thickness / 2.0;
        var rect = new Rect(-expand, -expand, size.Width + 2 * expand, size.Height + 2 * expand);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var cornerRadius = Visual.GetCornerRadius(this);
        var outer = new CornerRadius(
            cornerRadius.TopLeft > 0 ? Math.Max(0, cornerRadius.TopLeft + expand) : 0,
            cornerRadius.TopRight > 0 ? Math.Max(0, cornerRadius.TopRight + expand) : 0,
            cornerRadius.BottomRight > 0 ? Math.Max(0, cornerRadius.BottomRight + expand) : 0,
            cornerRadius.BottomLeft > 0 ? Math.Max(0, cornerRadius.BottomLeft + expand) : 0);

        var pen = GetOrCreateOutlinePen(brush, thickness, style);
        if (style == OutlineStyle.Solid)
        {
            drawingContext.DrawRoundedRectangle(null, pen, rect, outer);
        }
        else
        {
            // The native per-corner rounded-rect stroke ignores Pen.DashStyle; dashed
            // strokes must go through the geometry path's managed dashed stroking.
            var geometry = GetOrCreateOutlineGeometry(rect, outer);
            drawingContext.DrawGeometry(null, pen, geometry);
        }
    }

    private Pen GetOrCreateOutlinePen(Brush brush, double thickness, OutlineStyle style)
    {
        if (_cachedOutlinePen is { } cached &&
            ReferenceEquals(_cachedOutlinePenBrush, brush) &&
            _cachedOutlinePenThickness == thickness &&
            _cachedOutlinePenStyle == style)
        {
            return cached;
        }

        var pen = new Pen(brush, thickness);
        switch (style)
        {
            case OutlineStyle.Dashed:
                pen.DashStyle = DashStyles.Dash;
                break;
            case OutlineStyle.Dotted:
                // DashStyles.Dot uses zero-length dashes that rely on round caps; the
                // managed dashed stroker draws butt segments, so use 1×thickness squares.
                pen.DashStyle = s_outlineDotStyle ??= CreateFrozenDotStyle();
                break;
        }

        _cachedOutlinePen = pen;
        _cachedOutlinePenBrush = brush;
        _cachedOutlinePenThickness = thickness;
        _cachedOutlinePenStyle = style;
        return pen;
    }

    private static DashStyle CreateFrozenDotStyle()
    {
        var style = new DashStyle();
        style.Dashes.Add(1.0);
        style.Dashes.Add(2.0);
        if (style.CanFreeze)
        {
            style.Freeze();
        }

        return style;
    }

    private PathGeometry GetOrCreateOutlineGeometry(Rect rect, CornerRadius cornerRadius)
    {
        if (_cachedOutlineGeometry is { } cached &&
            _cachedOutlineGeometryRect == rect &&
            _cachedOutlineGeometryRadius == cornerRadius)
        {
            return cached;
        }

        var geometry = DrawingContext.CreateRoundedRectGeometry(rect, cornerRadius);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        _cachedOutlineGeometry = geometry;
        _cachedOutlineGeometryRect = rect;
        _cachedOutlineGeometryRadius = cornerRadius;
        return geometry;
    }
}
