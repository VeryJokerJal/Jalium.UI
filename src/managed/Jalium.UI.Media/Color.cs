namespace Jalium.UI.Media;

/// <summary>
/// Represents an ARGB color.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>
    /// Gets the alpha component (0-255).
    /// </summary>
    public byte A { get; }

    /// <summary>
    /// Gets the red component (0-255).
    /// </summary>
    public byte R { get; }

    /// <summary>
    /// Gets the green component (0-255).
    /// </summary>
    public byte G { get; }

    /// <summary>
    /// Gets the blue component (0-255).
    /// </summary>
    public byte B { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Color"/> struct.
    /// </summary>
    public Color(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>
    /// Creates a color from ARGB components.
    /// </summary>
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>
    /// Creates a color from a packed ARGB uint value.
    /// </summary>
    public static Color FromArgb(uint argb) => new(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    /// <summary>
    /// Creates a color from a packed ARGB int value.
    /// </summary>
    public static Color FromArgb(int argb) => FromArgb(unchecked((uint)argb));

    /// <summary>
    /// Converts the color to a packed ARGB uint value.
    /// </summary>
    public uint ToArgb() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    /// <summary>
    /// Converts the color to a packed ARGB int value.
    /// </summary>
    public int ToArgbInt32() => unchecked((int)ToArgb());

    /// <summary>
    /// Creates an opaque color from RGB components.
    /// </summary>
    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>
    /// Creates a color from scRGB (linear) float values (0.0 - 1.0).
    /// RGB components are in linear light space and are converted to sRGB
    /// gamma for storage.  Alpha is linear in both spaces.
    /// </summary>
    public static Color FromScRgb(float a, float r, float g, float b)
    {
        return new Color(
            (byte)Math.Round(Math.Clamp(a, 0f, 1f) * 255),
            (byte)Math.Round(LinearToSrgb(Math.Clamp(r, 0f, 1f)) * 255),
            (byte)Math.Round(LinearToSrgb(Math.Clamp(g, 0f, 1f)) * 255),
            (byte)Math.Round(LinearToSrgb(Math.Clamp(b, 0f, 1f)) * 255));
    }

    /// <summary>
    /// Gets the alpha component as a normalized float (0.0 - 1.0).
    /// </summary>
    public float ScA => A / 255f;

    /// <summary>
    /// Gets the red component as a linear-light scRGB float (0.0 - 1.0).
    /// </summary>
    public float ScR => SrgbToLinear(R / 255f);

    /// <summary>
    /// Gets the green component as a linear-light scRGB float (0.0 - 1.0).
    /// </summary>
    public float ScG => SrgbToLinear(G / 255f);

    /// <summary>
    /// Gets the blue component as a linear-light scRGB float (0.0 - 1.0).
    /// </summary>
    public float ScB => SrgbToLinear(B / 255f);

    private static float SrgbToLinear(float s)
        => s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float l)
        => l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1f / 2.4f) - 0.055f;

    #region Predefined Colors

    public static Color Transparent => new(0, 255, 255, 255);
    public static Color Black => new(255, 0, 0, 0);
    public static Color White => new(255, 255, 255, 255);
    public static Color Red => new(255, 255, 0, 0);
    public static Color Green => new(255, 0, 128, 0);
    public static Color Blue => new(255, 0, 0, 255);
    public static Color Yellow => new(255, 255, 255, 0);
    public static Color Cyan => new(255, 0, 255, 255);
    public static Color Magenta => new(255, 255, 0, 255);
    public static Color Orange => new(255, 255, 165, 0);
    public static Color Purple => new(255, 128, 0, 128);
    public static Color Gray => new(255, 128, 128, 128);
    public static Color LightGray => new(255, 211, 211, 211);
    public static Color DarkGray => new(255, 169, 169, 169);

    #endregion

    /// <inheritdoc />
    public bool Equals(Color other) => A == other.A && R == other.R && G == other.G && B == other.B;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);

    /// <inheritdoc />
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
