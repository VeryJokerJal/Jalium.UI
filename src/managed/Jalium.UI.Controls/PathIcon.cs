using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents an icon that uses a vector path as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.PathIcon.
/// </summary>
public class PathIcon : IconElement
{
    private const double DefaultIconSize = 20;
    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(PathIcon), new PropertyMetadata(null, OnDataChanged));
    /// <summary>
    /// Gets or sets the Geometry that specifies the shape to be drawn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Geometry? Data { get => (Geometry? )GetValue(DataProperty); set => SetValue(DataProperty, value); }

    /// <summary>Identifies the optional SVG-style source view box.</summary>
    public static readonly DependencyProperty ViewBoxProperty = DependencyProperty.Register(nameof(ViewBox), typeof(Rect), typeof(PathIcon), new PropertyMetadata(Rect.Empty, OnDataChanged), value => value is Rect rect && (rect.IsEmpty || double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0));
    /// <summary>Gets or sets a source view box that preserves optical insets. Empty fits the geometry bounds.</summary>
    public Rect ViewBox { get => (Rect)GetValue(ViewBoxProperty)!; set => SetValue(ViewBoxProperty, value); }
    private Rect SourceBounds => ViewBox.IsEmpty ? Data?.Bounds ?? Rect.Empty : ViewBox;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var bounds = SourceBounds;
        var hasExplicitWidth = !double.IsNaN(Width);
        var hasExplicitHeight = !double.IsNaN(Height);
        var aspectRatio = bounds.Width > 0 && bounds.Height > 0 ? bounds.Width / bounds.Height : 1.0;
        double width = hasExplicitWidth ? Width : DefaultIconSize;
        double height = hasExplicitHeight ? Height : DefaultIconSize;
        if (hasExplicitWidth && !hasExplicitHeight && aspectRatio > 0)
        {
            height = width / aspectRatio;
        }
        else if (!hasExplicitWidth && hasExplicitHeight)
        {
            width = height * aspectRatio;
        }

        return new Size(Math.Min(width, availableSize.Width), Math.Min(height, availableSize.Height));
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        if (Data == null)
            return;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;
        var foreground = GetEffectiveForeground();
        var fitMatrix = CreateFitMatrix(SourceBounds, RenderSize);
        // Only handle the fit-to-size matrix here. Any user-supplied
        // RenderTransform (rotation/skew/scale) is pushed generically by
        // Visual.RenderDirect around this element's draw pass — applying it
        // again here would double-transform the icon.
        if (fitMatrix.IsIdentity)
        {
            dc.DrawGeometry(foreground, null, Data);
            return;
        }

        dc.PushTransform(new MatrixTransform(fitMatrix));
        try
        {
            dc.DrawGeometry(foreground, null, Data);
        }
        finally
        {
            dc.Pop();
        }
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PathIcon icon)
        {
            icon.InvalidateMeasure();
            icon.InvalidateVisual();
        }
    }

    private static Matrix CreateFitMatrix(Rect bounds, Size targetSize)
    {
        var hasWidth = bounds.Width > 0;
        var hasHeight = bounds.Height > 0;
        if (hasWidth && hasHeight)
        {
            var scale = Math.Min(targetSize.Width / bounds.Width, targetSize.Height / bounds.Height);
            return new Matrix(scale, 0, 0, scale, (targetSize.Width - bounds.Width * scale) / 2 - bounds.X * scale, (targetSize.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        }

        if (hasWidth)
        {
            var scale = targetSize.Width / bounds.Width;
            return new Matrix(scale, 0, 0, scale, (targetSize.Width - bounds.Width * scale) / 2 - bounds.X * scale, targetSize.Height / 2 - bounds.Y * scale);
        }

        if (hasHeight)
        {
            var scale = targetSize.Height / bounds.Height;
            return new Matrix(scale, 0, 0, scale, targetSize.Width / 2 - bounds.X * scale, (targetSize.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        }

        return new Matrix(1, 0, 0, 1, targetSize.Width / 2 - bounds.X, targetSize.Height / 2 - bounds.Y);
    }
}
