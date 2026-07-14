using System.Collections.ObjectModel;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

namespace Jalium.UI.Tests;

public sealed class ImagingDecoderParityTests
{
    [Fact]
    public void ConcreteDecodersExposeUriConstructorsAndCanonicalFramesCollection()
    {
        Type[] constructorParameters =
        [
            typeof(Uri),
            typeof(BitmapCreateOptions),
            typeof(BitmapCacheOption),
        ];

        Assert.NotNull(typeof(BmpBitmapDecoder).GetConstructor(constructorParameters));
        Assert.NotNull(typeof(GifBitmapDecoder).GetConstructor(constructorParameters));
        Assert.NotNull(typeof(TiffBitmapDecoder).GetConstructor(constructorParameters));

        Assert.Equal(
            typeof(ReadOnlyCollection<BitmapFrame>),
            typeof(BitmapDecoder).GetProperty(nameof(BitmapDecoder.Frames))!.PropertyType);
        Assert.Equal(0, (int)BitmapCacheOption.OnDemand);
        Assert.Equal(1, (int)BitmapCacheOption.OnLoad);
        Assert.Equal(2, (int)BitmapCacheOption.None);
    }

    [Fact]
    public void ConcreteDecoderUriConstructorsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BmpBitmapDecoder((Uri)null!, BitmapCreateOptions.None, BitmapCacheOption.Default));
        Assert.Throws<ArgumentNullException>(() =>
            new GifBitmapDecoder((Uri)null!, BitmapCreateOptions.None, BitmapCacheOption.Default));
        Assert.Throws<ArgumentNullException>(() =>
            new TiffBitmapDecoder((Uri)null!, BitmapCreateOptions.None, BitmapCacheOption.Default));
    }

    [Fact]
    public void DelayCreationDefersUriAccessUntilFramesAreRequested()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"jalium-missing-{Guid.NewGuid():N}.bmp");
        var decoder = new BmpBitmapDecoder(
            new Uri(missingPath),
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.OnDemand);

        Assert.Throws<FileNotFoundException>(() => _ = decoder.Frames);
    }

    [Fact]
    public void DelayCreationDefersStreamReadAndContainerValidation()
    {
        using var stream = new MemoryStream("GIF89a"u8.ToArray());
        var decoder = new BmpBitmapDecoder(
            stream,
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.OnDemand);

        Assert.Equal(0, stream.Position);
        Assert.Throws<InvalidDataException>(() => _ = decoder.Frames);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void EagerCreationValidatesTheConcreteContainer()
    {
        using var stream = new MemoryStream("GIF89a"u8.ToArray());

        Assert.Throws<InvalidDataException>(() =>
            new BmpBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnDemand));
    }

    [Fact]
    public void BitmapPaletteValidatesCustomPaletteSize()
    {
        Assert.Throws<InvalidOperationException>(() => new BitmapPalette(Array.Empty<Color>()));
        Assert.Throws<InvalidOperationException>(() =>
            new BitmapPalette(Enumerable.Repeat(Colors.Black, 257).ToList()));
    }

    [Fact]
    public void BitmapPaletteExtractsObservableSourceColors()
    {
        var bitmap = new WriteableBitmap(2, 1, 96, 96, PixelFormat.Bgra32, palette: null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, 2, 1),
            new byte[]
            {
                0, 0, 255, 255, // red, BGRA
                255, 0, 0, 255, // blue, BGRA
            },
            stride: 8,
            offset: 0);

        var palette = new BitmapPalette(bitmap, maxColorCount: 2);

        Assert.Equal(2, palette.Colors.Count);
        Assert.Contains(Colors.Red, palette.Colors);
        Assert.Contains(Colors.Blue, palette.Colors);
    }

    [Fact]
    public void BitmapPalettePreservesAnExistingPaletteWithinTheLimit()
    {
        var existing = new BitmapPalette([Colors.Red, Colors.Green, Colors.Blue]);
        var source = new PaletteBitmapSource(existing);

        var derived = new BitmapPalette(source, maxColorCount: 3);

        Assert.Equal(existing.Colors.ToArray(), derived.Colors.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitmapPalette(source, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitmapPalette(source, 257));
    }

    private sealed class PaletteBitmapSource(BitmapPalette palette) : BitmapSource
    {
        public override BitmapPalette Palette => palette;

        public override nint NativeHandle => nint.Zero;

        public override double Width => 1;

        public override double Height => 1;
    }
}
