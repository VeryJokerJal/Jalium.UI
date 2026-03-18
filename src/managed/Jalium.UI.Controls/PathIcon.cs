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
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(PathIcon),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>
    /// Gets or sets the Geometry that specifies the shape to be drawn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var bounds = Data?.Bounds ?? Rect.Empty;
        var hasExplicitWidth = !double.IsNaN(Width);
        var hasExplicitHeight = !double.IsNaN(Height);
        var aspectRatio = bounds.Width > 0 && bounds.Height > 0
            ? bounds.Width / bounds.Height
            : 1.0;

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

        return new Size(
            Math.Min(width, availableSize.Width),
            Math.Min(height, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        if (Data == null) return;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var foreground = GetEffectiveForeground();
        var fitMatrix = CreateFitMatrix(Data.Bounds, RenderSize);
        var finalMatrix = fitMatrix;

        if (RenderTransform is Transform renderTransform)
        {
            var renderMatrix = renderTransform.Value;
            if (!renderMatrix.IsIdentity)
            {
                finalMatrix = Matrix.Multiply(
                    finalMatrix,
                    CreateCenteredMatrix(renderMatrix, RenderSize.Width / 2, RenderSize.Height / 2));
            }
        }

        if (finalMatrix.IsIdentity)
        {
            dc.DrawGeometry(foreground, null, Data);
            return;
        }

        dc.PushTransform(new MatrixTransform(finalMatrix));
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
            return new Matrix(
                scale,
                0,
                0,
                scale,
                (targetSize.Width - bounds.Width * scale) / 2 - bounds.X * scale,
                (targetSize.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        }

        if (hasWidth)
        {
            var scale = targetSize.Width / bounds.Width;
            return new Matrix(
                scale,
                0,
                0,
                scale,
                (targetSize.Width - bounds.Width * scale) / 2 - bounds.X * scale,
                targetSize.Height / 2 - bounds.Y * scale);
        }

        if (hasHeight)
        {
            var scale = targetSize.Height / bounds.Height;
            return new Matrix(
                scale,
                0,
                0,
                scale,
                targetSize.Width / 2 - bounds.X * scale,
                (targetSize.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        }

        return new Matrix(
            1,
            0,
            0,
            1,
            targetSize.Width / 2 - bounds.X,
            targetSize.Height / 2 - bounds.Y);
    }

    private static Matrix CreateCenteredMatrix(Matrix matrix, double centerX, double centerY)
    {
        return new Matrix(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX + centerX - centerX * matrix.M11 - centerY * matrix.M21,
            matrix.OffsetY + centerY - centerX * matrix.M12 - centerY * matrix.M22);
    }
}
