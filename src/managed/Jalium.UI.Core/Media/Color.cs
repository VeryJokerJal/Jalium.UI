using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>Represents an sRGB, scRGB, or color-profile-backed color.</summary>
public struct Color : IEquatable<Color>, IFormattable
{
    private byte _a;
    private byte _r;
    private byte _g;
    private byte _b;
    private float _scA;
    private float _scR;
    private float _scG;
    private float _scB;
    private bool _isScRgb;
    private ColorContext? _colorContext;
    private float[]? _nativeColorValues;

    public Color(byte a, byte r, byte g, byte b)
    {
        _a = a;
        _r = r;
        _g = g;
        _b = b;
        _scA = a / 255f;
        _scR = SrgbToLinear(r / 255f);
        _scG = SrgbToLinear(g / 255f);
        _scB = SrgbToLinear(b / 255f);
        _isScRgb = false;
        _colorContext = null;
        _nativeColorValues = null;
    }

    public ColorContext? ColorContext => _colorContext;

    public byte A
    {
        readonly get => _a;
        set
        {
            _a = value;
            _scA = value / 255f;
        }
    }

    public byte R
    {
        readonly get => _r;
        set
        {
            _r = value;
            _scR = SrgbToLinear(value / 255f);
            UpdateNativeChannel(0, value / 255f);
        }
    }

    public byte G
    {
        readonly get => _g;
        set
        {
            _g = value;
            _scG = SrgbToLinear(value / 255f);
            UpdateNativeChannel(1, value / 255f);
        }
    }

    public byte B
    {
        readonly get => _b;
        set
        {
            _b = value;
            _scB = SrgbToLinear(value / 255f);
            UpdateNativeChannel(2, value / 255f);
        }
    }

    public float ScA
    {
        readonly get => _scA;
        set
        {
            _scA = value;
            _a = LinearAlphaToByte(value);
        }
    }

    public float ScR
    {
        readonly get => _scR;
        set
        {
            _scR = value;
            _r = LinearRgbToByte(value);
            UpdateNativeChannel(0, _r / 255f);
        }
    }

    public float ScG
    {
        readonly get => _scG;
        set
        {
            _scG = value;
            _g = LinearRgbToByte(value);
            UpdateNativeChannel(1, _g / 255f);
        }
    }

    public float ScB
    {
        readonly get => _scB;
        set
        {
            _scB = value;
            _b = LinearRgbToByte(value);
            UpdateNativeChannel(2, _b / 255f);
        }
    }

    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static Color FromArgb(uint argb) => new(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    public static Color FromArgb(int argb) => FromArgb(unchecked((uint)argb));

    public readonly uint ToArgb() => ((uint)_a << 24) | ((uint)_r << 16) | ((uint)_g << 8) | _b;

    public readonly int ToArgbInt32() => unchecked((int)ToArgb());

    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static Color FromScRgb(float a, float r, float g, float b)
    {
        return new Color
        {
            _a = LinearAlphaToByte(a),
            _r = LinearRgbToByte(r),
            _g = LinearRgbToByte(g),
            _b = LinearRgbToByte(b),
            _scA = a,
            _scR = r,
            _scG = g,
            _scB = b,
            _isScRgb = true,
        };
    }

    public static Color FromValues(float[] values, Uri profileUri)
        => FromAValues(1.0f, values, profileUri);

    public static Color FromAValues(float a, float[] values, Uri profileUri)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(profileUri);
        if (values.Length != 3)
        {
            throw new ArgumentException("Color context dimensions mismatch.", nameof(values));
        }

        var native = (float[])values.Clone();
        return new Color
        {
            _a = NormalizedToByte(a),
            _r = NormalizedToByte(native[0]),
            _g = NormalizedToByte(native[1]),
            _b = NormalizedToByte(native[2]),
            _scA = a,
            _scR = SrgbToLinear(NormalizedToByte(native[0]) / 255f),
            _scG = SrgbToLinear(NormalizedToByte(native[1]) / 255f),
            _scB = SrgbToLinear(NormalizedToByte(native[2]) / 255f),
            _colorContext = new ColorContext(profileUri),
            _nativeColorValues = native,
        };
    }

    public readonly float[] GetNativeColorValues()
    {
        if (_nativeColorValues is null)
        {
            throw new InvalidOperationException("This color is not associated with a color context.");
        }

        return (float[])_nativeColorValues.Clone();
    }

    public void Clamp()
    {
        _scA = Math.Clamp(_scA, 0f, 1f);
        _scR = Math.Clamp(_scR, 0f, 1f);
        _scG = Math.Clamp(_scG, 0f, 1f);
        _scB = Math.Clamp(_scB, 0f, 1f);
        _a = LinearAlphaToByte(_scA);
        _r = LinearRgbToByte(_scR);
        _g = LinearRgbToByte(_scG);
        _b = LinearRgbToByte(_scB);
        if (_nativeColorValues is { Length: >= 3 })
        {
            for (int index = 0; index < _nativeColorValues.Length; index++)
            {
                _nativeColorValues[index] = Math.Clamp(_nativeColorValues[index], 0f, 1f);
            }
        }
    }

    public static Color Add(Color color1, Color color2) => FromScRgb(
        color1._scA + color2._scA,
        color1._scR + color2._scR,
        color1._scG + color2._scG,
        color1._scB + color2._scB);

    public static Color Subtract(Color color1, Color color2) => FromScRgb(
        color1._scA - color2._scA,
        color1._scR - color2._scR,
        color1._scG - color2._scG,
        color1._scB - color2._scB);

    public static Color Multiply(Color color, float coefficient) => FromScRgb(
        color._scA * coefficient,
        color._scR * coefficient,
        color._scG * coefficient,
        color._scB * coefficient);

    public static bool AreClose(Color color1, Color color2)
        => AreClose(color1._scA, color2._scA)
            && AreClose(color1._scR, color2._scR)
            && AreClose(color1._scG, color2._scG)
            && AreClose(color1._scB, color2._scB)
            && Equals(color1._colorContext, color2._colorContext);

    public static bool Equals(Color color1, Color color2) => color1.Equals(color2);

    public readonly bool Equals(Color other)
    {
        if (_scA != other._scA || _scR != other._scR || _scG != other._scG || _scB != other._scB
            || !Equals(_colorContext, other._colorContext))
        {
            return false;
        }

        if (_nativeColorValues is null || other._nativeColorValues is null)
        {
            return _nativeColorValues is null && other._nativeColorValues is null;
        }

        return _nativeColorValues.AsSpan().SequenceEqual(other._nativeColorValues);
    }

    public override readonly bool Equals(object? obj) => obj is Color other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_scA);
        hash.Add(_scR);
        hash.Add(_scG);
        hash.Add(_scB);
        hash.Add(_colorContext);
        if (_nativeColorValues is not null)
        {
            foreach (float value in _nativeColorValues)
            {
                hash.Add(value);
            }
        }

        return hash.ToHashCode();
    }

    public override readonly string ToString() => ToString(CultureInfo.CurrentCulture);

    public readonly string ToString(IFormatProvider? provider)
    {
        provider ??= CultureInfo.CurrentCulture;
        if (_colorContext is not null && _nativeColorValues is not null)
        {
            Uri? profileUri = _colorContext.ProfileUri;
            string uri = profileUri is null
                ? string.Empty
                : profileUri.IsAbsoluteUri
                    ? profileUri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped)
                    : profileUri.OriginalString;
            string values = string.Join(",", _nativeColorValues.Select(value => value.ToString(provider)));
            return $"ContextColor {uri} {_scA.ToString(provider)},{values}";
        }

        if (_isScRgb)
        {
            return $"sc#{_scA.ToString(provider)}, {_scR.ToString(provider)}, {_scG.ToString(provider)}, {_scB.ToString(provider)}";
        }

        return $"#{_a:X2}{_r:X2}{_g:X2}{_b:X2}";
    }

    readonly string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
        => ToString(formatProvider);

    public static Color operator +(Color color1, Color color2) => Add(color1, color2);

    public static Color operator -(Color color1, Color color2) => Subtract(color1, color2);

    public static Color operator *(Color color, float coefficient) => Multiply(color, coefficient);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    private void UpdateNativeChannel(int index, float value)
    {
        if (_nativeColorValues is { Length: > 2 })
        {
            _nativeColorValues[index] = value;
        }
    }

    private static byte LinearAlphaToByte(float value) => NormalizedToByte(value);

    private static byte NormalizedToByte(float value)
        => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    private static byte LinearRgbToByte(float value)
        => NormalizedToByte(LinearToSrgb(Math.Clamp(value, 0f, 1f)));

    private static float SrgbToLinear(float value)
        => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float value)
        => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    private static bool AreClose(float first, float second)
    {
        if (first == second)
        {
            return true;
        }

        float tolerance = (Math.Abs(first) + Math.Abs(second) + 10f) * 1e-6f;
        return Math.Abs(first - second) < tolerance;
    }

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
}
