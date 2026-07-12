namespace Jalium.UI.Media;

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
        if (pixelFormat != PixelFormats.Bgr24 && pixelFormat != PixelFormats.Bgr32
            && pixelFormat != PixelFormats.Bgra32 && pixelFormat != PixelFormats.Pbgra32
            && pixelFormat != PixelFormats.Rgb24 && pixelFormat != PixelFormats.Rgb48
            && pixelFormat != PixelFormats.Rgba64 && pixelFormat != PixelFormats.Prgba64)
        {
            throw new NotSupportedException($"Pixel format '{pixelFormat}' does not have a standard color context.");
        }

        _pixelFormat = pixelFormat;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows))
        {
            string profile = Path.Combine(windows, "System32", "spool", "drivers", "color", "sRGB Color Space Profile.icm");
            _profileUri = new Uri(profile, UriKind.Absolute);
        }
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

    public override bool Equals(object? obj)
    {
        if (obj is not ColorContext other)
        {
            return false;
        }

        if (_profileUri is null || other._profileUri is null)
        {
            return _profileUri is null && other._profileUri is null && _pixelFormat == other._pixelFormat;
        }

        if (!_profileUri.IsAbsoluteUri || !other._profileUri.IsAbsoluteUri)
        {
            return string.Equals(
                _profileUri.OriginalString,
                other._profileUri.OriginalString,
                StringComparison.OrdinalIgnoreCase);
        }

        return Uri.Compare(_profileUri, other._profileUri, UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    public override int GetHashCode()
        => _profileUri is not null
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(_profileUri.IsAbsoluteUri
                ? _profileUri.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped)
                : _profileUri.OriginalString)
            : _pixelFormat.GetHashCode();

    public static bool operator ==(ColorContext? context1, ColorContext? context2)
        => Equals(context1, context2);

    public static bool operator !=(ColorContext? context1, ColorContext? context2)
        => !Equals(context1, context2);
}
