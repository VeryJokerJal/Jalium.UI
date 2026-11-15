namespace Jalium.UI.Media;

/// <summary>
/// Paint-space mapping shared by bitmap, vector and native drawing. WPF brushes
/// project linear endpoints into the physical box before interpolation; SVG
/// objectBoundingBox paint servers interpolate in the unit box before mapping.
/// A brush transform changes paint coordinates without changing the filled path.
/// </summary>
internal static class GradientPaintCoordinates
{
    internal static bool UsesUnitBox(GradientBrush brush) =>
        brush.UsesNormalizedInterpolation && brush.MappingMode == BrushMappingMode.RelativeToBoundingBox;

    internal static bool IsCssImage(Brush? brush) => brush is GradientBrush gradient &&
        (gradient.UsesCssImageCoordinates || gradient.CssImageLayout is not null);

    internal static Rect StrokeBounds(Brush? brush, Rect geometryBounds, double thickness)
    {
        if (IsCssImage(brush) && (geometryBounds.Width <= 0 || geometryBounds.Height <= 0))
            geometryBounds.Inflate(thickness / 2, thickness / 2);
        return geometryBounds;
    }

    internal static Point MapPoint(GradientBrush brush, Point point, Rect bounds, Point offset) =>
        UsesUnitBox(brush) ? point : brush.MappingMode == BrushMappingMode.RelativeToBoundingBox
            ? new(bounds.X + point.X * bounds.Width, bounds.Y + point.Y * bounds.Height)
            : new(point.X + offset.X, point.Y + offset.Y);

    internal static double MapRadius(GradientBrush brush, double radius, double extent) =>
        brush.MappingMode == BrushMappingMode.RelativeToBoundingBox && !UsesUnitBox(brush) ? radius * extent : radius;

    internal static Matrix GradientToTarget(GradientBrush brush, Rect bounds, Point offset)
    {
        var box = new Matrix(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y);
        var result = UsesUnitBox(brush) ? box : Matrix.Identity;
        if (brush.RelativeTransform is { } relative && !relative.Value.IsIdentity)
        {
            if (!TryInvert(box, out var normalize)) return new Matrix(0, 0, 0, 0, 0, 0);
            result *= normalize * relative.Value * box;
        }
        if (brush.Transform is { } absolute && !absolute.Value.IsIdentity)
        {
            result *= new Matrix(1, 0, 0, 1, -offset.X, -offset.Y) * absolute.Value *
                new Matrix(1, 0, 0, 1, offset.X, offset.Y);
        }
        return result;
    }

    // Avoid the general layout matrix's near-zero determinant cutoff: a tiny
    // but invertible paint scale is valid. Reject non-finite results explicitly.
    internal static bool TryInvert(Matrix matrix, out Matrix inverse)
    {
        var determinant = matrix.Determinant;
        inverse = default;
        if (!IsFinite(matrix) || determinant == 0 || !double.IsFinite(determinant)) return false;
        inverse = new(matrix.M22 / determinant, -matrix.M12 / determinant,
            -matrix.M21 / determinant, matrix.M11 / determinant,
            (matrix.M21 * matrix.OffsetY - matrix.M22 * matrix.OffsetX) / determinant,
            (matrix.M12 * matrix.OffsetX - matrix.M11 * matrix.OffsetY) / determinant);
        return IsFinite(inverse);
    }

    private static bool IsFinite(Matrix matrix) => double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) && double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);
}
