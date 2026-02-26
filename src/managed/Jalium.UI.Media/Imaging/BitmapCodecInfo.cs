namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides information about an imaging codec.
/// </summary>
public class BitmapCodecInfo
{
    /// <summary>
    /// Gets or sets the author of the codec.
    /// </summary>
    public virtual string? Author { get; }

    /// <summary>
    /// Gets or sets the container format GUID.
    /// </summary>
    public virtual Guid ContainerFormat { get; }

    /// <summary>
    /// Gets or sets the device manufacturer of the codec.
    /// </summary>
    public virtual string? DeviceManufacturer { get; }

    /// <summary>
    /// Gets or sets the device models supported by the codec.
    /// </summary>
    public virtual string? DeviceModels { get; }

    /// <summary>
    /// Gets or sets the file extensions associated with the codec.
    /// </summary>
    public virtual string? FileExtensions { get; }

    /// <summary>
    /// Gets or sets the friendly name of the codec.
    /// </summary>
    public virtual string? FriendlyName { get; }

    /// <summary>
    /// Gets or sets the MIME types associated with the codec.
    /// </summary>
    public virtual string? MimeTypes { get; }

    /// <summary>
    /// Gets or sets the version of the codec.
    /// </summary>
    public virtual Version? Version { get; }

    /// <summary>
    /// Gets or sets the specification version the codec supports.
    /// </summary>
    public virtual Version? SpecificationVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the codec supports animation.
    /// </summary>
    public virtual bool SupportsAnimation { get; }

    /// <summary>
    /// Gets a value indicating whether the codec supports lossless encoding.
    /// </summary>
    public virtual bool SupportsLossless { get; }

    /// <summary>
    /// Gets a value indicating whether the codec supports multiple frames.
    /// </summary>
    public virtual bool SupportsMultipleFrames { get; }
}
