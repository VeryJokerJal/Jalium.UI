using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Describes the location and size of an integer rectangle.
/// </summary>
[Serializable]
[TypeConverter(typeof(Int32RectConverter))]
public struct Int32Rect : IEquatable<Int32Rect>, IFormattable
{
    private static readonly Int32Rect s_empty = new(0, 0, 0, 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="Int32Rect"/> struct.
    /// </summary>
    public Int32Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the empty rectangle.
    /// </summary>
    public static Int32Rect Empty => s_empty;

    /// <summary>
    /// Gets or sets the x-coordinate of the rectangle's upper-left corner.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Gets or sets the y-coordinate of the rectangle's upper-left corner.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the rectangle.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the rectangle.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets a value indicating whether this rectangle is the empty rectangle.
    /// </summary>
    public readonly bool IsEmpty => X == 0 && Y == 0 && Width == 0 && Height == 0;

    /// <summary>
    /// Gets a value indicating whether both dimensions are greater than zero.
    /// </summary>
    public readonly bool HasArea => Width > 0 && Height > 0;

    /// <summary>
    /// Determines whether two rectangles have identical components.
    /// </summary>
    public static bool Equals(Int32Rect int32Rect1, Int32Rect int32Rect2)
    {
        if (int32Rect1.IsEmpty)
        {
            return int32Rect2.IsEmpty;
        }

        return int32Rect1.X.Equals(int32Rect2.X)
            && int32Rect1.Y.Equals(int32Rect2.Y)
            && int32Rect1.Width.Equals(int32Rect2.Width)
            && int32Rect1.Height.Equals(int32Rect2.Height);
    }

    /// <summary>
    /// Creates an <see cref="Int32Rect"/> from its invariant string representation.
    /// </summary>
    public static Int32Rect Parse(string source)
    {
        var tokenizer = new InvariantTokenizer(source);
        string firstToken = tokenizer.NextTokenRequired();

        Int32Rect value;
        if (firstToken == "Empty")
        {
            value = Empty;
        }
        else
        {
            value = new Int32Rect(
                ParseComponent(firstToken),
                ParseComponent(tokenizer.NextTokenRequired()),
                ParseComponent(tokenizer.NextTokenRequired()),
                ParseComponent(tokenizer.NextTokenRequired()));
        }

        tokenizer.LastTokenRequired();
        return value;
    }

    /// <inheritdoc />
    public readonly bool Equals(Int32Rect other) => Equals(this, other);

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Int32Rect other && Equals(this, other);

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
        if (IsEmpty)
        {
            return 0;
        }

        return X.GetHashCode() ^ Y.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();
    }

    /// <inheritdoc />
    public override readonly string ToString() => ConvertToString(null, null);

    /// <summary>
    /// Creates a string representation using the supplied format provider.
    /// </summary>
    public readonly string ToString(IFormatProvider? provider) => ConvertToString(null, provider);

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ConvertToString(format, formatProvider);

    private static int ParseComponent(string token) =>
        int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private readonly string ConvertToString(string? format, IFormatProvider? provider)
    {
        if (IsEmpty)
        {
            return "Empty";
        }

        char separator = GetNumericListSeparator(provider);
        return string.Format(
            provider,
            "{1:" + format + "}{0}{2:" + format + "}{0}{3:" + format + "}{0}{4:" + format + "}",
            separator,
            X,
            Y,
            Width,
            Height);
    }

    private static char GetNumericListSeparator(IFormatProvider? provider)
    {
        NumberFormatInfo numberFormat = NumberFormatInfo.GetInstance(provider);
        return numberFormat.NumberDecimalSeparator.Contains(',', StringComparison.Ordinal) ? ';' : ',';
    }

    public static bool operator ==(Int32Rect left, Int32Rect right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

    public static bool operator !=(Int32Rect left, Int32Rect right) => !(left == right);

    private ref struct InvariantTokenizer
    {
        private readonly ReadOnlySpan<char> _source;
        private int _position;

        public InvariantTokenizer(string? source)
        {
            _source = source.AsSpan();
            _position = 0;
        }

        public string NextTokenRequired()
        {
            SkipWhitespace();
            if (_position >= _source.Length)
            {
                throw new InvalidOperationException("Premature string termination encountered while parsing an Int32Rect.");
            }

            if (_source[_position] == ',')
            {
                throw new InvalidOperationException("Empty token encountered while parsing an Int32Rect.");
            }

            int start = _position;
            while (_position < _source.Length && !char.IsWhiteSpace(_source[_position]) && _source[_position] != ',')
            {
                _position++;
            }

            string token = _source[start.._position].ToString();
            ConsumeSeparator();
            return token;
        }

        public void LastTokenRequired()
        {
            SkipWhitespace();
            if (_position < _source.Length)
            {
                throw new InvalidOperationException("Extra data encountered while parsing an Int32Rect.");
            }
        }

        private void ConsumeSeparator()
        {
            bool consumedWhitespace = SkipWhitespace();
            if (_position < _source.Length && _source[_position] == ',')
            {
                _position++;
                SkipWhitespace();
                if (_position >= _source.Length || _source[_position] == ',')
                {
                    throw new InvalidOperationException("Empty token encountered while parsing an Int32Rect.");
                }
            }
            else if (!consumedWhitespace && _position < _source.Length)
            {
                throw new InvalidOperationException("Invalid token separator encountered while parsing an Int32Rect.");
            }
        }

        private bool SkipWhitespace()
        {
            int start = _position;
            while (_position < _source.Length && char.IsWhiteSpace(_source[_position]))
            {
                _position++;
            }

            return _position != start;
        }
    }
}
