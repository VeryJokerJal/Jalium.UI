namespace Jalium.UI.Resources;

/// <summary>
/// Describes a stream returned by one of the application resource APIs.
/// </summary>
public class StreamResourceInfo
{
    /// <summary>
    /// Initializes an empty resource descriptor.
    /// </summary>
    public StreamResourceInfo()
    {
    }

    /// <summary>
    /// Initializes a resource descriptor for the supplied stream and MIME type.
    /// </summary>
    public StreamResourceInfo(Stream stream, string contentType)
    {
        Stream = stream;
        ContentType = contentType;
    }

    /// <summary>
    /// Gets or sets the MIME type associated with <see cref="Stream"/>.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the resource payload.
    /// </summary>
    public Stream? Stream { get; set; }
}
