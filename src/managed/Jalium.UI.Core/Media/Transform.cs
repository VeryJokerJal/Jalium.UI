namespace Jalium.UI.Media;

using System.Collections.ObjectModel;
using System.Globalization;

/// <summary>
/// Base class for all transforms.
/// Raises <see cref="Changed"/> whenever any sub-property mutates so that
/// owning elements can invalidate their composition without the user having
/// to reassign the Transform reference.
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

    /// <summary>
    /// Raised when any sub-property of this transform changes.
    /// Used by <see cref="UIElement.RenderTransformProperty"/> to invalidate
    /// composition on the owning element.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Notifies subscribers that a sub-property of this transform has changed.
    /// </summary>
    protected void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Represents a 3x2 affine transformation matrix.
/// </summary>
public struct Matrix : IFormattable, IEquatable<Matrix>
{
    // 与 WPF 对齐：Matrix 是可变值类型，提供 Append/Prepend/Rotate/Scale/Skew/Translate 等
    // 就地变换方法（mutator 修改 this），属性可读可写。乘法/Transform 采用行向量约定
    // （point * matrix），与 WPF 完全一致。本实现不带 WPF 的 _type 快速路径优化，纯数学，
    // 但数值结果与 WPF 逐位一致。
    private const double SingularEpsilon = 1e-12;

    /// <summary>Gets or sets the value of the first row and first column (scale X / cos).</summary>
    public double M11 { get; set; }

    /// <summary>Gets or sets the value of the first row and second column.</summary>
    public double M12 { get; set; }

    /// <summary>Gets or sets the value of the second row and first column.</summary>
    public double M21 { get; set; }

    /// <summary>Gets or sets the value of the second row and second column (scale Y / cos).</summary>
    public double M22 { get; set; }

    /// <summary>Gets or sets the value of the third row and first column (translation X).</summary>
    public double OffsetX { get; set; }

    /// <summary>Gets or sets the value of the third row and second column (translation Y).</summary>
    public double OffsetY { get; set; }

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
    public readonly bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

    /// <summary>Sets this matrix to the identity matrix.</summary>
    public void SetIdentity()
    {
        M11 = 1; M12 = 0; M21 = 0; M22 = 1; OffsetX = 0; OffsetY = 0;
    }

    /// <summary>
    /// Multiplies two matrices (row-vector convention: <paramref name="a"/> applied first).
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

    /// <summary>Appends <paramref name="matrix"/> (this = this * matrix).</summary>
    public void Append(Matrix matrix) => this = Multiply(this, matrix);

    /// <summary>Prepends <paramref name="matrix"/> (this = matrix * this).</summary>
    public void Prepend(Matrix matrix) => this = Multiply(matrix, this);

    /// <summary>Appends a rotation (degrees) about the origin.</summary>
    public void Rotate(double angle)
    {
        angle %= 360.0;
        this = Multiply(this, CreateRotationRadians(angle * (Math.PI / 180.0)));
    }

    /// <summary>Prepends a rotation (degrees) about the origin.</summary>
    public void RotatePrepend(double angle)
    {
        angle %= 360.0;
        this = Multiply(CreateRotationRadians(angle * (Math.PI / 180.0)), this);
    }

    /// <summary>Appends a rotation (degrees) about the given point.</summary>
    public void RotateAt(double angle, double centerX, double centerY)
    {
        angle %= 360.0;
        this = Multiply(this, CreateRotationRadians(angle * (Math.PI / 180.0), centerX, centerY));
    }

    /// <summary>Prepends a rotation (degrees) about the given point.</summary>
    public void RotateAtPrepend(double angle, double centerX, double centerY)
    {
        angle %= 360.0;
        this = Multiply(CreateRotationRadians(angle * (Math.PI / 180.0), centerX, centerY), this);
    }

    /// <summary>Appends a scale about the origin.</summary>
    public void Scale(double scaleX, double scaleY) => this = Multiply(this, CreateScaling(scaleX, scaleY));

    /// <summary>Prepends a scale about the origin.</summary>
    public void ScalePrepend(double scaleX, double scaleY) => this = Multiply(CreateScaling(scaleX, scaleY), this);

    /// <summary>Appends a scale about the given center.</summary>
    public void ScaleAt(double scaleX, double scaleY, double centerX, double centerY)
        => this = Multiply(this, CreateScaling(scaleX, scaleY, centerX, centerY));

    /// <summary>Prepends a scale about the given center.</summary>
    public void ScaleAtPrepend(double scaleX, double scaleY, double centerX, double centerY)
        => this = Multiply(CreateScaling(scaleX, scaleY, centerX, centerY), this);

    /// <summary>Appends a skew (degrees).</summary>
    public void Skew(double skewX, double skewY)
    {
        skewX %= 360.0;
        skewY %= 360.0;
        this = Multiply(this, CreateSkewRadians(skewX * (Math.PI / 180.0), skewY * (Math.PI / 180.0)));
    }

    /// <summary>Prepends a skew (degrees).</summary>
    public void SkewPrepend(double skewX, double skewY)
    {
        skewX %= 360.0;
        skewY %= 360.0;
        this = Multiply(CreateSkewRadians(skewX * (Math.PI / 180.0), skewY * (Math.PI / 180.0)), this);
    }

    /// <summary>Appends a translation (additive on the offset, matching WPF).</summary>
    public void Translate(double offsetX, double offsetY)
    {
        OffsetX += offsetX;
        OffsetY += offsetY;
    }

    /// <summary>Prepends a translation.</summary>
    public void TranslatePrepend(double offsetX, double offsetY)
        => this = Multiply(CreateTranslation(offsetX, offsetY), this);

    /// <summary>
    /// Transforms a point by this matrix.
    /// </summary>
    public readonly Point Transform(Point point)
    {
        return new Point(
            point.X * M11 + point.Y * M21 + OffsetX,
            point.X * M12 + point.Y * M22 + OffsetY);
    }

    /// <summary>Transforms each point in the array in place.</summary>
    public readonly void Transform(Point[] points)
    {
        if (points == null) return;
        for (int i = 0; i < points.Length; i++)
            points[i] = Transform(points[i]);
    }

    /// <summary>Transforms a vector by this matrix (ignores translation).</summary>
    public readonly Vector Transform(Vector vector)
    {
        return new Vector(
            vector.X * M11 + vector.Y * M21,
            vector.X * M12 + vector.Y * M22);
    }

    /// <summary>Transforms each vector in the array in place (ignores translation).</summary>
    public readonly void Transform(Vector[] vectors)
    {
        if (vectors == null) return;
        for (int i = 0; i < vectors.Length; i++)
            vectors[i] = Transform(vectors[i]);
    }

    /// <summary>Gets the determinant of this matrix.</summary>
    public readonly double Determinant => M11 * M22 - M12 * M21;

    /// <summary>Gets a value indicating whether this matrix is invertible.</summary>
    public readonly bool HasInverse => Math.Abs(Determinant) >= SingularEpsilon;

    /// <summary>
    /// Inverts this matrix in place.
    /// </summary>
    /// <exception cref="InvalidOperationException">The matrix is not invertible.</exception>
    public void Invert()
    {
        if (!TryInvert(out var inverse))
            throw new InvalidOperationException("Transform is not invertible.");
        this = inverse;
    }

    /// <summary>
    /// Attempts to compute the inverse of this 2D affine matrix.
    /// Returns false if the matrix is singular (determinant ~= 0,
    /// e.g. a ScaleTransform with 0 scale).
    /// </summary>
    public readonly bool TryInvert(out Matrix inverse)
    {
        var det = Determinant;
        if (Math.Abs(det) < SingularEpsilon)
        {
            inverse = default;
            return false;
        }
        var invDet = 1.0 / det;
        inverse = new Matrix(
             M22 * invDet,
            -M12 * invDet,
            -M21 * invDet,
             M11 * invDet,
            (M21 * OffsetY - M22 * OffsetX) * invDet,
            (M12 * OffsetX - M11 * OffsetY) * invDet);
        return true;
    }

    // --- WPF rotation/scale/skew/translation factories (radians; general form, no _type) ---

    private static Matrix CreateRotationRadians(double angle) => CreateRotationRadians(angle, 0, 0);

    private static Matrix CreateRotationRadians(double angle, double centerX, double centerY)
    {
        double sin = Math.Sin(angle);
        double cos = Math.Cos(angle);
        double dx = (centerX * (1.0 - cos)) + (centerY * sin);
        double dy = (centerY * (1.0 - cos)) - (centerX * sin);
        return new Matrix(cos, sin, -sin, cos, dx, dy);
    }

    private static Matrix CreateScaling(double scaleX, double scaleY)
        => new(scaleX, 0, 0, scaleY, 0, 0);

    private static Matrix CreateScaling(double scaleX, double scaleY, double centerX, double centerY)
        => new(scaleX, 0, 0, scaleY, centerX - scaleX * centerX, centerY - scaleY * centerY);

    private static Matrix CreateSkewRadians(double skewX, double skewY)
        => new(1.0, Math.Tan(skewY), Math.Tan(skewX), 1.0, 0.0, 0.0);

    private static Matrix CreateTranslation(double offsetX, double offsetY)
        => new(1, 0, 0, 1, offsetX, offsetY);

    /// <inheritdoc />
    public readonly bool Equals(Matrix other) =>
        M11 == other.M11 && M12 == other.M12 &&
        M21 == other.M21 && M22 == other.M22 &&
        OffsetX == other.OffsetX && OffsetY == other.OffsetY;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Matrix other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);

    public static bool operator ==(Matrix left, Matrix right) => left.Equals(right);
    public static bool operator !=(Matrix left, Matrix right) => !left.Equals(right);
    public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);

    /// <summary>
    /// Parses a <see cref="Matrix"/> from a string of the form "m11,m12,m21,m22,offsetX,offsetY"
    /// or "Identity".
    /// </summary>
    public static Matrix Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var s = source.Trim();
        if (s.Equals("Identity", StringComparison.Ordinal))
            return Identity;

        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
            throw new FormatException($"'{source}' is not a valid Matrix.");

        return new Matrix(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture),
            double.Parse(parts[4], CultureInfo.InvariantCulture),
            double.Parse(parts[5], CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override readonly string ToString() => ConvertToString(null, null);

    /// <summary>Formats the matrix using the given format provider.</summary>
    public readonly string ToString(IFormatProvider? provider) => ConvertToString(null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? provider) => ConvertToString(format, provider);

    private readonly string ConvertToString(string? format, IFormatProvider? provider)
    {
        if (IsIdentity)
            return "Identity";

        provider ??= CultureInfo.InvariantCulture;
        char sep = ',';
        string fmt = "{1:" + (format ?? string.Empty) + "}{0}{2:" + (format ?? string.Empty) + "}{0}{3:" + (format ?? string.Empty)
                   + "}{0}{4:" + (format ?? string.Empty) + "}{0}{5:" + (format ?? string.Empty) + "}{0}{6:" + (format ?? string.Empty) + "}";
        return string.Format(provider, fmt, sep, M11, M12, M21, M22, OffsetX, OffsetY);
    }
}

/// <summary>
/// Applies an arbitrary matrix transform.
/// </summary>
public sealed class MatrixTransform : Transform
{
    private Matrix _matrix = Matrix.Identity;

    /// <summary>
    /// Gets or sets the transform matrix.
    /// </summary>
    public Matrix Matrix
    {
        get => _matrix;
        set
        {
            if (_matrix == value) return;
            _matrix = value;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override Matrix Value => _matrix;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatrixTransform"/> class.
    /// </summary>
    public MatrixTransform()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MatrixTransform"/> class.
    /// </summary>
    /// <param name="matrix">The transform matrix.</param>
    public MatrixTransform(Matrix matrix)
    {
        _matrix = matrix;
    }
}

/// <summary>
/// Scales an object in the 2D coordinate system.
/// </summary>
public sealed class ScaleTransform : Transform
{
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _centerX;
    private double _centerY;

    public ScaleTransform() { }

    public ScaleTransform(double scaleX, double scaleY)
    {
        _scaleX = scaleX;
        _scaleY = scaleY;
    }

    public ScaleTransform(double scaleX, double scaleY, double centerX, double centerY)
    {
        _scaleX = scaleX;
        _scaleY = scaleY;
        _centerX = centerX;
        _centerY = centerY;
    }

    /// <summary>
    /// Gets or sets the X scale factor.
    /// </summary>
    public double ScaleX
    {
        get => _scaleX;
        set
        {
            if (_scaleX == value) return;
            _scaleX = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the Y scale factor.
    /// </summary>
    public double ScaleY
    {
        get => _scaleY;
        set
        {
            if (_scaleY == value) return;
            _scaleY = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX
    {
        get => _centerX;
        set
        {
            if (_centerX == value) return;
            _centerX = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY
    {
        get => _centerY;
        set
        {
            if (_centerY == value) return;
            _centerY = value;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            if (_centerX == 0 && _centerY == 0)
            {
                return new Matrix(_scaleX, 0, 0, _scaleY, 0, 0);
            }

            return new Matrix(
                _scaleX, 0, 0, _scaleY,
                _centerX - _scaleX * _centerX,
                _centerY - _scaleY * _centerY);
        }
    }
}

/// <summary>
/// Rotates an object in the 2D coordinate system.
/// </summary>
public sealed class RotateTransform : Transform
{
    private double _angle;
    private double _centerX;
    private double _centerY;

    public RotateTransform() { }

    public RotateTransform(double angle)
    {
        _angle = angle;
    }

    public RotateTransform(double angle, double centerX, double centerY)
    {
        _angle = angle;
        _centerX = centerX;
        _centerY = centerY;
    }

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double Angle
    {
        get => _angle;
        set
        {
            if (_angle == value) return;
            _angle = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX
    {
        get => _centerX;
        set
        {
            if (_centerX == value) return;
            _centerX = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY
    {
        get => _centerY;
        set
        {
            if (_centerY == value) return;
            _centerY = value;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            var radians = _angle * Math.PI / 180;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            if (_centerX == 0 && _centerY == 0)
            {
                return new Matrix(cos, sin, -sin, cos, 0, 0);
            }

            return new Matrix(
                cos, sin, -sin, cos,
                _centerX * (1 - cos) + _centerY * sin,
                _centerY * (1 - cos) - _centerX * sin);
        }
    }
}

/// <summary>
/// Translates an object in the 2D coordinate system.
/// </summary>
public sealed class TranslateTransform : Transform
{
    private double _x;
    private double _y;

    public TranslateTransform() { }

    public TranslateTransform(double x, double y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>
    /// Gets or sets the X translation.
    /// </summary>
    public double X
    {
        get => _x;
        set
        {
            if (_x == value) return;
            _x = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the Y translation.
    /// </summary>
    public double Y
    {
        get => _y;
        set
        {
            if (_y == value) return;
            _y = value;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override Matrix Value => new Matrix(1, 0, 0, 1, _x, _y);
}

/// <summary>
/// Skews an object in the 2D coordinate system.
/// </summary>
public sealed class SkewTransform : Transform
{
    private double _angleX;
    private double _angleY;
    private double _centerX;
    private double _centerY;

    /// <summary>
    /// Gets or sets the X skew angle in degrees.
    /// </summary>
    public double AngleX
    {
        get => _angleX;
        set
        {
            if (_angleX == value) return;
            _angleX = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the Y skew angle in degrees.
    /// </summary>
    public double AngleY
    {
        get => _angleY;
        set
        {
            if (_angleY == value) return;
            _angleY = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center X coordinate.
    /// </summary>
    public double CenterX
    {
        get => _centerX;
        set
        {
            if (_centerX == value) return;
            _centerX = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the center Y coordinate.
    /// </summary>
    public double CenterY
    {
        get => _centerY;
        set
        {
            if (_centerY == value) return;
            _centerY = value;
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            var tanX = Math.Tan(_angleX * Math.PI / 180);
            var tanY = Math.Tan(_angleY * Math.PI / 180);

            return new Matrix(
                1, tanY, tanX, 1,
                -_centerY * tanX,
                -_centerX * tanY);
        }
    }
}

/// <summary>
/// Represents a composite transform that combines multiple transforms.
/// Forwards each child transform's Changed event so the owning element
/// re-renders when an inner transform mutates.
/// </summary>
public sealed class TransformGroup : Transform
{
    private readonly TransformChildCollection _children;

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
        _children = new TransformChildCollection(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformGroup"/> class with the specified children.
    /// </summary>
    /// <param name="transforms">The transforms to add.</param>
    public TransformGroup(params Transform[] transforms)
        : this()
    {
        foreach (var t in transforms)
            _children.Add(t);
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

    private sealed class TransformChildCollection : Collection<Transform>
    {
        private readonly TransformGroup _owner;

        public TransformChildCollection(TransformGroup owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, Transform item)
        {
            base.InsertItem(index, item);
            if (item is not null) item.Changed += OnChildChanged;
            _owner.RaiseChanged();
        }

        protected override void RemoveItem(int index)
        {
            var removed = base[index];
            if (removed is not null) removed.Changed -= OnChildChanged;
            base.RemoveItem(index);
            _owner.RaiseChanged();
        }

        protected override void ClearItems()
        {
            foreach (var child in this)
            {
                if (child is not null) child.Changed -= OnChildChanged;
            }
            base.ClearItems();
            _owner.RaiseChanged();
        }

        protected override void SetItem(int index, Transform item)
        {
            var old = base[index];
            if (ReferenceEquals(old, item)) return;
            if (old is not null) old.Changed -= OnChildChanged;
            base.SetItem(index, item);
            if (item is not null) item.Changed += OnChildChanged;
            _owner.RaiseChanged();
        }

        private void OnChildChanged(object? sender, EventArgs e)
        {
            _owner.RaiseChanged();
        }
    }
}

/// <summary>
/// A composite transform that provides convenient access to common transform operations.
/// Similar to WPF's CompositeTransform in WinUI.
/// </summary>
public sealed class CompositeTransform : Transform
{
    private double _centerX;
    private double _centerY;
    private double _rotation;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _skewX;
    private double _skewY;
    private double _translateX;
    private double _translateY;

    /// <summary>
    /// Gets or sets the center X coordinate for all transforms.
    /// </summary>
    public double CenterX
    {
        get => _centerX;
        set { if (_centerX != value) { _centerX = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the center Y coordinate for all transforms.
    /// </summary>
    public double CenterY
    {
        get => _centerY;
        set { if (_centerY != value) { _centerY = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double Rotation
    {
        get => _rotation;
        set { if (_rotation != value) { _rotation = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the X scale factor.
    /// </summary>
    public double ScaleX
    {
        get => _scaleX;
        set { if (_scaleX != value) { _scaleX = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the Y scale factor.
    /// </summary>
    public double ScaleY
    {
        get => _scaleY;
        set { if (_scaleY != value) { _scaleY = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the X skew angle in degrees.
    /// </summary>
    public double SkewX
    {
        get => _skewX;
        set { if (_skewX != value) { _skewX = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the Y skew angle in degrees.
    /// </summary>
    public double SkewY
    {
        get => _skewY;
        set { if (_skewY != value) { _skewY = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the X translation.
    /// </summary>
    public double TranslateX
    {
        get => _translateX;
        set { if (_translateX != value) { _translateX = value; RaiseChanged(); } }
    }

    /// <summary>
    /// Gets or sets the Y translation.
    /// </summary>
    public double TranslateY
    {
        get => _translateY;
        set { if (_translateY != value) { _translateY = value; RaiseChanged(); } }
    }

    /// <inheritdoc />
    public override Matrix Value
    {
        get
        {
            // Order: Scale -> Skew -> Rotate -> Translate (standard order)
            var result = Matrix.Identity;

            // Apply center offset
            if (_centerX != 0 || _centerY != 0)
            {
                result = new Matrix(1, 0, 0, 1, -_centerX, -_centerY);
            }

            // Scale
            if (_scaleX != 1 || _scaleY != 1)
            {
                var scale = new Matrix(_scaleX, 0, 0, _scaleY, 0, 0);
                result = Matrix.Multiply(result, scale);
            }

            // Skew
            if (_skewX != 0 || _skewY != 0)
            {
                var tanX = Math.Tan(_skewX * Math.PI / 180);
                var tanY = Math.Tan(_skewY * Math.PI / 180);
                var skew = new Matrix(1, tanY, tanX, 1, 0, 0);
                result = Matrix.Multiply(result, skew);
            }

            // Rotate
            if (_rotation != 0)
            {
                var radians = _rotation * Math.PI / 180;
                var cos = Math.Cos(radians);
                var sin = Math.Sin(radians);
                var rotate = new Matrix(cos, sin, -sin, cos, 0, 0);
                result = Matrix.Multiply(result, rotate);
            }

            // Restore center offset
            if (_centerX != 0 || _centerY != 0)
            {
                var restore = new Matrix(1, 0, 0, 1, _centerX, _centerY);
                result = Matrix.Multiply(result, restore);
            }

            // Translate
            if (_translateX != 0 || _translateY != 0)
            {
                var translate = new Matrix(1, 0, 0, 1, _translateX, _translateY);
                result = Matrix.Multiply(result, translate);
            }

            return result;
        }
    }
}
