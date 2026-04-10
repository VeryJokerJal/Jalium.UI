namespace Jalium.UI.Media;

/// <summary>
/// Base class for all transforms.
/// </summary>
public abstract class Transform
{
    /// <summary>
    /// Gets the transform matrix.
    /// </summary>
    public abstract Matrix Value { get; }

    /// <summary>
    /// Gets the identity transform.
    /// </summary>
    public static Transform Identity => new MatrixTransform(Matrix.Identity);
}

/// <summary>
/// Represents a 3x2 affine transformation matrix.
/// </summary>
public readonly struct Matrix : IEquatable<Matrix>
{
    /// <summary>
    /// Gets the M11 value (scale X).
    /// </summary>
    public double M11 { get; }

    /// <summary>
    /// Gets the M12 value.
    /// </summary>
    public double M12 { get; }

    /// <summary>
    /// Gets the M21 value.
    /// </summary>
    public double M21 { get; }

    /// <summary>
    /// Gets the M22 value (scale Y).
    /// </summary>
    public double M22 { get; }

    /// <summary>
    /// Gets the OffsetX value (translation X).
    /// </summary>
    public double OffsetX { get; }

    /// <summary>
    /// Gets the OffsetY value (translation Y).
    /// </summary>
    public double OffsetY { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Matrix"/> struct.
    /// </summary>
    public Matrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    /// <summary>
    /// Gets the identity matrix.
    /// </summary>
    public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>
    /// Gets a value indicating whether this is an identity matrix.
    /// </summary>
    public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

    /// <summary>
    /// Multiplies two matrices.
    /// </summary>
    public static Matrix Multiply(Matrix a, Matrix b)
    {
        return new Matrix(
            a.M11 * b.M11 + a.M12 * b.M21,
            a.M11 * b.M12 + a.M12 * b.M22,
            a.M21 * b.M11 + a.M22 * b.M21,
            a.M21 * b.M12 + a.M22 * b.M22,
            a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX,
            a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);
    }

    /// <summary>
    /// Transforms a point by this matrix.
    /// </summary>
    public Point Transform(Point point)
    {
        return new Point(
            point.X * M11 + point.Y * M21 + OffsetX,
            point.X * M12 + point.Y * M22 + OffsetY);
    }

    /// <inheritdoc />
    public bool Equals(Matrix other) =>
        M11 == other.M11 && M12 == other.M12 &&
        M21 == other.M21 && M22 == other.M22 &&
        OffsetX == other.OffsetX && OffsetY == other.OffsetY;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Matrix other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);

    public static bool operator ==(Matrix left, Matrix right) => left.Equals(right);
    public static bool operator !=(Matrix left, Matrix right) => !left.Equals(right);
    public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);
}

/// <summary>
/// Applies an arbitrary matrix transform.
/// </summary>
public sealed class MatrixTransform : Transform
{
    /// <summary>
    /// Gets or sets the transform matrix.
    /// </summary>
    public Matrix Matrix { get; set; }

    /// <inheritdoc />
    public override Matrix Value => Matrix;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatrixTransform"/> class.
    /// </summary>
    public MatrixTransform()
    {
        Matrix = Matrix.Identity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MatrixTransform"/> class.
    /// </summary>
    /// <param name="matrix">The transform matrix.</param>
    public MatrixTransform(Matrix matrix)
    {
        Matrix = matrix;
    }
}

/// <summary>
/// Scales an object in the 2D coordinate system.
/// </summary>
public sealed class ScaleTransform : Transform
{
    public ScaleTransform() { }

    public ScaleTransform(double scaleX, double scaleY)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public ScaleTransform(double scaleX, double scaleY, double centerX, double centerY)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
        CenterX = centerX;
        CenterY = centerY;
    }

    /// <summary>
    /// Gets or sets the X scale factor.
    /// </summary>
    public double ScaleX { get; set; } = 1;

    /// <summary>
    /// Gets or sets the Y scale factor.
    /// </summary>
    public double ScaleY { get; set; } = 1;

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY { get; set; }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            if (CenterX == 0 && CenterY == 0)
            {
                return new Matrix(ScaleX, 0, 0, ScaleY, 0, 0);
            }

            return new Matrix(
                ScaleX, 0, 0, ScaleY,
                CenterX - ScaleX * CenterX,
                CenterY - ScaleY * CenterY);
        }
    }
}

/// <summary>
/// Rotates an object in the 2D coordinate system.
/// </summary>
public sealed class RotateTransform : Transform
{
    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY { get; set; }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            var radians = Angle * Math.PI / 180;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            if (CenterX == 0 && CenterY == 0)
            {
                return new Matrix(cos, sin, -sin, cos, 0, 0);
            }

            return new Matrix(
                cos, sin, -sin, cos,
                CenterX * (1 - cos) + CenterY * sin,
                CenterY * (1 - cos) - CenterX * sin);
        }
    }
}

/// <summary>
/// Translates an object in the 2D coordinate system.
/// </summary>
public sealed class TranslateTransform : Transform
{
    public TranslateTransform() { }

    public TranslateTransform(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets or sets the X translation.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y translation.
    /// </summary>
    public double Y { get; set; }

    /// <inheritdoc />
    public override Matrix Value => new Matrix(1, 0, 0, 1, X, Y);
}

/// <summary>
/// Skews an object in the 2D coordinate system.
/// </summary>
public sealed class SkewTransform : Transform
{
    /// <summary>
    /// Gets or sets the X skew angle in degrees.
    /// </summary>
    public double AngleX { get; set; }

    /// <summary>
    /// Gets or sets the Y skew angle in degrees.
    /// </summary>
    public double AngleY { get; set; }

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY { get; set; }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            var tanX = Math.Tan(AngleX * Math.PI / 180);
            var tanY = Math.Tan(AngleY * Math.PI / 180);

            return new Matrix(
                1, tanY, tanX, 1,
                -CenterY * tanX,
                -CenterX * tanY);
        }
    }
}

/// <summary>
/// Represents a composite transform that combines multiple transforms.
/// </summary>
public sealed class TransformGroup : Transform
{
    private readonly List<Transform> _children = new();

    /// <summary>
    /// Gets the collection of child transforms.
    /// </summary>
    public IList<Transform> Children => _children;

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            if (_children.Count == 0)
                return Matrix.Identity;

            var result = _children[0].Value;
            for (int i = 1; i < _children.Count; i++)
            {
                result = Matrix.Multiply(result, _children[i].Value);
            }
            return result;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformGroup"/> class.
    /// </summary>
    public TransformGroup()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformGroup"/> class with the specified children.
    /// </summary>
    /// <param name="transforms">The transforms to add.</param>
    public TransformGroup(params Transform[] transforms)
    {
        _children.AddRange(transforms);
    }

    /// <summary>
    /// Adds a transform to the group.
    /// </summary>
    /// <param name="transform">The transform to add.</param>
    /// <returns>This TransformGroup for fluent chaining.</returns>
    public TransformGroup Add(Transform transform)
    {
        _children.Add(transform);
        return this;
    }

    /// <summary>
    /// Removes a transform from the group.
    /// </summary>
    /// <param name="transform">The transform to remove.</param>
    /// <returns>True if the transform was removed; otherwise, false.</returns>
    public bool Remove(Transform transform)
    {
        return _children.Remove(transform);
    }

    /// <summary>
    /// Clears all transforms from the group.
    /// </summary>
    public void Clear()
    {
        _children.Clear();
    }
}

/// <summary>
/// A composite transform that provides convenient access to common transform operations.
/// Similar to WPF's CompositeTransform in WinUI.
/// </summary>
public sealed class CompositeTransform : Transform
{
    /// <summary>
    /// Gets or sets the center X coordinate for all transforms.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the center Y coordinate for all transforms.
    /// </summary>
    public double CenterY { get; set; }

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Gets or sets the X scale factor.
    /// </summary>
    public double ScaleX { get; set; } = 1;

    /// <summary>
    /// Gets or sets the Y scale factor.
    /// </summary>
    public double ScaleY { get; set; } = 1;

    /// <summary>
    /// Gets or sets the X skew angle in degrees.
    /// </summary>
    public double SkewX { get; set; }

    /// <summary>
    /// Gets or sets the Y skew angle in degrees.
    /// </summary>
    public double SkewY { get; set; }

    /// <summary>
    /// Gets or sets the X translation.
    /// </summary>
    public double TranslateX { get; set; }

    /// <summary>
    /// Gets or sets the Y translation.
    /// </summary>
    public double TranslateY { get; set; }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            // Order: Scale -> Skew -> Rotate -> Translate (standard order)
            var result = Matrix.Identity;

            // Apply center offset
            if (CenterX != 0 || CenterY != 0)
            {
                result = new Matrix(1, 0, 0, 1, -CenterX, -CenterY);
            }

            // Scale
            if (ScaleX != 1 || ScaleY != 1)
            {
                var scale = new Matrix(ScaleX, 0, 0, ScaleY, 0, 0);
                result = Matrix.Multiply(result, scale);
            }

            // Skew
            if (SkewX != 0 || SkewY != 0)
            {
                var tanX = Math.Tan(SkewX * Math.PI / 180);
                var tanY = Math.Tan(SkewY * Math.PI / 180);
                var skew = new Matrix(1, tanY, tanX, 1, 0, 0);
                result = Matrix.Multiply(result, skew);
            }

            // Rotate
            if (Rotation != 0)
            {
                var radians = Rotation * Math.PI / 180;
                var cos = Math.Cos(radians);
                var sin = Math.Sin(radians);
                var rotate = new Matrix(cos, sin, -sin, cos, 0, 0);
                result = Matrix.Multiply(result, rotate);
            }

            // Restore center offset
            if (CenterX != 0 || CenterY != 0)
            {
                var restore = new Matrix(1, 0, 0, 1, CenterX, CenterY);
                result = Matrix.Multiply(result, restore);
            }

            // Translate
            if (TranslateX != 0 || TranslateY != 0)
            {
                var translate = new Matrix(1, 0, 0, 1, TranslateX, TranslateY);
                result = Matrix.Multiply(result, translate);
            }

            return result;
        }
    }
}
