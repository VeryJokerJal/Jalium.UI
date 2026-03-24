using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Draws a border, background, or both around another element.
/// </summary>
[ContentProperty("Child")]
public class Border : FrameworkElement
{
    private UIElement? _child;
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
    private float _lgHighlightBoost;

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

    /// <summary>
    /// Gets or sets the child element.
    /// </summary>
    public UIElement? Child
    {
        get => _child;
        set
        {
            if (_child == value) return;

            if (_child != null)
            {
                RemoveVisualChild(_child);
            }

            _child = value;

            if (_child != null)
            {
                AddVisualChild(_child);
            }

            InvalidateMeasure();
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var border = BorderThickness;
        var padding = Padding;
        var totalHorizontal = border.Left + border.Right + padding.Left + padding.Right;
        var totalVertical = border.Top + border.Bottom + padding.Top + padding.Bottom;

        var childAvailable = new Size(
            Math.Max(0, availableSize.Width - totalHorizontal),
            Math.Max(0, availableSize.Height - totalVertical));

        var childSize = Size.Empty;

        if (_child != null)
        {
            _child.Measure(childAvailable);
            childSize = _child.DesiredSize;
        }

        return new Size(
            childSize.Width + totalHorizontal,
            childSize.Height + totalVertical);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var border = BorderThickness;
        var padding = Padding;

        if (_child != null)
        {
            var childRect = new Rect(
                border.Left + padding.Left,
                border.Top + padding.Top,
                Math.Max(0, finalSize.Width - border.Left - border.Right - padding.Left - padding.Right),
                Math.Max(0, finalSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom));

            _child.Arrange(childRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    /// <inheritdoc />
    internal override object? GetLayoutClip()
    {
        if (!ClipToBounds)
            return null;

        // Clip to the Border's outer shape. Child layout is already inset by BorderThickness
        // and Padding, so shrinking the clip here removes visible edge pixels from children.
        var clipRect = new Rect(0, 0, _renderSize.Width, _renderSize.Height);

        if (Shape == BorderShape.SuperEllipse)
        {
            return CreateSuperEllipseGeometry(clipRect, SuperEllipseN);
        }

        var cornerRadius = CornerRadius;
        var maxRadius = Math.Max(
            Math.Max(cornerRadius.TopLeft, cornerRadius.TopRight),
            Math.Max(cornerRadius.BottomRight, cornerRadius.BottomLeft));

        if (maxRadius > 0)
        {
            return new RectangleGeometry(clipRect, maxRadius, maxRadius);
        }

        return clipRect;
    }

    #endregion

    #region Rendering

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

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var border = BorderThickness;
        var cornerRadius = CornerRadius;
        var borderWidth = Math.Max(border.Left, Math.Max(border.Top, Math.Max(border.Right, border.Bottom)));
        var halfBorder = borderWidth / 2;

        // Compute elastic deformation parameters for spring press interaction
        _lgHighlightBoost = 0f;
        _lgPushedTransform = false;
        double lgSx = 1.0, lgSy = 1.0, lgShiftX = 0, lgShiftY = 0;
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

                double compressW = 1.0 - (stretchH / Math.Max(rect.Height, 1.0)) * LgPerpendicularCompress;
                double compressH = 1.0 - (stretchW / Math.Max(rect.Width, 1.0)) * LgPerpendicularCompress;

                lgSx = (rect.Width + stretchW) / Math.Max(rect.Width, 1.0) * scaleX * compressW;
                lgSy = (rect.Height + stretchH) / Math.Max(rect.Height, 1.0) * scaleY * compressH;

                // Safety clamp: prevent runaway scaling from spring divergence
                lgSx = Math.Clamp(lgSx, 0.1, 3.2);
                lgSy = Math.Clamp(lgSy, 0.1, 3.2);

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
        if (LiquidGlass && dc is Interop.RenderTargetDrawingContext rtdc)
        {
            // Lazy wiring: OnRender guarantees we're in the visual tree
            if (!_lgEventsWired)
                TryWireLgWindowTracking();

            // Compute the deformed glass rect for the SDF shader
            var glassRect = rect;
            if (lgHasSpring)
            {
                double newW = rect.Width * lgSx;
                double newH = rect.Height * lgSy;
                double cx = rect.Width / 2.0;
                double cy = rect.Height / 2.0;
                glassRect = new Rect(
                    cx - newW / 2.0 + lgShiftX,
                    cy - newH / 2.0 + lgShiftY,
                    Math.Max(1, newW), Math.Max(1, newH));
            }

            var avgRadius = (float)((cornerRadius.TopLeft + cornerRadius.TopRight +
                                     cornerRadius.BottomRight + cornerRadius.BottomLeft) / 4.0);

            // Extract tint color from Background brush if it's a SolidColorBrush
            float tintR = 0.08f, tintG = 0.08f, tintB = 0.08f, tintOpacity = 0.3f;
            if (Background is SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                tintR = color.ScR;
                tintG = color.ScG;
                tintB = color.ScB;
                tintOpacity = color.ScA;
            }

            // Compute screen-space light position from local mouse coordinates
            float lightX = -1f, lightY = -1f;
            if (_lgMouseOver)
            {
                lightX = (float)(_lgLightLocal.X + rtdc.Offset.X);
                lightY = (float)(_lgLightLocal.Y + rtdc.Offset.Y);
            }

            // Cache screen-space rect for neighbor fusion queries
            _lgScreenRect = new Rect(
                Math.Round(glassRect.X + rtdc.Offset.X), Math.Round(glassRect.Y + rtdc.Offset.Y),
                glassRect.Width, glassRect.Height);
            _lgAvgCornerRadius = avgRadius;

            // Discover sibling liquid glass panels for fusion
            float fusionRadius = (float)LiquidGlassFusionRadius;
            int neighborCount = 0;
            Span<float> neighborData = stackalloc float[20]; // max 4 neighbors 鑴?5 floats
            if (fusionRadius > 0 && VisualParent is Panel parentPanel)
            {
                foreach (var child in parentPanel.Children)
                {
                    if (child is Border sibling && sibling != this &&
                        sibling.LiquidGlass && sibling._lgScreenRect.Width > 0)
                    {
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
                foreach (var c in pp.Children)
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

            rtdc.DrawLiquidGlass(
                glassRect,
                avgRadius,
                (float)LiquidGlassBlurRadius,
                (float)LiquidGlassRefractionAmount,
                (float)LiquidGlassChromaticAberration,
                tintR, tintG, tintB, tintOpacity,
                lightX, lightY, _lgHighlightBoost,
                (int)Shape, (float)SuperEllipseN,
                neighborCount, fusionRadius,
                neighborData[..(neighborCount * 5)]);
        }

        // Now push ScaleTransform for background, border, and children.
        // This is AFTER DrawLiquidGlass so the D2D1 snapshot/effect output is not affected.
        if (lgHasSpring)
        {
            double cx = rect.Width / 2.0;
            double cy = rect.Height / 2.0;
            dc.PushTransform(new MatrixTransform(new Matrix(
                lgSx, 0, 0, lgSy,
                cx * (1.0 - lgSx) + lgShiftX,
                cy * (1.0 - lgSy) + lgShiftY)));
            _lgPushedTransform = true;
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

            // Use SDF super ellipse rendering via the direct renderer for pixel-perfect AA.
            // Set shape type to SuperEllipse (1) before drawing, then reset to RoundedRect (0).
            if (dc is Interop.RenderTargetDrawingContext seDc)
            {
                seDc.SetShapeType(1, (float)seN);

                if (Background != null && !LiquidGlass)
                {
                    var backgroundRect = new Rect(
                        rect.X + halfBorder,
                        rect.Y + halfBorder,
                        Math.Max(0, rect.Width - borderWidth),
                        Math.Max(0, rect.Height - borderWidth));

                    // Use a single average corner radius — the SDF super ellipse shape
                    // is defined by the exponent N, not by per-corner radii.
                    var avgRadius = Math.Min(backgroundRect.Width, backgroundRect.Height) / 2.0;
                    dc.DrawRoundedRectangle(Background, null, backgroundRect,
                        new CornerRadius(avgRadius));
                }

                if (BorderBrush != null && borderWidth > 0)
                {
                    var pen = GetOrCreateBorderPen(BorderBrush, borderWidth);
                    var borderRect = new Rect(
                        rect.X + halfBorder,
                        rect.Y + halfBorder,
                        Math.Max(0, rect.Width - borderWidth),
                        Math.Max(0, rect.Height - borderWidth));

                    var avgRadius = Math.Min(borderRect.Width, borderRect.Height) / 2.0;
                    dc.DrawRoundedRectangle(null, pen, borderRect,
                        new CornerRadius(avgRadius));
                }

                seDc.SetShapeType(0, 4.0f);
            }
            else
            {
                // Fallback for non-D3D12 drawing contexts: use Bezier geometry
                if (Background != null && !LiquidGlass)
                {
                    var backgroundRect = new Rect(
                        rect.X + halfBorder,
                        rect.Y + halfBorder,
                        Math.Max(0, rect.Width - borderWidth),
                        Math.Max(0, rect.Height - borderWidth));
                    var bgGeo = CreateSuperEllipseGeometry(backgroundRect, seN);
                    dc.DrawGeometry(Background, null, bgGeo);
                }

                if (BorderBrush != null && borderWidth > 0)
                {
                    var pen = GetOrCreateBorderPen(BorderBrush, borderWidth);
                    var borderRect = new Rect(
                        rect.X + halfBorder,
                        rect.Y + halfBorder,
                        Math.Max(0, rect.Width - borderWidth),
                        Math.Max(0, rect.Height - borderWidth));
                    var borderGeo = CreateSuperEllipseGeometry(borderRect, seN);
                    dc.DrawGeometry(null, pen, borderGeo);
                }
            }
        }
        else
        {
            // Standard rounded rectangle path
            // Skip Background fill when LiquidGlass is enabled (same reason as SuperEllipse path).
            if (Background != null && !LiquidGlass)
            {
                var backgroundRect = new Rect(
                    rect.X + halfBorder,
                    rect.Y + halfBorder,
                    Math.Max(0, rect.Width - borderWidth),
                    Math.Max(0, rect.Height - borderWidth));

                var innerRadius = new CornerRadius(
                    Math.Max(0, cornerRadius.TopLeft - halfBorder),
                    Math.Max(0, cornerRadius.TopRight - halfBorder),
                    Math.Max(0, cornerRadius.BottomRight - halfBorder),
                    Math.Max(0, cornerRadius.BottomLeft - halfBorder));

                dc.DrawRoundedRectangle(Background, null, backgroundRect, innerRadius);
            }

            if (BorderBrush != null && borderWidth > 0)
            {
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
            }
        }

        // The ScaleTransform pushed AFTER DrawLiquidGlass stays active for children.
        // Visual.Render order: OnRender 閳?children 閳?OnPostRender.
        // Glass gets a deformed rect (SDF shape change); bg/border/children get ScaleTransform.
    }

    /// <inheritdoc />
    protected override void OnPostRender(object drawingContext)
    {
        if (_lgPushedTransform && drawingContext is DrawingContext dc)
        {
            dc.Pop();
            _lgPushedTransform = false;
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border)
        {
            // Skip if value didn't actually change
            if (Equals(e.OldValue, e.NewValue)) return;

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
        _lgMouseOver = true;

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

        InvalidateVisual();

        if (settledX && settledY && settledOffX && settledOffY && !_lgPressed)
        {
            StopLgSpringTimer();
        }
    }

    #endregion
}
