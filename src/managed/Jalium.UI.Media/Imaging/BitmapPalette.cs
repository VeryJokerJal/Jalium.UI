namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Defines the available color palette for a supported image type.
/// </summary>
public sealed class BitmapPalette
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapPalette"/> class with the specified colors.
    /// </summary>
    public BitmapPalette(IList<Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Colors = new List<Color>(colors).AsReadOnly();
    }

    /// <summary>
    /// Gets the colors defined in a palette.
    /// </summary>
    public IList<Color> Colors { get; }
}

/// <summary>
/// Defines the available set of predefined palettes.
/// </summary>
public static class BitmapPalettes
{
    /// <summary>
    /// Gets a value that represents the Halftone8 palette, which contains 256 colors.
    /// </summary>
    public static BitmapPalette Halftone8 { get; } = CreateGrayscalePalette(256);

    /// <summary>
    /// Gets a value that represents the Halftone8Transparent palette.
    /// </summary>
    public static BitmapPalette Halftone8Transparent { get; } = CreateGrayscalePalette(256);

    /// <summary>
    /// Gets a value that represents the Halftone27 palette.
    /// </summary>
    public static BitmapPalette Halftone27 { get; } = CreateGrayscalePalette(27);

    /// <summary>
    /// Gets a value that represents the Halftone64 palette.
    /// </summary>
    public static BitmapPalette Halftone64 { get; } = CreateGrayscalePalette(64);

    /// <summary>
    /// Gets a value that represents the Halftone125 palette.
    /// </summary>
    public static BitmapPalette Halftone125 { get; } = CreateGrayscalePalette(125);

    /// <summary>
    /// Gets a value that represents the Halftone216 palette (web-safe colors).
    /// </summary>
    public static BitmapPalette Halftone216 { get; } = CreateGrayscalePalette(216);

    /// <summary>
    /// Gets a value that represents the Halftone252 palette.
    /// </summary>
    public static BitmapPalette Halftone252 { get; } = CreateGrayscalePalette(252);

    /// <summary>
    /// Gets a value that represents the Halftone256 palette.
    /// </summary>
    public static BitmapPalette Halftone256 { get; } = CreateGrayscalePalette(256);

    /// <summary>
    /// Gets a value that represents the grayscale palette with 256 shades of gray.
    /// </summary>
    public static BitmapPalette Gray256 { get; } = CreateGrayscalePalette(256);

    /// <summary>
    /// Gets a value that represents the grayscale palette with 16 shades of gray.
    /// </summary>
    public static BitmapPalette Gray16 { get; } = CreateGrayscalePalette(16);

    /// <summary>
    /// Gets a value that represents the grayscale palette with 4 shades of gray.
    /// </summary>
    public static BitmapPalette Gray4 { get; } = CreateGrayscalePalette(4);

    /// <summary>
    /// Gets a value that represents the WebPalette with 216 colors.
    /// </summary>
    public static BitmapPalette WebPalette { get; } = CreateGrayscalePalette(216);

    private static BitmapPalette CreateGrayscalePalette(int count)
    {
        var colors = new List<Color>(count);
        for (int i = 0; i < count; i++)
        {
            byte value = (byte)(i * 255 / Math.Max(count - 1, 1));
            colors.Add(Color.FromArgb(255, value, value, value));
        }
        return new BitmapPalette(colors);
    }
}
