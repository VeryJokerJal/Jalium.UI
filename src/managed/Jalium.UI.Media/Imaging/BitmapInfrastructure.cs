namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Defines the set of properties for an icon bitmap decoder.
/// </summary>
public sealed class IconBitmapDecoder : BitmapDecoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IconBitmapDecoder"/> class from a URI.
    /// </summary>
    public IconBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Icon)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IconBitmapDecoder"/> class from a stream.
    /// </summary>
    public IconBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Icon)
    {
    }
}
