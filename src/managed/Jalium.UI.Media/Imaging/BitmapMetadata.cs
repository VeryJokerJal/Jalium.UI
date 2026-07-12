namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides support for reading and writing metadata to and from a bitmap image.
/// </summary>
public class BitmapMetadata : BitmapMetadataBase, IEnumerable<string>
{
    private readonly Dictionary<string, object?> _metadata = new();

    internal IEnumerable<KeyValuePair<string, object?>> Queries => _metadata;

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
    public System.Collections.ObjectModel.ReadOnlyCollection<string>? Author
    {
        get => GetQuery("/app:Author") as System.Collections.ObjectModel.ReadOnlyCollection<string>;
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
    public System.Collections.ObjectModel.ReadOnlyCollection<string>? Keywords
    {
        get => GetQuery("/app:Keywords") as System.Collections.ObjectModel.ReadOnlyCollection<string>;
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
    public bool IsReadOnly => IsFrozen;

    /// <summary>
    /// Gets a value indicating whether the metadata is a fixed size.
    /// </summary>
    public bool IsFixedSize => false;

    /// <summary>Gets the metadata query location represented by this object.</summary>
    public string Location => GetQuery("/app:Location") as string ?? string.Empty;

    /// <summary>
    /// Gets a metadata query reader that can query metadata from the bitmap.
    /// </summary>
    public object? GetQuery(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ReadPreamble();
        _metadata.TryGetValue(query, out var value);
        return value;
    }

    /// <summary>Determines whether a metadata query exists.</summary>
    public bool ContainsQuery(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ReadPreamble();
        return _metadata.ContainsKey(query);
    }

    /// <summary>
    /// Sets metadata at the specified query path.
    /// </summary>
    public void SetQuery(string query, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        WritePreamble();
        _metadata[query] = value;
        WritePostscript();
    }

    /// <summary>
    /// Removes metadata at the specified query path.
    /// </summary>
    public void RemoveQuery(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        WritePreamble();
        if (_metadata.Remove(query))
        {
            WritePostscript();
        }
    }

    /// <summary>
    /// Returns a deep copy of this metadata.
    /// </summary>
    public override BitmapMetadata Clone() => (BitmapMetadata)base.Clone();

    /// <summary>Returns a modifiable deep copy using current property values.</summary>
    public override BitmapMetadata CloneCurrentValue() => (BitmapMetadata)base.CloneCurrentValue();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new BitmapMetadata(Format);

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyQueries((BitmapMetadata)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyQueries((BitmapMetadata)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyQueries((BitmapMetadata)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyQueries((BitmapMetadata)sourceFreezable);
    }

    IEnumerator<string> IEnumerable<string>.GetEnumerator()
    {
        ReadPreamble();
        return _metadata.Keys.ToList().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        ((IEnumerable<string>)this).GetEnumerator();

    private void CopyQueries(BitmapMetadata source)
    {
        _metadata.Clear();
        foreach ((string query, object? value) in source._metadata)
        {
            _metadata[query] = value switch
            {
                ICloneable cloneable => cloneable.Clone(),
                _ => value,
            };
        }
    }
}
