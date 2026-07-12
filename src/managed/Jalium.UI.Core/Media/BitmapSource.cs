namespace Jalium.UI.Media;

/// <summary>Base class for bitmap-backed image sources.</summary>
public abstract class BitmapSource : ImageSource
{
    /// <inheritdoc />
    public override ImageMetadata? Metadata => null;

    /// <summary>Creates a modifiable copy of this bitmap source.</summary>
    public new BitmapSource Clone() => (BitmapSource)base.Clone();

    /// <summary>Creates a modifiable copy using current dependency-property values.</summary>
    public new BitmapSource CloneCurrentValue() => (BitmapSource)base.CloneCurrentValue();

    public virtual int PixelWidth => (int)Width;

    public virtual int PixelHeight => (int)Height;

    public virtual double DpiX => 96.0;

    public virtual double DpiY => 96.0;

    public virtual PixelFormat Format => PixelFormat.Bgra32;

    public virtual Imaging.BitmapPalette? Palette => null;

    public virtual void CopyPixels(byte[] pixels, int stride, int offset)
    {
        CopyPixels(new Int32Rect(0, 0, (int)Width, (int)Height), pixels, stride, offset);
    }

    public virtual void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
    }

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable) => base.CloneCore(sourceFreezable);

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) => base.CloneCurrentValueCore(sourceFreezable);

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable) => base.GetAsFrozenCore(sourceFreezable);

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) => base.GetCurrentValueAsFrozenCore(sourceFreezable);

    /// <inheritdoc />
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
}

/// <summary>
/// Describes the channel layout and storage size of a bitmap pixel.
/// </summary>
public readonly struct PixelFormat : IEquatable<PixelFormat>
{
    private readonly PixelFormatId _id;

    private PixelFormat(PixelFormatId id)
    {
        _id = id;
    }

    public int BitsPerPixel => _id switch
    {
        PixelFormatId.BlackWhite or PixelFormatId.Indexed1 => 1,
        PixelFormatId.Gray2 or PixelFormatId.Indexed2 => 2,
        PixelFormatId.Gray4 or PixelFormatId.Indexed4 => 4,
        PixelFormatId.Gray8 or PixelFormatId.Indexed8 => 8,
        PixelFormatId.Gray16 or PixelFormatId.Bgr555 or PixelFormatId.Bgr565 => 16,
        PixelFormatId.Bgr24 or PixelFormatId.Rgb24 => 24,
        PixelFormatId.Gray32Float or PixelFormatId.Bgr101010 or PixelFormatId.Bgr32 or
            PixelFormatId.Bgra32 or PixelFormatId.Pbgra32 or PixelFormatId.Cmyk32 or
            PixelFormatId.Rgba32 or PixelFormatId.Rgb32 => 32,
        PixelFormatId.Rgb48 => 48,
        PixelFormatId.Rgba64 or PixelFormatId.Prgba64 => 64,
        PixelFormatId.Rgb128Float or PixelFormatId.Rgba128Float or PixelFormatId.Prgba128Float => 128,
        _ => throw new NotSupportedException("No information is available for the default pixel format."),
    };

    public IList<PixelFormatChannelMask> Masks => PixelFormatMasks.Get(_id);

    public static bool Equals(PixelFormat left, PixelFormat right) => left._id == right._id;

    public bool Equals(PixelFormat pixelFormat) => _id == pixelFormat._id;

    public override bool Equals(object? obj) => obj is PixelFormat other && Equals(other);

    public override int GetHashCode() => (int)_id;

    public override string ToString() => _id.ToString();

    public static bool operator ==(PixelFormat left, PixelFormat right) => left.Equals(right);

    public static bool operator !=(PixelFormat left, PixelFormat right) => !left.Equals(right);

    public static PixelFormat Default => default;
    public static PixelFormat Indexed1 => new(PixelFormatId.Indexed1);
    public static PixelFormat Indexed2 => new(PixelFormatId.Indexed2);
    public static PixelFormat Indexed4 => new(PixelFormatId.Indexed4);
    public static PixelFormat Indexed8 => new(PixelFormatId.Indexed8);
    public static PixelFormat BlackWhite => new(PixelFormatId.BlackWhite);
    public static PixelFormat Gray2 => new(PixelFormatId.Gray2);
    public static PixelFormat Gray4 => new(PixelFormatId.Gray4);
    public static PixelFormat Gray8 => new(PixelFormatId.Gray8);
    public static PixelFormat Bgr555 => new(PixelFormatId.Bgr555);
    public static PixelFormat Bgr565 => new(PixelFormatId.Bgr565);
    public static PixelFormat Rgb128Float => new(PixelFormatId.Rgb128Float);
    public static PixelFormat Bgr24 => new(PixelFormatId.Bgr24);
    public static PixelFormat Rgb24 => new(PixelFormatId.Rgb24);
    public static PixelFormat Bgr101010 => new(PixelFormatId.Bgr101010);
    public static PixelFormat Bgr32 => new(PixelFormatId.Bgr32);
    public static PixelFormat Bgra32 => new(PixelFormatId.Bgra32);
    public static PixelFormat Pbgra32 => new(PixelFormatId.Pbgra32);
    public static PixelFormat Rgb48 => new(PixelFormatId.Rgb48);
    public static PixelFormat Rgba64 => new(PixelFormatId.Rgba64);
    public static PixelFormat Prgba64 => new(PixelFormatId.Prgba64);
    public static PixelFormat Gray16 => new(PixelFormatId.Gray16);
    public static PixelFormat Gray32Float => new(PixelFormatId.Gray32Float);
    public static PixelFormat Rgba128Float => new(PixelFormatId.Rgba128Float);
    public static PixelFormat Prgba128Float => new(PixelFormatId.Prgba128Float);
    public static PixelFormat Cmyk32 => new(PixelFormatId.Cmyk32);

    // Jalium compatibility formats used by the native decoder and writeable bitmap paths.
    public static PixelFormat Rgba32 => new(PixelFormatId.Rgba32);
    public static PixelFormat Rgb32 => new(PixelFormatId.Rgb32);
}

/// <summary>
/// Describes the bit mask occupied by one pixel-format channel.
/// </summary>
public readonly struct PixelFormatChannelMask : IEquatable<PixelFormatChannelMask>
{
    private readonly IList<byte>? _mask;

    internal PixelFormatChannelMask(params byte[] mask)
    {
        _mask = Array.AsReadOnly((byte[])mask.Clone());
    }

    public IList<byte> Mask => _mask ?? Array.Empty<byte>();

    public static bool Equals(PixelFormatChannelMask left, PixelFormatChannelMask right) => left.Equals(right);

    public bool Equals(PixelFormatChannelMask other) => Mask.SequenceEqual(other.Mask);

    public override bool Equals(object? obj) => obj is PixelFormatChannelMask other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte value in Mask)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(PixelFormatChannelMask left, PixelFormatChannelMask right) => left.Equals(right);

    public static bool operator !=(PixelFormatChannelMask left, PixelFormatChannelMask right) => !left.Equals(right);
}

internal enum PixelFormatId : byte
{
    Default,
    Indexed1,
    Indexed2,
    Indexed4,
    Indexed8,
    BlackWhite,
    Gray2,
    Gray4,
    Gray8,
    Bgr555,
    Bgr565,
    Rgb128Float,
    Bgr24,
    Rgb24,
    Bgr101010,
    Bgr32,
    Bgra32,
    Pbgra32,
    Rgb48,
    Rgba64,
    Prgba64,
    Gray16,
    Gray32Float,
    Rgba128Float,
    Prgba128Float,
    Cmyk32,
    Rgba32,
    Rgb32,
}

internal static class PixelFormatMasks
{
    private static readonly IList<PixelFormatChannelMask> OneBit = Masks([0x01]);
    private static readonly IList<PixelFormatChannelMask> TwoBits = Masks([0x03]);
    private static readonly IList<PixelFormatChannelMask> FourBits = Masks([0x0F]);
    private static readonly IList<PixelFormatChannelMask> EightBits = Masks([0xFF]);
    private static readonly IList<PixelFormatChannelMask> SixteenBits = Masks([0xFF, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> ThirtyTwoBits = Masks([0xFF, 0xFF, 0xFF, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> Bgr555 = Masks([0x1F, 0x00], [0xE0, 0x03], [0x00, 0x7C]);
    private static readonly IList<PixelFormatChannelMask> Bgr565 = Masks([0x1F, 0x00], [0xE0, 0x07], [0x00, 0xF8]);
    private static readonly IList<PixelFormatChannelMask> ThreeBytes = Masks([0xFF, 0x00, 0x00], [0x00, 0xFF, 0x00], [0x00, 0x00, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> Bgr101010 = Masks([0xFF, 0x03, 0x00, 0x00], [0x00, 0xFC, 0x0F, 0x00], [0x00, 0x00, 0xF0, 0x3F]);
    private static readonly IList<PixelFormatChannelMask> ThreeDWords = Masks([0xFF, 0x00, 0x00, 0x00], [0x00, 0xFF, 0x00, 0x00], [0x00, 0x00, 0xFF, 0x00]);
    private static readonly IList<PixelFormatChannelMask> FourDWords = Masks([0xFF, 0x00, 0x00, 0x00], [0x00, 0xFF, 0x00, 0x00], [0x00, 0x00, 0xFF, 0x00], [0x00, 0x00, 0x00, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> ThreeWords = Masks([0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00], [0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00], [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> FourWords = Masks([0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], [0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00], [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00], [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF]);
    private static readonly IList<PixelFormatChannelMask> ThreeFloats = Masks(FloatMask(0), FloatMask(4), FloatMask(8));
    private static readonly IList<PixelFormatChannelMask> FourFloats = Masks(FloatMask(0), FloatMask(4), FloatMask(8), FloatMask(12));

    public static IList<PixelFormatChannelMask> Get(PixelFormatId id) => id switch
    {
        PixelFormatId.BlackWhite or PixelFormatId.Indexed1 => OneBit,
        PixelFormatId.Gray2 or PixelFormatId.Indexed2 => TwoBits,
        PixelFormatId.Gray4 or PixelFormatId.Indexed4 => FourBits,
        PixelFormatId.Gray8 or PixelFormatId.Indexed8 => EightBits,
        PixelFormatId.Gray16 => SixteenBits,
        PixelFormatId.Gray32Float => ThirtyTwoBits,
        PixelFormatId.Bgr555 => Bgr555,
        PixelFormatId.Bgr565 => Bgr565,
        PixelFormatId.Bgr24 or PixelFormatId.Rgb24 => ThreeBytes,
        PixelFormatId.Bgr101010 => Bgr101010,
        PixelFormatId.Bgr32 or PixelFormatId.Rgb32 => ThreeDWords,
        PixelFormatId.Bgra32 or PixelFormatId.Pbgra32 or PixelFormatId.Rgba32 or PixelFormatId.Cmyk32 => FourDWords,
        PixelFormatId.Rgb48 => ThreeWords,
        PixelFormatId.Rgba64 or PixelFormatId.Prgba64 => FourWords,
        PixelFormatId.Rgb128Float => ThreeFloats,
        PixelFormatId.Rgba128Float or PixelFormatId.Prgba128Float => FourFloats,
        _ => throw new NotSupportedException("No information is available for the default pixel format."),
    };

    private static byte[] FloatMask(int byteOffset)
    {
        var mask = new byte[16];
        Array.Fill(mask, (byte)0xFF, byteOffset, 4);
        return mask;
    }

    private static IList<PixelFormatChannelMask> Masks(params byte[][] masks) =>
        Array.AsReadOnly(masks.Select(static mask => new PixelFormatChannelMask(mask)).ToArray());
}
