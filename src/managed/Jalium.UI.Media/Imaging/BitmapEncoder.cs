using System.Collections.ObjectModel;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Encodes a collection of BitmapFrame objects to an image stream.
/// </summary>
public abstract class BitmapEncoder : DispatcherObject
{
    private IList<BitmapFrame> _frames = new List<BitmapFrame>();

    /// <summary>
    /// Gets the collection of frames in this encoder.
    /// </summary>
    public virtual IList<BitmapFrame> Frames
    {
        get => _frames;
        set => _frames = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the codec info for this encoder.
    /// </summary>
    public virtual BitmapCodecInfo? CodecInfo => null;

    /// <summary>
    /// Gets or sets the color profile associated with this encoder.
    /// </summary>
    public virtual ReadOnlyCollection<ColorContext>? ColorContexts { get; set; }

    /// <summary>Gets or sets container metadata.</summary>
    public virtual BitmapMetadata? Metadata { get; set; }

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

    /// <summary>Creates an encoder for a WIC container format GUID.</summary>
    public static BitmapEncoder Create(Guid containerFormat)
    {
        if (containerFormat == new Guid("1b7cfaf4-713f-473c-bbcd-6137425faeaf")) return new PngBitmapEncoder();
        if (containerFormat == new Guid("19e4a5aa-5662-4fc5-a0c0-1758028e1057")) return new JpegBitmapEncoder();
        if (containerFormat == new Guid("0af1d87e-fcfe-4188-bdeb-a7906471cbe3")) return new BmpBitmapEncoder();
        if (containerFormat == new Guid("1f8a5601-7d4d-4cbd-9c82-1bc8d4eeb9a5")) return new GifBitmapEncoder();
        if (containerFormat == new Guid("163bcc30-e2e9-4f0b-961d-a3e9fdb788a3")) return new TiffBitmapEncoder();
        if (containerFormat == new Guid("57a37caa-367a-4540-916b-f183c5093a4b")) return new WmpBitmapEncoder();

        throw new NotSupportedException($"No bitmap encoder is registered for container format '{containerFormat}'.");
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

    public byte AlphaDataDiscardLevel { get; set; }
    public byte AlphaQualityLevel { get; set; }
    public bool CompressedDomainTranscode { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool FrequencyOrder { get; set; }
    public short HorizontalTileSlices { get; set; }
    public bool IgnoreOverlap { get; set; }
    public byte ImageDataDiscardLevel { get; set; }
    public bool InterleavedAlpha { get; set; }
    public byte OverlapLevel { get; set; }
    public byte QualityLevel { get; set; }
    public Rotation Rotation { get; set; }
    public byte SubsamplingLevel { get; set; }
    public bool UseCodecOptions { get; set; }
    public short VerticalTileSlices { get; set; }
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
