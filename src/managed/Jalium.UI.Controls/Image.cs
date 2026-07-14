using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Markup;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a framework element that displays an image.
/// </summary>
public class Image : FrameworkElement, IUriContext
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ImageAutomationPeer(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Forwards to the assigned <see cref="Source"/> when it implements
    /// <see cref="IReclaimableResource"/> (true for the built-in
    /// <see cref="BitmapImage"/>). For a <see cref="BitmapImage"/> source this
    /// drops the decoded BGRA8 pixel buffer and asks every active GPU bitmap
    /// cache to release its <c>NativeBitmap</c> upload, so both CPU and GPU
    /// memory shrink while the image is off-screen.
    /// </remarks>
    public void ReclaimIdleResources()
    {
        (Source as IReclaimableResource)?.ReclaimIdleResources();
    }

    private Uri? _baseUri;
    private bool _hasDpiChangedEverFired;

    /// <summary>Identifies the routed event raised when the image DPI changes.</summary>
    public static readonly RoutedEvent DpiChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(DpiChanged),
            RoutingStrategy.Bubble,
            typeof(DpiChangedEventHandler),
            typeof(Image));

    /// <summary>Identifies the routed event raised when loading or decoding the image fails.</summary>
    public static readonly RoutedEvent ImageFailedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ImageFailed),
            RoutingStrategy.Bubble,
            typeof(EventHandler<Jalium.UI.ExceptionRoutedEventArgs>),
            typeof(Image));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(Image),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(Image),
            new PropertyMetadata(Stretch.Uniform, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(Image),
            new PropertyMetadata(StretchDirection.Both, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets how the image is stretched to fill its allocated space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the direction to stretch the image.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    Uri? IUriContext.BaseUri
    {
        get => BaseUri;
        set => BaseUri = value;
    }

    /// <summary>Gets or sets the base URI used to resolve a relative image source.</summary>
    protected virtual Uri? BaseUri
    {
        get => _baseUri;
        set
        {
            if (Equals(_baseUri, value))
            {
                return;
            }

            _baseUri = value;
            ApplySourceBaseUri(Source);
        }
    }

    #endregion

    /// <summary>Occurs after the DPI used to render this image changes.</summary>
    public event DpiChangedEventHandler DpiChanged
    {
        add => AddHandler(DpiChangedEvent, value);
        remove => RemoveHandler(DpiChangedEvent, value);
    }

    /// <summary>Occurs when loading or decoding the current source fails.</summary>
    public event EventHandler<Jalium.UI.ExceptionRoutedEventArgs> ImageFailed
    {
        add => AddHandler(ImageFailedEvent, value);
        remove => RemoveHandler(ImageFailedEvent, value);
    }

    public Image()
    {
        ClipToBounds = true;
        AddHandler(ManipulationDeltaEvent, new RoutedEventHandler(OnManipulationDeltaHandler));
        AddHandler(ManipulationCompletedEvent, new RoutedEventHandler(OnManipulationCompletedHandler));
    }

    // ── Pinch-to-zoom / pan ─────────────────────────────────────────────

    /// <summary>Identifies the IsZoomEnabled dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsZoomEnabledProperty =
        DependencyProperty.Register(nameof(IsZoomEnabled), typeof(bool), typeof(Image),
            new PropertyMetadata(false, OnIsZoomEnabledChanged));

    /// <summary>True to allow pinch-to-zoom and single-finger pan via touch.</summary>
    public bool IsZoomEnabled
    {
        get => (bool)(GetValue(IsZoomEnabledProperty) ?? false);
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>Identifies the MinZoom dependency property.</summary>
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(Image), new PropertyMetadata(1.0));
    public double MinZoom { get => (double)GetValue(MinZoomProperty)!; set => SetValue(MinZoomProperty, value); }

    /// <summary>Identifies the MaxZoom dependency property.</summary>
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(Image), new PropertyMetadata(10.0));
    public double MaxZoom { get => (double)GetValue(MaxZoomProperty)!; set => SetValue(MaxZoomProperty, value); }

    private static readonly DependencyPropertyKey CurrentZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CurrentZoom), typeof(double), typeof(Image),
            new PropertyMetadata(1.0));

    /// <summary>Identifies the CurrentZoom read-only dependency property.</summary>
    public static readonly DependencyProperty CurrentZoomProperty = CurrentZoomPropertyKey.DependencyProperty;

    /// <summary>The current cumulative zoom factor (read-only).</summary>
    public double CurrentZoom => (double)GetValue(CurrentZoomProperty)!;

    private static void OnIsZoomEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image img && e.NewValue is bool enabled)
        {
            img.IsManipulationEnabled = enabled;
        }
    }

    private void OnManipulationDeltaHandler(object sender, RoutedEventArgs e)
    {
        if (!IsZoomEnabled || e is not ManipulationDeltaEventArgs args || args.DeltaManipulation == null) return;

        double newZoom = CurrentZoom * args.DeltaManipulation.Scale.X;
        double clamped = Math.Clamp(newZoom, Math.Max(0.0001, MinZoom), Math.Max(MinZoom, MaxZoom));
        if (Math.Abs(clamped - CurrentZoom) > 0.0001)
        {
            SetValue(CurrentZoomPropertyKey, clamped);
        }

        // Boundary feedback if the user is trying to zoom past Min/Max.
        if (Math.Abs(clamped - newZoom) > 0.0001)
        {
            args.ReportBoundaryFeedback(new Jalium.UI.Input.ManipulationDelta
            {
                Scale = new Vector(newZoom - clamped, newZoom - clamped)
            });
        }

        // Apply via RenderTransform — a CompositeTransform combining scale+translate.
        var rt = (RenderTransform as ScaleTransform) ?? new ScaleTransform();
        rt.ScaleX = CurrentZoom;
        rt.ScaleY = CurrentZoom;
        RenderTransform = rt;
        e.Handled = true;
    }

    private void OnManipulationCompletedHandler(object sender, RoutedEventArgs e)
    {
        // Inertia for image pan handled automatically by ManipulationInertiaProcessor.
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _hasDpiChangedEverFired = true;
        InvalidateMeasure();
        InvalidateVisual();
        RaiseEvent(new DpiChangedEventArgs(oldDpi, newDpi)
        {
            RoutedEvent = DpiChangedEvent,
            Source = this
        });
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_hasDpiChangedEverFired)
        {
            var scale = FrameworkElement.LayoutDpiScale;
            if (!(scale > 0) || !double.IsFinite(scale))
            {
                scale = 1.0;
            }

            var dpi = new DpiScale(scale, scale);
            OnDpiChanged(dpi, dpi);
        }

        return TryGetSourceSize(out _, out var imageSize)
            ? CalculateStretchSize(imageSize, availableSize)
            : default;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!TryGetSourceSize(out var source, out var imageSize) || source == null)
            return;

        var stretchedSize = CalculateStretchSize(imageSize, RenderSize);
        var x = (RenderSize.Width - stretchedSize.Width) / 2;
        var y = (RenderSize.Height - stretchedSize.Height) / 2;
        var mode = RenderOptions.GetBitmapScalingMode(this);
        if (mode == BitmapScalingMode.Unspecified)
            mode = BitmapScalingMode.HighQuality;

        drawingContext.DrawImage(
            source,
            new Rect(x, y, stretchedSize.Width, stretchedSize.Height),
            mode);
    }

    #region Property Changed Callbacks

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            // Unsubscribe from old source's async load / frame events
            if (e.OldValue is ImageSource oldSource)
            {
                oldSource.LoadFailed -= image.OnSourceLoadFailed;
            }

            if (e.OldValue is BitmapImage oldBitmap)
                oldBitmap.OnImageLoaded -= image.OnSourceAsyncLoaded;
            else if (e.OldValue is SvgImage oldSvg)
                oldSvg.OnSvgLoaded -= image.OnSourceAsyncLoaded;
            else if (e.OldValue is AnimatedBitmap oldAnim)
            {
                oldAnim.FrameChanged -= image.OnAnimatedFrameChanged;
                oldAnim.LoadCompleted -= image.OnSourceAsyncLoaded;
            }

            // Subscribe to new source's async load / frame events
            if (e.NewValue is ImageSource newSource)
            {
                newSource.LoadFailed += image.OnSourceLoadFailed;
                image.ApplySourceBaseUri(newSource);
                if (!ReferenceEquals(image.Source, newSource))
                {
                    return;
                }

                if (newSource.LoadFailure is { } failure)
                {
                    image.OnSourceLoadFailed(newSource, failure);
                    return;
                }
            }

            image.InvalidateMeasure();
            image.InvalidateVisual();
        }
    }

    private void ApplySourceBaseUri(ImageSource? source)
    {
        if (source is IUriContext { BaseUri: null } context && BaseUri != null)
        {
            context.BaseUri = BaseUri;
        }
    }

    private void OnSourceLoadFailed(ImageSource source, Exception exception)
    {
        if (!ReferenceEquals(Source, source))
        {
            return;
        }

        SetCurrentValue(SourceProperty, null);
        RaiseEvent(new Jalium.UI.ExceptionRoutedEventArgs(ImageFailedEvent, this, exception));
    }

    internal bool TryGetSourceSize(out ImageSource? source, out Size imageSize)
    {
        source = Source;
        imageSize = default;
        if (source == null)
        {
            return false;
        }

        try
        {
            ApplySourceBaseUri(source);
            var width = source.Width;
            var height = source.Height;
            if (!(width > 0) || !(height > 0))
            {
                return false;
            }

            imageSize = new Size(width, height);
            return true;
        }
        catch (Exception ex)
        {
            OnSourceLoadFailed(source, ex);
            source = null;
            return false;
        }
    }

    private void OnAnimatedFrameChanged(object? sender, EventArgs e)
    {
        // Frame index changed on the UI thread (the AnimatedBitmap timer runs
        // on the dispatcher). Re-render only — the bitmap dimensions are
        // constant across frames, so measure doesn't need to be invalidated.
        InvalidateVisual();
    }

    private void OnSourceAsyncLoaded(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            image.InvalidateMeasure();
            image.InvalidateVisual();
        }
    }

    private Size CalculateStretchSize(Size imageSize, Size availableSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
            return default;

        var width = imageSize.Width;
        var height = imageSize.Height;
        var maxWidth = availableSize.Width;
        var maxHeight = availableSize.Height;

        if (!double.IsNaN(Width) && Width > 0)
            width = Width;
        if (!double.IsNaN(Height) && Height > 0)
            height = Height;

        switch (Stretch)
        {
            case Stretch.None:
                break;
            case Stretch.Fill:
                if (!double.IsInfinity(maxWidth))
                    width = maxWidth;
                if (!double.IsInfinity(maxHeight))
                    height = maxHeight;
                break;
            case Stretch.Uniform:
            {
                var scaleX = double.IsInfinity(maxWidth) ? double.MaxValue : maxWidth / imageSize.Width;
                var scaleY = double.IsInfinity(maxHeight) ? double.MaxValue : maxHeight / imageSize.Height;
                var scale = ApplyStretchDirection(Math.Min(scaleX, scaleY), StretchDirection);
                width = imageSize.Width * scale;
                height = imageSize.Height * scale;
                break;
            }
            case Stretch.UniformToFill:
            {
                if (!double.IsInfinity(maxWidth) && !double.IsInfinity(maxHeight))
                {
                    var scale = ApplyStretchDirection(
                        Math.Max(maxWidth / imageSize.Width, maxHeight / imageSize.Height),
                        StretchDirection);
                    width = imageSize.Width * scale;
                    height = imageSize.Height * scale;
                }
                break;
            }
        }

        return new Size(width, height);
    }

    private static double ApplyStretchDirection(double scale, StretchDirection direction) =>
        direction switch
        {
            StretchDirection.UpOnly => Math.Max(1.0, scale),
            StretchDirection.DownOnly => Math.Min(1.0, scale),
            _ => scale,
        };

    #endregion
}
