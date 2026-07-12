using System.ComponentModel;

namespace Jalium.UI.Media.Imaging;

/// <summary>Crops a <see cref="BitmapSource"/> to a specified pixel rectangle.</summary>
public sealed class CroppedBitmap : BitmapSource, ISupportInitialize
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(CroppedBitmap),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty SourceRectProperty =
        DependencyProperty.Register(nameof(SourceRect), typeof(Int32Rect), typeof(CroppedBitmap),
            new PropertyMetadata(Int32Rect.Empty));

    private bool _initializing;
    private bool _initialized;

    public CroppedBitmap()
    {
    }

    public CroppedBitmap(BitmapSource source, Int32Rect sourceRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        BeginInit();
        Source = source;
        SourceRect = sourceRect;
        EndInit();
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Int32Rect SourceRect
    {
        get => (Int32Rect)GetValue(SourceRectProperty)!;
        set => SetValue(SourceRectProperty, value);
    }

    public override int PixelWidth => GetEffectiveRect().Width;

    public override int PixelHeight => GetEffectiveRect().Height;

    public override double DpiX => Source?.DpiX ?? 96;

    public override double DpiY => Source?.DpiY ?? 96;

    public override PixelFormat Format => Source?.Format ?? PixelFormat.Pbgra32;

    public override BitmapPalette? Palette => Source?.Palette;

    public override double Width => PixelWidth * 96d / DpiX;

    public override double Height => PixelHeight * 96d / DpiY;

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

        _ = GetEffectiveRect();
        _initializing = false;
        _initialized = true;
    }

    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        BitmapSource source = Source ?? throw new InvalidOperationException("'Source' property is not set.");
        Int32Rect crop = GetEffectiveRect();
        Int32Rect relative = BitmapPixelOperations.NormalizeRect(sourceRect, crop.Width, crop.Height);
        BitmapPixelOperations.PixelBuffer buffer = BitmapPixelOperations.Read(source);
        var translated = new Int32Rect(crop.X + relative.X, crop.Y + relative.Y, relative.Width, relative.Height);
        BitmapPixelOperations.Copy(buffer.Pixels, buffer.Width, buffer.Height, buffer.Stride, buffer.Format,
            translated, pixels, stride, offset);
    }

    public new CroppedBitmap Clone() => (CroppedBitmap)base.Clone();

    public new CroppedBitmap CloneCurrentValue() => (CroppedBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new CroppedBitmap();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyInitializationState((CroppedBitmap)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyInitializationState((CroppedBitmap)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyInitializationState((CroppedBitmap)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyInitializationState((CroppedBitmap)source);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (CroppedBitmap)dependencyObject;
        bitmap.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, SourceProperty);
    }

    private Int32Rect GetEffectiveRect()
    {
        BitmapSource? source = Source;
        if (source is null)
        {
            return Int32Rect.Empty;
        }

        return BitmapPixelOperations.NormalizeRect(SourceRect, source.PixelWidth, source.PixelHeight);
    }

    private void CopyInitializationState(CroppedBitmap source)
    {
        _initializing = source._initializing;
        _initialized = source._initialized;
    }
}

/// <summary>Provides CPU pixel-format conversion for a <see cref="BitmapSource"/>.</summary>
public sealed class FormatConvertedBitmap : BitmapSource, ISupportInitialize
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(FormatConvertedBitmap),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty DestinationFormatProperty =
        DependencyProperty.Register(nameof(DestinationFormat), typeof(PixelFormat), typeof(FormatConvertedBitmap),
            new PropertyMetadata(PixelFormat.Pbgra32, OnConversionPropertyChanged));

    public static readonly DependencyProperty DestinationPaletteProperty =
        DependencyProperty.Register(nameof(DestinationPalette), typeof(BitmapPalette), typeof(FormatConvertedBitmap),
            new PropertyMetadata(null, OnConversionPropertyChanged));

    public static readonly DependencyProperty AlphaThresholdProperty =
        DependencyProperty.Register(nameof(AlphaThreshold), typeof(double), typeof(FormatConvertedBitmap),
            new PropertyMetadata(0d, OnConversionPropertyChanged));

    private bool _initializing;
    private bool _initialized;
    private byte[]? _convertedPixels;
    private int _convertedStride;

    public FormatConvertedBitmap()
    {
    }

    public FormatConvertedBitmap(
        BitmapSource source,
        PixelFormat destinationFormat,
        BitmapPalette? destinationPalette,
        double alphaThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        BeginInit();
        Source = source;
        DestinationFormat = destinationFormat;
        DestinationPalette = destinationPalette;
        AlphaThreshold = alphaThreshold;
        EndInit();
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public PixelFormat DestinationFormat
    {
        get => (PixelFormat)GetValue(DestinationFormatProperty)!;
        set => SetValue(DestinationFormatProperty, value);
    }

    public BitmapPalette? DestinationPalette
    {
        get => (BitmapPalette?)GetValue(DestinationPaletteProperty);
        set => SetValue(DestinationPaletteProperty, value);
    }

    public double AlphaThreshold
    {
        get => (double)GetValue(AlphaThresholdProperty)!;
        set => SetValue(AlphaThresholdProperty, value);
    }

    public override int PixelWidth => Source?.PixelWidth ?? 0;

    public override int PixelHeight => Source?.PixelHeight ?? 0;

    public override double DpiX => Source?.DpiX ?? 96;

    public override double DpiY => Source?.DpiY ?? 96;

    public override PixelFormat Format => DestinationFormat;

    public override BitmapPalette? Palette => IsIndexed(DestinationFormat) ? DestinationPalette : null;

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

        _ = DestinationFormat.BitsPerPixel;
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

    public new FormatConvertedBitmap Clone() => (FormatConvertedBitmap)base.Clone();

    public new FormatConvertedBitmap CloneCurrentValue() => (FormatConvertedBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new FormatConvertedBitmap();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyInitializationState((FormatConvertedBitmap)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyInitializationState((FormatConvertedBitmap)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyInitializationState((FormatConvertedBitmap)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyInitializationState((FormatConvertedBitmap)source);
    }

    protected override void OnChanged()
    {
        InvalidatePixels();
        base.OnChanged();
    }

    private static bool IsIndexed(PixelFormat format)
        => format == PixelFormat.Indexed1 || format == PixelFormat.Indexed2 ||
           format == PixelFormat.Indexed4 || format == PixelFormat.Indexed8;

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (FormatConvertedBitmap)dependencyObject;
        bitmap.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, SourceProperty);
        bitmap.InvalidatePixels();
    }

    private static void OnConversionPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        => ((FormatConvertedBitmap)dependencyObject).InvalidatePixels();

    private void EnsurePixels()
    {
        if (_convertedPixels is not null)
        {
            return;
        }

        BitmapSource source = Source ?? throw new InvalidOperationException("'Source' property is not set.");
        BitmapPixelOperations.PixelBuffer converted = BitmapPixelOperations.Convert(
            source, DestinationFormat, DestinationPalette, AlphaThreshold);
        _convertedPixels = converted.Pixels;
        _convertedStride = converted.Stride;
    }

    private void InvalidatePixels()
    {
        _convertedPixels = null;
        _convertedStride = 0;
    }

    private void CopyInitializationState(FormatConvertedBitmap source)
    {
        _initializing = source._initializing;
        _initialized = source._initialized;
        InvalidatePixels();
    }
}

/// <summary>Scales, rotates, translates, or otherwise applies an affine transform to a bitmap.</summary>
public sealed class TransformedBitmap : BitmapSource, ISupportInitialize
{
    private static readonly Transform s_identityTransform = Transform.Identity;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(TransformedBitmap),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(nameof(Transform), typeof(Transform), typeof(TransformedBitmap),
            new PropertyMetadata(s_identityTransform, OnTransformChanged));

    private bool _initializing;
    private bool _initialized;
    private byte[]? _transformedPixels;
    private int _transformedStride;
    private int _pixelWidth;
    private int _pixelHeight;

    public TransformedBitmap()
    {
        AttachTransform(Transform);
    }

    public TransformedBitmap(BitmapSource source, Transform transform)
        : this()
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);
        BeginInit();
        Source = source;
        Transform = transform;
        EndInit();
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Transform Transform
    {
        get => (Transform?)GetValue(TransformProperty) ?? s_identityTransform;
        set => SetValue(TransformProperty, value);
    }

    public override int PixelWidth
    {
        get
        {
            EnsurePixels();
            return _pixelWidth;
        }
    }

    public override int PixelHeight
    {
        get
        {
            EnsurePixels();
            return _pixelHeight;
        }
    }

    public override double DpiX => Source?.DpiX ?? 96;

    public override double DpiY => Source?.DpiY ?? 96;

    public override PixelFormat Format => Source?.Format ?? PixelFormat.Pbgra32;

    public override BitmapPalette? Palette => Source?.Palette;

    public override double Width => PixelWidth * 96d / DpiX;

    public override double Height => PixelHeight * 96d / DpiY;

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

        if (GetValue(TransformProperty) is null)
        {
            throw new InvalidOperationException("'Transform' property is not set.");
        }

        if (!Transform.Value.HasInverse)
        {
            throw new InvalidOperationException("The bitmap transform must be invertible.");
        }

        _initializing = false;
        _initialized = true;
        InvalidatePixels();
    }

    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        EnsurePixels();
        BitmapPixelOperations.Copy(_transformedPixels!, _pixelWidth, _pixelHeight, _transformedStride,
            Format, sourceRect, pixels, stride, offset);
    }

    public new TransformedBitmap Clone() => (TransformedBitmap)base.Clone();

    public new TransformedBitmap CloneCurrentValue() => (TransformedBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new TransformedBitmap();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyInitializationState((TransformedBitmap)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyInitializationState((TransformedBitmap)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyInitializationState((TransformedBitmap)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyInitializationState((TransformedBitmap)source);
    }

    protected override void OnChanged()
    {
        InvalidatePixels();
        base.OnChanged();
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (TransformedBitmap)dependencyObject;
        bitmap.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, SourceProperty);
        bitmap.InvalidatePixels();
    }

    private static void OnTransformChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (TransformedBitmap)dependencyObject;
        bitmap.DetachTransform(e.OldValue as Transform);
        bitmap.AttachTransform(e.NewValue as Transform);
        bitmap.InvalidatePixels();
    }

    private void AttachTransform(Transform? transform)
    {
        if (transform is not null)
        {
            transform.Changed += OnTransformSubPropertyChanged;
        }
    }

    private void DetachTransform(Transform? transform)
    {
        if (transform is not null)
        {
            transform.Changed -= OnTransformSubPropertyChanged;
        }
    }

    private void OnTransformSubPropertyChanged(object? sender, EventArgs e)
    {
        InvalidatePixels();
        WritePostscript();
    }

    private void EnsurePixels()
    {
        if (_transformedPixels is not null)
        {
            return;
        }

        BitmapSource source = Source ?? throw new InvalidOperationException("'Source' property is not set.");
        BitmapPixelOperations.PixelBuffer transformed = BitmapPixelOperations.Transform(source, Transform.Value);
        _transformedPixels = transformed.Pixels;
        _transformedStride = transformed.Stride;
        _pixelWidth = transformed.Width;
        _pixelHeight = transformed.Height;
    }

    private void InvalidatePixels()
    {
        _transformedPixels = null;
        _transformedStride = 0;
        _pixelWidth = 0;
        _pixelHeight = 0;
    }

    private void CopyInitializationState(TransformedBitmap source)
    {
        _initializing = source._initializing;
        _initialized = source._initialized;
        InvalidatePixels();
    }
}
