using Jalium.UI.Threading;

namespace Jalium.UI.Media.Imaging;

public sealed class BitmapPalette : DispatcherObject
{
    public BitmapPalette(IList<Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Count is < 1 or > 256)
        {
            throw new InvalidOperationException("A bitmap palette must contain between 1 and 256 colors.");
        }

        Colors = new List<Color>(colors).AsReadOnly();
    }

    /// <summary>
    /// Creates a palette by analysing the colors exposed by <paramref name="bitmapSource"/>.
    /// </summary>
    /// <remarks>
    /// An existing source palette is preserved when it already fits the requested limit.
    /// Otherwise the most frequently occurring quantized colors are retained. This mirrors
    /// WPF's observable contract without tying the core assembly to a platform image codec.
    /// </remarks>
    public BitmapPalette(BitmapSource bitmapSource, int maxColorCount)
    {
        ArgumentNullException.ThrowIfNull(bitmapSource);
        if (maxColorCount is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxColorCount),
                maxColorCount,
                "The maximum palette size must be between 1 and 256 colors.");
        }

        var sourcePalette = bitmapSource.Palette;
        if (sourcePalette is not null && sourcePalette.Colors.Count <= maxColorCount)
        {
            Colors = new List<Color>(sourcePalette.Colors).AsReadOnly();
            return;
        }

        var histogram = new Dictionary<int, ColorBucket>();
        if (sourcePalette is not null)
        {
            foreach (var color in sourcePalette.Colors)
            {
                AddColor(histogram, color);
            }
        }
        else
        {
            AnalyzePixels(bitmapSource, histogram);
        }

        if (histogram.Count == 0)
        {
            throw new InvalidOperationException("The bitmap source did not expose any colors for palette analysis.");
        }

        Colors = histogram.Values
            .OrderByDescending(static bucket => bucket.Count)
            .ThenBy(static bucket => bucket.SortKey)
            .Take(maxColorCount)
            .Select(static bucket => bucket.ToColor())
            .ToList()
            .AsReadOnly();
    }

    public IList<Color> Colors { get; }

    private static void AnalyzePixels(BitmapSource source, Dictionary<int, ColorBucket> histogram)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The bitmap source must have positive pixel dimensions.");
        }

        var bytesPerPixel = GetBytesPerPixel(source.Format);
        var stride = checked(width * bytesPerPixel);
        var pixels = new byte[checked(stride * height)];
        source.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

        // Bound analysis cost for very large sources while sampling the full image area.
        var pixelCount = (long)width * height;
        var sampleStep = pixelCount <= 1_000_000
            ? 1
            : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixelCount / 1_000_000d)));

        for (var y = 0; y < height; y += sampleStep)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x += sampleStep)
            {
                AddColor(histogram, ReadColor(pixels, rowOffset + (x * bytesPerPixel), source.Format));
            }
        }
    }

    private static int GetBytesPerPixel(PixelFormat format)
    {
        if (format == PixelFormat.Bgra32 || format == PixelFormat.Rgba32 ||
            format == PixelFormat.Rgb32 || format == PixelFormat.Pbgra32)
        {
            return 4;
        }

        if (format == PixelFormat.Bgr24 || format == PixelFormat.Rgb24)
        {
            return 3;
        }

        if (format == PixelFormat.Gray8)
        {
            return 1;
        }

        if (format == PixelFormat.Gray16)
        {
            return 2;
        }

        throw new NotSupportedException($"Pixel format '{format}' cannot be analysed for a palette.");
    }

    private static Color ReadColor(byte[] pixels, int offset, PixelFormat format)
    {
        if (format == PixelFormat.Bgra32)
        {
            return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }

        if (format == PixelFormat.Rgba32)
        {
            return Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }

        if (format == PixelFormat.Rgb32)
        {
            return Color.FromRgb(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }

        if (format == PixelFormat.Pbgra32)
        {
            var alpha = pixels[offset + 3];
            if (alpha == 0)
            {
                return Color.FromArgb(0, 0, 0, 0);
            }

            return Color.FromArgb(
                alpha,
                Unpremultiply(pixels[offset + 2], alpha),
                Unpremultiply(pixels[offset + 1], alpha),
                Unpremultiply(pixels[offset], alpha));
        }

        if (format == PixelFormat.Bgr24)
        {
            return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }

        if (format == PixelFormat.Rgb24)
        {
            return Color.FromRgb(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
        }

        if (format == PixelFormat.Gray8)
        {
            return Color.FromRgb(pixels[offset], pixels[offset], pixels[offset]);
        }

        if (format == PixelFormat.Gray16)
        {
            var value = (byte)((pixels[offset] | (pixels[offset + 1] << 8)) >> 8);
            return Color.FromRgb(value, value, value);
        }

        throw new NotSupportedException($"Pixel format '{format}' cannot be analysed for a palette.");
    }

    private static byte Unpremultiply(byte component, byte alpha)
        => (byte)Math.Min(255, ((component * 255) + (alpha / 2)) / alpha);

    private static void AddColor(Dictionary<int, ColorBucket> histogram, Color color)
    {
        // Five bits per RGB component plus four alpha bits bounds the histogram while
        // retaining transparency and enough resolution for a 256-entry palette.
        var key = ((color.A >> 4) << 15) |
                  ((color.R >> 3) << 10) |
                  ((color.G >> 3) << 5) |
                  (color.B >> 3);

        if (histogram.TryGetValue(key, out var bucket))
        {
            bucket.Add(color);
            histogram[key] = bucket;
        }
        else
        {
            histogram.Add(key, new ColorBucket(key, color));
        }
    }

    private struct ColorBucket
    {
        private long _alpha;
        private long _red;
        private long _green;
        private long _blue;

        public ColorBucket(int sortKey, Color color)
        {
            SortKey = sortKey;
            Count = 1;
            _alpha = color.A;
            _red = color.R;
            _green = color.G;
            _blue = color.B;
        }

        public int SortKey { get; }

        public int Count { get; private set; }

        public void Add(Color color)
        {
            Count++;
            _alpha += color.A;
            _red += color.R;
            _green += color.G;
            _blue += color.B;
        }

        public readonly Color ToColor()
            => Color.FromArgb(
                (byte)(_alpha / Count),
                (byte)(_red / Count),
                (byte)(_green / Count),
                (byte)(_blue / Count));
    }
}

public static class BitmapPalettes
{
    private static readonly Color[] AdditionalSystemColors =
    [
        Color.FromRgb(0x80, 0x00, 0x00),
        Color.FromRgb(0x80, 0x80, 0x00),
        Color.FromRgb(0x00, 0x80, 0x00),
        Color.FromRgb(0x80, 0x00, 0x80),
        Color.FromRgb(0x00, 0x00, 0x80),
        Color.FromRgb(0x80, 0x80, 0x80),
        Color.FromRgb(0xC0, 0xC0, 0xC0),
        Color.FromRgb(0x00, 0x80, 0x80),
    ];

    public static BitmapPalette BlackAndWhite { get; } = new(
        [Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255)]);

    public static BitmapPalette BlackAndWhiteTransparent { get; } = new(
        [Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255)]);

    public static BitmapPalette Halftone8 { get; } = CreateColorCubePalette(2, 2, 2, AdditionalSystemColors);
    public static BitmapPalette Halftone8Transparent { get; } = WithTransparentEntry(Halftone8);
    public static BitmapPalette Halftone27 { get; } = CreateColorCubePalette(3, 3, 3, [Color.FromRgb(0xC0, 0xC0, 0xC0)]);
    public static BitmapPalette Halftone27Transparent { get; } = WithTransparentEntry(Halftone27);
    public static BitmapPalette Halftone64 { get; } = CreateColorCubePalette(4, 4, 4, AdditionalSystemColors);
    public static BitmapPalette Halftone64Transparent { get; } = WithTransparentEntry(Halftone64);
    public static BitmapPalette Halftone125 { get; } = CreateColorCubePalette(5, 5, 5, [Color.FromRgb(0xC0, 0xC0, 0xC0)]);
    public static BitmapPalette Halftone125Transparent { get; } = WithTransparentEntry(Halftone125);
    public static BitmapPalette Halftone216 { get; } = CreateColorCubePalette(6, 6, 6, AdditionalSystemColors);
    public static BitmapPalette Halftone216Transparent { get; } = WithTransparentEntry(Halftone216);
    public static BitmapPalette Halftone252 { get; } = CreateColorCubePalette(6, 7, 6, []);
    public static BitmapPalette Halftone252Transparent { get; } = WithTransparentEntry(Halftone252);
    public static BitmapPalette Halftone256 { get; } = CreateHalftone256Palette();
    public static BitmapPalette Halftone256Transparent { get; } = WithTransparentEntry(Halftone256);
    public static BitmapPalette Gray256 { get; } = CreateGrayscalePalette(256);
    public static BitmapPalette Gray256Transparent { get; } = CreateGrayscalePalette(256);
    public static BitmapPalette Gray16 { get; } = CreateGrayscalePalette(16);
    public static BitmapPalette Gray16Transparent { get; } = CreateGrayscalePalette(16);
    public static BitmapPalette Gray4 { get; } = CreateGrayscalePalette(4);
    public static BitmapPalette Gray4Transparent { get; } = CreateGrayscalePalette(4);
    public static BitmapPalette WebPalette { get; } = CreateColorCubePalette(6, 6, 6, AdditionalSystemColors);
    public static BitmapPalette WebPaletteTransparent { get; } = WithTransparentEntry(WebPalette);

    private static BitmapPalette CreateGrayscalePalette(int count)
    {
        var colors = new List<Color>(count);
        for (var index = 0; index < count; index++)
        {
            var value = (byte)(index * 255 / Math.Max(count - 1, 1));
            colors.Add(Color.FromArgb(255, value, value, value));
        }

        return new BitmapPalette(colors);
    }

    private static BitmapPalette CreateColorCubePalette(
        int redLevels,
        int greenLevels,
        int blueLevels,
        IReadOnlyList<Color> additionalColors)
    {
        var colors = new List<Color>((redLevels * greenLevels * blueLevels) + additionalColors.Count);
        for (var red = 0; red < redLevels; red++)
        {
            for (var green = 0; green < greenLevels; green++)
            {
                for (var blue = 0; blue < blueLevels; blue++)
                {
                    colors.Add(Color.FromRgb(
                        ToLevel(red, redLevels),
                        ToLevel(green, greenLevels),
                        ToLevel(blue, blueLevels)));
                }
            }
        }

        colors.AddRange(additionalColors);
        return new BitmapPalette(colors);
    }

    private static BitmapPalette CreateHalftone256Palette()
    {
        var colors = new List<Color>(256);
        colors.AddRange(CreateColorCubePalette(6, 6, 6, []).Colors);
        for (var index = 1; colors.Count < 256; index++)
        {
            var value = (byte)(index * 255 / 40);
            colors.Add(Color.FromRgb(value, value, value));
        }

        return new BitmapPalette(colors);
    }

    private static BitmapPalette WithTransparentEntry(BitmapPalette palette)
    {
        var colors = new List<Color>(palette.Colors);
        if (colors.Count == 256)
        {
            colors[^1] = Color.FromArgb(0, 0, 0, 0);
        }
        else
        {
            colors.Add(Color.FromArgb(0, 0, 0, 0));
        }

        return new BitmapPalette(colors);
    }

    private static byte ToLevel(int index, int count) =>
        (byte)(index * 255 / Math.Max(count - 1, 1));
}
