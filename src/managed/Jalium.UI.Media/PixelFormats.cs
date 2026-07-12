namespace Jalium.UI.Media;

/// <summary>
/// Represents a collection of predefined pixel formats.
/// </summary>
public static class PixelFormats
{
    public static PixelFormat Default => PixelFormat.Default;
    public static PixelFormat Indexed1 => PixelFormat.Indexed1;
    public static PixelFormat Indexed2 => PixelFormat.Indexed2;
    public static PixelFormat Indexed4 => PixelFormat.Indexed4;
    public static PixelFormat Indexed8 => PixelFormat.Indexed8;
    public static PixelFormat BlackWhite => PixelFormat.BlackWhite;
    public static PixelFormat Gray2 => PixelFormat.Gray2;
    public static PixelFormat Gray4 => PixelFormat.Gray4;

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

    public static PixelFormat Bgr555 => PixelFormat.Bgr555;
    public static PixelFormat Bgr565 => PixelFormat.Bgr565;
    public static PixelFormat Rgb128Float => PixelFormat.Rgb128Float;
    public static PixelFormat Bgr101010 => PixelFormat.Bgr101010;
    public static PixelFormat Bgr32 => PixelFormat.Bgr32;
    public static PixelFormat Rgb48 => PixelFormat.Rgb48;
    public static PixelFormat Rgba64 => PixelFormat.Rgba64;
    public static PixelFormat Prgba64 => PixelFormat.Prgba64;

    /// <summary>
    /// Gets the pixel format specifying 16 bits per pixel grayscale.
    /// </summary>
    public static PixelFormat Gray16 => PixelFormat.Gray16;

    public static PixelFormat Gray32Float => PixelFormat.Gray32Float;
    public static PixelFormat Rgba128Float => PixelFormat.Rgba128Float;
    public static PixelFormat Prgba128Float => PixelFormat.Prgba128Float;
    public static PixelFormat Cmyk32 => PixelFormat.Cmyk32;

    /// <summary>
    /// Gets the pixel format specifying 32 bits per pixel pre-multiplied BGRA format.
    /// </summary>
    public static PixelFormat Pbgra32 => PixelFormat.Pbgra32;

    /// <summary>
    /// Gets the bits per pixel for the specified pixel format.
    /// </summary>
    public static int GetBitsPerPixel(PixelFormat format) => format.BitsPerPixel;
}
