namespace Jalium.UI.Media;

/// <summary>
/// Represents a collection of predefined pixel formats.
/// </summary>
public static class PixelFormats
{
    /// <summary>
    /// Gets the pixel format specifying 32 bits per pixel in the BGRA channel order.
    /// </summary>
    public static PixelFormat Bgra32 => PixelFormat.Bgra32;

    /// <summary>
    /// Gets the pixel format specifying 32 bits per pixel in the RGBA channel order.
    /// </summary>
    public static PixelFormat Rgba32 => PixelFormat.Rgba32;

    /// <summary>
    /// Gets the pixel format specifying 32 bits per pixel in the RGB channel order (alpha ignored).
    /// </summary>
    public static PixelFormat Rgb32 => PixelFormat.Rgb32;

    /// <summary>
    /// Gets the pixel format specifying 24 bits per pixel in the BGR channel order.
    /// </summary>
    public static PixelFormat Bgr24 => PixelFormat.Bgr24;

    /// <summary>
    /// Gets the pixel format specifying 24 bits per pixel in the RGB channel order.
    /// </summary>
    public static PixelFormat Rgb24 => PixelFormat.Rgb24;

    /// <summary>
    /// Gets the pixel format specifying 8 bits per pixel grayscale.
    /// </summary>
    public static PixelFormat Gray8 => PixelFormat.Gray8;

    /// <summary>
    /// Gets the pixel format specifying 16 bits per pixel grayscale.
    /// </summary>
    public static PixelFormat Gray16 => PixelFormat.Gray16;

    /// <summary>
    /// Gets the pixel format specifying 32 bits per pixel pre-multiplied BGRA format.
    /// </summary>
    public static PixelFormat Pbgra32 => PixelFormat.Pbgra32;

    /// <summary>
    /// Gets the default pixel format (Bgra32).
    /// </summary>
    public static PixelFormat Default => PixelFormat.Bgra32;

    /// <summary>
    /// Gets the bits per pixel for the specified pixel format.
    /// </summary>
    public static int GetBitsPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Bgra32 => 32,
        PixelFormat.Rgba32 => 32,
        PixelFormat.Rgb32 => 32,
        PixelFormat.Pbgra32 => 32,
        PixelFormat.Bgr24 => 24,
        PixelFormat.Rgb24 => 24,
        PixelFormat.Gray16 => 16,
        PixelFormat.Gray8 => 8,
        _ => 32
    };
}
