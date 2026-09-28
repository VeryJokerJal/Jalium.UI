using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a vertical line that separates items in a CommandBar.
/// </summary>
public class AppBarSeparator : Control, ICommandBarElement
{
    private Border? _cssBorderPainter;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Separator);

    private static readonly SolidColorBrush s_fallbackSeparatorBrush = new(ThemeColors.ControlBorder);

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AppBarSeparator),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DynamicOverflowOrder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DynamicOverflowOrderProperty =
        DependencyProperty.Register(nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarSeparator),
            new PropertyMetadata(0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority of this element's dynamic overflow behavior.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int DynamicOverflowOrder
    {
        get => (int)GetValue(DynamicOverflowOrderProperty)!;
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the AppBarSeparator class.
    /// </summary>
    public AppBarSeparator()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        return new Size(
            ControlRenderGeometry.GetAvailableLength(1 + insets.Left + insets.Right, availableSize.Width),
            ControlRenderGeometry.GetAvailableLength(
                (IsCompact ? 24 : 32) + insets.Top + insets.Bottom, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        base.OnRender(drawingContext);
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
        if (content.Width <= 0 || content.Height <= 8)
            return;

        var brush = ResolveSeparatorBrush();
        var pen = new Pen(brush, 1);
        var x = content.X + content.Width / 2;

        dc.DrawLine(pen, new Point(x, content.Y + 4), new Point(x, content.Bottom - 4));
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, drawNative: true);
    }

    private Brush ResolveSeparatorBrush()
    {
        if (Foreground is { } authoredForeground &&
            (HasLocalOrAnimatedValue(ForegroundProperty) ||
             GetEffectiveValueLayer(ForegroundProperty) is
                 (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)))
        {
            return authoredForeground;
        }

        return TryFindResource("AppBarSeparatorForeground") as Brush
            ?? Foreground
            ?? s_fallbackSeparatorBrush;
    }
}
