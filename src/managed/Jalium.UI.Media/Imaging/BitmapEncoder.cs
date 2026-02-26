namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Encodes a collection of BitmapFrame objects to an image stream.
/// </summary>
public abstract class BitmapEncoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <summary>
    /// Gets the collection of frames in this encoder.
    /// </summary>
    public IList<BitmapFrame> Frames => _frames;

    /// <summary>
    /// Gets the codec info for this encoder.
    /// </summary>
    public virtual BitmapCodecInfo? CodecInfo => null;

    /// <summary>
    /// Gets or sets the color profile associated with this encoder.
    /// </summary>
    public virtual ColorContext? ColorContexts { get; set; }

    /// <summary>
    /// Gets or sets the bitmap palette.
    /// </summary>
    public virtual BitmapPalette? Palette { get; set; }

    /// <summary>
    /// Gets or sets the preview thumbnail.
    /// </summary>
    public virtual BitmapSource? Preview { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail for the bitmap.
    /// </summary>
    public virtual BitmapSource? Thumbnail { get; set; }

    /// <summary>
    /// Encodes a bitmap image to a specified stream.
    /// </summary>
    public virtual void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // Subclasses implement actual encoding
    }
}

/// <summary>
/// Defines an encoder that is used to encode PNG format images.
/// </summary>
public sealed class PngBitmapEncoder : BitmapEncoder
{
    /// <summary>
    /// Gets or sets the interlace option.
    /// </summary>
    public PngInterlaceOption Interlace { get; set; } = PngInterlaceOption.Default;
}

/// <summary>
/// Defines an encoder that is used to encode JPEG format images.
/// </summary>
public sealed class JpegBitmapEncoder : BitmapEncoder
{
    /// <summary>
    /// Gets or sets the quality level (1-100).
    /// </summary>
    public int QualityLevel { get; set; } = 75;

    /// <summary>
    /// Gets or sets whether to flip the image horizontally.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    /// Gets or sets whether to flip the image vertically.
    /// </summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// Gets or sets the rotation.
    /// </summary>
    public Rotation Rotation { get; set; } = Rotation.Rotate0;
}

/// <summary>
/// Defines an encoder that is used to encode BMP format images.
/// </summary>
public sealed class BmpBitmapEncoder : BitmapEncoder
{
}

/// <summary>
/// Defines an encoder that is used to encode GIF format images.
/// </summary>
public sealed class GifBitmapEncoder : BitmapEncoder
{
}

/// <summary>
/// Defines an encoder that is used to encode TIFF format images.
/// </summary>
public sealed class TiffBitmapEncoder : BitmapEncoder
{
    /// <summary>
    /// Gets or sets the compression type.
    /// </summary>
    public TiffCompressOption Compression { get; set; } = TiffCompressOption.Default;
}

/// <summary>
/// Defines an encoder that is used to encode WMP (Windows Media Photo) format images.
/// </summary>
public sealed class WmpBitmapEncoder : BitmapEncoder
{
    /// <summary>
    /// Gets or sets the image quality level (0.0-1.0).
    /// </summary>
    public float ImageQualityLevel { get; set; } = 0.9f;

    /// <summary>
    /// Gets or sets whether lossless encoding is used.
    /// </summary>
    public bool Lossless { get; set; }
}

#region Enums

/// <summary>
/// Specifies the PNG interlace option.
/// </summary>
public enum PngInterlaceOption
{
    Default,
    On,
    Off
}

/// <summary>
/// Specifies the rotation to apply.
/// </summary>
public enum Rotation
{
    Rotate0 = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3
}

/// <summary>
/// Specifies the TIFF compression option.
/// </summary>
public enum TiffCompressOption
{
    Default,
    None,
    Ccitt3,
    Ccitt4,
    Lzw,
    Rle,
    Zip
}

#endregion
