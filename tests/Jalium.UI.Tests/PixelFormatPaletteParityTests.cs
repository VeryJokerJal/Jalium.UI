using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public sealed class PixelFormatPaletteParityTests
{
    [Fact]
    public void PixelFormatIsAValueTypeWithWpfEqualityAndMetadataContracts()
    {
        Assert.True(typeof(PixelFormat).IsValueType);
        Assert.False(typeof(PixelFormat).IsEnum);
        Assert.True(typeof(IEquatable<PixelFormat>).IsAssignableFrom(typeof(PixelFormat)));
        Assert.True(typeof(PixelFormatChannelMask).IsValueType);

        Assert.Equal(32, PixelFormats.Bgra32.BitsPerPixel);
        Assert.Equal(16, PixelFormats.Bgr565.BitsPerPixel);
        Assert.Equal(128, PixelFormats.Rgba128Float.BitsPerPixel);
        Assert.Throws<NotSupportedException>(() => PixelFormats.Default.BitsPerPixel);

        Assert.Equal(PixelFormats.Bgra32, PixelFormat.Bgra32);
        Assert.True(PixelFormat.Equals(PixelFormats.Bgra32, PixelFormat.Bgra32));
        Assert.False(PixelFormats.Bgra32 == PixelFormats.Pbgra32);
        Assert.Equal("Bgra32", PixelFormats.Bgra32.ToString());
        Assert.Equal("Default", default(PixelFormat).ToString());
    }

    [Fact]
    public void PixelFormatMasksExposeCanonicalChannelLayouts()
    {
        AssertMask(PixelFormats.BlackWhite, [0x01]);
        AssertMask(PixelFormats.Bgr555, [0x1F, 0x00], [0xE0, 0x03], [0x00, 0x7C]);
        AssertMask(PixelFormats.Bgr565, [0x1F, 0x00], [0xE0, 0x07], [0x00, 0xF8]);
        AssertMask(
            PixelFormats.Bgra32,
            [0xFF, 0x00, 0x00, 0x00],
            [0x00, 0xFF, 0x00, 0x00],
            [0x00, 0x00, 0xFF, 0x00],
            [0x00, 0x00, 0x00, 0xFF]);

        PixelFormatChannelMask first = PixelFormats.Bgra32.Masks[0];
        Assert.Equal(first, PixelFormats.Bgra32.Masks[0]);
        Assert.True(PixelFormatChannelMask.Equals(first, PixelFormats.Bgra32.Masks[0]));
        Assert.Throws<NotSupportedException>(() => PixelFormats.Default.Masks);
    }

    [Fact]
    public void PixelFormatsExposeTheCompleteWpfCatalog()
    {
        (PixelFormat Format, int Bits)[] formats =
        [
            (PixelFormats.Indexed1, 1), (PixelFormats.Indexed2, 2),
            (PixelFormats.Indexed4, 4), (PixelFormats.Indexed8, 8),
            (PixelFormats.Gray2, 2), (PixelFormats.Gray4, 4),
            (PixelFormats.Gray8, 8), (PixelFormats.Gray16, 16),
            (PixelFormats.Gray32Float, 32), (PixelFormats.Bgr101010, 32),
            (PixelFormats.Bgr32, 32), (PixelFormats.Rgb48, 48),
            (PixelFormats.Rgba64, 64), (PixelFormats.Prgba64, 64),
            (PixelFormats.Rgb128Float, 128), (PixelFormats.Rgba128Float, 128),
            (PixelFormats.Prgba128Float, 128), (PixelFormats.Cmyk32, 32),
        ];

        foreach ((PixelFormat format, int bits) in formats)
        {
            Assert.Equal(bits, format.BitsPerPixel);
            Assert.NotEmpty(format.Masks);
        }
    }

    [Fact]
    public void PixelFormatConverterRoundTripsCanonicalNames()
    {
        var converter = new PixelFormatConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.Equal(PixelFormats.Bgr565, converter.ConvertFromString("bgr565"));
        Assert.Equal(
            "Rgba128Float",
            converter.ConvertTo(null, CultureInfo.InvariantCulture, PixelFormats.Rgba128Float, typeof(string)));
        Assert.Throws<FormatException>(() => converter.ConvertFromString("not-a-format"));
    }

    [Fact]
    public void BitmapPalettesExposeStableCanonicalCountsAndTransparency()
    {
        Assert.Same(BitmapPalettes.BlackAndWhite, BitmapPalettes.BlackAndWhite);
        Assert.Equal(2, BitmapPalettes.BlackAndWhite.Colors.Count);
        Assert.Equal(4, BitmapPalettes.Gray4Transparent.Colors.Count);
        Assert.Equal(16, BitmapPalettes.Gray16Transparent.Colors.Count);
        Assert.Equal(256, BitmapPalettes.Gray256Transparent.Colors.Count);

        AssertPalette(BitmapPalettes.Halftone8, 16, false);
        AssertPalette(BitmapPalettes.Halftone8Transparent, 17, true);
        AssertPalette(BitmapPalettes.Halftone27, 28, false);
        AssertPalette(BitmapPalettes.Halftone27Transparent, 29, true);
        AssertPalette(BitmapPalettes.Halftone64, 72, false);
        AssertPalette(BitmapPalettes.Halftone64Transparent, 73, true);
        AssertPalette(BitmapPalettes.Halftone125, 126, false);
        AssertPalette(BitmapPalettes.Halftone125Transparent, 127, true);
        AssertPalette(BitmapPalettes.Halftone216, 224, false);
        AssertPalette(BitmapPalettes.Halftone216Transparent, 225, true);
        AssertPalette(BitmapPalettes.Halftone252, 252, false);
        AssertPalette(BitmapPalettes.Halftone252Transparent, 253, true);
        AssertPalette(BitmapPalettes.Halftone256, 256, false);
        AssertPalette(BitmapPalettes.Halftone256Transparent, 256, true);
        AssertPalette(BitmapPalettes.WebPalette, 224, false);
        AssertPalette(BitmapPalettes.WebPaletteTransparent, 225, true);
    }

    private static void AssertMask(PixelFormat format, params byte[][] expected)
    {
        Assert.Equal(expected.Length, format.Masks.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], format.Masks[index].Mask);
        }
    }

    private static void AssertPalette(BitmapPalette palette, int count, bool transparentLast)
    {
        Assert.Equal(count, palette.Colors.Count);
        Assert.Equal((byte)(transparentLast ? 0 : 255), palette.Colors[^1].A);
    }
}
