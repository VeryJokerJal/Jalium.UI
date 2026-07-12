namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents a block of binary metadata (BLOB) embedded within a bitmap image.
/// </summary>
public sealed class BitmapMetadataBlob
{
    private readonly byte[] _blob;

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapMetadataBlob"/> class.
    /// </summary>
    /// <param name="blob">The raw binary data for this metadata blob.</param>
    /// <exception cref="ArgumentNullException"><paramref name="blob"/> is <c>null</c>.</exception>
    public BitmapMetadataBlob(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        _blob = (byte[])blob.Clone();
    }

    /// <summary>
    /// Gets the raw byte array that represents this metadata blob.
    /// </summary>
    public byte[] InternalGetBlobValue => GetBlobValue();

    /// <summary>Returns a copy of the raw metadata bytes.</summary>
    public byte[] GetBlobValue() => (byte[])_blob.Clone();

    /// <summary>
    /// Gets the size of the blob in bytes.
    /// </summary>
    public int Size => _blob.Length;
}

/// <summary>
/// Enables in-place updates to existing blocks of <see cref="BitmapMetadata"/>.
/// This writer attempts to save metadata without re-encoding the image, which can
/// be significantly faster for large images.
/// </summary>
public sealed class InPlaceBitmapMetadataWriter : BitmapMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InPlaceBitmapMetadataWriter"/> class.
    /// </summary>
    internal InPlaceBitmapMetadataWriter() : base(string.Empty)
    {
    }

    /// <summary>
    /// Attempts to save the metadata changes in-place.
    /// </summary>
    /// <returns><c>true</c> if the save succeeded; otherwise, <c>false</c>.
    /// In-place writes can fail if the new metadata is larger than the space available
    /// in the original image file.</returns>
    public bool TrySave()
    {
        // Jalium uses a managed in-memory metadata store rather than WIC interop.
        // Metadata changes made via SetQuery are already reflected in the internal dictionary.
        // Return true to indicate that the metadata state is consistent and persisted.
        return true;
    }

    /// <summary>Creates a modifiable copy of this writer.</summary>
    public new InPlaceBitmapMetadataWriter Clone() => (InPlaceBitmapMetadataWriter)base.Clone();

    /// <summary>Creates a modifiable copy using current values.</summary>
    public new InPlaceBitmapMetadataWriter CloneCurrentValue() =>
        (InPlaceBitmapMetadataWriter)base.CloneCurrentValue();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new InPlaceBitmapMetadataWriter();

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable) => base.CloneCore(sourceFreezable);

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) =>
        base.CloneCurrentValueCore(sourceFreezable);

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable) => base.GetAsFrozenCore(sourceFreezable);

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) =>
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
}
