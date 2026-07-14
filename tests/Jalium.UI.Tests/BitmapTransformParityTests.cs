using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

namespace Jalium.UI.Tests;

public sealed class BitmapTransformParityTests
{
    [Fact]
    public void TransformSourcesExposeDependencyPropertiesInitializationAndTypedCloneContracts()
    {
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(BitmapSource)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(CroppedBitmap)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(FormatConvertedBitmap)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(TransformedBitmap)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(ColorConvertedBitmap)));

        Assert.Same(CroppedBitmap.SourceProperty, GetField(CroppedBitmap.SourceProperty, nameof(CroppedBitmap.SourceProperty)));
        Assert.Same(CroppedBitmap.SourceRectProperty, GetField(CroppedBitmap.SourceRectProperty, nameof(CroppedBitmap.SourceRectProperty)));
        Assert.Same(FormatConvertedBitmap.DestinationFormatProperty,
            GetField(FormatConvertedBitmap.DestinationFormatProperty, nameof(FormatConvertedBitmap.DestinationFormatProperty)));
        Assert.Same(TransformedBitmap.TransformProperty,
            GetField(TransformedBitmap.TransformProperty, nameof(TransformedBitmap.TransformProperty)));
        Assert.Same(ColorConvertedBitmap.DestinationColorContextProperty,
            GetField(ColorConvertedBitmap.DestinationColorContextProperty, nameof(ColorConvertedBitmap.DestinationColorContextProperty)));

        Assert.Equal(PixelFormats.Pbgra32, new FormatConvertedBitmap().DestinationFormat);
        Assert.Equal(PixelFormats.Pbgra32, new ColorConvertedBitmap().DestinationFormat);
        Assert.True(new TransformedBitmap().Transform.Value.IsIdentity);
        Assert.Throws<InvalidOperationException>(() => new CroppedBitmap().EndInit());

        static DependencyProperty GetField(DependencyProperty value, string name)
        {
            FieldInfo field = value.OwnerType.GetField(name, BindingFlags.Public | BindingFlags.Static)!;
            Assert.True(field.IsInitOnly);
            return (DependencyProperty)field.GetValue(null)!;
        }
    }

    [Fact]
    public void CroppedBitmapCopiesTheSelectedPixelRectangle()
    {
        WriteableBitmap source = CreateBgraBitmap(3, 2,
        [
            0, 0, 255, 255,   0, 255, 0, 255,   255, 0, 0, 255,
            0, 255, 255, 255, 255, 0, 255, 255, 255, 255, 0, 255,
        ]);
        var cropped = new CroppedBitmap(source, new Int32Rect(1, 0, 2, 2));

        Assert.Equal(2, cropped.PixelWidth);
        Assert.Equal(2, cropped.PixelHeight);
        Assert.Equal(PixelFormats.Bgra32, cropped.Format);
        Assert.Equal(
        [
            0, 255, 0, 255,   255, 0, 0, 255,
            255, 0, 255, 255, 255, 255, 0, 255,
        ], CopyAll(cropped));

        var onePixel = new byte[4];
        cropped.CopyPixels(new Int32Rect(1, 1, 1, 1), onePixel, 4, 0);
        Assert.Equal([255, 255, 0, 255], onePixel);
    }

    [Fact]
    public void FormatConvertedBitmapPerformsColorGrayAndIndexedConversions()
    {
        WriteableBitmap source = CreateBgraBitmap(2, 1,
        [
            0, 0, 255, 255,
            255, 255, 255, 0,
        ]);

        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        byte[] grayPixels = CopyAll(gray);
        Assert.InRange(grayPixels[0], (byte)53, (byte)55);
        Assert.Equal(255, grayPixels[1]);

        var rgba = new FormatConvertedBitmap(source, PixelFormats.Rgba32, null, 0);
        Assert.Equal([255, 0, 0, 255, 255, 255, 255, 0], CopyAll(rgba));

        var palette = new BitmapPalette(
        [
            Color.FromArgb(0, 0, 0, 0),
            Color.FromRgb(255, 0, 0),
        ]);
        var indexed = new FormatConvertedBitmap(source, PixelFormats.Indexed1, palette, 50);
        Assert.Equal(PixelFormats.Indexed1, indexed.Format);
        Assert.Same(palette, indexed.Palette);
        Assert.Equal([0b1000_0000], CopyAll(indexed));
    }

    [Fact]
    public void TransformedBitmapRotatesPixelsAndTracksMutableTransform()
    {
        WriteableBitmap source = CreateBgraBitmap(2, 1,
        [
            0, 0, 255, 255,
            0, 255, 0, 255,
        ]);
        var rotation = new RotateTransform(90);
        var transformed = new TransformedBitmap(source, rotation);

        Assert.Equal(1, transformed.PixelWidth);
        Assert.Equal(2, transformed.PixelHeight);
        Assert.Equal([0, 0, 255, 255, 0, 255, 0, 255], CopyAll(transformed));

        rotation.Angle = 0;
        Assert.Equal(2, transformed.PixelWidth);
        Assert.Equal(1, transformed.PixelHeight);
        Assert.Equal([0, 0, 255, 255, 0, 255, 0, 255], CopyAll(transformed));
    }

    [Fact]
    public void CachedBitmapIsASnapshotAndClonesRemainIndependent()
    {
        WriteableBitmap source = CreateBgraBitmap(1, 1, [10, 20, 30, 255]);
        var cached = new CachedBitmap(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        source.WritePixels(new Int32Rect(0, 0, 1, 1), [100, 110, 120, 255], 4, 0);
        Assert.Equal([10, 20, 30, 255], CopyAll(cached));

        CachedBitmap clone = cached.Clone();
        Assert.NotSame(cached, clone);
        Assert.Equal([10, 20, 30, 255], CopyAll(clone));
        clone.Freeze();
        Assert.True(clone.IsFrozen);
    }

    [Fact]
    public void ColorConvertedBitmapValidatesContextsAndConvertsDestinationFormat()
    {
        WriteableBitmap source = CreateBgraBitmap(1, 1, [10, 20, 30, 128]);
        var sourceContext = new ColorContext(new Uri("source.icc", UriKind.Relative));
        var destinationContext = new ColorContext(new Uri("destination.icc", UriKind.Relative));
        var converted = new ColorConvertedBitmap(
            source, sourceContext, destinationContext, PixelFormats.Rgba32);

        Assert.Same(source, converted.Source);
        Assert.Same(sourceContext, converted.SourceColorContext);
        Assert.Same(destinationContext, converted.DestinationColorContext);
        Assert.Equal([30, 20, 10, 128], CopyAll(converted));

        var incomplete = new ColorConvertedBitmap();
        incomplete.BeginInit();
        incomplete.Source = source;
        Assert.Throws<InvalidOperationException>(() => incomplete.EndInit());
    }

    [Fact]
    public void BitmapTransformClonesAndFrozenCopiesPreserveFunctionalPixels()
    {
        WriteableBitmap mutable = CreateBgraBitmap(2, 1, [1, 2, 3, 255, 4, 5, 6, 255]);
        var snapshot = new CachedBitmap(mutable, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var cropped = new CroppedBitmap(snapshot, new Int32Rect(1, 0, 1, 1));

        CroppedBitmap clone = cropped.CloneCurrentValue();
        var frozen = (CroppedBitmap)cropped.GetAsFrozen();

        Assert.Equal([4, 5, 6, 255], CopyAll(clone));
        Assert.Equal([4, 5, 6, 255], CopyAll(frozen));
        Assert.True(frozen.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => frozen.SourceRect = Int32Rect.Empty);
    }

    private static WriteableBitmap CreateBgraBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bitmap;
    }

    private static byte[] CopyAll(BitmapSource source)
    {
        int stride = checked((source.PixelWidth * source.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight), pixels, stride, 0);
        return pixels;
    }
}
