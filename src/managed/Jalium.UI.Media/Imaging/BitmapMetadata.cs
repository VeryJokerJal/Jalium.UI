namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides support for reading and writing metadata to and from a bitmap image.
/// </summary>
public class BitmapMetadata
{
    private readonly Dictionary<string, object?> _metadata = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapMetadata"/> class.
    /// </summary>
    public BitmapMetadata(string containerFormat)
    {
        Format = containerFormat;
    }

    /// <summary>
    /// Gets the container format for the metadata.
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets or sets the application name that generated the image.
    /// </summary>
    public string? ApplicationName
    {
        get => GetQuery("/app:ApplicationName") as string;
        set => SetQuery("/app:ApplicationName", value);
    }

    /// <summary>
    /// Gets or sets the author of the image.
    /// </summary>
    public IReadOnlyCollection<string>? Author
    {
        get => GetQuery("/app:Author") as IReadOnlyCollection<string>;
        set => SetQuery("/app:Author", value);
    }

    /// <summary>
    /// Gets or sets the camera manufacturer.
    /// </summary>
    public string? CameraManufacturer
    {
        get => GetQuery("/app:CameraManufacturer") as string;
        set => SetQuery("/app:CameraManufacturer", value);
    }

    /// <summary>
    /// Gets or sets the camera model.
    /// </summary>
    public string? CameraModel
    {
        get => GetQuery("/app:CameraModel") as string;
        set => SetQuery("/app:CameraModel", value);
    }

    /// <summary>
    /// Gets or sets the comment for the image.
    /// </summary>
    public string? Comment
    {
        get => GetQuery("/app:Comment") as string;
        set => SetQuery("/app:Comment", value);
    }

    /// <summary>
    /// Gets or sets the copyright for the image.
    /// </summary>
    public string? Copyright
    {
        get => GetQuery("/app:Copyright") as string;
        set => SetQuery("/app:Copyright", value);
    }

    /// <summary>
    /// Gets or sets the date the image was taken.
    /// </summary>
    public string? DateTaken
    {
        get => GetQuery("/app:DateTaken") as string;
        set => SetQuery("/app:DateTaken", value);
    }

    /// <summary>
    /// Gets or sets the keywords for the image.
    /// </summary>
    public IReadOnlyCollection<string>? Keywords
    {
        get => GetQuery("/app:Keywords") as IReadOnlyCollection<string>;
        set => SetQuery("/app:Keywords", value);
    }

    /// <summary>
    /// Gets or sets the rating for the image (0-5).
    /// </summary>
    public int Rating
    {
        get => GetQuery("/app:Rating") is int r ? r : 0;
        set => SetQuery("/app:Rating", value);
    }

    /// <summary>
    /// Gets or sets the subject of the image.
    /// </summary>
    public string? Subject
    {
        get => GetQuery("/app:Subject") as string;
        set => SetQuery("/app:Subject", value);
    }

    /// <summary>
    /// Gets or sets the title of the image.
    /// </summary>
    public string? Title
    {
        get => GetQuery("/app:Title") as string;
        set => SetQuery("/app:Title", value);
    }

    /// <summary>
    /// Gets a value indicating whether the metadata is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether the metadata is a fixed size.
    /// </summary>
    public bool IsFixedSize => false;

    /// <summary>
    /// Gets a metadata query reader that can query metadata from the bitmap.
    /// </summary>
    public object? GetQuery(string query)
    {
        _metadata.TryGetValue(query, out var value);
        return value;
    }

    /// <summary>
    /// Sets metadata at the specified query path.
    /// </summary>
    public void SetQuery(string query, object? value)
    {
        _metadata[query] = value;
    }

    /// <summary>
    /// Removes metadata at the specified query path.
    /// </summary>
    public void RemoveQuery(string query)
    {
        _metadata.Remove(query);
    }

    /// <summary>
    /// Returns a deep copy of this metadata.
    /// </summary>
    public BitmapMetadata Clone()
    {
        var clone = new BitmapMetadata(Format);
        foreach (var kvp in _metadata)
        {
            clone._metadata[kvp.Key] = kvp.Value;
        }
        return clone;
    }
}
