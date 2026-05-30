namespace Jalium.UI.Media;

/// <summary>
/// Resolves an encoded image reference (URI / file path / byte array) into the
/// concrete <see cref="ImageSource"/> that best fits the payload: a multi-frame
/// source (animated GIF / APNG / animated WebP) becomes an
/// <see cref="AnimatedBitmap"/> so it plays back, while a single-frame source
/// stays a plain <see cref="BitmapImage"/>.
/// </summary>
/// <remarks>
/// This is the single choke point the framework's string/URI → <see cref="ImageSource"/>
/// conversions route through (the <c>ImageSourceConverter</c>, the XAML reader,
/// and the GPU bundle renderer), so <c>&lt;Image Source="cat.gif"/&gt;</c> animates
/// without callers having to know about <see cref="AnimatedBitmap"/>. Frame count
/// is probed from the encoded bytes' metadata only (no pixel decode), so the
/// dominant single-frame PNG/JPEG case pays just one cheap header read.
///
/// Remote <c>http(s)</c> URIs are loaded asynchronously by <see cref="BitmapImage"/>
/// (their bytes are not available synchronously here), so a remote animated GIF
/// currently shows its first frame; animating those would require an async
/// multi-frame loader on <see cref="AnimatedBitmap"/>.
/// </remarks>
public static class ImageSourceLoader
{
    /// <summary>
    /// Resolves encoded image <paramref name="data"/> into an animated or static
    /// <see cref="ImageSource"/> based on its frame count.
    /// </summary>
    public static ImageSource FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return BitmapImage.ProbeFrameCount(data) > 1
            ? AnimatedBitmap.FromBytes(data)
            : BitmapImage.FromBytes(data);
    }

    /// <summary>
    /// Reads <paramref name="filePath"/> and resolves it into an animated or
    /// static <see cref="ImageSource"/> based on its frame count.
    /// </summary>
    public static ImageSource FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return FromBytes(System.IO.File.ReadAllBytes(filePath));
    }

    /// <summary>
    /// Resolves <paramref name="uri"/> into an animated or static
    /// <see cref="ImageSource"/>. Synchronously-resolvable sources (file /
    /// embedded resource / disk-relative) are upgraded to
    /// <see cref="AnimatedBitmap"/> when the payload is multi-frame; remote
    /// <c>http(s)</c> URIs keep <see cref="BitmapImage"/>'s async load.
    /// </summary>
    public static ImageSource FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // Reuse BitmapImage's own URI resolver (file / manifest resource /
        // disk-relative / http). For synchronously-resolved sources the encoded
        // bytes are available immediately via ImageData; if they turn out to be
        // multi-frame, swap in an AnimatedBitmap built from the same bytes.
        var bitmap = new BitmapImage(uri);
        var encoded = bitmap.ImageData;
        if (encoded is not null && BitmapImage.ProbeFrameCount(encoded) > 1)
        {
            bitmap.Dispose();
            return AnimatedBitmap.FromBytes(encoded);
        }

        return bitmap;
    }
}
