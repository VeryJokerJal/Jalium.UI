using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Represents an x- and y-coordinate pair in two-dimensional space.
/// </summary>
[TypeConverter(typeof(PointConverter))]
public struct Point : IEquatable<Point>, IFormattable
{
    /// <summary>
    /// Gets the X coordinate.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets the Y coordinate.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Point"/> struct.
    /// </summary>
    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets a point at the origin (0, 0).
    /// </summary>
    public static Point Zero => new(0, 0);

    /// <inheritdoc />
    public readonly bool Equals(Point other) => Equals(this, other);

    /// <summary>
    /// Compares two points for value equality. Unlike the equality operator,
    /// this method treats <see cref="double.NaN"/> as equal to itself.
    /// </summary>
    public static bool Equals(Point point1, Point point2) =>
        point1.X.Equals(point2.X) && point1.Y.Equals(point2.Y);

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Point other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => PrimitiveFormatting.FormatPair(X, Y, null, null);

    /// <summary>
    /// Formats this point using the supplied culture.
    /// </summary>
    public readonly string ToString(IFormatProvider? provider) =>
        PrimitiveFormatting.FormatPair(X, Y, null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        PrimitiveFormatting.FormatPair(X, Y, format, formatProvider);

    /// <summary>
    /// Parses a point from its invariant XAML representation.
    /// </summary>
    public static Point Parse(string source)
    {
        (double x, double y) = PrimitiveFormatting.ParsePair(source, nameof(source));
        return new Point(x, y);
    }

    /// <summary>
    /// Offsets this point in place.
    /// </summary>
    public void Offset(double offsetX, double offsetY)
    {
        X += offsetX;
        Y += offsetY;
    }

    public static bool operator ==(Point left, Point right) => left.X == right.X && left.Y == right.Y;
    public static bool operator !=(Point left, Point right) => !(left == right);
    public static Point operator +(Point left, Vector right) => new(left.X + right.X, left.Y + right.Y);
    public static Point operator -(Point left, Vector right) => new(left.X - right.X, left.Y - right.Y);
    public static Vector operator -(Point left, Point right) => new(left.X - right.X, left.Y - right.Y);
    public static Point operator *(Point point, Matrix matrix) => matrix.Transform(point);

    public static Point Add(Point point, Vector vector) => point + vector;
    public static Point Subtract(Point point, Vector vector) => point - vector;
    public static Vector Subtract(Point point1, Point point2) => point1 - point2;
    public static Point Multiply(Point point, Matrix matrix) => point * matrix;

    public static explicit operator Size(Point point) => new(Math.Abs(point.X), Math.Abs(point.Y));
    public static explicit operator Vector(Point point) => new(point.X, point.Y);
}

/// <summary>
/// Represents a displacement in 2D space.
/// </summary>
[TypeConverter(typeof(VectorConverter))]
public struct Vector : IEquatable<Vector>, IFormattable
{
    /// <summary>
    /// Gets the X component.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets the Y component.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vector"/> struct.
    /// </summary>
    public Vector(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets a zero vector.
    /// </summary>
    public static Vector Zero => new(0, 0);

    /// <summary>
    /// Gets the length of this vector.
    /// </summary>
    public readonly double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>
    /// Gets the squared length without performing a square root.
    /// </summary>
    public readonly double LengthSquared => X * X + Y * Y;

    /// <inheritdoc />
    public readonly bool Equals(Vector other) => Equals(this, other);

    /// <summary>
    /// Compares two vectors for value equality. Unlike the equality operator,
    /// this method treats <see cref="double.NaN"/> as equal to itself.
    /// </summary>
    public static bool Equals(Vector vector1, Vector vector2) =>
        vector1.X.Equals(vector2.X) && vector1.Y.Equals(vector2.Y);

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Vector other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => PrimitiveFormatting.FormatPair(X, Y, null, null);

    /// <summary>
    /// Formats this vector using the supplied culture.
    /// </summary>
    public readonly string ToString(IFormatProvider? provider) =>
        PrimitiveFormatting.FormatPair(X, Y, null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        PrimitiveFormatting.FormatPair(X, Y, format, formatProvider);

    /// <summary>
    /// Parses a vector from its invariant XAML representation.
    /// </summary>
    public static Vector Parse(string source)
    {
        (double x, double y) = PrimitiveFormatting.ParsePair(source, nameof(source));
        return new Vector(x, y);
    }

    /// <summary>
    /// Normalizes this vector in place while avoiding intermediate overflow.
    /// </summary>
    public void Normalize()
    {
        this /= Math.Max(Math.Abs(X), Math.Abs(Y));
        this /= Length;
    }

    /// <summary>
    /// Negates this vector in place.
    /// </summary>
    public void Negate()
    {
        X = -X;
        Y = -Y;
    }

    public static bool operator ==(Vector left, Vector right) => left.X == right.X && left.Y == right.Y;
    public static bool operator !=(Vector left, Vector right) => !(left == right);
    public static Vector operator -(Vector vector) => new(-vector.X, -vector.Y);
    public static Vector operator +(Vector left, Vector right) => new(left.X + right.X, left.Y + right.Y);
    public static Vector operator -(Vector left, Vector right) => new(left.X - right.X, left.Y - right.Y);
    public static Point operator +(Vector vector, Point point) => new(point.X + vector.X, point.Y + vector.Y);
    public static Vector operator *(Vector vector, double scalar) => new(vector.X * scalar, vector.Y * scalar);
    public static Vector operator *(double scalar, Vector vector) => new(vector.X * scalar, vector.Y * scalar);
    public static Vector operator /(Vector vector, double scalar) => vector * (1.0 / scalar);
    public static Vector operator *(Vector vector, Matrix matrix) => matrix.Transform(vector);
    public static double operator *(Vector vector1, Vector vector2) =>
        vector1.X * vector2.X + vector1.Y * vector2.Y;

    public static Vector Add(Vector vector1, Vector vector2) => vector1 + vector2;
    public static Point Add(Vector vector, Point point) => vector + point;
    public static Vector Subtract(Vector vector1, Vector vector2) => vector1 - vector2;
    public static Vector Multiply(Vector vector, double scalar) => vector * scalar;
    public static Vector Multiply(double scalar, Vector vector) => scalar * vector;
    public static Vector Divide(Vector vector, double scalar) => vector / scalar;
    public static Vector Multiply(Vector vector, Matrix matrix) => vector * matrix;
    public static double Multiply(Vector vector1, Vector vector2) => vector1 * vector2;
    public static double CrossProduct(Vector vector1, Vector vector2) =>
        vector1.X * vector2.Y - vector1.Y * vector2.X;
    public static double Determinant(Vector vector1, Vector vector2) => CrossProduct(vector1, vector2);
    public static double AngleBetween(Vector vector1, Vector vector2)
    {
        double sin = CrossProduct(vector1, vector2);
        double cos = Multiply(vector1, vector2);
        return Math.Atan2(sin, cos) * (180.0 / Math.PI);
    }

    public static explicit operator Size(Vector vector) => new(Math.Abs(vector.X), Math.Abs(vector.Y));
    public static explicit operator Point(Vector vector) => new(vector.X, vector.Y);
}

/// <summary>
/// Represents a width and height in two-dimensional space.
/// </summary>
[Serializable]
[TypeConverter(typeof(SizeConverter))]
public struct Size : IEquatable<Size>, IFormattable
{
    private double _width;
    private double _height;

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width
    {
        readonly get => _width;
        set
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException("Cannot modify an empty Size.");
            }

            if (value < 0)
            {
                throw new ArgumentException("Width cannot be negative.");
            }

            _width = value;
        }
    }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height
    {
        readonly get => _height;
        set
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException("Cannot modify an empty Size.");
            }

            if (value < 0)
            {
                throw new ArgumentException("Height cannot be negative.");
            }

            _height = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Size"/> struct.
    /// </summary>
    public Size(double width, double height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentException("Width and Height cannot be negative.");
        }

        _width = width;
        _height = height;
    }

    /// <summary>
    /// Gets the special empty size whose dimensions are negative infinity.
    /// </summary>
    public static Size Empty => s_empty;

    /// <summary>
    /// Gets a size representing infinity.
    /// </summary>
    public static Size Infinity => new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>
    /// Gets a value indicating whether this is the special <see cref="Empty"/> value.
    /// A zero width and height do not make a size empty.
    /// </summary>
    public readonly bool IsEmpty => _width < 0;

    /// <inheritdoc />
    public readonly bool Equals(Size other) => Equals(this, other);

    /// <summary>
    /// Compares two sizes for value equality. Unlike the equality operator,
    /// this method treats <see cref="double.NaN"/> as equal to itself.
    /// </summary>
    public static bool Equals(Size size1, Size size2)
    {
        if (size1.IsEmpty)
        {
            return size2.IsEmpty;
        }

        return size1.Width.Equals(size2.Width) && size1.Height.Equals(size2.Height);
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Size other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() =>
        IsEmpty ? 0 : Width.GetHashCode() ^ Height.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => ConvertToString(null, null);

    /// <summary>
    /// Formats this size using the supplied culture.
    /// </summary>
    public readonly string ToString(IFormatProvider? provider) => ConvertToString(null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ConvertToString(format, formatProvider);

    /// <summary>
    /// Parses a size from its invariant XAML representation.
    /// </summary>
    public static Size Parse(string source) => PrimitiveFormatting.ParseSize(source);

    internal readonly string ConvertToString(string? format, IFormatProvider? provider)
    {
        return IsEmpty
            ? "Empty"
            : PrimitiveFormatting.FormatPair(Width, Height, format, provider);
    }

    public static bool operator ==(Size left, Size right) =>
        left.Width == right.Width && left.Height == right.Height;

    public static bool operator !=(Size left, Size right) => !(left == right);

    public static explicit operator Point(Size size) => new(size.Width, size.Height);
    public static explicit operator Vector(Size size) => new(size.Width, size.Height);

    private static Size CreateEmptySize()
    {
        var size = default(Size);
        size._width = double.NegativeInfinity;
        size._height = double.NegativeInfinity;
        return size;
    }

    private static readonly Size s_empty = CreateEmptySize();
}

/// <summary>
/// Represents a rectangle defined by its position and size.
/// </summary>
[Serializable]
[TypeConverter(typeof(RectConverter))]
public struct Rect : IEquatable<Rect>, IFormattable
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;

    /// <summary>
    /// Gets or sets the X coordinate of the left edge.
    /// </summary>
    public double X
    {
        readonly get => _x;
        set
        {
            ThrowIfEmpty();
            _x = value;
        }
    }

    /// <summary>
    /// Gets or sets the Y coordinate of the top edge.
    /// </summary>
    public double Y
    {
        readonly get => _y;
        set
        {
            ThrowIfEmpty();
            _y = value;
        }
    }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width
    {
        readonly get => _width;
        set
        {
            ThrowIfEmpty();
            if (value < 0)
            {
                throw new ArgumentException("Width cannot be negative.");
            }

            _width = value;
        }
    }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height
    {
        readonly get => _height;
        set
        {
            ThrowIfEmpty();
            if (value < 0)
            {
                throw new ArgumentException("Height cannot be negative.");
            }

            _height = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(double x, double y, double width, double height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentException("Width and Height cannot be negative.");
        }

        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Initializes a new rectangle that bounds the two specified points.
    /// </summary>
    public Rect(Point point1, Point point2)
    {
        _x = Math.Min(point1.X, point2.X);
        _y = Math.Min(point1.Y, point2.Y);
        _width = Math.Max(Math.Max(point1.X, point2.X) - _x, 0);
        _height = Math.Max(Math.Max(point1.Y, point2.Y) - _y, 0);
    }

    /// <summary>
    /// Initializes a new rectangle that bounds a point and the point reached by the specified vector.
    /// </summary>
    public Rect(Point point, Vector vector)
        : this(point, point + vector)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(Point location, Size size)
    {
        if (size.IsEmpty)
        {
            this = s_empty;
        }
        else
        {
            _x = location.X;
            _y = location.Y;
            _width = size.Width;
            _height = size.Height;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(Size size)
    {
        if (size.IsEmpty)
        {
            this = s_empty;
        }
        else
        {
            _x = 0;
            _y = 0;
            _width = size.Width;
            _height = size.Height;
        }
    }

    /// <summary>
    /// Gets an empty rectangle.
    /// </summary>
    public static Rect Empty => s_empty;

    /// <summary>
    /// Gets or sets the rectangle location.
    /// </summary>
    public Point Location
    {
        readonly get => new(_x, _y);
        set
        {
            ThrowIfEmpty();
            _x = value.X;
            _y = value.Y;
        }
    }

    /// <summary>
    /// Gets or sets the rectangle size.
    /// </summary>
    public Size Size
    {
        readonly get => IsEmpty ? Size.Empty : new Size(_width, _height);
        set
        {
            if (value.IsEmpty)
            {
                this = s_empty;
                return;
            }

            ThrowIfEmpty();
            _width = value.Width;
            _height = value.Height;
        }
    }

    /// <summary>
    /// Gets the left edge X coordinate.
    /// </summary>
    public readonly double Left => _x;

    /// <summary>
    /// Gets the top edge Y coordinate.
    /// </summary>
    public readonly double Top => _y;

    /// <summary>
    /// Gets the right edge X coordinate.
    /// </summary>
    public readonly double Right => IsEmpty ? double.NegativeInfinity : _x + _width;

    /// <summary>
    /// Gets the bottom edge Y coordinate.
    /// </summary>
    public readonly double Bottom => IsEmpty ? double.NegativeInfinity : _y + _height;

    /// <summary>
    /// Gets the top-left corner.
    /// </summary>
    public readonly Point TopLeft => new(Left, Top);

    /// <summary>
    /// Gets the top-right corner.
    /// </summary>
    public readonly Point TopRight => new(Right, Top);

    /// <summary>
    /// Gets the bottom-left corner.
    /// </summary>
    public readonly Point BottomLeft => new(Left, Bottom);

    /// <summary>
    /// Gets the bottom-right corner.
    /// </summary>
    public readonly Point BottomRight => new(Right, Bottom);

    /// <summary>
    /// Gets the center point.
    /// </summary>
    public readonly Point Center => new(_x + _width / 2, _y + _height / 2);

    /// <summary>
    /// Gets a value indicating whether this rectangle is empty.
    /// </summary>
    public readonly bool IsEmpty => _width < 0;

    /// <summary>
    /// Determines whether this rectangle contains the specified point.
    /// </summary>
    public readonly bool Contains(Point point) => Contains(point.X, point.Y);

    /// <summary>
    /// Determines whether this rectangle contains the specified coordinates.
    /// </summary>
    public readonly bool Contains(double x, double y)
    {
        if (IsEmpty)
        {
            return false;
        }

        // Subtracting the extent keeps the comparison valid for an origin of
        // negative infinity paired with a positive-infinite extent.
        return x >= _x && x - _width <= _x && y >= _y && y - _height <= _y;
    }

    /// <summary>
    /// Determines whether this rectangle contains the specified rectangle.
    /// </summary>
    public readonly bool Contains(Rect rect)
    {
        if (IsEmpty || rect.IsEmpty)
        {
            return false;
        }

        return _x <= rect._x
            && _y <= rect._y
            && _x + _width >= rect._x + rect._width
            && _y + _height >= rect._y + rect._height;
    }

    /// <summary>
    /// Determines whether this rectangle intersects with the specified rectangle.
    /// </summary>
    public readonly bool IntersectsWith(Rect rect)
    {
        if (IsEmpty || rect.IsEmpty)
        {
            return false;
        }

        return rect.Left <= Right
            && rect.Right >= Left
            && rect.Top <= Bottom
            && rect.Bottom >= Top;
    }

    /// <summary>
    /// Updates this rectangle to the intersection of itself and another rectangle.
    /// </summary>
    public void Intersect(Rect rect)
    {
        if (!IntersectsWith(rect))
        {
            this = s_empty;
            return;
        }

        double left = Math.Max(Left, rect.Left);
        double top = Math.Max(Top, rect.Top);
        _width = Math.Max(Math.Min(Right, rect.Right) - left, 0);
        _height = Math.Max(Math.Min(Bottom, rect.Bottom) - top, 0);
        _x = left;
        _y = top;
    }

    /// <summary>
    /// Returns the intersection of two rectangles.
    /// </summary>
    public static Rect Intersect(Rect rect1, Rect rect2)
    {
        rect1.Intersect(rect2);
        return rect1;
    }

    /// <summary>
    /// Updates this rectangle to the union of itself and another rectangle.
    /// </summary>
    public void Union(Rect rect)
    {
        if (IsEmpty)
        {
            this = rect;
            return;
        }

        if (rect.IsEmpty)
        {
            return;
        }

        double left = Math.Min(Left, rect.Left);
        double top = Math.Min(Top, rect.Top);

        _width = rect.Width == double.PositiveInfinity || Width == double.PositiveInfinity
            ? double.PositiveInfinity
            : Math.Max(Math.Max(Right, rect.Right) - left, 0);
        _height = rect.Height == double.PositiveInfinity || Height == double.PositiveInfinity
            ? double.PositiveInfinity
            : Math.Max(Math.Max(Bottom, rect.Bottom) - top, 0);
        _x = left;
        _y = top;
    }

    /// <summary>
    /// Returns the union of two rectangles.
    /// </summary>
    public static Rect Union(Rect rect1, Rect rect2)
    {
        rect1.Union(rect2);
        return rect1;
    }

    /// <summary>
    /// Updates this rectangle to contain the specified point.
    /// </summary>
    public void Union(Point point) => Union(new Rect(point, point));

    /// <summary>
    /// Returns the union of a rectangle and a point.
    /// </summary>
    public static Rect Union(Rect rect, Point point)
    {
        rect.Union(new Rect(point, point));
        return rect;
    }

    /// <summary>
    /// Offsets this rectangle by the specified vector.
    /// </summary>
    public void Offset(Vector offsetVector) => Offset(offsetVector.X, offsetVector.Y);

    /// <summary>
    /// Offsets this rectangle by the specified amounts.
    /// </summary>
    public void Offset(double offsetX, double offsetY)
    {
        ThrowIfEmptyForMethod();
        _x += offsetX;
        _y += offsetY;
    }

    /// <summary>
    /// Returns the result of offsetting a rectangle by a vector.
    /// </summary>
    public static Rect Offset(Rect rect, Vector offsetVector)
    {
        rect.Offset(offsetVector.X, offsetVector.Y);
        return rect;
    }

    /// <summary>
    /// Returns the result of offsetting a rectangle by the specified amounts.
    /// </summary>
    public static Rect Offset(Rect rect, double offsetX, double offsetY)
    {
        rect.Offset(offsetX, offsetY);
        return rect;
    }

    /// <summary>
    /// Inflates this rectangle in all directions by the specified size.
    /// </summary>
    public void Inflate(Size size) => Inflate(size.Width, size.Height);

    /// <summary>
    /// Inflates this rectangle in all directions by the specified amounts.
    /// </summary>
    public void Inflate(double width, double height)
    {
        ThrowIfEmptyForMethod();

        _x -= width;
        _y -= height;
        _width += width;
        _width += width;
        _height += height;
        _height += height;

        if (!(_width >= 0 && _height >= 0))
        {
            this = s_empty;
        }
    }

    /// <summary>
    /// Returns the result of inflating a rectangle by the specified size.
    /// </summary>
    public static Rect Inflate(Rect rect, Size size)
    {
        rect.Inflate(size.Width, size.Height);
        return rect;
    }

    /// <summary>
    /// Returns the result of inflating a rectangle by the specified amounts.
    /// </summary>
    public static Rect Inflate(Rect rect, double width, double height)
    {
        rect.Inflate(width, height);
        return rect;
    }

    /// <summary>
    /// Returns the axis-aligned bounds of a transformed rectangle.
    /// </summary>
    public static Rect Transform(Rect rect, Matrix matrix)
    {
        rect.Transform(matrix);
        return rect;
    }

    /// <summary>
    /// Updates this rectangle to the axis-aligned bounds of its transformed value.
    /// </summary>
    public void Transform(Matrix matrix)
    {
        if (IsEmpty)
        {
            return;
        }

        // Mirror MatrixUtil.TransformRect's identity/scale/translate paths instead
        // of always transforming four corners. Apart from avoiding unnecessary
        // work, this is observable for unbounded rectangles: Infinity * 0 in the
        // general point formula would otherwise turn an identity transform into
        // NaN even though WPF leaves the rectangle untouched.
        if (matrix.IsIdentity)
        {
            return;
        }

        if (matrix.M12 == 0 && matrix.M21 == 0)
        {
            _x *= matrix.M11;
            _y *= matrix.M22;
            _width *= matrix.M11;
            _height *= matrix.M22;

            if (_width < 0)
            {
                _x += _width;
                _width = -_width;
            }

            if (_height < 0)
            {
                _y += _height;
                _height = -_height;
            }

            _x += matrix.OffsetX;
            _y += matrix.OffsetY;
            return;
        }

        Point topLeft = matrix.Transform(new Point(Left, Top));
        Point topRight = matrix.Transform(new Point(Right, Top));
        Point bottomRight = matrix.Transform(new Point(Right, Bottom));
        Point bottomLeft = matrix.Transform(new Point(Left, Bottom));

        double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));

        _x = left;
        _y = top;
        _width = right - left;
        _height = bottom - top;
    }

    /// <summary>
    /// Scales this rectangle about the origin.
    /// </summary>
    public void Scale(double scaleX, double scaleY)
    {
        if (IsEmpty)
        {
            return;
        }

        _x *= scaleX;
        _y *= scaleY;
        _width *= scaleX;
        _height *= scaleY;

        if (scaleX < 0)
        {
            _x += _width;
            _width *= -1;
        }

        if (scaleY < 0)
        {
            _y += _height;
            _height *= -1;
        }
    }

    /// <summary>
    /// Parses a rectangle from its invariant XAML representation.
    /// </summary>
    public static Rect Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<string> tokens = Tokenize(source);
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("A Rect value must contain a token.");
        }

        Rect value;
        if (tokens[0] == "Empty")
        {
            value = Empty;
            if (tokens.Count != 1)
            {
                throw new InvalidOperationException("Rect text contains additional data.");
            }
        }
        else
        {
            IFormatProvider provider = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            value = new Rect(
                Convert.ToDouble(tokens[0], provider),
                Convert.ToDouble(GetRequiredToken(tokens, 1), provider),
                Convert.ToDouble(GetRequiredToken(tokens, 2), provider),
                Convert.ToDouble(GetRequiredToken(tokens, 3), provider));

            if (tokens.Count != 4)
            {
                throw new InvalidOperationException("Rect text contains additional data.");
            }
        }

        return value;
    }

    private static string GetRequiredToken(IReadOnlyList<string> tokens, int index)
    {
        if (index >= tokens.Count)
        {
            throw new InvalidOperationException("A Rect value must contain another token.");
        }

        return tokens[index];
    }

    /// <inheritdoc />
    public readonly bool Equals(Rect other) => Equals(this, other);

    /// <summary>
    /// Compares two rectangles for value equality. Unlike the equality operator,
    /// this method treats <see cref="double.NaN"/> as equal to itself.
    /// </summary>
    public static bool Equals(Rect rect1, Rect rect2)
    {
        if (rect1.IsEmpty)
        {
            return rect2.IsEmpty;
        }

        return rect1.X.Equals(rect2.X)
            && rect1.Y.Equals(rect2.Y)
            && rect1.Width.Equals(rect2.Width)
            && rect1.Height.Equals(rect2.Height);
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Rect other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() =>
        IsEmpty ? 0 : X.GetHashCode() ^ Y.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => ConvertToString(null, null);

    /// <summary>
    /// Formats this rectangle using the supplied culture.
    /// </summary>
    public readonly string ToString(IFormatProvider? provider) => ConvertToString(null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ConvertToString(format, formatProvider);

    internal readonly string ConvertToString(string? format, IFormatProvider? provider)
    {
        if (IsEmpty)
        {
            return "Empty";
        }

        char separator = PrimitiveFormatting.GetNumericListSeparator(provider);
        string componentFormat = format ?? string.Empty;
        return string.Format(
            provider,
            "{1:" + componentFormat + "}{0}{2:" + componentFormat + "}{0}{3:" + componentFormat + "}{0}{4:" + componentFormat + "}",
            separator,
            _x,
            _y,
            _width,
            _height);
    }

    public static bool operator ==(Rect left, Rect right) =>
        left.X == right.X
        && left.Y == right.Y
        && left.Width == right.Width
        && left.Height == right.Height;

    public static bool operator !=(Rect left, Rect right) => !(left == right);

    private readonly void ThrowIfEmpty()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot modify an empty Rect.");
        }
    }

    private readonly void ThrowIfEmptyForMethod()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot call this method on an empty Rect.");
        }
    }

    private static List<string> Tokenize(string source)
    {
        var tokens = new List<string>(4);
        int index = 0;

        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        while (index < source.Length)
        {
            if (source[index] == ',')
            {
                throw new InvalidOperationException("Rect text contains an empty component.");
            }

            int start = index;
            while (index < source.Length && !char.IsWhiteSpace(source[index]) && source[index] != ',')
            {
                index++;
            }

            tokens.Add(source[start..index]);
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index < source.Length && source[index] == ',')
            {
                index++;
                while (index < source.Length && char.IsWhiteSpace(source[index]))
                {
                    index++;
                }

                if (index >= source.Length || source[index] == ',')
                {
                    throw new InvalidOperationException("Rect text contains an empty component.");
                }
            }
        }

        return tokens;
    }

    private static Rect CreateEmptyRect()
    {
        var rect = default(Rect);
        rect._x = double.PositiveInfinity;
        rect._y = double.PositiveInfinity;
        rect._width = double.NegativeInfinity;
        rect._height = double.NegativeInfinity;
        return rect;
    }

    private static readonly Rect s_empty = CreateEmptyRect();
}

/// <summary>
/// Represents the thickness of a frame around a rectangle.
/// </summary>
[TypeConverter(typeof(ThicknessConverter))]
public struct Thickness : IEquatable<Thickness>
{
    /// <summary>
    /// Gets the left thickness.
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// Gets the top thickness.
    /// </summary>
    public double Top { get; set; }

    /// <summary>
    /// Gets the right thickness.
    /// </summary>
    public double Right { get; set; }

    /// <summary>
    /// Gets the bottom thickness.
    /// </summary>
    public double Bottom { get; set; }

    /// <summary>
    /// Gets a thickness with all values set to zero.
    /// </summary>
    public static Thickness Zero => new(0, 0, 0, 0);

    /// <summary>
    /// Initializes a new instance with uniform thickness.
    /// </summary>
    public Thickness(double uniformLength)
        : this(uniformLength, uniformLength, uniformLength, uniformLength)
    {
    }

    /// <summary>
    /// Initializes a new instance with horizontal and vertical thickness.
    /// </summary>
    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    /// <summary>
    /// Initializes a new instance with individual values.
    /// </summary>
    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>
    /// Gets the total horizontal thickness (Left + Right).
    /// </summary>
    public double TotalWidth => Left + Right;

    /// <summary>
    /// Gets the total vertical thickness (Top + Bottom).
    /// </summary>
    public double TotalHeight => Top + Bottom;

    /// <inheritdoc />
    public bool Equals(Thickness other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    /// <inheritdoc />
    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";

    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);
    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);
}

/// <summary>
/// Represents a corner radius for a rectangle.
/// </summary>
[TypeConverter(typeof(CornerRadiusConverter))]
public struct CornerRadius : IEquatable<CornerRadius>
{
    /// <summary>
    /// Gets the top-left corner radius.
    /// </summary>
    public double TopLeft { get; set; }

    /// <summary>
    /// Gets the top-right corner radius.
    /// </summary>
    public double TopRight { get; set; }

    /// <summary>
    /// Gets the bottom-right corner radius.
    /// </summary>
    public double BottomRight { get; set; }

    /// <summary>
    /// Gets the bottom-left corner radius.
    /// </summary>
    public double BottomLeft { get; set; }

    /// <summary>
    /// Initializes a new instance with uniform radius.
    /// </summary>
    public CornerRadius(double uniformRadius)
        : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius)
    {
    }

    /// <summary>
    /// Initializes a new instance with individual values.
    /// </summary>
    public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>
    /// Scales corner radii down proportionally so adjacent corners always fit within the bounds.
    /// </summary>
    internal CornerRadius Normalize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return new CornerRadius(0);
        }

        static double Sanitize(double radius) =>
            double.IsFinite(radius) && radius > 0 ? radius : 0;

        static double GetScale(double available, double first, double second)
        {
            var sum = first + second;
            return sum > 0 ? Math.Min(1.0, available / sum) : 1.0;
        }

        var tl = Sanitize(TopLeft);
        var tr = Sanitize(TopRight);
        var br = Sanitize(BottomRight);
        var bl = Sanitize(BottomLeft);

        var scale = 1.0;
        scale = Math.Min(scale, GetScale(width, tl, tr));
        scale = Math.Min(scale, GetScale(width, bl, br));
        scale = Math.Min(scale, GetScale(height, tl, bl));
        scale = Math.Min(scale, GetScale(height, tr, br));

        if (scale < 1.0)
        {
            tl *= scale;
            tr *= scale;
            br *= scale;
            bl *= scale;
        }

        return new CornerRadius(tl, tr, br, bl);
    }

    /// <inheritdoc />
    public bool Equals(CornerRadius other) =>
        TopLeft == other.TopLeft && TopRight == other.TopRight &&
        BottomRight == other.BottomRight && BottomLeft == other.BottomLeft;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CornerRadius other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

    /// <inheritdoc />
    public override string ToString() => $"{TopLeft},{TopRight},{BottomRight},{BottomLeft}";

    public static bool operator ==(CornerRadius left, CornerRadius right) => left.Equals(right);
    public static bool operator !=(CornerRadius left, CornerRadius right) => !left.Equals(right);
}

/// <summary>
/// Represents an integer x- and y-coordinate pair for pixel-level positioning.
/// </summary>
public readonly struct PixelPoint : IEquatable<PixelPoint>
{
    public int X { get; }
    public int Y { get; }

    public PixelPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static PixelPoint Zero => new(0, 0);

    public bool Equals(PixelPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is PixelPoint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";

    public static bool operator ==(PixelPoint left, PixelPoint right) => left.Equals(right);
    public static bool operator !=(PixelPoint left, PixelPoint right) => !left.Equals(right);
}

/// <summary>
/// Represents an integer rectangle for pixel-level bounds.
/// </summary>
public readonly struct PixelRect : IEquatable<PixelRect>
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public PixelRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static PixelRect Empty => new(0, 0, 0, 0);

    public static PixelRect FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool IsEmpty => Width == 0 && Height == 0;

    public bool Equals(PixelRect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is PixelRect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";

    public static bool operator ==(PixelRect left, PixelRect right) => left.Equals(right);
    public static bool operator !=(PixelRect left, PixelRect right) => !left.Equals(right);
}
