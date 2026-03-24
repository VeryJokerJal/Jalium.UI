using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Shared matrix transformation utilities used by <see cref="Shapes.Path"/> and <see cref="PathIcon"/>.
/// </summary>
internal static class MatrixHelper
{
    /// <summary>
    /// Creates a matrix equivalent to translating the origin to (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>), applying <paramref name="matrix"/>, and translating back.
    /// </summary>
    internal static Matrix CreateCenteredMatrix(Matrix matrix, double centerX, double centerY)
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
