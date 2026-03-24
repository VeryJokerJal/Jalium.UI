using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// A bitmap effect that applies a 5x4 color transformation matrix to the element's rendered content.
/// The matrix transforms each pixel's RGBA channels: [R',G',B',A'] = [matrix] * [R,G,B,A,1].
/// Supports common presets like grayscale, sepia, hue rotation, brightness, contrast, and saturation.
/// </summary>
public sealed class ColorMatrixEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Matrix dependency property.
    /// </summary>
    public static readonly DependencyProperty MatrixProperty =
        DependencyProperty.Register(nameof(Matrix), typeof(ColorMatrix), typeof(ColorMatrixEffect),
            new PropertyMetadata(ColorMatrix.Identity, OnPropertyChanged));

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance with the identity matrix (no transformation).
    /// </summary>
    public ColorMatrixEffect()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified color matrix.
    /// </summary>
    /// <param name="matrix">The color transformation matrix.</param>
    public ColorMatrixEffect(ColorMatrix matrix)
    {
        Matrix = matrix;
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the 5x4 color transformation matrix.
    /// Default value is the identity matrix.
    /// </summary>
    public ColorMatrix Matrix
    {
        get => (ColorMatrix)(GetValue(MatrixProperty) ?? ColorMatrix.Identity);
        set => SetValue(MatrixProperty, value);
    }

    #endregion

    #region Computed Properties

    /// <inheritdoc />
    public override bool HasEffect => !Matrix.IsIdentity;

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.ColorMatrix;

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a grayscale color matrix effect using luminance weights.
    /// </summary>
    /// <param name="amount">The grayscale amount (0 = original, 1 = full grayscale).</param>
    public static ColorMatrixEffect CreateGrayscale(double amount = 1.0)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new ColorMatrixEffect(ColorMatrix.CreateGrayscale(amount));
    }

    /// <summary>
    /// Creates a sepia tone color matrix effect.
    /// </summary>
    /// <param name="amount">The sepia amount (0 = original, 1 = full sepia).</param>
    public static ColorMatrixEffect CreateSepia(double amount = 1.0)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new ColorMatrixEffect(ColorMatrix.CreateSepia(amount));
    }

    /// <summary>
    /// Creates a brightness adjustment color matrix effect.
    /// </summary>
    /// <param name="brightness">The brightness factor (0 = black, 1 = original, &gt;1 = brighter).</param>
    public static ColorMatrixEffect CreateBrightness(double brightness)
    {
        return new ColorMatrixEffect(ColorMatrix.CreateBrightness(brightness));
    }

    /// <summary>
    /// Creates a contrast adjustment color matrix effect.
    /// </summary>
    /// <param name="contrast">The contrast factor (0 = gray, 1 = original, &gt;1 = higher contrast).</param>
    public static ColorMatrixEffect CreateContrast(double contrast)
    {
        return new ColorMatrixEffect(ColorMatrix.CreateContrast(contrast));
    }

    /// <summary>
    /// Creates a saturation adjustment color matrix effect.
    /// </summary>
    /// <param name="saturation">The saturation factor (0 = desaturated, 1 = original, &gt;1 = oversaturated).</param>
    public static ColorMatrixEffect CreateSaturation(double saturation)
    {
        return new ColorMatrixEffect(ColorMatrix.CreateSaturation(saturation));
    }

    /// <summary>
    /// Creates a hue rotation color matrix effect.
    /// </summary>
    /// <param name="degrees">The hue rotation angle in degrees.</param>
    public static ColorMatrixEffect CreateHueRotation(double degrees)
    {
        return new ColorMatrixEffect(ColorMatrix.CreateHueRotation(degrees));
    }

    /// <summary>
    /// Creates a color inversion effect.
    /// </summary>
    /// <param name="amount">The inversion amount (0 = original, 1 = fully inverted).</param>
    public static ColorMatrixEffect CreateInvert(double amount = 1.0)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new ColorMatrixEffect(ColorMatrix.CreateInvert(amount));
    }

    /// <summary>
    /// Creates a color tint effect that shifts all colors toward the specified color.
    /// </summary>
    /// <param name="tintColor">The tint color.</param>
    /// <param name="amount">The tint amount (0 = original, 1 = fully tinted).</param>
    public static ColorMatrixEffect CreateTint(Color tintColor, double amount = 0.5)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new ColorMatrixEffect(ColorMatrix.CreateTint(tintColor, amount));
    }

    #endregion

    #region Property Changed

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorMatrixEffect effect)
        {
            effect.OnEffectChanged();
        }
    }

    #endregion
}

/// <summary>
/// Represents a 5x4 color transformation matrix used by <see cref="ColorMatrixEffect"/>.
/// The matrix operates on [R, G, B, A, 1] vectors to produce transformed [R', G', B', A'] values.
/// Row-major layout: M11-M14 is the first row (transforms to R'), M21-M24 is the second row (G'), etc.
/// The fifth column (M15, M25, M35, M45) provides additive offsets.
/// </summary>
public struct ColorMatrix : IEquatable<ColorMatrix>
{
    // Row 1: R output
    public float M11, M12, M13, M14, M15;
    // Row 2: G output
    public float M21, M22, M23, M24, M25;
    // Row 3: B output
    public float M31, M32, M33, M34, M35;
    // Row 4: A output
    public float M41, M42, M43, M44, M45;

    /// <summary>
    /// Gets the identity color matrix (no transformation).
    /// </summary>
    public static ColorMatrix Identity => new()
    {
        M11 = 1, M22 = 1, M33 = 1, M44 = 1
    };

    /// <summary>
    /// Gets whether this matrix is the identity (no-op) transformation.
    /// </summary>
    public readonly bool IsIdentity =>
        M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 && M15 == 0 &&
        M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 && M25 == 0 &&
        M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 && M35 == 0 &&
        M41 == 0 && M42 == 0 && M43 == 0 && M44 == 1 && M45 == 0;

    /// <summary>
    /// Creates a grayscale matrix using standard luminance weights (Rec. 709).
    /// </summary>
    public static ColorMatrix CreateGrayscale(double amount)
    {
        var t = (float)amount;
        var it = 1f - t;
        const float lr = 0.2126f;
        const float lg = 0.7152f;
        const float lb = 0.0722f;

        return new ColorMatrix
        {
            M11 = lr * t + it, M12 = lg * t, M13 = lb * t,
            M21 = lr * t, M22 = lg * t + it, M23 = lb * t,
            M31 = lr * t, M32 = lg * t, M33 = lb * t + it,
            M44 = 1
        };
    }

    /// <summary>
    /// Creates a sepia tone matrix.
    /// </summary>
    public static ColorMatrix CreateSepia(double amount)
    {
        var t = (float)amount;
        var it = 1f - t;

        return new ColorMatrix
        {
            M11 = it + t * 0.393f, M12 = t * 0.769f, M13 = t * 0.189f,
            M21 = t * 0.349f, M22 = it + t * 0.686f, M23 = t * 0.168f,
            M31 = t * 0.272f, M32 = t * 0.534f, M33 = it + t * 0.131f,
            M44 = 1
        };
    }

    /// <summary>
    /// Creates a brightness adjustment matrix.
    /// </summary>
    public static ColorMatrix CreateBrightness(double brightness)
    {
        var b = (float)brightness;
        return new ColorMatrix
        {
            M11 = b, M22 = b, M33 = b, M44 = 1
        };
    }

    /// <summary>
    /// Creates a contrast adjustment matrix.
    /// </summary>
    public static ColorMatrix CreateContrast(double contrast)
    {
        var c = (float)contrast;
        var offset = (1f - c) * 0.5f;

        return new ColorMatrix
        {
            M11 = c, M15 = offset,
            M22 = c, M25 = offset,
            M33 = c, M35 = offset,
            M44 = 1
        };
    }

    /// <summary>
    /// Creates a saturation adjustment matrix.
    /// </summary>
    public static ColorMatrix CreateSaturation(double saturation)
    {
        var s = (float)saturation;
        var is_ = 1f - s;
        const float lr = 0.2126f;
        const float lg = 0.7152f;
        const float lb = 0.0722f;

        return new ColorMatrix
        {
            M11 = lr * is_ + s, M12 = lg * is_, M13 = lb * is_,
            M21 = lr * is_, M22 = lg * is_ + s, M23 = lb * is_,
            M31 = lr * is_, M32 = lg * is_, M33 = lb * is_ + s,
            M44 = 1
        };
    }

    /// <summary>
    /// Creates a hue rotation matrix.
    /// </summary>
    public static ColorMatrix CreateHueRotation(double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = (float)Math.Cos(radians);
        var sin = (float)Math.Sin(radians);

        // Hue rotation: rotate in the plane orthogonal to (1,1,1) in RGB space
        const float lr = 0.2126f;
        const float lg = 0.7152f;
        const float lb = 0.0722f;

        return new ColorMatrix
        {
            M11 = lr + cos * (1 - lr) + sin * (-lr),
            M12 = lg + cos * (-lg) + sin * (-lg),
            M13 = lb + cos * (-lb) + sin * (1 - lb),

            M21 = lr + cos * (-lr) + sin * 0.143f,
            M22 = lg + cos * (1 - lg) + sin * 0.140f,
            M23 = lb + cos * (-lb) + sin * (-0.283f),

            M31 = lr + cos * (-lr) + sin * (-(1 - lr)),
            M32 = lg + cos * (-lg) + sin * lg,
            M33 = lb + cos * (1 - lb) + sin * lb,

            M44 = 1
        };
    }

    /// <summary>
    /// Creates a color inversion matrix.
    /// </summary>
    public static ColorMatrix CreateInvert(double amount)
    {
        var t = (float)amount;
        var it = 1f - t;

        return new ColorMatrix
        {
            M11 = it - t, M15 = t,
            M22 = it - t, M25 = t,
            M33 = it - t, M35 = t,
            M44 = 1
        };
    }

    /// <summary>
    /// Creates a color tint matrix.
    /// </summary>
    public static ColorMatrix CreateTint(Color tintColor, double amount)
    {
        var t = (float)amount;
        var it = 1f - t;
        var r = tintColor.R / 255f;
        var g = tintColor.G / 255f;
        var b = tintColor.B / 255f;

        return new ColorMatrix
        {
            M11 = it, M15 = r * t,
            M22 = it, M25 = g * t,
            M33 = it, M35 = b * t,
            M44 = 1
        };
    }

    /// <summary>
    /// Multiplies two color matrices.
    /// </summary>
    public static ColorMatrix Multiply(ColorMatrix a, ColorMatrix b)
    {
        return new ColorMatrix
        {
            M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
            M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
            M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
            M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
            M15 = a.M11 * b.M15 + a.M12 * b.M25 + a.M13 * b.M35 + a.M14 * b.M45 + a.M15,

            M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
            M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
            M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
            M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
            M25 = a.M21 * b.M15 + a.M22 * b.M25 + a.M23 * b.M35 + a.M24 * b.M45 + a.M25,

            M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
            M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
            M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
            M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
            M35 = a.M31 * b.M15 + a.M32 * b.M25 + a.M33 * b.M35 + a.M34 * b.M45 + a.M35,

            M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
            M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
            M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
            M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44,
            M45 = a.M41 * b.M15 + a.M42 * b.M25 + a.M43 * b.M35 + a.M44 * b.M45 + a.M45,
        };
    }

    public static ColorMatrix operator *(ColorMatrix a, ColorMatrix b) => Multiply(a, b);

    public readonly bool Equals(ColorMatrix other) =>
        M11 == other.M11 && M12 == other.M12 && M13 == other.M13 && M14 == other.M14 && M15 == other.M15 &&
        M21 == other.M21 && M22 == other.M22 && M23 == other.M23 && M24 == other.M24 && M25 == other.M25 &&
        M31 == other.M31 && M32 == other.M32 && M33 == other.M33 && M34 == other.M34 && M35 == other.M35 &&
        M41 == other.M41 && M42 == other.M42 && M43 == other.M43 && M44 == other.M44 && M45 == other.M45;

    public override readonly bool Equals(object? obj) => obj is ColorMatrix other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(
        HashCode.Combine(M11, M12, M13, M14, M15),
        HashCode.Combine(M21, M22, M23, M24, M25),
        HashCode.Combine(M31, M32, M33, M34, M35),
        HashCode.Combine(M41, M42, M43, M44, M45));

    public static bool operator ==(ColorMatrix left, ColorMatrix right) => left.Equals(right);
    public static bool operator !=(ColorMatrix left, ColorMatrix right) => !left.Equals(right);
}
