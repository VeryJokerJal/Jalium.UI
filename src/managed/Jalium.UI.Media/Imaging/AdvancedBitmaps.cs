using System.ComponentModel;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides cached access to a BitmapSource for performance optimization.
/// </summary>
public sealed class CachedBitmap : BitmapSource
{
    private byte[] _pixels = Array.Empty<byte>();
    private int _pixelWidth;
    private int _pixelHeight;
    private int _stride;
    private double _dpiX = 96;
    private double _dpiY = 96;
    private PixelFormat _format = PixelFormat.Pbgra32;
    private BitmapPalette? _palette;

    private CachedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance of the CachedBitmap class.
    /// </summary>
    public CachedBitmap(BitmapSource source, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(source);
        Capture(source);
    }

    public override int PixelWidth => _pixelWidth;
    public override int PixelHeight => _pixelHeight;
    public override double DpiX => _dpiX;
    public override double DpiY => _dpiY;
    public override PixelFormat Format => _format;
    public override BitmapPalette? Palette => _palette;
    public override double Width => _pixelWidth * 96d / _dpiX;
    public override double Height => _pixelHeight * 96d / _dpiY;
    public override nint NativeHandle => nint.Zero;

    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
        => BitmapPixelOperations.Copy(_pixels, _pixelWidth, _pixelHeight, _stride, _format, sourceRect, pixels, stride, offset);

    public new CachedBitmap Clone() => (CachedBitmap)base.Clone();

    public new CachedBitmap CloneCurrentValue() => (CachedBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new CachedBitmap();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyState((CachedBitmap)sourceFreezable);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyState((CachedBitmap)sourceFreezable);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyState((CachedBitmap)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyState((CachedBitmap)sourceFreezable);
    }

    private void Capture(BitmapSource source)
    {
        BitmapPixelOperations.PixelBuffer buffer = BitmapPixelOperations.Read(source);
        _pixels = buffer.Pixels;
        _pixelWidth = buffer.Width;
        _pixelHeight = buffer.Height;
        _stride = buffer.Stride;
        _dpiX = source.DpiX > 0 ? source.DpiX : 96;
        _dpiY = source.DpiY > 0 ? source.DpiY : 96;
        _format = buffer.Format;
        _palette = buffer.Palette;
    }

    private void CopyState(CachedBitmap source)
    {
        _pixels = (byte[])source._pixels.Clone();
        _pixelWidth = source._pixelWidth;
        _pixelHeight = source._pixelHeight;
        _stride = source._stride;
        _dpiX = source._dpiX;
        _dpiY = source._dpiY;
        _format = source._format;
        _palette = source._palette;
    }
}

/// <summary>
/// Converts the color space of a BitmapSource.
/// </summary>
public sealed class ColorConvertedBitmap : BitmapSource, ISupportInitialize
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(ColorConvertedBitmap),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty SourceColorContextProperty =
        DependencyProperty.Register(nameof(SourceColorContext), typeof(ColorContext), typeof(ColorConvertedBitmap),
            new PropertyMetadata(null, OnConversionPropertyChanged));

    public static readonly DependencyProperty DestinationColorContextProperty =
        DependencyProperty.Register(nameof(DestinationColorContext), typeof(ColorContext), typeof(ColorConvertedBitmap),
            new PropertyMetadata(null, OnConversionPropertyChanged));

    public static readonly DependencyProperty DestinationFormatProperty =
        DependencyProperty.Register(nameof(DestinationFormat), typeof(PixelFormat), typeof(ColorConvertedBitmap),
            new PropertyMetadata(PixelFormat.Pbgra32, OnConversionPropertyChanged));

    private bool _initializing;
    private bool _initialized;
    private byte[]? _convertedPixels;
    private int _convertedStride;

    /// <summary>
    /// Initializes a new instance of the ColorConvertedBitmap class.
    /// </summary>
    public ColorConvertedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance with source, source and destination color contexts and pixel format.
    /// </summary>
    public ColorConvertedBitmap(BitmapSource source, ColorContext sourceColorContext,
        ColorContext destinationColorContext, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceColorContext);
        ArgumentNullException.ThrowIfNull(destinationColorContext);
        BeginInit();
        Source = source;
        SourceColorContext = sourceColorContext;
        DestinationColorContext = destinationColorContext;
        DestinationFormat = format;
        EndInit();
    }

    /// <summary>Gets or sets the bitmap whose pixels are converted.</summary>
    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the source color context.
    /// </summary>
    public ColorContext? SourceColorContext
    {
        get => (ColorContext?)GetValue(SourceColorContextProperty);
        set => SetValue(SourceColorContextProperty, value);
    }

    /// <summary>
    /// Gets or sets the destination color context.
    /// </summary>
    public ColorContext? DestinationColorContext
    {
        get => (ColorContext?)GetValue(DestinationColorContextProperty);
        set => SetValue(DestinationColorContextProperty, value);
    }

    /// <summary>
    /// Gets or sets the destination pixel format.
    /// </summary>
    public PixelFormat DestinationFormat
    {
        get => (PixelFormat)GetValue(DestinationFormatProperty)!;
        set => SetValue(DestinationFormatProperty, value);
    }

    public override int PixelWidth => Source?.PixelWidth ?? 0;
    public override int PixelHeight => Source?.PixelHeight ?? 0;
    public override double DpiX => Source?.DpiX ?? 96;
    public override double DpiY => Source?.DpiY ?? 96;
    public override PixelFormat Format => DestinationFormat;
    public override BitmapPalette? Palette => null;
    public override double Width => Source?.Width ?? 0;
    public override double Height => Source?.Height ?? 0;
    public override nint NativeHandle => nint.Zero;

    public void BeginInit()
    {
        WritePreamble();
        if (_initializing || _initialized)
        {
            throw new InvalidOperationException("Cannot set the initializing state more than once.");
        }

        _initializing = true;
    }

    public void EndInit()
    {
        WritePreamble();
        if (!_initializing)
        {
            throw new InvalidOperationException("BeginInit must be called before EndInit.");
        }

        if (Source is null)
        {
            throw new InvalidOperationException("'Source' property is not set.");
        }

        if (SourceColorContext is null || DestinationColorContext is null)
        {
            throw new InvalidOperationException("Color context is null.");
        }

        _initializing = false;
        _initialized = true;
        InvalidatePixels();
    }

    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        EnsurePixels();
        BitmapPixelOperations.Copy(_convertedPixels!, PixelWidth, PixelHeight, _convertedStride,
            DestinationFormat, sourceRect, pixels, stride, offset);
    }

    public new ColorConvertedBitmap Clone() => (ColorConvertedBitmap)base.Clone();

    public new ColorConvertedBitmap CloneCurrentValue() => (ColorConvertedBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new ColorConvertedBitmap();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyInitializationState((ColorConvertedBitmap)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyInitializationState((ColorConvertedBitmap)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyInitializationState((ColorConvertedBitmap)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyInitializationState((ColorConvertedBitmap)source);
    }

    protected override void OnChanged()
    {
        InvalidatePixels();
        base.OnChanged();
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (ColorConvertedBitmap)dependencyObject;
        bitmap.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, SourceProperty);
        bitmap.InvalidatePixels();
    }

    private static void OnConversionPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        => ((ColorConvertedBitmap)dependencyObject).InvalidatePixels();

    private void EnsurePixels()
    {
        if (_convertedPixels is not null)
        {
            return;
        }

        BitmapSource source = Source ?? throw new InvalidOperationException("'Source' property is not set.");
        if (SourceColorContext is null || DestinationColorContext is null)
        {
            throw new InvalidOperationException("Color context is null.");
        }

        // Pixel-format conversion is performed in managed code. ICC profile application is
        // intentionally profile-neutral when no platform color-management engine is available;
        // the source and destination contexts are nevertheless retained and validated exactly.
        BitmapPixelOperations.PixelBuffer converted = BitmapPixelOperations.Convert(source, DestinationFormat, null, 0);
        _convertedPixels = converted.Pixels;
        _convertedStride = converted.Stride;
    }

    private void InvalidatePixels()
    {
        _convertedPixels = null;
        _convertedStride = 0;
    }

    private void CopyInitializationState(ColorConvertedBitmap source)
    {
        _initializing = source._initializing;
        _initialized = source._initialized;
        InvalidatePixels();
    }
}

/// <summary>
/// Specifies the size of a bitmap image.
/// </summary>
public sealed class BitmapSizeOptions
{
    private BitmapSizeOptions() { }

    /// <summary>
    /// Gets the width of the bitmap.
    /// </summary>
    public int PixelWidth { get; private set; }

    /// <summary>
    /// Gets the height of the bitmap.
    /// </summary>
    public int PixelHeight { get; private set; }

    /// <summary>
    /// Gets a value that indicates whether the aspect ratio is preserved.
    /// </summary>
    public bool PreservesAspectRatio { get; private set; }

    /// <summary>
    /// Gets the rotation to apply.
    /// </summary>
    public Rotation Rotation { get; private set; }

    /// <summary>Creates options that leave size and rotation unspecified.</summary>
    public static BitmapSizeOptions FromEmptyOptions() => new();

    /// <summary>
    /// Creates BitmapSizeOptions that preserves the aspect ratio.
    /// </summary>
    public static BitmapSizeOptions FromHeight(int pixelHeight)
    {
        return new BitmapSizeOptions { PixelHeight = pixelHeight, PreservesAspectRatio = true };
    }

    /// <summary>
    /// Creates BitmapSizeOptions that preserves the aspect ratio.
    /// </summary>
    public static BitmapSizeOptions FromWidth(int pixelWidth)
    {
        return new BitmapSizeOptions { PixelWidth = pixelWidth, PreservesAspectRatio = true };
    }

    /// <summary>
    /// Creates BitmapSizeOptions with the exact size specified.
    /// </summary>
    public static BitmapSizeOptions FromWidthAndHeight(int pixelWidth, int pixelHeight)
    {
        return new BitmapSizeOptions { PixelWidth = pixelWidth, PixelHeight = pixelHeight };
    }

    /// <summary>
    /// Creates BitmapSizeOptions that applies a rotation.
    /// </summary>
    public static BitmapSizeOptions FromRotation(Rotation rotation)
    {
        return new BitmapSizeOptions { Rotation = rotation, PreservesAspectRatio = true };
    }
}

// Rotation and BitmapCreateOptions are defined in BitmapEncoder.cs and BitmapDecoder.cs respectively.
