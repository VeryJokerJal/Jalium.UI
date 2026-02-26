namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides event data for bitmap download progress.
/// </summary>
public sealed class DownloadProgressEventArgs : EventArgs
{
    public DownloadProgressEventArgs(int progress)
    {
        Progress = progress;
    }

    /// <summary>
    /// Gets the download progress as a percentage (0-100).
    /// </summary>
    public int Progress { get; }
}

/// <summary>
/// Defines the set of properties for an icon bitmap decoder.
/// </summary>
public sealed class IconBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="IconBitmapDecoder"/> class from a URI.
    /// </summary>
    public IconBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IconBitmapDecoder"/> class from a stream.
    /// </summary>
    public IconBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}
