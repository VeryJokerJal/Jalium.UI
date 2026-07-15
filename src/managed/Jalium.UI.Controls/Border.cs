using Jalium.UI.Input;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

namespace Jalium.UI.Controls;

/// <summary>
/// Draws a border, background, or both around another element.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Child")]
public class Border : Decorator
{
    private const float DefaultGlassTintChannel = 0.08f;
    private const float DefaultGlassTintOpacity = 0.3f;

    private Pen? _cachedBorderPen;
    private Brush? _cachedBorderBrush;
    private double _cachedBorderWidth;

    // Liquid glass mouse-following highlight (window-level tracking)
    private Point _lgLightLocal;
    private bool _lgMouseOver;
    private bool _lgEventsWired;
    private Window? _lgTrackingWindow;

    // Liquid glass spring press interaction
    private SpringAxis _lgSpringX = new() { Position = 1.0, Target = 1.0 };
    private SpringAxis _lgSpringY = new() { Position = 1.0, Target = 1.0 };
    private SpringAxis _lgSpringOffX;
    private SpringAxis _lgSpringOffY;
    private bool _lgPressed;
    private Point _lgPressPoint;
    private bool _lgSpringSubscribed;
    private long _lgLastTickTime;
    private bool _lgPushedTransform;
    private bool _lgPushedNativeTextTransform;
    private float _lgHighlightBoost;

    // GetExtraDirtyPadding can run on a background dirty-registration thread,
    // so publish the UI-thread-computed ink extent through an atomic field.
    private volatile int _liquidGlassDirtyPadding = 32;

    // Liquid glass fusion: screen-space rect cached from last render
    internal Rect _lgScreenRect;
    internal float _lgAvgCornerRadius;
    private bool _lgFusionRetryPending;

    private const double LgPressScale = 0.97;
    private const double LgPressStiffness = 1200.0;
    private const double LgReleaseStiffness = 800.0;
    private const double LgDampingX = 0.6;
    private const double LgDampingY = 0.7;
    private const double LgOffsetDamping = 0.45;
    private const double LgOffsetStiffness = 400.0;
    private const double LgDragPower = 0.5;
    private const double LgDragScale = 2.5;
    private const double LgPerpendicularCompress = 0.3;
    private const double LgDragAsymmetry = 0.7;

    private Pen? GetOrCreateBorderPen(Brush borderBrush, double borderWidth)
    {
        if (borderBrush == null || borderWidth <= 0)
            return null;

        // Check if cache is still valid
        if (_cachedBorderPen != null &&
            _cachedBorderBrush == borderBrush &&
            _cachedBorderWidth == borderWidth)
        {
            return _cachedBorderPen;
        }

        // Create new Pen
        _cachedBorderPen = new Pen(borderBrush, borderWidth);
        _cachedBorderBrush = borderBrush;
        _cachedBorderWidth = borderWidth;

        return _cachedBorderPen;
    }

    private void InvalidateBorderPenCache()
    {
        _cachedBorderPen = null;
        _cachedBorderBrush = null;
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(Border),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(Border),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(Border),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the CornerRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(Border),
            new PropertyMetadata(new CornerRadius(0), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(Border),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlass dependency property.
    /// When true, renders the background using the liquid glass effect
    /// with SDF-based refraction, edge highlights, and inner shadow.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LiquidGlassProperty =
        DependencyProperty.Register(nameof(LiquidGlass), typeof(bool), typeof(Border),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlassBlurRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LiquidGlassBlurRadiusProperty =
        DependencyProperty.Register(nameof(LiquidGlassBlurRadius), typeof(double), typeof(Border),
            new PropertyMetadata(8.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlassRefractionAmount dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LiquidGlassRefractionAmountProperty =
        DependencyProperty.Register(nameof(LiquidGlassRefractionAmount), typeof(double), typeof(Border),
            new PropertyMetadata(60.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlassChromaticAberration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LiquidGlassChromaticAberrationProperty =
        DependencyProperty.Register(nameof(LiquidGlassChromaticAberration), typeof(double), typeof(Border),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlassInteractive dependency property.
    /// When true, enables spring-based press animation on the liquid glass effect.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LiquidGlassInteractiveProperty =
        DependencyProperty.Register(nameof(LiquidGlassInteractive), typeof(bool), typeof(Border),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LiquidGlassFusionRadius dependency property.
    /// Controls the smooth-min radius (in pixels) for fusion between adjacent liquid glass panels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LiquidGlassFusionRadiusProperty =
        DependencyProperty.Register(nameof(LiquidGlassFusionRadius), typeof(double), typeof(Border),
            new PropertyMetadata(30.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Shape dependency property.
    /// Controls whether the border uses standard rounded rectangle or superellipse shape.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShapeProperty =
        DependencyProperty.Register(nameof(Shape), typeof(BorderShape), typeof(Border),
            new PropertyMetadata(BorderShape.RoundedRectangle, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SuperEllipseN dependency property.
    /// Controls the superellipse exponent when Shape is SuperEllipse. Default is 4 (iOS-style squircle).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SuperEllipseNProperty =
        DependencyProperty.Register(nameof(SuperEllipseN), typeof(double), typeof(Border),
            new PropertyMetadata(4.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding inside the border.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to render the liquid glass effect.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool LiquidGlass
    {
        get => (bool)GetValue(LiquidGlassProperty)!;
        set => SetValue(LiquidGlassProperty, value);
    }

    /// <summary>
    /// Gets or sets the liquid glass blur radius (default 8).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LiquidGlassBlurRadius
    {
        get => (double)GetValue(LiquidGlassBlurRadiusProperty)!;
        set => SetValue(LiquidGlassBlurRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the liquid glass refraction amount (default 60).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LiquidGlassRefractionAmount
    {
        get => (double)GetValue(LiquidGlassRefractionAmountProperty)!;
        set => SetValue(LiquidGlassRefractionAmountProperty, value);
    }

    /// <summary>
    /// Gets or sets the chromatic aberration amount (0-1, default 0).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LiquidGlassChromaticAberration
    {
        get => (double)GetValue(LiquidGlassChromaticAberrationProperty)!;
        set => SetValue(LiquidGlassChromaticAberrationProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the liquid glass effect responds to press with a spring animation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool LiquidGlassInteractive
    {
        get => (bool)GetValue(LiquidGlassInteractiveProperty)!;
        set => SetValue(LiquidGlassInteractiveProperty, value);
    }

    /// <summary>
    /// Gets or sets the fusion radius for merging adjacent liquid glass panels.
    /// Higher values create a wider, smoother blend between nearby glass panels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LiquidGlassFusionRadius
    {
        get => (double)GetValue(LiquidGlassFusionRadiusProperty)!;
        set => SetValue(LiquidGlassFusionRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the border shape (RoundedRectangle or SuperEllipse).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public BorderShape Shape
    {
        get => (BorderShape)(GetValue(ShapeProperty) ?? BorderShape.RoundedRectangle);
        set => SetValue(ShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets the superellipse exponent (default 4.0, iOS-style squircle).
    /// Only used when Shape is SuperEllipse. Higher values = more rectangular, lower = more circular.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SuperEllipseN
    {
        get => (double)GetValue(SuperEllipseNProperty)!;
        set => SetValue(SuperEllipseNProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // BorderThickness 必须按 ArrangeOverride 同样的物理像素 snap 计算,
        // 否则 MeasureOverride 用 raw 1px、ArrangeOverride 用 snapped(如 DPI=1.5 时 ≈1.333px),
        // 子元素 measure 时以为自己有 N-2px 空间,arrange 实际只拿到 N-2.667px,
        // 子内容刚好溢出 ScrollViewer viewport,会冒出 0.5–0.7px 的虚假滚动条。
        var rawBorder = BorderThickness;
        var snappedLeft = SnapLayoutValue(rawBorder.Left);
        var snappedTop = SnapLayoutValue(rawBorder.Top);
        var snappedRight = SnapLayoutValue(rawBorder.Right);
        var snappedBottom = SnapLayoutValue(rawBorder.Bottom);

        var padding = Padding;
        var totalHorizontal = snappedLeft + snappedRight + padding.Left + padding.Right;
        var totalVertical = snappedTop + snappedBottom + padding.Top + padding.Bottom;

        var childAvailable = new Size(
            Math.Max(0, availableSize.Width - totalHorizontal),
            Math.Max(0, availableSize.Height - totalVertical));

        var childSize = default(Size);

        UIElement? child = Child;
        if (child != null)
        {
            child.Measure(childAvailable);
            childSize = child.DesiredSize;
        }

        return new Size(
            childSize.Width + totalHorizontal,
            childSize.Height + totalVertical);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rawBorder = BorderThickness;
        var padding = Padding;

        UIElement? child = Child;
        if (child != null)
        {
            // Snap each side of BorderThickness independently before computing
            // the inner rect so the child's _visualBounds lines up exactly
            // with the background/stroke geometry OnRender paints — both
            // sides of the equation reduce the snapped per-side BorderThickness
            // from the available size, so there is no 0.5 px disagreement
            // mid-transition (raw BT=1.5 → snapped left=2, snapped right=2,
            // inner rect = full - 4 — matching what OnRender computes).
            // Padding is *not* snapped; it stays in DIPs since the user
            // expressed it in DIPs and it's the same on every frame.
            var snappedLeft = SnapLayoutValue(rawBorder.Left);
            var snappedTop = SnapLayoutValue(rawBorder.Top);
            var snappedRight = SnapLayoutValue(rawBorder.Right);
            var snappedBottom = SnapLayoutValue(rawBorder.Bottom);

            var leftInset = snappedLeft + padding.Left;
            var topInset = snappedTop + padding.Top;
            var rightInset = snappedRight + padding.Right;
            var bottomInset = snappedBottom + padding.Bottom;

            // right / bottom 不要再 SnapLayoutValue —— 那是按"最近物理像素四舍五入",
            // 当 finalSize 小数部分 < 0.5 时会向下取整,childRect 比 MeasureOverride 用
            // (snapped 的 totalHorizontal/Vertical) 算出来的 childAvailable 少 0.25–0.5px,
            // 子元素 measure 时拿到 N-totalInset 空间,arrange 实际只拿 SnapDown(N-totalInset),
            // 内嵌 ScrollViewer 的 _extentHeight 比 finalSize.Height 多 0.25–0.5,
            // 触发 Auto 模式下的虚假 0.5px 滚动条(典型 ComboBox dropdown 表现)。
            // x / y(insets)继续 snap,因为它们是从原点向内的偏移,需要和 OnRender 画的
            // background/stroke 顶左对齐;right / bottom 直接用精确值,允许子元素的右下边
            // 落在亚像素位置,边缘最多 0.5px 落进 stroke 区——而 stroke 在 OnPostRender
            // 之后画,会盖住子内容的亚像素溢出,视觉上无副作用。
            var x = SnapLayoutValue(leftInset);
            var y = SnapLayoutValue(topInset);
            var right = finalSize.Width - rightInset;
            var bottom = finalSize.Height - bottomInset;

            var childRect = new Rect(
                x,
                y,
                Math.Max(0, right - x),
                Math.Max(0, bottom - y));

            child.Arrange(childRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected internal override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // FrameworkElement applies layout rounding after ArrangeOverride. Publish
        // padding from the final RenderSize so the shader and dirty bounds use the
        // same dimensions even at fractional DPI/layout sizes.
        UpdateLiquidGlassDirtyPadding(sizeInfo.NewSize);
    }

    /// <inheritdoc />
    internal override Geometry? GetLayoutClip()
    {
        if (!ClipToBounds)
            return null;

        // Clip to the Border's *inner* shape — the same rect + per-corner radii
        // that OnRender uses for the Background fill, NOT the outer RenderSize box.
        //
        // Why inner instead of outer:
        //   The stroke ring sits between the outer shape and the inner shape,
        //   `BorderThickness` pixels wide. If the layout clip were the outer
        //   shape, child fragments along the corner arc would smoothstep-fade
        //   past the visible Background and bleed 1–2 px into the stroke ring —
        //   exactly the "red leaks past the grey stroke" artefact observed on
        //   nested rounded panels. Clipping at the inner shape stops child
        //   content cleanly at the stroke's inner edge.
        //
        // Why this doesn't truncate the stroke:
        //   Visual.RenderDirect now pops the layout clip BEFORE invoking
        //   OnPostRender (where Border paints its stroke). So the stroke is
        //   rendered outside the inner clip, on the outer/centred ring as
        //   intended; only children and the Border's own Background pass
        //   through the inner clip, and both already conform to the inner
        //   shape exactly. Result: stroke draws full width; child fragments
        //   stop at the stroke's inner edge; nothing leaks into or past the
        //   BorderThickness ring.
        var rawBorder = BorderThickness;
        var snappedLeft = SnapLayoutValue(rawBorder.Left);
        var snappedTop = SnapLayoutValue(rawBorder.Top);
        var snappedRight = SnapLayoutValue(rawBorder.Right);
        var snappedBottom = SnapLayoutValue(rawBorder.Bottom);

        var innerLeft = snappedLeft;
        var innerTop = snappedTop;
        var innerRight = _renderSize.Width - snappedRight;
        var innerBottom = _renderSize.Height - snappedBottom;
        var clipRect = new Rect(
            innerLeft,
            innerTop,
            Math.Max(0, innerRight - innerLeft),
            Math.Max(0, innerBottom - innerTop));

        if (Shape == BorderShape.SuperEllipse)
        {
            return CreateSuperEllipseGeometry(clipRect, SuperEllipseN);
        }

        var cornerRadius = CornerRadius;
        // Per-corner inner radius — same WPF formula OnRender uses for the
        // Background fill, so the layout clip matches the Background outline.
        var innerRadius = new CornerRadius(
            Math.Max(0, cornerRadius.TopLeft - Math.Max(snappedLeft, snappedTop)),
            Math.Max(0, cornerRadius.TopRight - Math.Max(snappedTop, snappedRight)),
            Math.Max(0, cornerRadius.BottomRight - Math.Max(snappedRight, snappedBottom)),
            Math.Max(0, cornerRadius.BottomLeft - Math.Max(snappedBottom, snappedLeft)));

        var maxRadius = Math.Max(
            Math.Max(innerRadius.TopLeft, innerRadius.TopRight),
            Math.Max(innerRadius.BottomRight, innerRadius.BottomLeft));

        if (maxRadius > 0)
        {
            return new RectangleGeometry(clipRect, innerRadius);
        }

        return new RectangleGeometry(clipRect);
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Liquid glass opts out of the retained-mode render cache. The effect
    /// is inherently per-frame: it samples a fresh background snapshot for
    /// refraction, tracks the mouse-follow light position, discovers
    /// sibling glass panels for SDF fusion, and — when
    /// <see cref="LiquidGlassInteractive"/> is set — drives a spring
    /// animation whose <c>PushTransform</c> in <c>OnRender</c> must pair
    /// with the matching <c>Pop</c> in <c>OnPostRender</c> in the same
    /// render pass. Caching would either replay stale geometry on later
    /// frames or desync the push / pop bookkeeping across record-vs-replay
    /// boundaries. Plain borders (no <c>LiquidGlass</c>) still benefit from
    /// the cache; only LG-bearing borders render immediate-mode.
    /// </summary>
    protected override bool ParticipatesInRenderCache => !LiquidGlass;

    private static StreamGeometry CreateSuperEllipseGeometry(Rect rect, double n)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double cx = rect.X + rect.Width / 2;
            double cy = rect.Y + rect.Height / 2;
            double a = rect.Width / 2;
            double b = rect.Height / 2;

            // Bezier kappa: matches the superellipse midpoint at the diagonal
            double k = (Math.Pow(0.5, 1.0 / n) - 0.5) / 0.375;

            ctx.BeginFigure(new Point(cx + a, cy), true, true);

            // Right 閳?Bottom
            ctx.BezierTo(
                new Point(cx + a, cy + b * k),
                new Point(cx + a * k, cy + b),
                new Point(cx, cy + b), true, false);

            // Bottom 閳?Left
            ctx.BezierTo(
                new Point(cx - a * k, cy + b),
                new Point(cx - a, cy + b * k),
                new Point(cx - a, cy), true, false);

            // Left 閳?Top
            ctx.BezierTo(
                new Point(cx - a, cy - b * k),
                new Point(cx - a * k, cy - b),
                new Point(cx, cy - b), true, false);

            // Top 閳?Right
            ctx.BezierTo(
                new Point(cx + a * k, cy - b),
                new Point(cx + a, cy - b * k),
                new Point(cx + a, cy), true, false);
        }
        return geo;
    }

    /// <summary>
    /// Computes the exact SDF rectangle submitted to the liquid-glass shader.
    /// Dirty-bound tracking calls this same method so it cannot drift from the
    /// animated geometry painted by either native backend.
    /// </summary>
    internal Rect ComputeLiquidGlassRect(Rect rect)
    {
        if (!LiquidGlass || !LiquidGlassInteractive)
            return rect;

        double scaleX = _lgSpringX.Position;
        double scaleY = _lgSpringY.Position;
        double offX = _lgSpringOffX.Position;
        double offY = _lgSpringOffY.Position;
        bool hasSpring = scaleX != 1.0 || scaleY != 1.0 ||
                         offX != 0 || offY != 0 ||
                         _lgSpringX.Velocity != 0 || _lgSpringY.Velocity != 0 ||
                         _lgSpringOffX.Velocity != 0 || _lgSpringOffY.Velocity != 0;
        if (!hasSpring)
            return rect;

        double rectWidth = Math.Max(rect.Width, 1.0);
        double rectHeight = Math.Max(rect.Height, 1.0);
        double shapeScaleX = (rectWidth + Math.Abs(offX)) / rectWidth * scaleX *
                             (1.0 - Math.Abs(offY) / rectHeight * LgPerpendicularCompress);
        double shapeScaleY = (rectHeight + Math.Abs(offY)) / rectHeight * scaleY *
                             (1.0 - Math.Abs(offX) / rectWidth * LgPerpendicularCompress);
        shapeScaleX = Math.Clamp(shapeScaleX, 0.1, 3.2);
        shapeScaleY = Math.Clamp(shapeScaleY, 0.1, 3.2);

        double deformedWidth = rect.Width * shapeScaleX;
        double deformedHeight = rect.Height * shapeScaleY;
        return new Rect(
            rect.Width / 2.0 - deformedWidth / 2.0 + offX * LgDragAsymmetry,
            rect.Height / 2.0 - deformedHeight / 2.0 + offY * LgDragAsymmetry,
            Math.Max(1, deformedWidth),
            Math.Max(1, deformedHeight));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var rawBorder = BorderThickness;
        var cornerRadius = CornerRadius;

        // Snap each side of BorderThickness to physical pixels so the stroke,
        // the Background fill, and the child's _visualBounds all land on the
        // same pixel rows/columns. ArrangeCore snaps _visualBounds origin via
        // SnapLayoutValue; if we used the raw fractional BorderThickness for
        // stroke/background here, mid-transition values like 1.5 would put
        // the stroke inner edge at X=1.5 while the snapped child arrives at
        // X=2 — the 0.5 px seam between them shows through to whatever lies
        // behind the Border (typically the window background), which is the
        // visible "BorderThickness doesn't work during transition" symptom.
        //
        // Snapping per side rather than collapsing to a single uniform value
        // preserves asymmetric-thickness behaviour; rendering will only ever
        // disagree with rawBorder by sub-pixel amounts that the layout
        // pipeline already discarded.
        var border = new Thickness(
            SnapLayoutValue(rawBorder.Left),
            SnapLayoutValue(rawBorder.Top),
            SnapLayoutValue(rawBorder.Right),
            SnapLayoutValue(rawBorder.Bottom));
        var borderWidth = Math.Max(border.Left, Math.Max(border.Top, Math.Max(border.Right, border.Bottom)));
        var halfBorder = borderWidth / 2;

        // Compute elastic deformation parameters for spring press interaction.
        //
        // Two scales are tracked independently:
        //  - lgSx / lgSy — SHAPE deformation for the SDF glass rect.
        //    Drag stretch at full magnitude + press scale + perpendicular
        //    compression, giving the glass its "liquid being pulled"
        //    silhouette.
        //  - lgContentSx / lgContentSy — CONTENT transform for background,
        //    border, and children. Same functional form as the shape, but
        //    the drag stretch / compression contributions are attenuated
        //    by LgContentDeformationGain. At gain=1 content tracks the
        //    shape exactly (reads as "content magnified"); at gain=0
        //    content ignores the drag entirely (reads as "glass is liquid
        //    but content is rigid, no pull-through"). The intermediate
        //    value gives the intended taffy-like feel: content visibly
        //    follows the pull of the glass but does not balloon in size.
        _lgHighlightBoost = 0f;
        _lgPushedTransform = false;
        _lgPushedNativeTextTransform = false;
        double lgSx = 1.0, lgSy = 1.0, lgShiftX = 0, lgShiftY = 0;
        double lgContentSx = 1.0, lgContentSy = 1.0;
        bool lgHasSpring = false;
        if (LiquidGlass && LiquidGlassInteractive)
        {
            double scaleX = _lgSpringX.Position;
            double scaleY = _lgSpringY.Position;
            double offX = _lgSpringOffX.Position;
            double offY = _lgSpringOffY.Position;
            lgHasSpring = scaleX != 1.0 || scaleY != 1.0 ||
                          offX != 0 || offY != 0 ||
                          _lgSpringX.Velocity != 0 || _lgSpringY.Velocity != 0 ||
                          _lgSpringOffX.Velocity != 0 || _lgSpringOffY.Velocity != 0;
            if (lgHasSpring)
            {
                double tx = offX;
                double ty = offY;

                double stretchW = Math.Abs(tx);
                double stretchH = Math.Abs(ty);
                double rectWidth = Math.Max(rect.Width, 1.0);
                double rectHeight = Math.Max(rect.Height, 1.0);

                double compressW = 1.0 - (stretchH / rectHeight) * LgPerpendicularCompress;
                double compressH = 1.0 - (stretchW / rectWidth) * LgPerpendicularCompress;

                // Shape (SDF glass) gets the full drag stretch + press scale
                // + perpendicular compression.
                lgSx = (rectWidth + stretchW) / rectWidth * scaleX * compressW;
                lgSy = (rectHeight + stretchH) / rectHeight * scaleY * compressH;

                // Safety clamp: prevent runaway scaling from spring divergence
                lgSx = Math.Clamp(lgSx, 0.1, 3.2);
                lgSy = Math.Clamp(lgSy, 0.1, 3.2);

                // Content and border stroke share the exact same scale as
                // the SDF glass silhouette: lgSx / lgSy already carries
                // the drag stretch + press spring + perpendicular
                // compression that the glass is using. Reusing it here
                // makes the inner box (children, child Border strokes,
                // backgrounds) deform in lock-step with the outer glass
                // shape — when the outer rectangle gets squeezed into a
                // taller narrower box, the inner rectangle does the same
                // thing at the same proportions, with the same center.
                //
                // The previous isovolumetric (±gain·dragRatio) formula
                // intentionally kept content's X·Y close to 1 to avoid a
                // perceived zoom, but the side effect was that the inner
                // box only deformed by ~17 % while the outer one
                // deformed by 50 % — visually the inner stayed
                // rectangular while the outer flattened, which is the
                // "only the content didn't deform" symptom.
                lgContentSx = lgSx;
                lgContentSy = lgSy;

                lgShiftX = tx * LgDragAsymmetry;
                lgShiftY = ty * LgDragAsymmetry;

                double minScale = Math.Min(scaleX, scaleY);
                if (minScale < 1.0)
                    _lgHighlightBoost = (float)((1.0 - minScale) / (1.0 - LgPressScale) * 0.15);
            }
        }

        // Draw liquid glass effect (if enabled)
        // The glass effect is drawn WITHOUT a D2D1 transform so the snapshot capture
        // and refracted background content stay stable. Instead, we pass a deformed rect
        // to the shader so the SDF glass shape visually deforms.
        //
        // We route through the base DrawingContext.DrawLiquidGlass virtual so
        // this call survives the retained-mode render cache round-trip: under
        // Visual's cache path, OnRender is invoked against a DrawingRecorder
        // — not the live RenderTargetDrawingContext — and a direct type check
        // would silently drop the glass draw on cached frames. The virtual is
        // a no-op on contexts that don't know how to render glass, a
        // command-record on the recorder, and the real GPU call on the live
        // target; Replayer fans the recorded command back to the live target
        // on every replay.
        if (LiquidGlass)
        {
            // Lazy wiring: OnRender guarantees we're in the visual tree
            if (!_lgEventsWired)
                TryWireLgWindowTracking();

            // Compute the deformed glass rect for the SDF shader
            var glassRect = ComputeLiquidGlassRect(rect);

            var avgRadius = (float)((cornerRadius.TopLeft + cornerRadius.TopRight +
                                     cornerRadius.BottomRight + cornerRadius.BottomLeft) / 4.0);

            // Extract tint color from Background brush if it's a SolidColorBrush
            float tintR = DefaultGlassTintChannel, tintG = DefaultGlassTintChannel, tintB = DefaultGlassTintChannel, tintOpacity = DefaultGlassTintOpacity;
            if (Background is SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                tintR = color.ScR;
                tintG = color.ScG;
                tintB = color.ScB;
                tintOpacity = color.ScA;
            }

            // Pull the current drawing offset through the ambient-state
            // interface so we agree with whatever context we're running
            // against (live RenderTargetDrawingContext, recorder proxying to
            // the live target, or a test context). Defaulting to (0, 0)
            // keeps the call coherent on contexts that don't expose an
            // offset at all.
            var offset = (dc as IOffsetDrawingContext)?.Offset ?? default;

            // Compute screen-space light position from local mouse coordinates
            float lightX = -1f, lightY = -1f;
            if (_lgMouseOver)
            {
                lightX = (float)(_lgLightLocal.X + offset.X);
                lightY = (float)(_lgLightLocal.Y + offset.Y);
            }

            // Cache screen-space rect for neighbor fusion queries
            _lgScreenRect = new Rect(
                glassRect.X + offset.X, glassRect.Y + offset.Y,
                glassRect.Width, glassRect.Height);
            _lgAvgCornerRadius = avgRadius;

            // Discover sibling liquid glass panels for fusion
            float fusionRadius = (float)LiquidGlassFusionRadius;
            int neighborCount = 0;
            float[]? neighborData = null;
            if (fusionRadius > 0 && VisualParent is Panel parentPanel)
            {
                foreach (UIElement child in parentPanel.Children)
                {
                    if (child is Border sibling && sibling != this &&
                        sibling.LiquidGlass && sibling._lgScreenRect.Width > 0)
                    {
                        neighborData ??= new float[20]; // max 4 neighbors × 5 floats
                        int i = neighborCount * 5;
                        neighborData[i + 0] = (float)sibling._lgScreenRect.X;
                        neighborData[i + 1] = (float)sibling._lgScreenRect.Y;
                        neighborData[i + 2] = (float)sibling._lgScreenRect.Width;
                        neighborData[i + 3] = (float)sibling._lgScreenRect.Height;
                        neighborData[i + 4] = sibling._lgAvgCornerRadius;
                        neighborCount++;
                        if (neighborCount >= 4) break;
                    }
                }
            }

            // If we found no neighbors but there are unrendered siblings with LiquidGlass,
            // schedule a deferred re-render so we pick them up on the next pass.
            if (neighborCount == 0 && fusionRadius > 0 && !_lgFusionRetryPending &&
                VisualParent is Panel pp)
            {
                foreach (UIElement c in pp.Children)
                {
                    if (c is Border sib && sib != this &&
                        sib.LiquidGlass && sib._lgScreenRect.Width == 0)
                    {
                        _lgFusionRetryPending = true;
                        Dispatcher.MainDispatcher?.BeginInvoke(() =>
                        {
                            _lgFusionRetryPending = false;
                            InvalidateVisual();
                        });
                        break;
                    }
                }
            }

            // Allocate a fresh parameters instance per frame: under the
            // retained-mode cache the recorder clones it defensively, but on
            // the direct path we want the live target to see exactly these
            // values without cross-frame mutation risk.
            dc.DrawLiquidGlass(new LiquidGlassParameters
            {
                Rectangle = glassRect,
                CornerRadius = avgRadius,
                BlurRadius = (float)LiquidGlassBlurRadius,
                RefractionAmount = (float)LiquidGlassRefractionAmount,
                ChromaticAberration = (float)LiquidGlassChromaticAberration,
                TintR = tintR,
                TintG = tintG,
                TintB = tintB,
                TintOpacity = tintOpacity,
                LightX = lightX,
                LightY = lightY,
                HighlightBoost = _lgHighlightBoost,
                ShapeType = (int)Shape,
                ShapeExponent = (float)SuperEllipseN,
                NeighborCount = neighborCount,
                FusionRadius = fusionRadius,
                NeighborData = neighborData,
            });
        }

        // Now push ScaleTransform for background, border, and children.
        // This is AFTER DrawLiquidGlass so the D2D1 snapshot/effect output is not affected.
        //
        // lgContentSx / lgContentSy currently match the glass SDF shape
        // scales exactly so background, stroke, and children deform in the
        // same local coordinate space. The shift keeps that transformed
        // subtree travelling with the dragged glass silhouette.
        if (lgHasSpring)
        {
            double cx = rect.Width / 2.0;
            double cy = rect.Height / 2.0;
            dc.PushTransform(new MatrixTransform(new Matrix(
                lgContentSx, 0, 0, lgContentSy,
                cx * (1.0 - lgContentSx) + lgShiftX,
                cy * (1.0 - lgContentSy) + lgShiftY)));
            _lgPushedTransform = true;

            if (dc is RenderTargetDrawingContext liveDc)
            {
                liveDc.PushNativeTextTransform();
                _lgPushedNativeTextTransform = true;
            }
        }

        // Draw backdrop effect (if any)
        var backdropEffect = BackdropEffect;
        if (backdropEffect != null && backdropEffect.HasEffect)
        {
            dc.DrawBackdropEffect(rect, backdropEffect, cornerRadius);
        }

        // Draw background and border
        var isSuperEllipse = Shape == BorderShape.SuperEllipse;

        if (isSuperEllipse)
        {
            var seN = SuperEllipseN;

            // SuperEllipse via SetShapeType + DrawRoundedRectangle. SetShapeType is
            // a recordable DrawingContext op, so this works on the live
            // RenderTargetDrawingContext AND through the cached / whole-frame
            // recorder (which replays both in draw order) — the path Android's
            // renderer uses. The previous `else` branch fell back to a Bezier
            // DrawGeometry for non-RenderTarget (recorder) contexts; that squircle
            // fill went through the engine batch which drains AFTER the in-order
            // replay commands, so a card's background painted over its own text.
            // DrawRoundedRectangle under shapeType==1 records in order instead.
            // Contexts that don't model a superellipse get the no-op SetShapeType
            // default and render an ordinary rounded rectangle.
            dc.SetShapeType(1, (float)seN);

            // Pass the element's REAL CornerRadius (not min(w,h)/2). A squircle is
            // a rounded rect with continuous-curvature corners of that radius +
            // straight edges; min(w,h)/2 would collapse a wide/short element into a
            // flattened oval (the "squished" shape). The native squircle tessellator
            // clamps each corner to the half-extent.
            if (Background != null && !LiquidGlass)
            {
                var backgroundRect = new Rect(
                    rect.X + halfBorder,
                    rect.Y + halfBorder,
                    Math.Max(0, rect.Width - borderWidth),
                    Math.Max(0, rect.Height - borderWidth));
                dc.DrawRoundedRectangle(Background, null, backgroundRect, cornerRadius);
            }

            if (BorderBrush != null && borderWidth > 0)
            {
                var pen = GetOrCreateBorderPen(BorderBrush, borderWidth);
                var borderRect = new Rect(
                    rect.X + halfBorder,
                    rect.Y + halfBorder,
                    Math.Max(0, rect.Width - borderWidth),
                    Math.Max(0, rect.Height - borderWidth));
                dc.DrawRoundedRectangle(null, pen, borderRect, cornerRadius);
            }

            dc.SetShapeType(0, 4.0f);
        }
        else
        {
            // Standard rounded rectangle path.
            //
            // The native renderer's stroke path centres the pen on the rect's
            // edge — half the pen sits inside the rect, half sits outside.
            // We deliberately draw the *Background* on the inner rect (the
            // child's arranged rect, inset by BorderThickness on each side)
            // rather than on the stroke's centre rect: the inner-half of the
            // stroke would otherwise overpaint Background pixels that the
            // child needs to read against (text antialiasing, hover-state
            // tints, etc.), which manifests as the border colour bleeding
            // into the content area.
            //
            // Stroke uses the maximum side as a single pen thickness — this
            // is correct when BorderThickness is uniform; for asymmetric
            // thicknesses the four sides receive the max width on every
            // side, which the child's arranged rect masks on the thinner
            // sides as it renders on top of the stroke. (Asymmetric thickness
            // with a fully-transparent child is the only case where this
            // shortcut shows; non-uniform per-side stroke would need a
            // path-fill ring which the native compound-fill pipeline doesn't
            // exactly match the SDF stroke renderer for sub-pixel AA yet.)
            // Inner rect for Background fill / inner-radius compute. Originally
            // this snapped all four sides to physical pixels via SnapLayoutValue,
            // paired with ArrangeCore also snapping _visualBounds origin so that
            // the fill and the child landed on the same integer rows/columns.
            //
            // Now that ArrangeCore lets origins stay as continuous floats (to
            // unblock smooth spring/transition animations), this snap MUST also
            // go: rounding `rect.Width - border.Right` quantises animated widths
            // like 14.967 → 15 each frame, producing the very 1px jitter the
            // float-origin change was meant to eliminate. BorderThickness itself
            // is still snapped per-side (see line ~606) so static borders stay
            // pixel-aligned; only the *fractional remainder* now passes through.
            var innerLeft = rect.X + border.Left;
            var innerTop = rect.Y + border.Top;
            var innerRight = rect.X + rect.Width - border.Right;
            var innerBottom = rect.Y + rect.Height - border.Bottom;

            var innerRect = new Rect(
                innerLeft,
                innerTop,
                Math.Max(0, innerRight - innerLeft),
                Math.Max(0, innerBottom - innerTop));

            if (Background != null && !LiquidGlass)
            {
                var innerRadius = new CornerRadius(
                    Math.Max(0, cornerRadius.TopLeft - Math.Max(border.Left, border.Top)),
                    Math.Max(0, cornerRadius.TopRight - Math.Max(border.Top, border.Right)),
                    Math.Max(0, cornerRadius.BottomRight - Math.Max(border.Right, border.Bottom)),
                    Math.Max(0, cornerRadius.BottomLeft - Math.Max(border.Bottom, border.Left)));

                if (innerRect.Width > 0 && innerRect.Height > 0)
                {
                    dc.DrawRoundedRectangle(Background, null, innerRect, innerRadius);
                }
            }

            // Standard rounded rect 路径下，stroke 绘制延迟到 OnPostRender。
            // 原因：Visual.Render 顺序为 OnRender → children → OnPostRender；
            // 若 stroke 在 OnRender 中绘制，children 在其上渲染 Background 时
            // 亚像素抗锯齿区域会盖住 stroke 内半部分（视觉表现："上面 stroke
            // 看起来 1px、下面看起来 2px"，取决于 child Background 颜色与
            // stroke 颜色的对比度）。把 stroke 推到 OnPostRender 保证它始终
            // 是当前 Border subtree 的最上层。
            // SuperEllipse 路径仍在 OnRender 中绘制（其 children 一般是文字
            // 或图标，不会出现整片 Background 覆盖到 stroke 边缘的情况）。
        }

        // The ScaleTransform pushed AFTER DrawLiquidGlass stays active for children.
        // Visual.Render order: OnRender 閳?children 閳?OnPostRender.
        // Glass gets a deformed rect (SDF shape change); bg/border/children get ScaleTransform.
    }

    /// <inheritdoc />
    /// <remarks>
    /// 这里做两件事：
    ///   1. Border stroke：在 children 渲染之后再画一次 stroke。
    ///      原因：Visual.Render 顺序为 OnRender → children → OnPostRender，
    ///      若 stroke 只在 OnRender 中绘制，children 的 Background 在亚像素
    ///      抗锯齿区域会盖住 stroke 内半边（视觉表现："上面 stroke 看起来
    ///      1px、下面看起来 2px"，取决于 child Background 与 stroke 颜色对比度）。
    ///      把 stroke 放到 OnPostRender 保证它始终是当前 Border subtree 的最上层。
    ///      SuperEllipse 路径的 stroke 已经在 OnRender 中由原生渲染器特殊处理，
    ///      此处不再重画。
    ///      在 Liquid Glass 模式下 stroke 必须在 Pop 之前画——这样描边和子内容
    ///      共享同一个 lgContentSx/lgContentSy 形变矩阵，跟外层 SDF 玻璃保持
    ///      一致的轮廓。否则 stroke 按原 RenderSize 静止绘制，外层玻璃和
    ///      内层 stroke 不再同步，看起来"玻璃被压扁但 border 还在原位"。
    ///   2. Liquid Glass：弹出 OnRender 中 push 的 ScaleTransform。
    /// </remarks>
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        if (drawingContext is DrawingContext dc)
        {
            // 1. Re-draw stroke on top of children, still under the
            //    liquid-glass transform so the border deforms together
            //    with the SDF glass silhouette.
            DrawStrokeAboveChildren(dc);

            // 2. Pop liquid-glass ScaleTransform pushed in OnRender.
            if (_lgPushedTransform)
            {
                dc.Pop();
                _lgPushedTransform = false;
            }

            if (_lgPushedNativeTextTransform)
            {
                if (dc is RenderTargetDrawingContext liveDc)
                {
                    liveDc.PopNativeTextTransform();
                }

                _lgPushedNativeTextTransform = false;
            }
        }
    }

    /// <summary>
    /// 把 BorderBrush stroke 画在 children 之上，避免 child 的 Background 把 stroke
    /// 内半边覆盖掉。仅对 Standard rounded rect 路径生效。
    ///
    /// 对称 BorderThickness（四边相同）走 pen-stroke 快路径，GPU SDF stroke 边缘更锐利。
    /// 非对称 BorderThickness 走 donut-geometry-fill 慢路径：用 outer 圆角矩形 +
    /// inner 圆角矩形 + FillRule.EvenOdd 形成空心 ring 几何，BorderBrush 填充。
    /// 这样每条边的实际厚度由 inner rect 各自 inset 决定，完全独立。
    /// </summary>
    private void DrawStrokeAboveChildren(DrawingContext dc)
    {
        if (BorderBrush == null) return;
        if (Shape == BorderShape.SuperEllipse) return;

        var rawBorder = BorderThickness;
        var border = new Thickness(
            SnapLayoutValue(rawBorder.Left),
            SnapLayoutValue(rawBorder.Top),
            SnapLayoutValue(rawBorder.Right),
            SnapLayoutValue(rawBorder.Bottom));
        if (border.Left <= 0 && border.Top <= 0 && border.Right <= 0 && border.Bottom <= 0) return;

        var rect = new Rect(RenderSize);
        var cornerRadius = CornerRadius;

        var isUniform =
            AreApproximatelyEqual(border.Left, border.Top) &&
            AreApproximatelyEqual(border.Top, border.Right) &&
            AreApproximatelyEqual(border.Right, border.Bottom);

        if (isUniform)
        {
            // Symmetric: pen-stroke fast path (SDF rounded-rect stroke on the GPU).
            var borderWidth = border.Left;
            var halfBorder = borderWidth / 2;
            var pen = GetOrCreateBorderPen(BorderBrush, borderWidth);

            var borderRect = new Rect(
                rect.X + halfBorder,
                rect.Y + halfBorder,
                Math.Max(0, rect.Width - borderWidth),
                Math.Max(0, rect.Height - borderWidth));

            var strokeRadius = new CornerRadius(
                Math.Max(0, cornerRadius.TopLeft - halfBorder),
                Math.Max(0, cornerRadius.TopRight - halfBorder),
                Math.Max(0, cornerRadius.BottomRight - halfBorder),
                Math.Max(0, cornerRadius.BottomLeft - halfBorder));

            dc.DrawRoundedRectangle(null, pen, borderRect, strokeRadius);
            return;
        }

        // Asymmetric: 构造一个空心 ring PathGeometry 用 BorderBrush 填充。
        //   outer figure  = 完整圆角矩形（沿 RenderSize 走外轮廓）
        //   inner figure  = 各边按对应 BorderThickness inset 后的圆角矩形
        //   FillRule.EvenOdd → 两个嵌套 figure 的"差"被填充 = 边框 ring。
        // 每条边的实际可见厚度即 (outer 外缘 - inner 内缘)，所以
        // BorderThickness=(1,0,1,1) 在 top 上 inner 与 outer 共边，top 边自动为 0。
        var innerLeft = rect.X + border.Left;
        var innerTop = rect.Y + border.Top;
        var innerRight = rect.X + rect.Width - border.Right;
        var innerBottom = rect.Y + rect.Height - border.Bottom;
        var innerRect = new Rect(
            innerLeft,
            innerTop,
            Math.Max(0, innerRight - innerLeft),
            Math.Max(0, innerBottom - innerTop));

        // Inner 圆角 = 外圆角减去相邻两边里较大的 thickness（WPF Border 同公式）。
        var innerCorners = new CornerRadius(
            Math.Max(0, cornerRadius.TopLeft - Math.Max(border.Left, border.Top)),
            Math.Max(0, cornerRadius.TopRight - Math.Max(border.Top, border.Right)),
            Math.Max(0, cornerRadius.BottomRight - Math.Max(border.Right, border.Bottom)),
            Math.Max(0, cornerRadius.BottomLeft - Math.Max(border.Bottom, border.Left)));

        var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
        ring.Figures.Add(BuildRoundedRectFigure(rect, cornerRadius));
        if (innerRect.Width > 0 && innerRect.Height > 0)
        {
            ring.Figures.Add(BuildRoundedRectFigure(innerRect, innerCorners));
        }

        dc.DrawGeometry(BorderBrush, null, ring);
    }

    /// <summary>
    /// 构造一个闭合圆角矩形 PathFigure：起点位于 top 边左侧（top-left 圆角后），
    /// 顺时针绕一圈回到起点。圆角用 ArcSegment（与 Jalium.UI 内部其它圆角几何
    /// 一致），半径为 0 的角直接连线段。
    /// </summary>
    private static PathFigure BuildRoundedRectFigure(Rect rect, CornerRadius radii)
    {
        // 归一化：四角半径之和不能超过对应边的长度，避免画出"打结"的几何。
        var normalized = radii.Normalize(rect.Width, rect.Height);
        double tl = normalized.TopLeft;
        double tr = normalized.TopRight;
        double br = normalized.BottomRight;
        double bl = normalized.BottomLeft;

        double x = rect.X;
        double y = rect.Y;
        double w = rect.Width;
        double h = rect.Height;

        var figure = new PathFigure
        {
            StartPoint = new Point(x + tl, y),
            IsClosed = true,
            IsFilled = true,
        };

        // Top edge → top-right corner
        figure.Segments.Add(new LineSegment(new Point(x + w - tr, y), true));
        if (tr > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w, y + tr),
                new Size(tr, tr),
                0, false, SweepDirection.Clockwise, true));
        }

        // Right edge → bottom-right corner
        figure.Segments.Add(new LineSegment(new Point(x + w, y + h - br), true));
        if (br > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w - br, y + h),
                new Size(br, br),
                0, false, SweepDirection.Clockwise, true));
        }

        // Bottom edge → bottom-left corner
        figure.Segments.Add(new LineSegment(new Point(x + bl, y + h), true));
        if (bl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x, y + h - bl),
                new Size(bl, bl),
                0, false, SweepDirection.Clockwise, true));
        }

        // Left edge → top-left corner
        figure.Segments.Add(new LineSegment(new Point(x, y + tl), true));
        if (tl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + tl, y),
                new Size(tl, tl),
                0, false, SweepDirection.Clockwise, true));
        }

        return figure;
    }

    private static bool AreApproximatelyEqual(double a, double b)
        => Math.Abs(a - b) < 0.01;

    #endregion

    #region Property Changed Callbacks

    // Thread-safe mirror of the LiquidGlass DP for GetExtraDirtyPadding (the dirty
    // pipeline reads it from background registration threads, where DP getters are
    // off-limits — same contract as UIElement._effectForDirtyBounds).
    private volatile bool _liquidGlassForDirtyBounds;

    // The native liquid-glass quad expands 32 DIPs past the deformed glass rect for the
    // outer shadow + fusion-bridge bleed (kLiquidGlassVS padding). The dirty
    // pipeline must track that ring or a moving/animating glass panel leaves the
    // shadow behind (its ink is clamped to the tracked dirty region).
    private const double LiquidGlassNativePadding = 32.0;

    internal override double GetExtraDirtyPadding()
        => _liquidGlassForDirtyBounds ? _liquidGlassDirtyPadding : 0.0;

    private void UpdateLiquidGlassDirtyPadding(Size size)
    {
        int padding = (int)LiquidGlassNativePadding;
        if (_liquidGlassForDirtyBounds)
        {
            var glassRect = ComputeLiquidGlassRect(new Rect(size));
            double leftOverflow = Math.Max(0, LiquidGlassNativePadding - glassRect.Left);
            double topOverflow = Math.Max(0, LiquidGlassNativePadding - glassRect.Top);
            double rightOverflow = Math.Max(
                0,
                glassRect.Right + LiquidGlassNativePadding - size.Width);
            double bottomOverflow = Math.Max(
                0,
                glassRect.Bottom + LiquidGlassNativePadding - size.Height);
            double required = Math.Max(
                Math.Max(leftOverflow, topOverflow),
                Math.Max(rightOverflow, bottomOverflow));
            if (double.IsFinite(required))
            {
                padding = (int)Math.Min(int.MaxValue, Math.Ceiling(required));
            }
        }

        _liquidGlassDirtyPadding = Math.Max((int)LiquidGlassNativePadding, padding);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border)
        {
            // Skip if value didn't actually change
            if (Equals(e.OldValue, e.NewValue)) return;

            if (e.Property == LiquidGlassProperty)
            {
                border._liquidGlassForDirtyBounds = e.NewValue is true;
            }

            // Invalidate pen cache if BorderBrush changed
            if (e.Property == BorderBrushProperty)
            {
                // Skip if border thickness is 0 (brush change has no visual effect)
                var thickness = border.BorderThickness;
                if (thickness.Left == 0 && thickness.Top == 0 &&
                    thickness.Right == 0 && thickness.Bottom == 0)
                    return;
                border.InvalidateBorderPenCache();
            }

            // Wire/unwire mouse events for liquid glass highlight
            if (e.Property == LiquidGlassProperty)
            {
                border.UpdateLiquidGlassMouseTracking((bool)(e.NewValue ?? false));
            }

            // Wire/unwire press events for liquid glass interaction
            if (e.Property == LiquidGlassInteractiveProperty)
            {
                border.UpdateLiquidGlassPressTracking((bool)(e.NewValue ?? false));
            }

            if (e.Property == LiquidGlassProperty ||
                e.Property == LiquidGlassInteractiveProperty)
            {
                border.UpdateLiquidGlassDirtyPadding(border.RenderSize);
            }

            border.InvalidateVisual();
        }
    }

    private void UpdateLiquidGlassMouseTracking(bool enabled)
    {
        if (enabled && !_lgEventsWired)
        {
            if (!TryWireLgWindowTracking())
            {
                // Not in visual tree yet 閳?defer to Loaded
                Loaded += OnLgDeferredLoaded;
            }
        }
        else if (!enabled)
        {
            UnwireLgWindowTracking();
        }
    }

    private Input.MouseEventHandler? _lgMouseMoveHandler;
    private Input.MouseEventHandler? _lgMouseLeaveHandler;

    private bool TryWireLgWindowTracking()
    {
        if (_lgEventsWired) return true;

        var window = FindAncestorWindow();
        if (window == null) return false;

        _lgTrackingWindow = window;
        _lgMouseMoveHandler = new Input.MouseEventHandler(OnLgWindowMouseMove);
        _lgMouseLeaveHandler = new Input.MouseEventHandler(OnLgWindowMouseLeave);
        window.AddHandler(MouseMoveEvent, _lgMouseMoveHandler, handledEventsToo: true);
        window.AddHandler(MouseLeaveEvent, _lgMouseLeaveHandler, handledEventsToo: true);

        // Also wire MouseUp for interactive press tracking if handler is ready
        if (_lgMouseUpHandler != null)
            window.AddHandler(MouseUpEvent, _lgMouseUpHandler, handledEventsToo: true);

        _lgEventsWired = true;
        return true;
    }

    private void UnwireLgWindowTracking()
    {
        Loaded -= OnLgDeferredLoaded;

        if (_lgTrackingWindow != null && _lgMouseMoveHandler != null)
        {
            _lgTrackingWindow.RemoveHandler(MouseMoveEvent, _lgMouseMoveHandler);
            _lgTrackingWindow.RemoveHandler(MouseLeaveEvent, _lgMouseLeaveHandler!);
            if (_lgMouseUpHandler != null)
                _lgTrackingWindow.RemoveHandler(MouseUpEvent, _lgMouseUpHandler);
            _lgTrackingWindow = null;
            _lgMouseMoveHandler = null;
            _lgMouseLeaveHandler = null;
        }
        _lgEventsWired = false;
        _lgMouseOver = false;
    }

    private void OnLgDeferredLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLgDeferredLoaded;
        if (LiquidGlass)
            TryWireLgWindowTracking();
    }

    private void OnLgWindowMouseMove(object sender, Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        _lgLightLocal = pos;
        var influence = Math.Max(LiquidGlassRefractionAmount,
            Math.Max(LiquidGlassFusionRadius, LiquidGlassBlurRadius * 2.0));
        _lgMouseOver = _lgPressed ||
            new Rect(-influence, -influence,
                Math.Max(0, ActualWidth) + influence * 2.0,
                Math.Max(0, ActualHeight) + influence * 2.0).Contains(pos);

        // Track drag offset while pressed
        // Power curve applied at input for diminishing returns during drag;
        // spring and render use linear values so the snap-back animation is smooth.
        if (_lgPressed)
        {
            double dx = pos.X - _lgPressPoint.X;
            double dy = pos.Y - _lgPressPoint.Y;
            double tx = Math.Sign(dx) * LgDragScale * Math.Pow(Math.Abs(dx), LgDragPower);
            double ty = Math.Sign(dy) * LgDragScale * Math.Pow(Math.Abs(dy), LgDragPower);
            _lgSpringOffX.Position = tx;
            _lgSpringOffX.Target = tx;
            _lgSpringOffX.Velocity = 0;
            _lgSpringOffY.Position = ty;
            _lgSpringOffY.Target = ty;
            _lgSpringOffY.Velocity = 0;
        }

        UpdateLiquidGlassDirtyPadding(RenderSize);
        InvalidateVisual();
    }

    private void OnLgWindowMouseLeave(object sender, Input.MouseEventArgs e)
    {
        _lgMouseOver = false;
        InvalidateVisual();
    }

    private Window? FindAncestorWindow()
    {
        Visual? current = this;
        while (current != null)
        {
            if (current is Window w) return w;
            current = current.VisualParent;
        }
        return null;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border)
        {
            if (e.Property == BorderThicknessProperty)
            {
                border.InvalidateBorderPenCache();
            }
            border.InvalidateMeasure();
        }
    }

    #endregion

    #region Liquid Glass Press Interaction

    private Input.MouseButtonEventHandler? _lgMouseDownHandler;
    private Input.MouseButtonEventHandler? _lgMouseUpHandler;

    private void UpdateLiquidGlassPressTracking(bool enabled)
    {
        if (enabled)
        {
            _lgMouseDownHandler = new Input.MouseButtonEventHandler(OnLgMouseDown);
            _lgMouseUpHandler = new Input.MouseButtonEventHandler(OnLgMouseUp);
            // MouseDown on Border (press starts here)
            AddHandler(MouseDownEvent, _lgMouseDownHandler, handledEventsToo: true);
            // MouseUp on Window (release works even when mouse is outside Border)
            _lgTrackingWindow?.AddHandler(MouseUpEvent, _lgMouseUpHandler, handledEventsToo: true);
            // Handle lost mouse capture (e.g. window deactivation) to release drag state
            LostMouseCapture += OnLgLostMouseCapture;
        }
        else
        {
            LostMouseCapture -= OnLgLostMouseCapture;
            if (_lgMouseDownHandler != null)
            {
                RemoveHandler(MouseDownEvent, _lgMouseDownHandler);
                _lgTrackingWindow?.RemoveHandler(MouseUpEvent, _lgMouseUpHandler!);
                _lgMouseDownHandler = null;
                _lgMouseUpHandler = null;
            }
            StopLgSpringTimer();
            _lgSpringX = new SpringAxis { Position = 1.0, Target = 1.0 };
            _lgSpringY = new SpringAxis { Position = 1.0, Target = 1.0 };
            _lgSpringOffX = default;
            _lgSpringOffY = default;
            _lgPressed = false;
        }
    }

    private void OnLgMouseDown(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _lgPressed = true;
            _lgPressPoint = e.GetPosition(this);
            _lgSpringX.Target = LgPressScale;
            _lgSpringY.Target = LgPressScale;
            // Reset drag offset springs to zero (fresh drag)
            _lgSpringOffX = default;
            _lgSpringOffY = default;
            StartLgSpringTimer();
            // Capture mouse to prevent child elements from stealing events mid-drag
            CaptureMouse();
        }
    }

    private void OnLgMouseUp(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _lgPressed)
        {
            _lgPressed = false;
            _lgSpringX.Target = 1.0;
            _lgSpringY.Target = 1.0;
            // Spring offset back to zero on release
            _lgSpringOffX.Target = 0;
            _lgSpringOffY.Target = 0;
            ReleaseMouseCapture();
        }
    }

    private void OnLgLostMouseCapture(object sender, RoutedEventArgs e)
    {
        // Mouse capture was stolen (e.g. window deactivation, another element captured) 閳?
        // treat as release to prevent stuck drag state.
        if (_lgPressed)
        {
            _lgPressed = false;
            _lgSpringX.Target = 1.0;
            _lgSpringY.Target = 1.0;
            _lgSpringOffX.Target = 0;
            _lgSpringOffY.Target = 0;
        }
    }

    private void StartLgSpringTimer()
    {
        if (_lgSpringSubscribed) return;

        _lgLastTickTime = Environment.TickCount64;
        _lgSpringSubscribed = true;
        CompositionTarget.Rendering += OnLgSpringTick;
        CompositionTarget.Subscribe();
    }

    private void StopLgSpringTimer()
    {
        if (_lgSpringSubscribed)
        {
            _lgSpringSubscribed = false;
            CompositionTarget.Rendering -= OnLgSpringTick;
            CompositionTarget.Unsubscribe();
        }
    }

    private void OnLgSpringTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = (now - _lgLastTickTime) / 1000.0;
        _lgLastTickTime = now;

        if (dt <= 0) return;

        double stiffness = _lgPressed ? LgPressStiffness : LgReleaseStiffness;
        // maxDisplacement=0.5 prevents scale spring from diverging beyond 鍗?.5 of target (0.5..1.5 range)
        bool settledX = _lgSpringX.Step(dt, stiffness, LgDampingX, 0.5);
        bool settledY = _lgSpringY.Step(dt, stiffness, LgDampingY, 0.5);

        // Step drag offset springs (only meaningful after release, during drag they track mouse directly)
        // maxDisplacement=200 prevents offset from diverging beyond 鍗?00px
        bool settledOffX = _lgSpringOffX.Step(dt, LgOffsetStiffness, LgOffsetDamping, 200);
        bool settledOffY = _lgSpringOffY.Step(dt, LgOffsetStiffness, LgOffsetDamping, 200);

        UpdateLiquidGlassDirtyPadding(RenderSize);
        InvalidateVisual();

        if (settledX && settledY && settledOffX && settledOffY && !_lgPressed)
        {
            StopLgSpringTimer();
        }
    }

    #endregion
}
