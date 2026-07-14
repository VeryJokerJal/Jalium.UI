using System.Collections.ObjectModel;
using System.Net.Cache;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using BitmapImage = Jalium.UI.Media.Imaging.BitmapImage;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using RenderTargetBitmap = Jalium.UI.Media.Imaging.RenderTargetBitmap;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

namespace Jalium.UI.Tests;

public sealed class ImagingRefreshedParityTests
{
    [Fact]
    public void CanonicalBitmapTypesLiveInImagingNamespaceAndOwnTheirRuntimeImplementation()
    {
        Assert.Equal("Jalium.UI.Media.Imaging", typeof(BitmapSource).Namespace);
        Assert.Equal("Jalium.UI.Media.Imaging", typeof(BitmapImage).Namespace);
        Assert.Equal("Jalium.UI.Media.Imaging", typeof(RenderTargetBitmap).Namespace);
        Assert.Equal("Jalium.UI.Media.Imaging", typeof(WriteableBitmap).Namespace);
        Assert.Equal(typeof(ImageSource), typeof(BitmapSource).BaseType);
        Assert.True(typeof(BitmapSource).IsAssignableFrom(typeof(BitmapImage)));
        Assert.True(typeof(BitmapSource).IsAssignableFrom(typeof(RenderTargetBitmap)));
        Assert.True(typeof(BitmapSource).IsAssignableFrom(typeof(WriteableBitmap)));
        Assert.True(typeof(BitmapImage).IsSealed);
        Assert.True(typeof(BitmapSource).IsAbstract);
    }

    [Fact]
    public void CanonicalBitmapTypesExposeJaliumNativeAndMutableExtensions()
    {
        Assert.Equal(
            typeof(BitmapImage),
            typeof(BitmapImage).GetMethod(
                nameof(BitmapImage.FromPixels),
                [typeof(byte[]), typeof(int), typeof(int), typeof(int)])?.ReturnType);
        Assert.Equal(
            typeof(BitmapImage),
            typeof(BitmapImage).GetMethod(nameof(BitmapImage.FromDecodedImage))?.ReturnType);
        Assert.Equal(
            typeof(BitmapImage),
            typeof(BitmapImage).GetMethod(nameof(BitmapImage.FromMediaFrame))?.ReturnType);
        Assert.NotNull(typeof(BitmapImage).GetMethod(nameof(BitmapImage.SetDecoder)));

        var source = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        source.SetPixel(0, 0, Colors.Red);
        source.SetPixel(1, 0, Colors.Blue);

        WriteableBitmap cropped = source.Crop(new Int32Rect(1, 0, 1, 1));
        Assert.Equal(Colors.Blue, cropped.GetPixel(0, 0));

        var target = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        target.Blit(source, new Int32Rect(0, 0, 2, 1), new Point(0, 0));
        Assert.Equal(Colors.Red, target.GetPixel(0, 0));
        Assert.Equal(Colors.Blue, target.GetPixel(1, 0));
    }

    [Fact]
    public void BitmapSourceArrayFactoryCopiesPixelsAndKeepsCallerStorageIndependent()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        BitmapSource source = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        pixels[0] = 99;

        var copied = new byte[8];
        source.CopyPixels(copied, 8, 0);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, copied);
        Assert.Equal(2, source.PixelWidth);
        Assert.Equal(1, source.PixelHeight);
    }

    [Fact]
    public void WriteableBitmapDestinationOverloadAndTypedClonePreservePixels()
    {
        var bitmap = new WriteableBitmap(3, 2, 96, 96, PixelFormats.Bgra32, null);
        byte[] source =
        [
            1, 2, 3, 255, 4, 5, 6, 255,
            7, 8, 9, 255, 10, 11, 12, 255,
        ];

        bitmap.WritePixels(new Int32Rect(1, 0, 1, 2), source, 8, 2, 0);
        WriteableBitmap clone = bitmap.Clone();
        var copied = new byte[24];
        clone.CopyPixels(new Int32Rect(0, 0, 3, 2), copied, 12, 0);

        Assert.Equal(new byte[] { 4, 5, 6, 255 }, copied[8..12]);
        Assert.Equal(new byte[] { 10, 11, 12, 255 }, copied[20..24]);
        Assert.True(bitmap.TryLock(new Duration(TimeSpan.Zero)));
        bitmap.Unlock();
    }

    [Fact]
    public void RenderTargetClearWithoutColorUsesTransparentPixels()
    {
        var bitmap = new RenderTargetBitmap(2, 1, 96, 96, PixelFormats.Bgra32);
        bitmap.Clear(Colors.Red);
        bitmap.Clear();
        var pixels = new byte[8];
        bitmap.CopyPixels(new Int32Rect(0, 0, 2, 1), pixels, 8, 0);

        Assert.Equal(new byte[8], pixels);
    }

    [Fact]
    public void BitmapImageDeclaresWpfInitializationAndDependencyPropertySurface()
    {
        Type type = typeof(BitmapImage);
        Assert.NotNull(type.GetConstructor([typeof(Uri), typeof(RequestCachePolicy)]));
        Assert.NotNull(type.GetMethod(nameof(BitmapImage.BeginInit), Type.EmptyTypes));
        Assert.NotNull(type.GetMethod(nameof(BitmapImage.EndInit), Type.EmptyTypes));
        Assert.NotNull(type.GetProperty(nameof(BitmapImage.StreamSource), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.NotNull(type.GetProperty(nameof(BitmapImage.DecodePixelWidth), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Same(BitmapImage.UriSourceProperty,
            type.GetField(nameof(BitmapImage.UriSourceProperty), BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(null));

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.DecodePixelWidth = 24;
        bitmap.DecodePixelHeight = 12;
        bitmap.Rotation = Rotation.Rotate90;
        bitmap.EndInit();
        Assert.Equal(24, bitmap.DecodePixelWidth);
        Assert.Equal(12, bitmap.DecodePixelHeight);
    }

    [Fact]
    public void MetadataIsFreezableEnumerableAndClonesQueryState()
    {
        Assert.Equal(typeof(ImageMetadata), typeof(BitmapMetadata).BaseType);
        Assert.True(typeof(ImageMetadata).IsAbstract);
        Assert.Null(typeof(BitmapMetadata).Assembly.GetType("Jalium.UI.Media.BitmapMetadataBase", throwOnError: false));

        var metadata = new BitmapMetadata("png")
        {
            Author = new ReadOnlyCollection<string>(["Ada"]),
            Keywords = new ReadOnlyCollection<string>(["ui", "bitmap"]),
        };
        metadata.SetQuery("/custom/value", 42);

        BitmapMetadata clone = metadata.Clone();
        Assert.True(clone.ContainsQuery("/custom/value"));
        Assert.Equal(42, clone.GetQuery("/custom/value"));
        Assert.Contains("/custom/value", (IEnumerable<string>)clone);

        metadata.Freeze();
        Assert.True(metadata.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => metadata.SetQuery("/custom/other", 1));
    }

    [Fact]
    public void DecoderEncoderAndFrameExposeRefreshedContracts()
    {
        Assert.True(typeof(BitmapCodecInfo).IsAbstract);
        Assert.True(typeof(BitmapFrame).IsAbstract);
        Assert.NotNull(typeof(WmpBitmapDecoder).GetConstructor(
            [typeof(Stream), typeof(BitmapCreateOptions), typeof(BitmapCacheOption)]));
        Assert.True(typeof(LateBoundBitmapDecoder).IsSealed);
        Assert.Equal(typeof(BitmapMetadata), typeof(BitmapDecoder).GetProperty(nameof(BitmapDecoder.Metadata))?.PropertyType);
        Assert.Equal(typeof(BitmapMetadata), typeof(BitmapEncoder).GetProperty(nameof(BitmapEncoder.Metadata))?.PropertyType);
        Assert.True(typeof(BitmapFrame).GetProperty(nameof(BitmapFrame.BaseUri))?.GetMethod?.IsAbstract is true);
        Assert.True(typeof(BitmapFrame).GetMethod(nameof(BitmapFrame.CreateInPlaceBitmapMetadataWriter))?.IsAbstract is true);

        BitmapEncoder encoder = BitmapEncoder.Create(new Guid("1b7cfaf4-713f-473c-bbcd-6137425faeaf"));
        Assert.IsType<PngBitmapEncoder>(encoder);
        var wmp = new WmpBitmapEncoder
        {
            UseCodecOptions = true,
            QualityLevel = 80,
            Rotation = Rotation.Rotate90,
            HorizontalTileSlices = 2,
        };
        Assert.Equal((byte)80, wmp.QualityLevel);
        Assert.Equal((short)2, wmp.HorizontalTileSlices);
    }
}
