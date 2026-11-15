using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control used to separate items in a list or menu.
/// </summary>
[TemplatePart(Name = "PART_HorizontalLine", Type = typeof(Border))]
[TemplatePart(Name = "PART_VerticalLine", Type = typeof(Border))]
[TemplateVisualState(Name = "Horizontal", GroupName = "OrientationStates")]
[TemplateVisualState(Name = "Vertical", GroupName = "OrientationStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class Separator : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.SeparatorAutomationPeer(this);
    }

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultStrokeBrush = new(Color.FromRgb(60, 60, 60));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Separator),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StrokeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(Separator),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StrokeThickness dependency property. The value must be a
    /// non-negative finite number (NaN is not a valid thickness): MeasureOverride
    /// feeds it straight into the desired <see cref="Size"/>, whose constructor
    /// rejects negative dimensions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Separator),
            new PropertyMetadata(1.0, OnLayoutPropertyChanged), IsStrokeThicknessValid);

    private static bool IsStrokeThicknessValid(object? value)
        => value is double v && !double.IsNaN(v) && v >= 0.0 && !double.IsPositiveInfinity(v);

    #endregion

    private static readonly DependencyPropertyKey ResolvedStrokeBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ResolvedStrokeBrush), typeof(Brush), typeof(Separator),
            new PropertyMetadata(null));

    /// <summary>Identifies the resolved stroke brush used by default and custom templates.</summary>
    public static readonly DependencyProperty ResolvedStrokeBrushProperty = ResolvedStrokeBrushPropertyKey.DependencyProperty;

    /// <summary>Gets the stroke brush after native, CSS and inherited values are resolved.</summary>
    public Brush? ResolvedStrokeBrush => (Brush?)GetValue(ResolvedStrokeBrushProperty);

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Separator"/> class.
    /// </summary>
    public Separator()
    {
        DefaultStyleKey = typeof(Separator);
        // Separators are not focusable
        Focusable = false;
        IsHitTestVisible = false;
    }

    #endregion

    #region Template Parts

    private Border? _cssBorderPainter;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdatePresentation();
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdatePresentation();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == StrokeBrushProperty || e.Property == ForegroundProperty || e.Property == BorderBrushProperty)
            SetValue(ResolvedStrokeBrushPropertyKey, ResolveStrokeBrush());
    }

    private void UpdatePresentation()
    {
        SetValue(ResolvedStrokeBrushPropertyKey, ResolveStrokeBrush());
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, Orientation == Orientation.Vertical ? "Vertical" : "Horizontal", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateDefaultStyle();
        if (Template is not null) return base.MeasureOverride(availableSize);
        var thickness = StrokeThickness;
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);

        if (Orientation == Orientation.Horizontal)
        {
            // Horizontal separator: zero desired width (stretches to fill), minimal height
            return new Size(
                ControlRenderGeometry.GetAvailableLength(insets.Left + insets.Right, availableSize.Width),
                ControlRenderGeometry.GetAvailableLength(
                    thickness + insets.Top + insets.Bottom, availableSize.Height));
        }
        else
        {
            // Vertical separator: minimal width, zero desired height (stretches to fill)
            return new Size(
                ControlRenderGeometry.GetAvailableLength(
                    thickness + insets.Left + insets.Right, availableSize.Width),
                ControlRenderGeometry.GetAvailableLength(insets.Top + insets.Bottom, availableSize.Height));
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Template is not null)
        {
            base.OnRender(drawingContext);
            return;
        }

        var dc = drawingContext;

        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var rect = new Rect(RenderSize);
        if (Background is { } background)
        {
            var backgroundLayer = GetEffectiveValueLayer(BackgroundProperty);
            var cssRadius = CssBorderRadiusProperties.Get(this);
            if (cssRadius is null && backgroundLayer is not
                    (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
                dc.DrawRoundedRectangle(background, null, rect, CornerRadius);
            else
            {
                var radii = cssRadius?.Resolve(RenderSize) ??
                    CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
                var shape = new CssRoundedRectangleGeometry(rect, radii);
                var (border, padding) = CssBoxMetrics.BackgroundInsets(this,
                    CssLayout?.ContainingWidthCache ?? RenderSize.Width);
                if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, background, dc,
                        rect, radii, border, padding,
                        brush => dc.DrawGeometry(brush, null, shape)))
                    dc.DrawGeometry(background, null, shape);
            }
        }

        var content = ControlRenderGeometry.GetContentRect(rect,
            CssBoxMetrics.ContentInsets(this,
                CssLayout?.ContainingWidthCache ?? RenderSize.Width));
        if (content.Width <= 0 || content.Height <= 0)
            return;

        var brush = ResolveStrokeBrush();
        var thickness = StrokeThickness;
        var pen = new Pen(brush, thickness);

        if (Orientation == Orientation.Horizontal)
        {
            // Draw horizontal line
            var y = content.Y + content.Height / 2;
            dc.DrawLine(pen, new Point(content.X, y), new Point(content.Right, y));
        }
        else
        {
            // Draw vertical line
            var x = content.X + content.Width / 2;
            dc.DrawLine(pen, new Point(x, content.Y), new Point(x, content.Bottom));
        }
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (Template is null)
            CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, drawNative: true);
    }

    private Brush ResolveStrokeBrush()
    {
        if (HasLocalOrAnimatedValue(StrokeBrushProperty) && StrokeBrush is { } localStroke)
            return localStroke;
        if (Foreground is { } authoredForeground &&
            (HasLocalOrAnimatedValue(ForegroundProperty) ||
             GetEffectiveValueLayer(ForegroundProperty) is
                 (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)))
            return authoredForeground;
        if (StrokeBrush is { } stroke)
            return stroke;
        if (BorderBrush is { } border)
            return border;
        if (Foreground is { } foreground &&
            DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource ==
                BaseValueSource.Inherited)
            return foreground;
        return s_defaultStrokeBrush;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Separator separator)
        {
            separator.UpdatePresentation();
            separator.InvalidateMeasure();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Separator separator)
        {
            separator.InvalidateVisual();
        }
    }

    #endregion
}
