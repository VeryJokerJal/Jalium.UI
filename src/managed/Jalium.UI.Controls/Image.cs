using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Markup;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a framework element that displays an image.
/// </summary>
public class Image : FrameworkElement, IUriContext
{
    private sealed class SourceFailureListener
    {
        private readonly WeakReference<Image> _owner;

        public SourceFailureListener(Image owner)
        {
            _owner = new WeakReference<Image>(owner);
        }

        public void OnLoadFailed(ImageSource source, Exception exception)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnSourceLoadFailed(source, exception);
            }
            else
            {
                source.LoadFailed -= OnLoadFailed;
            }
        }
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ImageAutomationPeer(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>Forwards to the assigned <see cref="Source"/> when it implements
    /// <see cref="IReclaimableResource"/> (true for the built-in
    /// <see cref="BitmapImage"/>). For a <see cref="BitmapImage"/> source this
    /// drops the decoded BGRA8 pixel buffer and asks every active GPU bitmap
    /// cache to release its <c>NativeBitmap</c> upload, so both CPU and GPU
    /// memory shrink while the image is off-screen.</para>
    /// <para>Unless somebody else is still drawing that source. The reclaimer's idle test is per
    /// ELEMENT, but the resource being freed belongs to a SHARED <see cref="ImageSource"/>: one
    /// bitmap routinely backs several elements at once, and non-element consumers (an image brush,
    /// an image drawing) hold no element the reclaimer can even see. Forwarding unconditionally
    /// therefore let an element that had been off-screen for the idle window free the pixels of an
    /// image another element was painting on that very frame — which reads to the user as the live
    /// image blanking for the length of one re-decode and coming back, repeatedly, for as long as
    /// anything keeps producing frames. It is self-sustaining, too: the reclaim clears the
    /// published decode state, so the live consumer's next draw re-requests a full decode, which
    /// republishes, which the next scan reclaims again.</para>
    /// <para>The test is a comparison, not a timeout, so it needs no knowledge of the reclaimer's
    /// idle window: this element has been idle since <see cref="Visual.LastRenderedTickMs"/>, so a
    /// source drawn AFTER that instant was drawn by somebody else and must be left alone. A source
    /// only this element draws stamps the two together on its last frame, the comparison is false,
    /// and reclamation proceeds exactly as before.</para>
    /// </remarks>
    public void ReclaimIdleResources()
    {
        if (Source is not { } source)
            return;

        if (source.LastDrawnTickMs > LastRenderedTickMs)
            return;

        (source as IReclaimableResource)?.ReclaimIdleResources();
    }

    private Uri? _baseUri;
    private bool _hasDpiChangedEverFired;
    private ImageSource? _failureEventSource;
    private SourceFailureListener? _failureEventListener;

    /// <summary>
    /// The source instance that most recently reported a failure and has not since recovered.
    /// </summary>
    /// <remarks>
    /// This replaces the <c>SetCurrentValue(SourceProperty, null)</c> that <see cref="OnSourceLoadFailed"/>
    /// used to perform. Nulling the DP made a transient failure permanent and lossy: it destroyed
    /// the application's own value (and, with a one-way binding, the only handle it had on the
    /// image), it made every later recovery unreachable because <see cref="TryGetSourceSize"/>
    /// returns false for a null source and <see cref="OnRender"/> bails before drawing anything,
    /// and it does not match WPF, which never rewrites <c>Image.Source</c> on a decode failure.
    /// A private, reference-compared marker gets the same "do not paint the broken thing" result
    /// without touching any public state.
    /// </remarks>
    private ImageSource? _suppressedFailedSource;

    /// <summary>
    /// Intrinsic source size observed at the previous async-load notification, and whether one has
    /// been observed at all. Used to tell a first publication (layout input appeared) apart from a
    /// pure raster upgrade (layout input unchanged) so only the former invalidates measure.
    /// </summary>
    private Size _lastNotifiedSourceSize;
    private bool _hasNotifiedSourceSize;

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
        Loaded += OnImageLoaded;
        Unloaded += OnImageUnloaded;
    }

    private void OnImageLoaded(object? sender, RoutedEventArgs e)
    {
        if (Source is not { } source)
        {
            return;
        }

        AttachFailureListener(source);
        if (source.LoadFailure is { } failure)
        {
            // Re-reported on load so a handler attached after the failure still sees it. It no
            // longer short-circuits the invalidation below: Source survives a failure now, so
            // layout still owes this element a pass, and a source that recovers later needs the
            // element already measuring and rendering to show the result.
            OnSourceLoadFailed(source, failure);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnImageUnloaded(object? sender, RoutedEventArgs e) =>
        DetachFailureListener();

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
    /// <remarks>
    /// Registers the prefetch hint as soon as the element's rendered size is known, which is one
    /// layout pass BEFORE the first <see cref="OnRender"/>. Asking from inside OnRender only is
    /// what made the very first frame of an image always miss: the request went in and the decode
    /// started while that same frame was already drawing nothing.
    /// </remarks>
    protected internal override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RequestBitmapDecode(RenderSize);
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

        // Ask with whatever measure knows. A finite slot is an upper bound on the rendered size,
        // so it is a real hint — and it is a far better FIRST hint than the (0,0) an unarranged
        // element reports from OnRender, which the decoder can only read as "unbounded" and
        // answer with a full-resolution decode that the bucket ladder, being growth-only, can
        // never walk back. RequestBitmapDecode is idempotent and only enqueues work when a bigger
        // bucket is actually reachable, so calling it on every measure costs a lock acquisition.
        RequestBitmapDecode(availableSize);
        if (!TryGetSourceSize(out _, out var imageSize)) return default;
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - insets.Left - insets.Right),
            Math.Max(0, availableSize.Height - insets.Top - insets.Bottom));
        if (EffectiveCssObjectFit() is not CssObjectFit.Unspecified)
        {
            // object-fit sizes the content, not the replaced element's own box.
            return new Size(imageSize.Width + insets.Left + insets.Right,
                imageSize.Height + insets.Top + insets.Bottom);
        }
        var measured = CalculateStretchSize(imageSize, contentAvailable);
        return new Size(measured.Width + insets.Left + insets.Right,
            measured.Height + insets.Top + insets.Bottom);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        RequestBitmapDecode(RenderSize);
        if (!TryGetSourceSize(out var source, out var imageSize) || source == null)
            return;

        if (IsSourceSuppressedByFailure())
        {
            // The assigned source failed to LOAD and has not recovered. Skipping the draw is a
            // rendering decision and nothing more — Source, and any binding to it, are untouched —
            // so when a later load clears the source's own failure the check above stops holding
            // and the image reappears with no action from the application. This is also the hook a
            // visible failure placeholder replaces.
            //
            // A GPU upload failure does NOT reach here; see IsSourceSuppressedByFailure. Returning
            // early for one would remove the retry that clears it, because the retry IS the
            // DrawImage below.
            return;
        }

        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? RenderSize.Width);
        var contentWidth = Math.Max(0, RenderSize.Width - insets.Left - insets.Right);
        var contentHeight = Math.Max(0, RenderSize.Height - insets.Top - insets.Bottom);
        if (contentWidth <= 0 || contentHeight <= 0) return;
        var content = new Rect(insets.Left, insets.Top, contentWidth, contentHeight);
        var fit = EffectiveCssObjectFit();
        var stretchedSize = fit == CssObjectFit.Unspecified
            ? CalculateStretchSize(imageSize, content.Size)
            : CssImageObjectProperties.ConcreteSize(imageSize, content.Size, fit);
        if (stretchedSize.Width <= 0 || stretchedSize.Height <= 0) return;
        var drawn = CssImageObjectProperties.PositionedRect(this, content, stretchedSize);
        if (drawn.IsEmpty) return;
        var mode = RenderOptions.GetBitmapScalingMode(this);
        if (mode == BitmapScalingMode.Unspecified)
            mode = CssImageRenderingProperties.Resolve(this, BitmapScalingMode.HighQuality);

        // Replaced content clips to its content box even when overflow:visible
        // or a native ClipToBounds value leaves the element box unclipped.
        var clipContent = CssImageObjectProperties.NeedsContentClip(this, fit, content, RenderSize);
        if (clipContent) drawingContext.PushClip(new RectangleGeometry(content));
        drawingContext.DrawImage(source, drawn, mode);
        if (clipContent) drawingContext.Pop();
    }

    #region Property Changed Callbacks

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            // A new value is a clean slate: the previous source's failure must never suppress the
            // one replacing it. Cleared before the callback can re-report against the NEW source
            // further down.
            image._suppressedFailedSource = null;

            // Likewise the intrinsic size remembered for the previous source: carrying it over
            // would let a new source whose size happens to match the old one skip the measure that
            // its FIRST publication owes.
            image._hasNotifiedSourceSize = false;
            image._lastNotifiedSourceSize = default;

            // Unsubscribe from old source's async load / frame events
            if (e.OldValue is ImageSource oldSource)
            {
                image.DetachFailureListener(oldSource);
            }

            if (e.OldValue is BitmapImage oldBitmap)
            {
                oldBitmap.OnImageLoaded -= image.OnSourceAsyncLoaded;
                oldBitmap.MetadataResolved -= image.OnSourceAsyncLoaded;
            }
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
                image.AttachFailureListener(newSource);
                image.ApplySourceBaseUri(newSource);
                if (!ReferenceEquals(image.Source, newSource))
                {
                    return;
                }

                if (newSource is BitmapImage newBitmap)
                {
                    newBitmap.OnImageLoaded += image.OnSourceAsyncLoaded;

                    // The header probe's answer arrives on its own channel — see
                    // BitmapImage.MetadataResolved for why it is not folded into OnImageLoaded.
                    // The same handler is right for both: it re-reads the intrinsic size and only
                    // invalidates measure when that size actually changed, so a probe result
                    // followed by the decode that confirms it costs one layout pass, not two.
                    newBitmap.MetadataResolved += image.OnSourceAsyncLoaded;
                }
                else if (newSource is SvgImage newSvg)
                    newSvg.OnSvgLoaded += image.OnSourceAsyncLoaded;
                else if (newSource is AnimatedBitmap newAnim)
                {
                    newAnim.FrameChanged += image.OnAnimatedFrameChanged;
                    newAnim.LoadCompleted += image.OnSourceAsyncLoaded;
                }

                // Reported AFTER the load subscription, deliberately. This used to report and then
                // `return`, which was harmless only because reporting nulled Source and re-entered
                // this callback. Now that Source survives a failure, skipping the subscription
                // would leave the element deaf to the very decode that would clear the failure —
                // turning a recoverable state into a permanently blank one.
                if (newSource.LoadFailure is { } failure)
                {
                    image.OnSourceLoadFailed(newSource, failure);
                }
            }

            image.InvalidateMeasure();
            image.InvalidateVisual();
        }
    }

    private void AttachFailureListener(ImageSource source)
    {
        if (ReferenceEquals(_failureEventSource, source) && _failureEventListener != null)
        {
            return;
        }

        DetachFailureListener();
        var listener = new SourceFailureListener(this);
        _failureEventSource = source;
        _failureEventListener = listener;
        source.LoadFailed += listener.OnLoadFailed;
    }

    private void DetachFailureListener(ImageSource? expectedSource = null)
    {
        var source = _failureEventSource;
        var listener = _failureEventListener;
        if (source == null || listener == null ||
            (expectedSource != null && !ReferenceEquals(source, expectedSource)))
        {
            return;
        }

        source.LoadFailed -= listener.OnLoadFailed;
        _failureEventSource = null;
        _failureEventListener = null;
    }

    private void ApplySourceBaseUri(ImageSource? source)
    {
        if (source is IUriContext { BaseUri: null } context && BaseUri != null)
        {
            context.BaseUri = BaseUri;
        }
    }

    /// <summary>
    /// Whether the assigned <see cref="Source"/> is the instance that reported a LOAD failure and
    /// has not recovered since — the render path's signal to skip drawing it.
    /// </summary>
    /// <remarks>
    /// <para>Three things keep this from becoming a sticky blank image, which is the failure mode
    /// that would simply move the old <c>Source = null</c> bug behind a private field. It is
    /// compared by reference against the live <see cref="Source"/>, so a different source can never
    /// inherit the previous one's failure state; it clears as soon as the source drops its own
    /// latched failure; and it ignores an <see cref="ImageSourceFailureKind.Upload"/> failure
    /// entirely. The second point is load-bearing because not every source type raises a load event
    /// this element subscribes to — a custom <see cref="ImageSource"/> raises none at all — so
    /// <see cref="OnSourceAsyncLoaded"/> cannot be the only place recovery is noticed.</para>
    /// <para>The third is the one that is easy to get wrong, because the gate looks harmless. A GPU
    /// upload failure — VRAM pressure, a device-removed frame, a texture dimension over the
    /// adapter's limit on a WARP or Remote Desktop adapter — is cleared by exactly one thing:
    /// uploading again on the next frame. The upload is attempted from inside
    /// <c>DrawImage</c>, so suppressing the draw removes the retry, and the latch that the retry
    /// would have released is then held forever. That turns a single unlucky frame on one machine
    /// into an element that is blank for the life of the window — the reported symptom, reproduced
    /// through the failure path. Upload failures are still fully reported
    /// (<c>ImageDiagnostics.UploadFailed</c> every time, <see cref="ImageFailed"/> once per
    /// episode); they simply never stop the paint.</para>
    /// <para>The classification is read here rather than at the point the marker is written, so
    /// this stays the single place the decision is made. A source that suffers an upload failure
    /// and LATER fails to load re-reports through <see cref="OnSourceLoadFailed"/> with the new
    /// class, and this call then suppresses it.</para>
    /// </remarks>
    private bool IsSourceSuppressedByFailure()
    {
        if (_suppressedFailedSource is not { } suppressed)
        {
            return false;
        }

        if (!ReferenceEquals(suppressed, Source) ||
            !suppressed.TryGetLoadFailure(out _, out var kind))
        {
            _suppressedFailedSource = null;
            return false;
        }

        return kind != ImageSourceFailureKind.Upload;
    }

    private void OnSourceLoadFailed(ImageSource source, Exception exception)
    {
        if (!ReferenceEquals(Source, source))
        {
            return;
        }

        // Reporting a failure never mutates a public dependency property. WPF does not clear
        // Image.Source when a decode fails, and clearing it here destroyed the application's value
        // — with a one-way binding, irrecoverably — while making graceful degradation impossible,
        // because every later frame then saw a null source and returned before drawing anything.
        // The suppression marker below is private state that the render path consults; the source
        // itself is left exactly as the application set it. Whether the marker actually suppresses
        // anything depends on the failure's CLASS, which IsSourceSuppressedByFailure reads live —
        // an upload failure records and reports but never stops the paint.
        _suppressedFailedSource = source;

        // Live in Release. The routed event is an APPLICATION channel that most apps never handle,
        // so without this a failed image is invisible to support: the pipeline's own counters must
        // record it too. Reported before the raise so an application handler that throws cannot
        // cost the record. The stage names the class, because a support capture that shows "load"
        // for a GPU upload rejection sends the engineer looking for a missing asset.
        var isUpload =
            source.TryGetLoadFailure(out var latched, out var kind) &&
            ReferenceEquals(latched, exception) &&
            kind == ImageSourceFailureKind.Upload;

        Jalium.UI.Diagnostics.ImageDiagnostics.DecodeFailed(
            DescribeSourceForDiagnostics(source),
            isUpload ? "Image.Source GPU upload" : "Image.Source load",
            exception);

        RaiseEvent(new Jalium.UI.ExceptionRoutedEventArgs(ImageFailedEvent, this, exception));
    }

    /// <summary>
    /// Stable identity for a source in diagnostics records. Deliberately does not touch
    /// <see cref="ImageSource.Width"/>/<see cref="ImageSource.Height"/>: this runs on a path that
    /// is already handling an exception those very members may have thrown.
    /// </summary>
    private static string DescribeSourceForDiagnostics(ImageSource source) =>
        source switch
        {
            BitmapImage bitmap => bitmap.DiagnosticSourceName,
            SvgImage { UriSource: { } uri } => uri.ToString(),
            _ => source.GetType().Name,
        };

    /// <summary>
    /// The element's intrinsic size: the only sizing input <see cref="MeasureOverride"/> and
    /// <see cref="OnRender"/>'s <c>CalculateStretchSize</c> have.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ImageSource.Width"/>/<see cref="ImageSource.Height"/> must therefore be
    /// INVARIANT across the display buckets a deferred <c>BitmapImage</c> climbs through, and
    /// <c>BitmapImage</c> guarantees that by reporting its canonical decoded size here rather than
    /// the bucket currently resident. The relationship is a cycle and the invariance is what breaks
    /// it: this size becomes the draw rect (exactly so, for <c>Stretch.None</c>), the renderer
    /// turns the draw rect into the next decode hint, and the decode publishes the next bucket. A
    /// bucket-valued intrinsic size therefore grew itself, one full native decode and one full
    /// measure/arrange pass at a time, and made the measure-skip in
    /// <see cref="OnSourceAsyncLoaded"/> unreachable because the reported size changed on every
    /// publish.</para>
    /// <para>Read through the general <see cref="ImageSource"/> members rather than through a
    /// <c>BitmapImage</c>-specific one so <c>SvgImage</c>, <c>AnimatedBitmap</c>,
    /// <c>WriteableBitmap</c> and any application-defined source obey the same contract. The
    /// header-probe fallback below is the single exception, and only because
    /// <c>BitmapImage</c> is the only source type whose size arrives asynchronously.</para>
    /// </remarks>
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
                return TryGetPreDecodeSourceSize(source, out imageSize);
            }

            imageSize = new Size(width, height);
            return true;
        }
        catch (Exception ex)
        {
            // Report, but do not rewrite the caller's view of the source. Nulling the out parameter
            // here was the same mistake OnSourceLoadFailed used to make with the dependency
            // property: it erases the only handle the caller has on the image, so no later frame
            // can degrade gracefully or recover. The `false` return already says "no usable size".
            OnSourceLoadFailed(source, ex);
            imageSize = default;
            return false;
        }
    }

    /// <summary>
    /// The intrinsic size of a URI-backed <see cref="BitmapImage"/> that has not published any
    /// pixels yet, taken from the decoder's header probe, and the request that arms that probe.
    /// </summary>
    /// <remarks>
    /// <para>Without this the first measure of every URI-backed image returns <c>default</c>, so
    /// image-sized content lays out at zero and reflows once the decode lands — a visible jump on
    /// any page whose layout depends on its images. WPF has no such window because it decodes
    /// inside <c>EndInit</c>; this framework decodes off the UI thread on purpose, so the header
    /// read is what closes the gap. It is a fraction of a decode: dimensions only, no pixels, and
    /// drained ahead of every pixel job by the scheduler.</para>
    /// <para>Arming from here rather than when the source is constructed is deliberate — see
    /// <c>BitmapImage.RequestMetadata</c>, which also owns the bound that keeps a per-pass caller
    /// from turning a failing probe into a header read per frame.</para>
    /// <para>The answer is exact or absent: <c>TryGetIntrinsicMetadataSize</c> refuses to answer
    /// for a source carrying decode options, whose canonical size the header alone does not
    /// determine.</para>
    /// </remarks>
    private static bool TryGetPreDecodeSourceSize(ImageSource source, out Size imageSize)
    {
        imageSize = default;
        if (source is not BitmapImage bitmap)
        {
            return false;
        }

        bitmap.RequestMetadata();
        if (!bitmap.TryGetIntrinsicMetadataSize(out var width, out var height))
        {
            return false;
        }

        imageSize = new Size(width, height);
        return true;
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
        // No suppression bookkeeping here on purpose. A successful load clears the source's own
        // latched failure (ImageSource.ClearLoadFailure runs before this event), and
        // IsSourceSuppressedByFailure reads exactly that, so the InvalidateVisual below is what
        // makes a recovered image reappear. Keeping the decision in one place is what stops the two
        // copies from drifting and re-creating a permanently blank element.
        //
        // Measure is invalidated only when this notification actually changed what layout would
        // compute. A deferred source publishes once per display-bucket step — the first decode,
        // then an upgrade whenever a larger bucket becomes reachable — and a PURE raster upgrade
        // that leaves the reported size alone has nothing for measure to redo. Invalidating measure
        // on every publish put a full measure/arrange pass of the subtree behind each one, which on
        // a page of images is a layout storm for a change only the render pass cares about.
        // An unknown size counts as changed: that is the state before intrinsics arrive, and
        // skipping measure there would leave a zero-sized element nothing ever re-measures.
        var hasSize = TryGetSourceSize(out _, out var sourceSize);
        var layoutInputChanged =
            !hasSize || !_hasNotifiedSourceSize || !sourceSize.Equals(_lastNotifiedSourceSize);

        _hasNotifiedSourceSize = hasSize;
        _lastNotifiedSourceSize = hasSize ? sourceSize : default;

        if (layoutInputChanged)
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Advisory prefetch hint: tells the source roughly how big this element expects to be, so the
    /// first decode is already in flight before anything tries to draw it.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately in DIPs, with NO DPI multiplication. The authoritative sizing now happens
    /// where the image is actually drawn — <c>RenderTargetDrawingContext.GetNativeBitmap</c> — from
    /// the true device rect: the draw rect folded through the live transform stack and the render
    /// target's own DPI. This method used to multiply by <c>FrameworkElement.LayoutDpiScale</c>,
    /// which is a process-global static assigned by whichever window last handled a DPI change, so
    /// it was wrong on a mixed-DPI multi-monitor desktop and blind to RenderTransform,
    /// LayoutTransform and ScrollViewer zoom in every configuration.</para>
    /// <para>An under-sized advisory hint is harmless: display buckets only ever grow, so the
    /// device-rect request at draw time supersedes this one. An OVER-sized hint would not be
    /// harmless, which is why guessing a DPI factor here is worse than omitting it — the bucket
    /// could never be walked back and the source would hold the larger buffer resident for life.</para>
    /// </remarks>
    private void RequestBitmapDecode(Size availableSize)
    {
        if (Source is not BitmapImage bitmap)
            return;

        var width = ResolveDecodeAxis(availableSize.Width, Width, RenderSize.Width);
        var height = ResolveDecodeAxis(availableSize.Height, Height, RenderSize.Height);
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        width = Math.Max(0, width - insets.Left - insets.Right);
        height = Math.Max(0, height - insets.Top - insets.Bottom);

        var pixelWidth = width > 0d
            ? Math.Clamp((int)Math.Ceiling(width), 1, 16384)
            : 0;
        var pixelHeight = height > 0d
            ? Math.Clamp((int)Math.Ceiling(height), 1, 16384)
            : 0;

        if (pixelWidth == 0 && pixelHeight == 0 &&
            double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            // Nothing about this element's size is known yet AND its slot is bounded, so this is
            // "not arranged", not "unbounded" — the state OnRender reports on the very first frame
            // of a non-Stretch-aligned image, whose DesiredSize is 0 until intrinsics arrive.
            //
            // Sending it as a request would be a lie the source can never recover from: the
            // decoder reads an all-zero request as "may need every pixel" and publishes the
            // NATURAL size, and because a bucket may only ever grow, a 1910x823 photo then holds
            // 6 MiB resident for the life of the source no matter how small it is actually drawn.
            // The bounded slot MeasureOverride already passed is the honest hint, so say nothing
            // here and let that one stand.
            return;
        }

        var fit = EffectiveCssObjectFit();
        if ((fit is CssObjectFit.None or CssObjectFit.ScaleDown) &&
            TryGetSourceSize(out _, out var natural) &&
            (fit == CssObjectFit.None || (natural.Width <= width && natural.Height <= height)))
        {
            // An unscaled object needs its full source pixels even when only a
            // small part of its natural rectangle is visible in the content box.
            pixelWidth = Math.Max(pixelWidth, Math.Clamp(bitmap.PixelWidth, 0, 16384));
            pixelHeight = Math.Max(pixelHeight, Math.Clamp(bitmap.PixelHeight, 0, 16384));
        }

        bitmap.RequestDecode(pixelWidth, pixelHeight,
            (fit is CssObjectFit.Cover or CssObjectFit.Fill) ||
            (fit == CssObjectFit.Unspecified && Stretch == Stretch.UniformToFill));
    }

    private static double ResolveDecodeAxis(double available, double explicitSize, double rendered)
    {
        if (double.IsFinite(rendered) && rendered > 0d)
            return rendered;
        if (double.IsFinite(explicitSize) && explicitSize > 0d)
            return explicitSize;
        if (double.IsFinite(available) && available > 0d)
            return available;
        return 0d;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            image.InvalidateMeasure();
            image.InvalidateVisual();
        }
    }

    private CssObjectFit EffectiveCssObjectFit()
        => HasLocalOrAnimatedValue(StretchProperty)
            ? CssObjectFit.Unspecified : CssImageObjectProperties.Fit(this);

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
