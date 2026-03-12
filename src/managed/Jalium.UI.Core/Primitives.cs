namespace Jalium.UI;

/// <summary>
/// Represents an x- and y-coordinate pair in two-dimensional space.
/// </summary>
public readonly struct Point : IEquatable<Point>
{
    /// <summary>
    /// Gets the X coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y coordinate.
    /// </summary>
    public double Y { get; }

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
    public bool Equals(Point other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Point other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";

    public static bool operator ==(Point left, Point right) => left.Equals(right);
    public static bool operator !=(Point left, Point right) => !left.Equals(right);
    public static Point operator +(Point left, Vector right) => new(left.X + right.X, left.Y + right.Y);
    public static Point operator -(Point left, Vector right) => new(left.X - right.X, left.Y - right.Y);
    public static Vector operator -(Point left, Point right) => new(left.X - right.X, left.Y - right.Y);
}

/// <summary>
/// Represents a displacement in 2D space.
/// </summary>
public readonly struct Vector : IEquatable<Vector>
{
    /// <summary>
    /// Gets the X component.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y component.
    /// </summary>
    public double Y { get; }

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
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <inheritdoc />
    public bool Equals(Vector other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";

    public static bool operator ==(Vector left, Vector right) => left.Equals(right);
    public static bool operator !=(Vector left, Vector right) => !left.Equals(right);
    public static Vector operator +(Vector left, Vector right) => new(left.X + right.X, left.Y + right.Y);
    public static Vector operator -(Vector left, Vector right) => new(left.X - right.X, left.Y - right.Y);
    public static Vector operator *(Vector vector, double scalar) => new(vector.X * scalar, vector.Y * scalar);
    public static Vector operator *(double scalar, Vector vector) => new(vector.X * scalar, vector.Y * scalar);
}

/// <summary>
/// Represents a width and height in two-dimensional space.
/// </summary>
public readonly struct Size : IEquatable<Size>
{
    /// <summary>
    /// Gets the width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Size"/> struct.
    /// </summary>
    public Size(double width, double height)
    {
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    /// <summary>
    /// Gets an empty size (0, 0).
    /// </summary>
    public static Size Empty => new(0, 0);

    /// <summary>
    /// Gets a size representing infinity.
    /// </summary>
    public static Size Infinity => new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>
    /// Gets a value indicating whether this size is empty.
    /// </summary>
    public bool IsEmpty => Width == 0 && Height == 0;

    /// <inheritdoc />
    public bool Equals(Size other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Size other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"{Width} x {Height}";

    public static bool operator ==(Size left, Size right) => left.Equals(right);
    public static bool operator !=(Size left, Size right) => !left.Equals(right);
}

/// <summary>
/// Represents a rectangle defined by its position and size.
/// </summary>
public readonly struct Rect : IEquatable<Rect>
{
    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(Point location, Size size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    public Rect(Size size)
        : this(0, 0, size.Width, size.Height)
    {
    }

    /// <summary>
    /// Gets an empty rectangle.
    /// </summary>
    public static Rect Empty => new(0, 0, 0, 0);

    /// <summary>
    /// Gets the left edge X coordinate.
    /// </summary>
    public double Left => X;

    /// <summary>
    /// Gets the top edge Y coordinate.
    /// </summary>
    public double Top => Y;

    /// <summary>
    /// Gets the right edge X coordinate.
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the bottom edge Y coordinate.
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Gets the top-left corner.
    /// </summary>
    public Point TopLeft => new(X, Y);

    /// <summary>
    /// Gets the top-right corner.
    /// </summary>
    public Point TopRight => new(Right, Y);

    /// <summary>
    /// Gets the bottom-left corner.
    /// </summary>
    public Point BottomLeft => new(X, Bottom);

    /// <summary>
    /// Gets the bottom-right corner.
    /// </summary>
    public Point BottomRight => new(Right, Bottom);

    /// <summary>
    /// Gets the center point.
    /// </summary>
    public Point Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>
    /// Gets the size.
    /// </summary>
    public Size Size => new(Width, Height);

    /// <summary>
    /// Gets a value indicating whether this rectangle is empty.
    /// </summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// Determines whether this rectangle contains the specified point.
    /// </summary>
    public bool Contains(Point point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>
    /// Determines whether this rectangle contains the specified rectangle.
    /// </summary>
    public bool Contains(Rect rect) =>
        X <= rect.X && Right >= rect.Right && Y <= rect.Y && Bottom >= rect.Bottom;

    /// <summary>
    /// Determines whether this rectangle intersects with the specified rectangle.
    /// </summary>
    public bool IntersectsWith(Rect rect) =>
        rect.Left < Right && rect.Right > Left && rect.Top < Bottom && rect.Bottom > Top;

    /// <summary>
    /// Returns the union of this rectangle and another rectangle.
    /// </summary>
    /// <param name="rect">The other rectangle.</param>
    /// <returns>A rectangle that contains both rectangles.</returns>
    public Rect Union(Rect rect)
    {
        if (IsEmpty) return rect;
        if (rect.IsEmpty) return this;

        var minX = Math.Min(X, rect.X);
        var minY = Math.Min(Y, rect.Y);
        var maxX = Math.Max(Right, rect.Right);
        var maxY = Math.Max(Bottom, rect.Bottom);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Returns the intersection of this rectangle and another rectangle.
    /// </summary>
    /// <param name="rect">The other rectangle.</param>
    /// <returns>A rectangle representing the intersection, or Empty if they don't intersect.</returns>
    public Rect Intersect(Rect rect)
    {
        if (!IntersectsWith(rect)) return Empty;

        var minX = Math.Max(X, rect.X);
        var minY = Math.Max(Y, rect.Y);
        var maxX = Math.Min(Right, rect.Right);
        var maxY = Math.Min(Bottom, rect.Bottom);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <inheritdoc />
    public bool Equals(Rect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Rect other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
}

/// <summary>
/// Represents the thickness of a frame around a rectangle.
/// </summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    /// <summary>
    /// Gets the left thickness.
    /// </summary>
    public double Left { get; }

    /// <summary>
    /// Gets the top thickness.
    /// </summary>
    public double Top { get; }

    /// <summary>
    /// Gets the right thickness.
    /// </summary>
    public double Right { get; }

    /// <summary>
    /// Gets the bottom thickness.
    /// </summary>
    public double Bottom { get; }

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
public readonly struct CornerRadius : IEquatable<CornerRadius>
{
    /// <summary>
    /// Gets the top-left corner radius.
    /// </summary>
    public double TopLeft { get; }

    /// <summary>
    /// Gets the top-right corner radius.
    /// </summary>
    public double TopRight { get; }

    /// <summary>
    /// Gets the bottom-right corner radius.
    /// </summary>
    public double BottomRight { get; }

    /// <summary>
    /// Gets the bottom-left corner radius.
    /// </summary>
    public double BottomLeft { get; }

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
