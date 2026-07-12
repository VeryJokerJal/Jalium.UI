using System.ComponentModel;
using System.Net.Cache;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// WPF-compatible bitmap image facade over Jalium's native-decoder-backed bitmap implementation.
/// </summary>
public sealed partial class BitmapImage : Jalium.UI.Media.BitmapImage, ISupportInitialize, IUriContext
{
    public static readonly DependencyProperty CacheOptionProperty =
        DependencyProperty.Register(nameof(CacheOption), typeof(BitmapCacheOption), typeof(BitmapImage),
            new PropertyMetadata(BitmapCacheOption.Default));

    public static readonly DependencyProperty CreateOptionsProperty =
        DependencyProperty.Register(nameof(CreateOptions), typeof(BitmapCreateOptions), typeof(BitmapImage),
            new PropertyMetadata(BitmapCreateOptions.None));

    public static readonly DependencyProperty DecodePixelHeightProperty =
        DependencyProperty.Register(nameof(DecodePixelHeight), typeof(int), typeof(BitmapImage),
            new PropertyMetadata(0));

    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.Register(nameof(DecodePixelWidth), typeof(int), typeof(BitmapImage),
            new PropertyMetadata(0));

    public static readonly DependencyProperty RotationProperty =
        DependencyProperty.Register(nameof(Rotation), typeof(Rotation), typeof(BitmapImage),
            new PropertyMetadata(Rotation.Rotate0));

    public static readonly DependencyProperty SourceRectProperty =
        DependencyProperty.Register(nameof(SourceRect), typeof(Int32Rect), typeof(BitmapImage),
            new PropertyMetadata(Int32Rect.Empty));

    public static readonly DependencyProperty StreamSourceProperty =
        DependencyProperty.Register(nameof(StreamSource), typeof(Stream), typeof(BitmapImage),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty UriCachePolicyProperty =
        DependencyProperty.Register(nameof(UriCachePolicy), typeof(RequestCachePolicy), typeof(BitmapImage),
            new PropertyMetadata(null));

    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(nameof(UriSource), typeof(Uri), typeof(BitmapImage),
            new PropertyMetadata(null, OnSourceChanged));

    private bool _initializing;
    private bool _initialized;
    private bool _applyingSource;
    private Uri? _baseUri;

    /// <summary>Initializes an empty bitmap image.</summary>
    public BitmapImage()
    {
        OnImageLoaded += HandleImageLoaded;
    }

    /// <summary>Initializes a bitmap image from a URI.</summary>
    public BitmapImage(Uri uriSource)
        : this(uriSource, null)
    {
    }

    /// <summary>Initializes a bitmap image from a URI and cache policy.</summary>
    public BitmapImage(Uri uriSource, RequestCachePolicy? uriCachePolicy)
    {
        ArgumentNullException.ThrowIfNull(uriSource);
        BeginInit();
        UriCachePolicy = uriCachePolicy;
        UriSource = uriSource;
        EndInit();
    }

    /// <summary>Gets or sets the base URI used for relative sources.</summary>
    public Uri? BaseUri
    {
        get => _baseUri;
        set
        {
            _baseUri = value;
            BaseUriCore = value;
        }
    }

    /// <summary>Gets or sets the bitmap cache mode.</summary>
    public BitmapCacheOption CacheOption
    {
        get => (BitmapCacheOption)GetValue(CacheOptionProperty)!;
        set => SetValue(CacheOptionProperty, value);
    }

    /// <summary>Gets or sets bitmap creation options.</summary>
    public BitmapCreateOptions CreateOptions
    {
        get => (BitmapCreateOptions)GetValue(CreateOptionsProperty)!;
        set => SetValue(CreateOptionsProperty, value);
    }

    /// <summary>Gets or sets the requested decoded height.</summary>
    public int DecodePixelHeight
    {
        get => (int)GetValue(DecodePixelHeightProperty)!;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            SetValue(DecodePixelHeightProperty, value);
        }
    }

    /// <summary>Gets or sets the requested decoded width.</summary>
    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty)!;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            SetValue(DecodePixelWidthProperty, value);
        }
    }

    /// <inheritdoc />
    public override bool IsDownloading => base.IsDownloading;

    /// <inheritdoc />
    public override ImageMetadata? Metadata => base.Metadata;

    /// <summary>Gets or sets rotation applied after decoding.</summary>
    public Rotation Rotation
    {
        get => (Rotation)GetValue(RotationProperty)!;
        set => SetValue(RotationProperty, value);
    }

    /// <summary>Gets or sets the source pixel rectangle.</summary>
    public Int32Rect SourceRect
    {
        get => (Int32Rect)GetValue(SourceRectProperty)!;
        set => SetValue(SourceRectProperty, value);
    }

    /// <summary>Gets or sets the encoded image stream.</summary>
    public Stream? StreamSource
    {
        get => (Stream?)GetValue(StreamSourceProperty);
        set => SetValue(StreamSourceProperty, value);
    }

    /// <summary>Gets or sets the URI cache policy.</summary>
    public RequestCachePolicy? UriCachePolicy
    {
        get => (RequestCachePolicy?)GetValue(UriCachePolicyProperty);
        set => SetValue(UriCachePolicyProperty, value);
    }

    /// <summary>Gets or sets the encoded image URI.</summary>
    public new Uri? UriSource
    {
        get => (Uri?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>Begins batched initialization.</summary>
    public void BeginInit()
    {
        WritePreamble();
        if (_initializing || _initialized)
        {
            throw new InvalidOperationException("BitmapImage initialization has already started.");
        }

        _initializing = true;
    }

    /// <summary>Ends initialization and decodes the configured source.</summary>
    public void EndInit()
    {
        WritePreamble();
        if (!_initializing)
        {
            throw new InvalidOperationException("BeginInit must be called before EndInit.");
        }

        if (StreamSource is not null && UriSource is not null)
        {
            throw new InvalidOperationException("StreamSource and UriSource cannot both be set.");
        }

        _initializing = false;
        _initialized = true;
        ApplySource();
    }

    /// <summary>Creates a modifiable copy.</summary>
    public new BitmapImage Clone() => (BitmapImage)base.Clone();

    /// <summary>Creates a modifiable copy with current values.</summary>
    public new BitmapImage CloneCurrentValue() => (BitmapImage)base.CloneCurrentValue();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new BitmapImage();

    /// <inheritdoc />
    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFacadeState((BitmapImage)source);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFacadeState((BitmapImage)source);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFacadeState((BitmapImage)source);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFacadeState((BitmapImage)source);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var bitmap = (BitmapImage)dependencyObject;
        if (!bitmap._initializing && bitmap._initialized)
        {
            bitmap.ApplySource();
        }
    }

    private void ApplySource()
    {
        if (_applyingSource)
        {
            return;
        }

        _applyingSource = true;
        try
        {
            BaseUriCore = _baseUri;
            if (StreamSource is { } stream)
            {
                LoadFromStreamSource(stream);
            }
            else if (UriSource is { } uri)
            {
                base.UriSource = uri;
            }
            else
            {
                return;
            }

            if (!IsDownloading)
            {
                ApplyDecodeOptions(SourceRect, DecodePixelWidth, DecodePixelHeight, Rotation);
            }
        }
        finally
        {
            _applyingSource = false;
        }
    }

    private void CopyFacadeState(BitmapImage source)
    {
        _initializing = false;
        _initialized = source._initialized;
        _baseUri = source._baseUri;
        CopyBitmapStateFrom(source);
    }

    private void HandleImageLoaded(object? sender, EventArgs e)
    {
        if (_initialized && !_initializing && !_applyingSource)
        {
            ApplyDecodeOptions(SourceRect, DecodePixelWidth, DecodePixelHeight, Rotation);
        }
    }
}
