using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays an image.
/// Uses ControlTemplate for visual customization (border, corner radius, etc.)
/// and an internal ImageHost element for actual bitmap rendering.
/// </summary>
public class Image : Control, IReclaimableResource
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ImageAutomationPeer(this);
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

    private ImageHost? _imageHost;
    private Border? _container;

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

    #endregion

    public Image()
    {
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
            SetValue(CurrentZoomPropertyKey.DependencyProperty, clamped);
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
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _container = GetTemplateChild("PART_Container") as Border;

        if (_container != null)
        {
            _imageHost = new ImageHost { Owner = this };
            _container.Child = _imageHost;

            // Source may have been assigned BEFORE this template applied — e.g. via
            // a TemplateBinding from a parent control's template, or an object
            // initializer / code-behind that ran before the first layout pass. In
            // that case OnSourceChanged already fired, but its
            // `_imageHost?.InvalidateVisual()` was a no-op because the host did not
            // exist yet, and a synchronously-loaded BitmapImage never raises a later
            // OnImageLoaded to re-trigger a render. On a static page with no further
            // invalidation the image would then stay blank forever. Force the
            // freshly-created host to measure + render the already-present Source.
            if (Source != null)
            {
                _imageHost.InvalidateMeasure();
                _imageHost.InvalidateVisual();
            }
        }
    }

    #region Property Changed Callbacks

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            // Unsubscribe from old source's async load / frame events
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
            if (e.NewValue is BitmapImage newBitmap)
                newBitmap.OnImageLoaded += image.OnSourceAsyncLoaded;
            else if (e.NewValue is SvgImage newSvg)
                newSvg.OnSvgLoaded += image.OnSourceAsyncLoaded;
            else if (e.NewValue is AnimatedBitmap newAnim)
            {
                newAnim.FrameChanged += image.OnAnimatedFrameChanged;
                newAnim.LoadCompleted += image.OnSourceAsyncLoaded;
            }

            image._imageHost?.InvalidateMeasure();
            image._imageHost?.InvalidateVisual();
        }
    }

    private void OnAnimatedFrameChanged(object? sender, EventArgs e)
    {
        // Frame index changed on the UI thread (the AnimatedBitmap timer runs
        // on the dispatcher). Re-render only — the bitmap dimensions are
        // constant across frames, so measure doesn't need to be invalidated.
        _imageHost?.InvalidateVisual();
    }

    private void OnSourceAsyncLoaded(object? sender, EventArgs e)
    {
        _imageHost?.InvalidateMeasure();
        _imageHost?.InvalidateVisual();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            image._imageHost?.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Internal element that handles actual image bitmap rendering inside the Image control's template.
/// </summary>
internal sealed class ImageHost : FrameworkElement
{
    internal Image? Owner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var source = Owner?.Source;
        if (source == null || source.Width <= 0 || source.Height <= 0)
            return Size.Empty;

        var imageSize = new Size(source.Width, source.Height);
        return CalculateStretchSize(imageSize, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var source = Owner?.Source;
        if (source == null || source.Width <= 0 || source.Height <= 0)
            return;

        var imageSize = new Size(source.Width, source.Height);
        var stretchedSize = CalculateStretchSize(imageSize, RenderSize);

        // Center the image in the render area
        var x = (RenderSize.Width - stretchedSize.Width) / 2;
        var y = (RenderSize.Height - stretchedSize.Height) / 2;

        // Resolve scaling mode from the owner's RenderOptions attached property.
        // Image defaults to HighQuality (anisotropic + mipmap) so icons and photos
        // that are scaled away from their native pixel size stay sharp by default —
        // matches WPF's "high quality is the right default for UI" intent.
        var mode = Owner != null
            ? RenderOptions.GetBitmapScalingMode(Owner)
            : BitmapScalingMode.Unspecified;
        if (mode == BitmapScalingMode.Unspecified)
            mode = BitmapScalingMode.HighQuality;

        dc.DrawImage(source, new Rect(x, y, stretchedSize.Width, stretchedSize.Height), mode);
    }

    private Size CalculateStretchSize(Size imageSize, Size availableSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
            return Size.Empty;

        var stretch = Owner?.Stretch ?? Stretch.Uniform;
        var stretchDirection = Owner?.StretchDirection ?? StretchDirection.Both;

        var width = imageSize.Width;
        var height = imageSize.Height;
        var maxWidth = availableSize.Width;
        var maxHeight = availableSize.Height;

        // Handle explicit width/height on owner
        if (Owner != null)
        {
            if (!double.IsNaN(Owner.Width) && Owner.Width > 0)
                width = Owner.Width;
            if (!double.IsNaN(Owner.Height) && Owner.Height > 0)
                height = Owner.Height;
        }

        switch (stretch)
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
                var scale = Math.Min(scaleX, scaleY);
                scale = ApplyStretchDirection(scale, stretchDirection);
                width = imageSize.Width * scale;
                height = imageSize.Height * scale;
                break;
            }

            case Stretch.UniformToFill:
            {
                if (!double.IsInfinity(maxWidth) && !double.IsInfinity(maxHeight))
                {
                    var scaleX = maxWidth / imageSize.Width;
                    var scaleY = maxHeight / imageSize.Height;
                    var scale = Math.Max(scaleX, scaleY);
                    scale = ApplyStretchDirection(scale, stretchDirection);
                    width = imageSize.Width * scale;
                    height = imageSize.Height * scale;
                }
                break;
            }
        }

        return new Size(width, height);
    }

    private static double ApplyStretchDirection(double scale, StretchDirection direction)
    {
        return direction switch
        {
            StretchDirection.UpOnly => Math.Max(1.0, scale),
            StretchDirection.DownOnly => Math.Min(1.0, scale),
            _ => scale
        };
    }
}
