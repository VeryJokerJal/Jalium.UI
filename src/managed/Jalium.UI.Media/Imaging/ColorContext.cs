namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents the International Color Consortium (ICC) or Image Color Management (ICM) color profile
/// that is associated with a bitmap image.
/// </summary>
public sealed class ColorContext
{
    private readonly Uri? _profileUri;
    private readonly PixelFormat _pixelFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorContext"/> class with the specified URI.
    /// </summary>
    public ColorContext(Uri profileUri)
    {
        ArgumentNullException.ThrowIfNull(profileUri);
        _profileUri = profileUri;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorContext"/> class with the specified pixel format.
    /// </summary>
    public ColorContext(PixelFormat pixelFormat)
    {
        _pixelFormat = pixelFormat;
    }

    /// <summary>
    /// Gets the URI of the ICC or ICM color profile.
    /// </summary>
    public Uri? ProfileUri => _profileUri;

    /// <summary>
    /// Opens a readable stream to the raw ICC or ICM color profile data.
    /// </summary>
    public Stream OpenProfileStream()
    {
        if (_profileUri != null && _profileUri.IsFile)
        {
            return File.OpenRead(_profileUri.LocalPath);
        }
        return new MemoryStream();
    }
}
