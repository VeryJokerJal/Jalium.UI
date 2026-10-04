using System.Diagnostics;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Media.Imaging;

public sealed partial class BitmapImage
{
    /// <summary>
    /// Hard bound on CONSECUTIVE decodes that publish a bucket no larger than the previous one.
    /// </summary>
    /// <remarks>
    /// <para>A correct <see cref="DecodeUpgradeNeededLocked"/> never produces even one unproductive
    /// decode, so in a healthy process this counter stays at zero forever. It exists because the
    /// predicate is the exact thing that broke: the old one demanded that the published raster be
    /// at least as large as the request, which two whole classes of input can never satisfy — a
    /// request larger than the natural size (a 24x24 icon asked for at 48px on a 200% display)
    /// and a request larger than <see cref="BitmapPixelResampler.MaxBucketEdge"/> (an 8000x6000
    /// photo asked for at 5120x3840). Both re-enqueued at the SAME source version, so every
    /// version gate passed and the scheduler re-ran a full native decode forever, on a worker
    /// pool that can be a single thread.</para>
    /// <para>A decode that FAILS is bounded too, but by <see cref="MaxFailedDecodeAttempts"/> and
    /// its retry delay rather than by this counter — see there for why the two shapes cannot share
    /// one bound.</para>
    /// </remarks>
    private const int MaxUnproductiveDecodeAttempts = 3;

    /// <summary>
    /// Hard bound on CONSECUTIVE decodes that threw, after which the source is terminal for this
    /// source version.
    /// </summary>
    /// <remarks>
    /// <para>A decode that throws publishes no snapshot, so nothing about the source ever looks
    /// saturated — and <c>Image.OnRender</c> calls <c>RequestBitmapDecode</c> on every render pass,
    /// so an un-decodable source (a missing OS codec, a corrupt asset) would be re-enqueued sixty
    /// times a second forever, re-reading the file each time and holding the two- or three-worker
    /// decode pool against every other image in the process. That is the original whole-page
    /// blackout reached through the failure path, so a bound is mandatory.</para>
    /// <para><b>Why this is not the same counter as <see cref="MaxUnproductiveDecodeAttempts"/>.</b>
    /// The two failures have opposite prognoses. A decode that publishes no bigger raster is
    /// deterministic — the same natural size and the same request resolve to the same bucket
    /// forever — so retrying is pure waste and a small bound with no retry is right. A decode that
    /// THREW is frequently transient: an on-access antivirus scan holding the asset, an SMB share
    /// hiccup, a momentarily exhausted handle table. Counting three consecutive frames' worth of
    /// those against a no-retry bound turned 50ms of bad luck at startup into a permanently blank
    /// image for the life of the process, because <c>Image.OnRender</c> spends the whole budget
    /// inside three frames. Hence a larger bound plus <see cref="FailureRetryDelayMs"/>, which
    /// spreads the attempts over several seconds of wall clock instead of ~50ms.</para>
    /// <para>The chain still terminates: at most <see cref="MaxFailedDecodeAttempts"/> failures
    /// plus <see cref="MaxUnproductiveDecodeAttempts"/> unproductive publishes plus the (finite,
    /// power-of-two) bucket ladder, per source version.</para>
    /// </remarks>
    private const int MaxFailedDecodeAttempts = 5;

    /// <summary>Delay before the first retry of a failed decode. Quadruples per attempt.</summary>
    private const double FailureRetryBaseDelayMs = 100d;

    /// <summary>Ceiling for the exponential retry delay.</summary>
    private const double FailureRetryMaxDelayMs = 30_000d;

    /// <summary>
    /// Hard bound on metadata (header-probe) jobs per source version.
    /// </summary>
    /// <remarks>
    /// Without it the metadata job repeats RC1's exact shape on a second path: nothing about a
    /// probe that threw — or that returned no dimensions for a container the decoder cannot read a
    /// header from — is recorded, so the next caller re-enqueues, forever. Metadata jobs are also
    /// drained AHEAD of every pixel job, so an unbounded stream of them starves pixel decoding
    /// process-wide.
    /// </remarks>
    private const int MaxMetadataProbeAttempts = 3;

    private readonly object _deferredDecodeLock = new();
    private Func<byte[]>? _deferredSourceLoader;
    private TaskCompletionSource? _deferredDecodeCompletion;
    private long _deferredSourceVersion;
    private int _requestedDecodeWidth;
    private int _requestedDecodeHeight;
    private int _requestedWidthOnlyMax;
    private int _requestedHeightOnlyMax;
    private bool _requestedDecodeCover;
    private int _canonicalPixelWidth;
    private int _canonicalPixelHeight;
    private int _publishedBucketWidth;
    private int _publishedBucketHeight;
    private int _unproductiveDecodeAttempts;
    private int _failedDecodeAttempts;
    private long _lastDecodeFailureTimestamp;
    private int _decodeAttempts;
    private bool _decodeQueuedOrRunning;
    private bool _metadataQueuedOrRunning;
    private int _metadataProbeAttempts;
    private int _metadataNaturalWidth;
    private int _metadataNaturalHeight;
    private int _metadataFrameCount;
    private bool _frameCountResolved;
    private bool _isDeferredSource;

    /// <summary>
    /// The author's crop / rotation / decode-size options, latched by <c>ApplySource</c> at
    /// <see cref="EndInit"/> time.
    /// </summary>
    /// <remarks>
    /// Latched rather than read live because a decode worker may not touch dependency-property
    /// state, and applied inside the decode rather than to the published raster because the raster
    /// is a DPI- and layout-dependent display bucket while the options are authored against the
    /// natural size. See <see cref="BitmapDecodeOptions"/>.
    /// </remarks>
    private BitmapDecodeOptions _decodeOptions;

    /// <summary>
    /// The internal multi-frame stand-in for a source whose encoded bytes turned out to hold more
    /// than one frame (animated GIF / APNG / animated WebP).
    /// </summary>
    /// <remarks>
    /// <para>Written by a metadata worker with a single <see cref="Volatile.Write{T}"/> and read by
    /// the render thread, which resolves pixels through it instead of through this instance's own
    /// raster. Critically, this is a SUBSTITUTE and not a replacement: the application's
    /// <c>Image.Source</c> — and any binding behind it — keeps pointing at this
    /// <see cref="BitmapImage"/>. <c>ImageSourceLoader.FromUri</c> could only upgrade a URI to an
    /// <see cref="AnimatedBitmap"/> when the encoded bytes were already materialised, which a
    /// deferred source's never are, so <c>&lt;Image Source="cat.gif"/&gt;</c> reached through the
    /// XAML type converter silently rendered one static frame.</para>
    /// <para>This instance still runs its own deferred decode of frame zero. That decode is what
    /// gives the source an intrinsic <see cref="Width"/>/<see cref="Height"/> for layout, and it is
    /// the fallback whenever the substitute could not be built at all.</para>
    /// </remarks>
    private AnimatedBitmap? _animatedSubstitute;

    /// <summary>
    /// Whether the substitute's dispatcher-affine half — its frame timer and this instance's
    /// repaint subscription — has been set up. Main dispatcher only.
    /// </summary>
    private bool _animatedSubstituteStarted;

    /// <summary>
    /// Whether this instance may dispose <see cref="_animatedSubstitute"/>. False on a clone, which
    /// shares the original's substitute by reference; see <see cref="CopyDeferredStateFrom"/>.
    /// </summary>
    private bool _ownsAnimatedSubstitute;

    /// <summary>
    /// The published pixels. Written once per publish with a single <see cref="Volatile.Write{T}"/>
    /// so an unsynchronized reader (the render thread, on by default on Windows) can never pair a
    /// buffer with dimensions that do not belong to it.
    /// </summary>
    private BitmapPixelSnapshot? _pixelSnapshot;

    /// <summary>
    /// Forces <see cref="DecodeUpgradeNeededLocked"/> to a fixed answer so a test can prove the
    /// hard attempt bound holds independently of the predicate. Per-instance rather than static
    /// so a parallel test class cannot corrupt an unrelated bitmap.
    /// </summary>
    private Func<bool>? _upgradePredicateOverrideForTests;

    /// <summary>Test-only. Never call this from framework code.</summary>
    internal void SetUpgradePredicateOverrideForTests(Func<bool>? predicate) =>
        _upgradePredicateOverrideForTests = predicate;

    /// <summary>
    /// Replaces the post-failure retry delay so a test can walk the whole failure budget without
    /// spending its wall clock on it. Per-instance rather than static, so a parallel test class
    /// cannot disarm an unrelated bitmap's backoff.
    /// </summary>
    private double? _failureRetryDelayOverrideForTests;

    /// <summary>Test-only. Never call this from framework code.</summary>
    internal void SetFailureRetryDelayOverrideForTests(double? delayMs) =>
        _failureRetryDelayOverrideForTests = delayMs;

    /// <summary>
    /// Whether this source has spent its budget of decodes that produced no additional pixels, or
    /// its budget of decodes that failed outright. Both the predicate and the pre-decode bound read
    /// it, so there is one comparison to get wrong instead of two.
    /// </summary>
    private bool DecodeChainSaturatedLocked() =>
        _unproductiveDecodeAttempts >= MaxUnproductiveDecodeAttempts ||
        _failedDecodeAttempts >= MaxFailedDecodeAttempts;

    /// <summary>
    /// Whether a decode that just failed is still inside its retry delay.
    /// </summary>
    /// <remarks>
    /// <c>Image.MeasureOverride</c> and <c>Image.OnRender</c> both re-request on every pass, so
    /// without a delay the entire failure budget is consumed within about three frames and a
    /// transient fault (an antivirus scan holding the file, an SMB stall) becomes permanent. The
    /// wait is passive: nothing here re-arms a decode by itself, it only refuses to start one
    /// early, and the next layout or render pass after the window closes drives the retry. That
    /// keeps the framework free of a self-driving background retry loop while still giving a
    /// transient fault several seconds to clear.
    /// </remarks>
    private bool DecodeRetryDeferredLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        if (_failedDecodeAttempts <= 0)
            return false;

        var delayMs = _failureRetryDelayOverrideForTests ?? FailureRetryDelayMs(_failedDecodeAttempts);
        return Stopwatch.GetElapsedTime(_lastDecodeFailureTimestamp).TotalMilliseconds < delayMs;
    }

    /// <summary>
    /// How long to wait after <paramref name="consecutiveFailures"/> consecutive decode failures
    /// before another attempt is allowed: 100ms, 400ms, 1.6s, 6.4s, capped at
    /// <see cref="FailureRetryMaxDelayMs"/>.
    /// </summary>
    private static double FailureRetryDelayMs(int consecutiveFailures) =>
        Math.Min(
            FailureRetryBaseDelayMs * Math.Pow(4d, Math.Max(0, consecutiveFailures - 1)),
            FailureRetryMaxDelayMs);

    /// <summary>
    /// Natural (pre-bucket) pixel width of the decoded source, falling back to the header probe
    /// before any pixels have been decoded.
    /// </summary>
    /// <remarks>
    /// Equal to <see cref="PixelWidth"/> whenever a raster has been published — the display bucket
    /// is no longer mirrored into the intrinsic size, so the two cannot disagree. It stays a
    /// separate member because it can additionally answer from the metadata probe, i.e. before the
    /// first pixel decode, where <see cref="PixelWidth"/> is still 0.
    /// </remarks>
    public int NaturalPixelWidth
    {
        get
        {
            var snapshot = Volatile.Read(ref _pixelSnapshot);
            if (snapshot is not null)
                return snapshot.CanonicalWidth;

            lock (_deferredDecodeLock)
            {
                if (_metadataNaturalWidth > 0)
                    return _metadataNaturalWidth;
            }

            return PixelWidth;
        }
    }

    /// <summary>
    /// Natural (pre-bucket) pixel height of the decoded source. See
    /// <see cref="NaturalPixelWidth"/>.
    /// </summary>
    public int NaturalPixelHeight
    {
        get
        {
            var snapshot = Volatile.Read(ref _pixelSnapshot);
            if (snapshot is not null)
                return snapshot.CanonicalHeight;

            lock (_deferredDecodeLock)
            {
                if (_metadataNaturalHeight > 0)
                    return _metadataNaturalHeight;
            }

            return PixelHeight;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>The publication id of the raster currently behind this instance. A deferred bitmap
    /// keeps its reference identity across an arbitrary number of decodes — the first publish, a
    /// bucket upgrade, a restore after the idle reclaimer dropped the pixels — so reference
    /// identity alone tells a cache nothing about whether its copy is still the right bytes.</para>
    /// <para>Read without the decode lock on purpose: this is polled once per cached bitmap per
    /// frame on the render thread, and a stamp read one publication late costs exactly one extra
    /// frame of the previous raster, never a torn buffer — the pixels themselves are only ever
    /// reached through <see cref="TryGetPixelSnapshot"/>, which pairs them with their own
    /// generation.</para>
    /// </remarks>
    internal override long ContentGeneration
    {
        get
        {
            var snapshot = Volatile.Read(ref _pixelSnapshot);
            var rasterGeneration = snapshot is not null
                ? snapshot.Generation
                : Volatile.Read(ref _legacyPixelGeneration);
            // A deferred multi-frame image preserves the application's BitmapImage identity and
            // presents an internal AnimatedBitmap. Its frame generation must participate in this
            // identity's cache stamp; otherwise a DrawingImage/ImageBrush raster keyed on the
            // outer BitmapImage can retain frame zero forever.
            var animatedGeneration = Volatile.Read(ref _animatedSubstitute)?.ContentGeneration ?? 0;
            return Math.Max(rasterGeneration, animatedGeneration);
        }
    }

    /// <summary>
    /// Configures an encoded source without decoding it on the caller thread.
    /// </summary>
    /// <param name="sourceLoader">
    /// Reads the encoded bytes. Invoked on a decode worker, and only when
    /// <paramref name="encodedBytes"/> is <see langword="null"/>.
    /// </param>
    /// <param name="encodedBytes">
    /// Encoded bytes the caller has ALREADY read, for <see cref="BitmapCacheOption.OnLoad"/>. They
    /// are seeded into the same <c>_imageData</c> cache a decode would fill, so
    /// <paramref name="sourceLoader"/> is never invoked and the source is completely detached from
    /// the file it came from — which is what makes the load-then-delete and load-then-overwrite
    /// idioms work. Everything downstream is unchanged: the bucket ladder, the upgrade predicate
    /// and the scheduler all see an ordinary deferred source whose bytes happen to be resident.
    /// </param>
    private void ConfigureDeferredSource(Func<byte[]> sourceLoader, byte[]? encodedBytes = null)
    {
        ArgumentNullException.ThrowIfNull(sourceLoader);

        TaskCompletionSource? superseded;
        AnimatedBitmap? retiredSubstitute;
        lock (_deferredDecodeLock)
        {
            superseded = _deferredDecodeCompletion;
            _deferredDecodeCompletion = null;
            _deferredSourceVersion++;
            _deferredSourceLoader = sourceLoader;
            ClearRequestedDecodeSizeLocked();
            _decodeQueuedOrRunning = false;
            ClearMetadataStateLocked();
            _isDeferredSource = true;
            retiredSubstitute = DetachAnimatedSubstituteLocked();
            ClearPublishedDecodeStateLocked(dropLegacyPixels: true);

            _nativeHandle = nint.Zero;
            _width = 0;
            _height = 0;
            _rasterPixelWidth = 0;
            _rasterPixelHeight = 0;
            _imageData = encodedBytes;
        }

        RetireAnimatedSubstitute(retiredSubstitute);
        superseded?.TrySetCanceled();
        RaiseGpuCacheEviction(this);
        ClearLoadFailure();
    }

    /// <summary>Returns the bitmap to the immediate/fully-decoded source model.</summary>
    private void ResetDeferredSource()
    {
        TaskCompletionSource? superseded;
        AnimatedBitmap? retiredSubstitute;
        lock (_deferredDecodeLock)
        {
            superseded = _deferredDecodeCompletion;
            _deferredDecodeCompletion = null;
            _deferredSourceVersion++;
            _deferredSourceLoader = null;
            _decodeQueuedOrRunning = false;
            ClearMetadataStateLocked();
            _isDeferredSource = false;
            retiredSubstitute = DetachAnimatedSubstituteLocked();
            ClearRequestedDecodeSizeLocked();

            // Keep the legacy pixel fields: LoadFromBytes calls this before decoding, and a
            // decoder that throws must leave the previously good raster on screen.
            ClearPublishedDecodeStateLocked(dropLegacyPixels: false);
        }

        RetireAnimatedSubstitute(retiredSubstitute);
        superseded?.TrySetCanceled();
    }

    /// <summary>
    /// Clears everything the metadata probe owns. The dimensions, the frame count and the attempt
    /// counter all describe ONE source version and must be reset together — leaving the frame count
    /// behind would make a new source inherit the previous one's animated verdict.
    /// </summary>
    private void ClearMetadataStateLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        _metadataQueuedOrRunning = false;
        _metadataProbeAttempts = 0;
        _metadataNaturalWidth = 0;
        _metadataNaturalHeight = 0;
        _metadataFrameCount = 0;
        _frameCountResolved = false;
    }

    /// <summary>
    /// Drops every trace of a previous publication. The snapshot, the mirrored bucket/canonical
    /// numbers and the unproductive-attempt counter describe ONE publication and must be cleared
    /// as a unit.
    /// </summary>
    /// <remarks>
    /// Clearing the counter here is load-bearing, not tidiness: an idle-resource reclaim drops
    /// the pixels and the next render re-decodes at the same bucket. If the counter survived,
    /// four reclaim/restore cycles would trip the saturation bound and freeze the image blank
    /// forever with no failure anywhere to explain it.
    /// </remarks>
    /// <param name="dropLegacyPixels">
    /// Whether the legacy <see cref="RawPixelData"/> mirror is dropped too. False when the caller
    /// is switching to the eager decode model and still owns valid pixels.
    /// </param>
    private void ClearPublishedDecodeStateLocked(bool dropLegacyPixels)
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        Volatile.Write(ref _pixelSnapshot, null);
        _canonicalPixelWidth = 0;
        _canonicalPixelHeight = 0;
        _publishedBucketWidth = 0;
        _publishedBucketHeight = 0;
        _unproductiveDecodeAttempts = 0;
        _failedDecodeAttempts = 0;
        _lastDecodeFailureTimestamp = 0;
        _decodeAttempts = 0;

        if (dropLegacyPixels)
        {
            _rawPixelData = null;
            _pixelStride = 0;

            // Cleared with the buffer, never separately: these describe THAT buffer, and a
            // non-zero pair left behind a null one is a geometry the legacy fallback would
            // otherwise be free to believe.
            _rasterPixelWidth = 0;
            _rasterPixelHeight = 0;
        }

        // _width/_height deliberately survive. They are the CANONICAL size, which a reclaim does
        // not change — an off-screen card that comes back must not first re-lay-out at zero — and
        // the only states that genuinely have no intrinsic size (a source swap, a cancel) zero
        // them at their own call sites.
    }

    /// <summary>
    /// The accumulated requested decode size, for diagnostics and for the reclaim restore.
    /// </summary>
    /// <remarks>
    /// This is NOT the bucket decision — see <see cref="ResolveRequestedBucketLocked"/>. An
    /// all-zero request means "the caller does not know the size yet", not "give me the full
    /// resolution"; it is deliberately not sticky, because <c>Image</c> reports an unresolved
    /// axis as 0 and a sticky reading of that would pin every image inside an unbounded-axis
    /// panel to a full-resolution decode for the life of the source.
    /// </remarks>
    private void GetRequestedDecodeSizeLocked(out int width, out int height, out bool cover)
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        width = _requestedDecodeWidth;
        height = _requestedDecodeHeight;
        cover = _requestedDecodeCover;
    }

    private void ClearRequestedDecodeSizeLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        _requestedDecodeWidth = 0;
        _requestedDecodeHeight = 0;
        _requestedWidthOnlyMax = 0;
        _requestedHeightOnlyMax = 0;
        _requestedDecodeCover = false;
    }

    /// <summary>
    /// Latches the author's decode options so a decode worker can read them without touching
    /// dependency-property state. Called by <c>ApplySource</c> before the source is configured.
    /// </summary>
    private void SetDecodeOptions(BitmapDecodeOptions options)
    {
        lock (_deferredDecodeLock)
        {
            _decodeOptions = options;
        }
    }

    /// <summary>Reads the latched decode options. Safe from any thread.</summary>
    private BitmapDecodeOptions CaptureDecodeOptions()
    {
        lock (_deferredDecodeLock)
        {
            return _decodeOptions;
        }
    }

    /// <summary>
    /// The internal multi-frame stand-in, or <see langword="null"/> for a single-frame source.
    /// Consumers resolve pixels through this in preference to this instance's own raster.
    /// </summary>
    /// <remarks>
    /// Read on the render thread, so it is a single <see cref="Volatile.Read{T}"/> rather than a
    /// lock acquisition: the substitute is published exactly once per source version and a read
    /// that lands one publication early costs one frame of the static first frame, never a torn
    /// object.
    /// </remarks>
    internal AnimatedBitmap? AnimatedSubstitute => Volatile.Read(ref _animatedSubstitute);

    /// <summary>
    /// Removes the current substitute from this instance, returning it so the caller can retire it
    /// outside the lock. Disposing an <see cref="AnimatedBitmap"/> stops a
    /// <c>DispatcherTimer</c> and disposes every frame, which is far too much work — and far too
    /// much foreign code — to run while holding the decode lock.
    /// </summary>
    private AnimatedBitmap? DetachAnimatedSubstituteLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        var substitute = Volatile.Read(ref _animatedSubstitute);
        Volatile.Write(ref _animatedSubstitute, null);
        _animatedSubstituteStarted = false;

        if (!_ownsAnimatedSubstitute)
        {
            // A clone shares the original's substitute; retiring it here would stop the original's
            // animation and dispose frames the original is still drawing.
            return null;
        }

        _ownsAnimatedSubstitute = false;
        return substitute;
    }

    /// <summary>Stops and releases a substitute this instance owned.</summary>
    private void RetireAnimatedSubstitute(AnimatedBitmap? substitute)
    {
        if (substitute is null)
        {
            return;
        }

        try
        {
            substitute.FrameChanged -= OnAnimatedSubstituteFrameChanged;
            substitute.Dispose();
        }
        catch (Exception ex)
        {
            // Teardown runs from a source swap, which is a dependency-property write on the UI
            // thread — an exception escaping here would surface as a failure of whatever assigned
            // the new source. Reported through the Release-live channel rather than swallowed.
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "animated substitute teardown", ex);
        }
    }

    /// <summary>
    /// Completes the dispatcher-affine half of publishing a substitute: subscribes the repaint
    /// channel and starts the frame timer.
    /// </summary>
    /// <remarks>
    /// <para>The frames are decoded on a scheduler worker, but a <c>DispatcherTimer</c> binds to
    /// <c>Dispatcher.CurrentDispatcher</c> at construction — on a pooled thread that both creates a
    /// dispatcher nothing will ever pump AND can claim the process's main-dispatcher slot if it
    /// gets there first. So the timer is only ever created from
    /// <see cref="RaiseAnimatedSubstituteReady"/>, which <see cref="BitmapDecodeNotifier"/>
    /// delivers on the main dispatcher.</para>
    /// <para>With ONE measured exception, which is why the null check below is not paranoia: the
    /// notifier deliberately falls back to inline delivery on the calling thread when nothing in
    /// the process can ever run a queued callback. The case that matters is a host with no main
    /// dispatcher at all, because there <c>CurrentDispatcher</c> would create one on a decode
    /// worker and that worker would own the main-dispatcher slot for the life of the process —
    /// which silently kills image notifications for every image, the exact failure
    /// <see cref="BitmapDecodeNotifier"/>'s remarks describe. The substitute still renders, frozen
    /// on frame zero, which is what a host with no dispatcher would see anyway.</para>
    /// </remarks>
    private void StartAnimatedSubstitute()
    {
        var substitute = Volatile.Read(ref _animatedSubstitute);
        if (substitute is null || _animatedSubstituteStarted)
        {
            return;
        }

        _animatedSubstituteStarted = true;

        // Unconditional: this is the repaint channel, and it costs nothing. An application that
        // advances frames itself through CurrentFrameIndex gets its repaint even where the timer
        // below is refused.
        substitute.FrameChanged += OnAnimatedSubstituteFrameChanged;

        if (Jalium.UI.Threading.Dispatcher.MainDispatcher is null)
        {
            ImageDiagnostics.Degraded(
                DiagnosticSourceName,
                "animated frames will not advance: this process has no main dispatcher to run the frame timer",
                substitute.FrameCount,
                0);
            return;
        }

        substitute.Play();
    }

    /// <summary>
    /// Turns a substitute frame advance into a repaint of everything drawing this source.
    /// </summary>
    /// <remarks>
    /// Runs on the main dispatcher (the substitute advances frames on a <c>DispatcherTimer</c>).
    /// <c>OnImageLoaded</c> is the channel <c>Image</c> already listens on — it reaches
    /// <c>Image.OnSourceAsyncLoaded</c>, i.e. a targeted <c>InvalidateVisual</c> on the element that
    /// owns this source — and it is the same channel a top-level <see cref="AnimatedBitmap"/>
    /// reaches through <c>Image.OnAnimatedFrameChanged</c>. Measure is not invalidated: the frames
    /// all share one size, so <c>OnSourceAsyncLoaded</c>'s own size comparison short-circuits.
    /// </remarks>
    private void OnAnimatedSubstituteFrameChanged(object? sender, EventArgs e) =>
        RaiseImageLoadedPerSubscriber();

    /// <summary>
    /// The pixel size the next decode will publish for a source whose natural size is
    /// <paramref name="canonicalWidth"/> x <paramref name="canonicalHeight"/>. This is the ONE
    /// decision the producer (<see cref="ProcessDeferredDecode"/>) and the predicate
    /// (<see cref="DecodeUpgradeNeededLocked"/>) are allowed to make.
    /// </summary>
    /// <remarks>
    /// <para>If those two ever resolve inputs that differ by even one pixel, the predicate can
    /// demand a bucket the producer will never emit and the decode chain stops terminating. They
    /// therefore call this method, and this method alone.</para>
    /// <para><b>Why this is a maximum over three answers rather than one.</b> A zero axis means
    /// "unconstrained", so <see cref="BitmapPixelResampler.ResolveBucket"/> scales by the other
    /// axis alone; with both axes present and <c>contain</c> semantics it scales by the SMALLER
    /// of the two. Accumulating requests axis-wise with <c>Math.Max</c> therefore is not monotone
    /// in the resolved bucket: a source measured once at (1900, unbounded) and then arranged one
    /// pixel tall accumulates to (1900, 1), whose resolved bucket is a handful of pixels — the
    /// wide axis silently downgraded by a transient collapsed row, with no upgrade path back
    /// because an upgrade needs strictly greater area. Resolving the width-only and height-only
    /// maxima as their own candidates and taking the largest restores monotonicity: every new
    /// observation can only grow a candidate or add one, so the answer never shrinks and the
    /// bucket ladder still terminates.</para>
    /// <para>An all-zero history leaves both maxima at zero and falls through to
    /// <c>ResolveBucket</c>'s all-zero early-out, i.e. the natural size — which is correct, since
    /// a genuinely unbounded slot may need every pixel.</para>
    /// <para>An explicit <c>DecodePixelWidth</c>/<c>DecodePixelHeight</c> bypasses the ladder
    /// entirely — see <see cref="ResolveExplicitDecodeSize"/>.</para>
    /// </remarks>
    private (int Width, int Height) ResolveRequestedBucketLocked(int canonicalWidth, int canonicalHeight)
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        return ResolveBucketForRequest(canonicalWidth, canonicalHeight, CaptureRequestLocked());
    }

    /// <summary>
    /// The accumulated request, frozen, together with the author's explicit decode size. The
    /// producer latches one of these BEFORE it decodes, so the raster it emits answers a request
    /// that actually existed; the predicate re-evaluates against the live accumulator afterwards
    /// and schedules an upgrade if layout moved on.
    /// </summary>
    /// <remarks>
    /// The decode size travels IN the request rather than being read separately by each side,
    /// because the producer and the predicate must resolve the same target from the same inputs —
    /// see <see cref="ResolveBucketForRequest"/>. It cannot change under either of them: the
    /// options are latched once by <c>ApplySource</c>, and a post-<c>EndInit</c> write is reported
    /// and ignored rather than applied.
    /// </remarks>
    private readonly record struct DecodeRequest(
        int Width,
        int Height,
        int WidthOnlyMax,
        int HeightOnlyMax,
        bool Cover,
        int DecodePixelWidth,
        int DecodePixelHeight);

    private DecodeRequest CaptureRequestLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        return new DecodeRequest(
            _requestedDecodeWidth,
            _requestedDecodeHeight,
            _requestedWidthOnlyMax,
            _requestedHeightOnlyMax,
            _requestedDecodeCover,
            _decodeOptions.DecodePixelWidth,
            _decodeOptions.DecodePixelHeight);
    }

    /// <summary>See <see cref="ResolveRequestedBucketLocked"/> for why this is a maximum.</summary>
    private static (int Width, int Height) ResolveBucketForRequest(
        int canonicalWidth,
        int canonicalHeight,
        in DecodeRequest request)
    {
        if (request.DecodePixelWidth > 0 || request.DecodePixelHeight > 0)
        {
            // The author named the decode resolution, so there is no ladder to climb: WPF's
            // contract is PixelWidth == DecodePixelWidth exactly, and it is the standard way an
            // application bounds image memory and guarantees a known raster size for CopyPixels
            // and encode work. Running the display bucket over it demoted the author's number
            // from "the decode resolution" to "an upper bound we may shrink further", silently
            // and DPI-dependently, and it disagreed with the eager AdoptDecoded path, so the same
            // markup produced one size through StreamSource and another through UriSource.
            return ResolveExplicitDecodeSize(
                canonicalWidth,
                canonicalHeight,
                request.DecodePixelWidth,
                request.DecodePixelHeight);
        }

        var best = BitmapPixelResampler.ResolveBucket(
            canonicalWidth, canonicalHeight, request.Width, request.Height, request.Cover);

        if (request.WidthOnlyMax > 0)
        {
            best = LargerBucket(best, BitmapPixelResampler.ResolveBucket(
                canonicalWidth, canonicalHeight, request.WidthOnlyMax, 0, request.Cover));
        }

        if (request.HeightOnlyMax > 0)
        {
            best = LargerBucket(best, BitmapPixelResampler.ResolveBucket(
                canonicalWidth, canonicalHeight, 0, request.HeightOnlyMax, request.Cover));
        }

        return best;
    }

    /// <summary>
    /// Picks the bigger of two candidate buckets by AREA. Every candidate is aspect-preserving and
    /// clamped to the source, so area is a total order over them and comparing it collapses the
    /// degenerate shapes (a 1px axis, a clamped extreme aspect ratio) instead of oscillating.
    /// </summary>
    private static (int Width, int Height) LargerBucket(
        (int Width, int Height) first,
        (int Width, int Height) second) =>
        (long)second.Width * second.Height > (long)first.Width * first.Height ? second : first;

    /// <summary>
    /// The size an author who set <c>DecodePixelWidth</c>/<c>DecodePixelHeight</c> asked for,
    /// resolved against a canonical size of <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    /// <remarks>
    /// <para>This reproduces <c>ApplyDecodeOptions</c>'s own scaling arithmetic, including how it
    /// derives the axis the author left at zero, so that running it on that method's OUTPUT
    /// returns the same numbers. That idempotence is what keeps the producer and the upgrade
    /// predicate in lockstep, which is the whole termination argument: the producer resamples the
    /// transformed image to this answer and records <paramref name="width"/> x
    /// <paramref name="height"/> as the canonical size, and the predicate then re-resolves from
    /// exactly those canonical numbers, gets the same pair, and reports no gain. The chain
    /// therefore ends on the FIRST decode.</para>
    /// <para>It is deliberately correct on the one path where the two inputs differ. When
    /// <c>ApplyDecodeOptions</c> THREW — an out-of-range <c>SourceRect</c>, an overflowing size —
    /// the producer publishes the untransformed image, so the canonical size is the encoded one;
    /// resolving the author's decode size against that still bounds the resident raster by the
    /// number the author actually named rather than by the encoded resolution, and both sides
    /// still agree because both resolve from the same published canonical pair.</para>
    /// <para>Nothing is clamped. An absurd decode size is the author's own value and WPF honours
    /// it verbatim, including upscaling past the natural size; a value large enough to overflow
    /// the resampler's <c>checked</c> arithmetic throws inside the decode, which is counted
    /// against <see cref="MaxFailedDecodeAttempts"/> and reported, rather than being silently
    /// rewritten into a size the author never asked for.</para>
    /// </remarks>
    private static (int Width, int Height) ResolveExplicitDecodeSize(
        int width,
        int height,
        int decodeWidth,
        int decodeHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return (width, height);
        }

        if (decodeWidth <= 0)
        {
            decodeWidth = Math.Max(1, (int)Math.Round(width * (decodeHeight / (double)height)));
        }
        else if (decodeHeight <= 0)
        {
            decodeHeight = Math.Max(1, (int)Math.Round(height * (decodeWidth / (double)width)));
        }

        return (decodeWidth, decodeHeight);
    }

    /// <summary>
    /// Decodes a deferred (URI-backed) source now, ahead of its first draw, sized for a slot of
    /// <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> device pixels. The returned
    /// task completes once pixels for that size are published — that is, once a draw of this
    /// source can no longer miss.
    /// </summary>
    /// <remarks>
    /// <para>The deferred pipeline otherwise starts a decode the first time something DRAWS the
    /// source, and that first draw paints nothing until a worker publishes. That is the right
    /// trade for a page of images — nothing decodes that is never shown — and the wrong one for a
    /// sprite animation, a carousel's next slide or a hover state, where the source about to be
    /// shown is known in advance and a blank first frame is a visible flicker. This is the "I know
    /// it is coming" hook. It runs the same scheduler, the same display-bucket ladder and the same
    /// reclaim/restore path as a draw-driven decode, so an application never has to reach for the
    /// eager <see cref="FromBytes"/> path — a synchronous, full-resolution decode on the calling
    /// thread that also disables display-bucket downsampling for the life of the source — just to
    /// get ahead of the renderer.</para>
    /// <para>Idempotent and cheap to repeat. A source whose published bucket already covers the
    /// request — including a non-deferred source, which always has its pixels — returns a
    /// completed task without touching the scheduler. A request larger than the published bucket
    /// schedules an upgrade; the pixels already published stay drawable until the upgrade lands.
    /// After the idle reclaimer has dropped the pixels, calling this again is what restores them
    /// before the next draw.</para>
    /// <para><paramref name="pixelWidth"/> / <paramref name="pixelHeight"/> are DEVICE pixels —
    /// multiply the layout size by the DPI scale. Zero on both axes means the natural size, which
    /// pins the full-resolution raster resident for the life of the source; pass the real slot
    /// size whenever it is known. A request on one axis only constrains that axis.
    /// <paramref name="cover"/> mirrors <c>Stretch.UniformToFill</c>: the bucket is sized to cover
    /// the slot rather than to fit inside it.</para>
    /// <para>The task faults with the decoder's exception when the source cannot be decoded (the
    /// failure is also reported through <c>Image.ImageFailed</c> and
    /// <see cref="Jalium.UI.Diagnostics.ImageDiagnostics"/>), and is cancelled when the source is
    /// reassigned before the decode lands. It completes on a decode worker, not on the UI thread;
    /// marshal back before touching elements.</para>
    /// </remarks>
    /// <param name="pixelWidth">Target slot width in device pixels, or 0 for unconstrained.</param>
    /// <param name="pixelHeight">Target slot height in device pixels, or 0 for unconstrained.</param>
    /// <param name="cover">Whether the slot must be covered (<c>Stretch.UniformToFill</c>) rather than fitted.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is negative.</exception>
    public Task PreloadAsync(int pixelWidth, int pixelHeight, bool cover = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelHeight);
        return RequestDecodeAsync(pixelWidth, pixelHeight, cover);
    }

    /// <summary>
    /// Requests pixels sized for a realized <see cref="Jalium.UI.Controls.Image"/>.
    /// The request is idempotent and upgrades an in-flight decode when a later layout
    /// needs a larger bucket.
    /// </summary>
    internal void RequestDecode(int targetWidth, int targetHeight, bool cover)
    {
        var decode = RequestDecodeAsync(targetWidth, targetHeight, cover);
        if (decode.IsCompletedSuccessfully || decode.IsCanceled)
            return;

        // Observe the fault. The failure is already reported through ImageDiagnostics AND through
        // the decode notifier, so nothing is being hidden here — but this overload is the one
        // Image.OnRender calls on every render pass, and an unobserved faulted Task raises
        // TaskScheduler.UnobservedTaskException from the finalizer thread once per frame for a
        // source whose decoder throws.
        decode.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal Task RequestDecodeAsync(int targetWidth, int targetHeight, bool cover)
    {
        targetWidth = Math.Clamp(targetWidth, 0, 16384);
        targetHeight = Math.Clamp(targetHeight, 0, 16384);

        long version;
        Task task;
        var enqueue = false;
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource)
                return Task.CompletedTask;

            // An all-zero request contributes nothing durable: it means "the caller does not know
            // the size yet", and it resolves to the natural size only while nothing else is known.
            _requestedDecodeWidth = Math.Max(_requestedDecodeWidth, targetWidth);
            _requestedDecodeHeight = Math.Max(_requestedDecodeHeight, targetHeight);
            _requestedDecodeCover |= cover;

            // A request that constrains exactly one axis is kept as its own observation, because
            // folding it into the axis-wise maximum is not monotone in the resolved bucket. See
            // ResolveRequestedBucketLocked.
            if (targetWidth > 0 && targetHeight <= 0)
            {
                _requestedWidthOnlyMax = Math.Max(_requestedWidthOnlyMax, targetWidth);
            }
            else if (targetHeight > 0 && targetWidth <= 0)
            {
                _requestedHeightOnlyMax = Math.Max(_requestedHeightOnlyMax, targetHeight);
            }

            if (!DecodeUpgradeNeededLocked())
                return Task.CompletedTask;

            _deferredDecodeCompletion ??=
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            task = _deferredDecodeCompletion.Task;
            version = _deferredSourceVersion;
            if (!_decodeQueuedOrRunning)
            {
                _decodeQueuedOrRunning = true;
                enqueue = true;
            }
        }

        if (enqueue)
        {
            BitmapDecodeScheduler.Enqueue(this, version, BitmapDecodeJobKind.Pixels);
        }

        return task;
    }

    /// <summary>
    /// Queues a metadata-only job: resolve the encoded bytes off the UI thread and read the
    /// natural dimensions and the frame count without decoding pixels for layout. Metadata jobs are
    /// drained ahead of pixel jobs, so a burst of decodes cannot delay intrinsic sizes.
    /// </summary>
    /// <remarks>
    /// <para>Armed by <c>Image.TryGetSourceSize</c> — an element that has been asked to measure a
    /// source with no intrinsic size yet — rather than when the source is configured. A probe reads
    /// the whole encoded payload and caches it, so arming every URI-backed
    /// <see cref="BitmapImage"/> at construction would pull every file a view model pre-created
    /// into memory, including the ones nothing ever draws; a thumbnail gallery that builds a
    /// thousand sources up front would pay for a thousand files to show twenty. Arming it from the
    /// element ties the read to an image that is about to be decoded anyway.</para>
    /// <para>The measure pass that arms this has usually just requested a pixel decode too, so the
    /// two jobs can run on two workers at once and BOTH read the payload — at most one extra read
    /// of it, ever, since whichever finishes first caches the bytes for every later job (the other
    /// job kind, a retry, an upgrade, a canonical pixel read). That is the price of an intrinsic
    /// size that beats the decode, and it is a rounding error next to the decode it precedes; the
    /// probe reads a header and returns in microseconds.</para>
    /// <para>Idempotent and hard-bounded, which is what makes it safe to call from a per-pass
    /// layout path: it refuses to enqueue while a probe is in flight, once the dimensions are
    /// known, and after <see cref="MaxMetadataProbeAttempts"/> attempts for this source version. A
    /// probe that always throws therefore costs three reads in total, not one per frame.</para>
    /// </remarks>
    internal void RequestMetadata()
    {
        long version;
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || _metadataQueuedOrRunning)
                return;

            if (_metadataNaturalWidth > 0 && _metadataNaturalHeight > 0)
                return;

            // The bound, at the only entry point that can enqueue a metadata job. A probe that
            // threw, or that simply could not read a header, leaves the dimensions at zero — so
            // without this the two checks above are both false forever and every caller
            // re-enqueues. Callers are layout-driven (once per pass, per image), which is RC1's
            // shape exactly: unbounded native work driven by a render loop.
            if (_metadataProbeAttempts >= MaxMetadataProbeAttempts)
                return;

            _metadataQueuedOrRunning = true;
            version = _deferredSourceVersion;
        }

        BitmapDecodeScheduler.Enqueue(this, version, BitmapDecodeJobKind.Metadata);
    }

    /// <summary>
    /// Reports the natural size discovered by a metadata job. Deliberately independent of the
    /// canonical size the decode publishes: only the decode's own <c>decoded.Width/Height</c> may
    /// feed <see cref="DecodeUpgradeNeededLocked"/>, because a header probe and a full decode can
    /// legitimately disagree (EXIF auto-rotation) and a disagreement there is a decode loop.
    /// </summary>
    internal bool TryGetNaturalMetadataSize(out int width, out int height)
    {
        lock (_deferredDecodeLock)
        {
            width = _metadataNaturalWidth;
            height = _metadataNaturalHeight;
            return width > 0 && height > 0;
        }
    }

    /// <summary>
    /// The INTRINSIC size an element may lay out with before any pixels exist, or
    /// <see langword="false"/> while there is no exact answer.
    /// </summary>
    /// <remarks>
    /// <para>Separate from <see cref="TryGetNaturalMetadataSize"/>, which reports what the header
    /// said. The intrinsic size is the CANONICAL one — the natural size with the author's decode
    /// options applied — and those two differ whenever a <c>SourceRect</c>, a <c>Rotation</c> or a
    /// <c>DecodePixelWidth</c>/<c>DecodePixelHeight</c> is set. Handing layout the header's answer
    /// there would size the element to something the decode is about to contradict.</para>
    /// <para>So this is exact or nothing: with identity options the header size IS the canonical
    /// size, and with any option set the source keeps reporting no intrinsic size until the decode
    /// publishes the real one, exactly as it did before this lane existed. A guess that
    /// self-corrects one frame later is a visible layout jump, and reproducing
    /// <c>ApplyDecodeOptions</c>'s geometry here would be a second copy of a policy that must not
    /// drift.</para>
    /// <para>Answers only from the probe. Once a raster is published, <see cref="Width"/> and
    /// <see cref="Height"/> carry the canonical size and are what every caller reads.</para>
    /// </remarks>
    internal bool TryGetIntrinsicMetadataSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        lock (_deferredDecodeLock)
        {
            if (!_decodeOptions.IsIdentity)
            {
                return false;
            }

            width = _metadataNaturalWidth;
            height = _metadataNaturalHeight;
            return width > 0 && height > 0;
        }
    }

    internal bool IsDeferredDecodePending
    {
        get
        {
            lock (_deferredDecodeLock)
            {
                return _isDeferredSource && _pixelSnapshot is null;
            }
        }
    }

    /// <summary>
    /// Reads the current publication as one consistent tuple.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="RawPixelData"/> + <see cref="PixelWidth"/> +
    /// <see cref="PixelHeight"/> + <see cref="PixelStride"/>: those are four independent reads,
    /// and a consumer that interleaves them with a publishing decode worker can hand a large
    /// dimension triple to a small buffer.
    /// </remarks>
    internal bool TryGetPixelSnapshot(out BitmapPixelSnapshot? snapshot)
    {
        snapshot = Volatile.Read(ref _pixelSnapshot);
        if (snapshot is not null)
            return true;

        // Legacy publication fallback, for the one state that has raw pixels and no snapshot:
        // ResetDeferredSource keeps the previous raster alive while LoadFromBytes re-decodes.
        //
        // This MUST be done under the decode lock. Validating the geometry is not enough on its
        // own, because a SMALLER dimension triple paired with a LARGER stale buffer is
        // arithmetically consistent and passes every check — a 200x200-at-stride-800 reading of a
        // resident 1024x1024 raster is a silently skewed texture, not a detectable error. Every
        // writer of the quad therefore publishes through PublishLegacyPixels, which holds this
        // same lock, so what is read here is always one writer's complete tuple.
        lock (_deferredDecodeLock)
        {
            snapshot = _pixelSnapshot;
            if (snapshot is not null)
                return true;

            var pixels = _rawPixelData;
            if (pixels is null)
                return false;

            // The RASTER geometry, which is what this buffer holds. Reading _width/_height here
            // was only correct while those mirrored the bucket; they are the canonical size now,
            // and pairing them with a bucketed buffer is exactly the out-of-range read this whole
            // snapshot channel exists to make unrepresentable.
            var width = _rasterPixelWidth;
            var height = _rasterPixelHeight;
            var stride = _pixelStride;
            if (width <= 0 || height <= 0 || stride < width * 4 ||
                (long)stride * height > pixels.Length)
            {
                return false;
            }

            var canonicalWidth = (int)Math.Round(_width);
            var canonicalHeight = (int)Math.Round(_height);
            snapshot = new BitmapPixelSnapshot(
                pixels,
                width,
                height,
                stride,
                NativePixelFormat.Bgra8,
                canonicalWidth > 0 ? canonicalWidth : width,
                canonicalHeight > 0 ? canonicalHeight : height,
                _legacyPixelGeneration);
            return true;
        }
    }

    /// <summary>
    /// Reads the publication a PUBLIC pixel consumer must see: one consistent tuple whose
    /// dimensions are the canonical ones <see cref="PixelWidth"/>/<see cref="PixelHeight"/> report.
    /// </summary>
    /// <remarks>
    /// <para><see cref="TryGetPixelSnapshot"/> hands back whatever the renderer currently holds,
    /// which for a deferred source is a display BUCKET. A 128x64 thumbnail of a 1024x512 asset is
    /// the right texture for a 100-DIP card and the wrong answer for the clipboard, an OLE drag
    /// image, the window icon, a notification or <c>CopyPixels</c> — all of which size their
    /// destination buffer from <c>PixelWidth</c>. Handing them the bucket either silently
    /// downgrades the image to thumbnail resolution or, once <c>PixelWidth</c> reports the
    /// canonical size, reads off the end of the buffer.</para>
    /// <para>The canonical raster is REBUILT rather than kept resident. Keeping it would defeat
    /// the display bucket entirely — the bucket exists so a 1910x823 photo shown in a 230x118 card
    /// does not hold 6 MiB for the life of the source — and a public pixel read is a rare,
    /// explicitly user-driven operation (copy, drag, save, set the window icon). One decode for
    /// one such operation is the same cost this framework paid on every path before the deferred
    /// decoder existed. It is deterministic too: <c>ApplyDecodeOptions</c> is pure over the
    /// decoded image, so it reproduces exactly the canonical raster the bucket was derived from.
    /// Nothing is published: a transient snapshot cannot disturb the upgrade predicate, whose
    /// whole termination argument rests on <c>_publishedBucketWidth</c> only ever growing.</para>
    /// <para>When the rebuild is impossible — no encoded bytes, or a decoder that has started
    /// failing — the resident bucket is UPSCALED to the canonical size. That is softer than the
    /// real thing and it is reported through the Release-live channel, but it is dimensionally
    /// correct: the alternatives are an out-of-range read (a black image) and returning nothing
    /// (no clipboard image at all).</para>
    /// </remarks>
    internal bool TryGetCanonicalPixelSnapshot(out BitmapPixelSnapshot? snapshot)
    {
        if (!TryGetPixelSnapshot(out snapshot) || snapshot is null)
            return false;

        var resident = snapshot;
        if (resident.Width == resident.CanonicalWidth && resident.Height == resident.CanonicalHeight)
            return true;

        byte[]? encoded;
        BitmapDecodeOptions options;
        lock (_deferredDecodeLock)
        {
            encoded = _imageData;
            options = _decodeOptions;
        }

        if (encoded is { Length: > 0 })
        {
            try
            {
                // Outside the decode lock on purpose: the render thread takes that lock on every
                // draw, and a full native decode is the last thing that may run underneath it.
                var decoded = ResolveDecoder().Decode(encoded);
                if (!options.IsIdentity)
                {
                    decoded = ApplyDecodeOptions(decoded, options);
                }

                if (decoded.Width == resident.CanonicalWidth &&
                    decoded.Height == resident.CanonicalHeight)
                {
                    var source = decoded.Pixels.Span;
                    var copy = new byte[source.Length];
                    source.CopyTo(copy);
                    snapshot = BitmapPixelSnapshot.Create(
                        copy,
                        decoded.Width,
                        decoded.Height,
                        decoded.Stride,
                        decoded.Format,
                        decoded.Width,
                        decoded.Height);
                    return true;
                }

                // A decoder that answers a different size for the same bytes than the one the
                // bucket was derived from. Not a crash — the upscale below still produces the
                // geometry the caller was promised — but it must be visible, because it means the
                // decoder is not deterministic and every size in this pipeline assumes it is.
                ImageDiagnostics.Degraded(
                    DiagnosticSourceName,
                    "a full-resolution re-decode reported a different natural size than the published one",
                    decoded.Width,
                    decoded.Height);
            }
            catch (Exception ex)
            {
                ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "canonical pixel read", ex);
            }
        }

        try
        {
            var upscaled = BitmapPixelResampler.ResampleTo(
                new DecodedImage(
                    resident.Pixels, resident.Width, resident.Height, resident.Stride, resident.Format),
                resident.CanonicalWidth,
                resident.CanonicalHeight);

            var pixels = upscaled.Pixels.Span;
            var copy = new byte[pixels.Length];
            pixels.CopyTo(copy);
            snapshot = BitmapPixelSnapshot.Create(
                copy,
                upscaled.Width,
                upscaled.Height,
                upscaled.Stride,
                upscaled.Format,
                upscaled.Width,
                upscaled.Height);

            ImageDiagnostics.Degraded(
                DiagnosticSourceName,
                "full-resolution pixels were unavailable; the resident display bucket was upscaled instead",
                resident.Width,
                resident.Height);
            return true;
        }
        catch (Exception ex)
        {
            // Nothing left that can answer the question. Reported rather than papered over: the
            // caller gets an exception naming a bitmap that has no readable pixels, which is
            // strictly better than a buffer whose contents nobody can account for.
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "canonical pixel upscale", ex);
            snapshot = null;
            return false;
        }
    }

    /// <summary>
    /// Publishes a raster produced OUTSIDE the deferred decoder — <c>FromPixels</c>,
    /// <c>FromMediaFrame</c>, <c>AdoptDecoded</c>, the reclaim restore, a clone — through the same
    /// immutable channel the deferred decoder uses.
    /// </summary>
    /// <remarks>
    /// <para>Every one of those sites used to assign <c>_rawPixelData</c>, <c>_width</c>,
    /// <c>_height</c> and <c>_pixelStride</c> as four independent, unsynchronized writes, and
    /// <c>TryRestorePixelData</c> does it from the render thread. A reader interleaving with any
    /// of them could pair one writer's buffer with another's dimensions. Publishing one immutable
    /// tuple under the decode lock makes that unrepresentable.</para>
    /// <para><paramref name="canonicalWidth"/>/<paramref name="canonicalHeight"/> default to the
    /// published size: a raster that did not come out of the display-bucket resampler IS its own
    /// natural size.</para>
    /// </remarks>
    private void PublishLegacyPixels(
        byte[] pixels,
        int width,
        int height,
        int stride,
        NativePixelFormat format,
        int canonicalWidth = 0,
        int canonicalHeight = 0)
    {
        // Built before the lock is taken and before anything is mutated: Create validates the
        // geometry and throws on a mismatch, and a throw must leave the previous publication
        // intact rather than half-replaced.
        var snapshot = BitmapPixelSnapshot.Create(
            pixels, width, height, stride, format, canonicalWidth, canonicalHeight);

        lock (_deferredDecodeLock)
        {
            _rawPixelData = snapshot.Pixels;
            _rasterPixelWidth = snapshot.Width;
            _rasterPixelHeight = snapshot.Height;
            _pixelStride = snapshot.Stride;

            // The intrinsic size is the CANONICAL one, which for every caller of this method is
            // the published size anyway (nothing here goes through the display-bucket resampler);
            // the one exception is a clone of a bucketed source, which passes its original's
            // canonical numbers through so the clone reports the same intrinsic size.
            _width = snapshot.CanonicalWidth;
            _height = snapshot.CanonicalHeight;

            _legacyPixelGeneration = snapshot.Generation;
            _canonicalPixelWidth = snapshot.CanonicalWidth;
            _canonicalPixelHeight = snapshot.CanonicalHeight;
            _publishedBucketWidth = snapshot.Width;
            _publishedBucketHeight = snapshot.Height;
            Volatile.Write(ref _pixelSnapshot, snapshot);
        }
    }

    internal void ProcessDeferredDecode(long sourceVersion)
    {
        if (TryStopSaturatedDecode(sourceVersion))
            return;

        Func<byte[]>? loader;
        byte[]? encoded;
        int attempt;
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;

            loader = _deferredSourceLoader;
            encoded = _imageData;
            attempt = ++_decodeAttempts;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var published = false;
        Exception? failure = null;
        var publishedWidth = 0;
        var publishedHeight = 0;
        var unproductiveAttempts = 0;
        var elapsedMs = 0d;
        var saturated = false;
        var enqueueUpgrade = false;

        try
        {
            encoded ??= loader?.Invoke()
                ?? throw new InvalidOperationException("The deferred bitmap source has no byte loader.");
            if (encoded.Length == 0)
                throw new InvalidDataException("The deferred bitmap source is empty.");

            DecodeRequest request;
            BitmapDecodeOptions options;
            lock (_deferredDecodeLock)
            {
                if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                    return;

                // Cache the encoded bytes BEFORE decoding, not after a successful publish. A
                // decoder that throws is re-requested by the next render pass, and caching only on
                // the success path made every one of those attempts re-open the file.
                _imageData = encoded;

                // Latch the target after I/O: layout may have refined the request while an
                // embedded resource or disk file was being opened. From here on this decode
                // answers THIS request; anything layout does afterwards is the upgrade
                // predicate's business, not this decode's.
                request = CaptureRequestLocked();
                options = _decodeOptions;
            }

            ImageDiagnostics.DecodeRequested(
                DiagnosticSourceName, request.Width, request.Height, attempt);

            var decoded = ResolveDecoder().Decode(encoded);

            // The author's crop / rotation / decode size, applied to the NATURAL image and BEFORE
            // the display-bucket resample. Both halves of that sentence are the fix: applying them
            // afterwards resolved SourceRect against a bucket whose edge is a function of the DPI
            // and the layout slot, so identical markup cropped a different region on a 150% display
            // than on a 100% one and threw for a legal rect whenever the bucket came out smaller
            // than the crop.
            //
            // The transformed image then becomes this source's CANONICAL size, so the bucket ladder
            // is capped by what the author asked for rather than by the encoded resolution. That is
            // safe for termination because ApplyDecodeOptions is pure: every later decode of the
            // same source resolves the same canonical numbers, so the upgrade predicate — which
            // compares against exactly those numbers — cannot ask for a bucket this method will
            // never produce.
            if (!options.IsIdentity)
            {
                try
                {
                    decoded = ApplyDecodeOptions(decoded, options);
                }
                catch (Exception ex)
                {
                    // An author error (a SourceRect outside the image, an overflowing decode size)
                    // is deterministic, so failing the decode would spend the whole failure budget
                    // and leave the element permanently blank over one bad rect. Report it and
                    // publish the untransformed raster instead: an uncropped picture is
                    // diagnosable, a blank one is not. The canonical size then describes the
                    // untransformed image, which is equally deterministic, so the chain still
                    // terminates.
                    ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "decode options", ex);
                }
            }

            // The canonical size is only known now, so this is the first moment the latched request
            // can be turned into a pixel count. The predicate resolves the same way from the same
            // canonical numbers, which is the whole reason the chain terminates. An explicit
            // decode size short-circuits the ladder inside this call and publishes the transformed
            // canonical raster verbatim, which ResampleTo below turns into a no-op.
            var (targetWidth, targetHeight) =
                ResolveBucketForRequest(decoded.Width, decoded.Height, request);
            var displayDecode = BitmapPixelResampler.ResampleTo(decoded, targetWidth, targetHeight);
            var pixels = displayDecode.Pixels.ToArray();

            TaskCompletionSource? completed;
            lock (_deferredDecodeLock)
            {
                if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                    return;

                var previousArea = (long)_publishedBucketWidth * _publishedBucketHeight;

                PublishDecodedPixelsLocked(decoded, displayDecode, pixels);
                _decodeQueuedOrRunning = false;

                // A publish ends any failure streak: whatever was wrong with the source (a locked
                // file, a stalled share) has cleared, so a later transient fault gets the full
                // retry budget again rather than inheriting a spent one.
                _failedDecodeAttempts = 0;

                var publishedArea = (long)_publishedBucketWidth * _publishedBucketHeight;
                if (publishedArea > previousArea)
                {
                    // Productive: this decode really did add pixels, so the next upgrade request
                    // is a new step up the bucket ladder rather than a repeat of this one.
                    _unproductiveDecodeAttempts = 0;
                }
                else
                {
                    // A full native decode that produced no more pixels than the previous one.
                    // Only a predicate bug reaches here; count it so the bound at the top of this
                    // method can stop the chain.
                    _unproductiveDecodeAttempts++;
                }

                // Complete on EVERY successful publish, including the ones that schedule an
                // upgrade. The completion means "there are pixels for the size you asked for",
                // not "the bucket ladder has finished climbing"; keeping the TCS alive across the
                // whole upgrade chain used to hang every awaiter for its full duration.
                completed = _deferredDecodeCompletion;
                _deferredDecodeCompletion = null;

                enqueueUpgrade = DecodeUpgradeNeededLocked();
                if (enqueueUpgrade)
                {
                    _decodeQueuedOrRunning = true;
                }

                saturated = DecodeChainSaturatedLocked();
                publishedWidth = _publishedBucketWidth;
                publishedHeight = _publishedBucketHeight;
                unproductiveAttempts = _unproductiveDecodeAttempts;
            }

            // Last statement in the try, on purpose. Everything after it — the diagnostics, the
            // upgrade enqueue, the notification — is bookkeeping about a decode that has ALREADY
            // succeeded, and must not be able to unwind into the catch below and have that decode
            // re-reported as a failure.
            elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            completed?.TrySetResult();
            published = true;
        }
        catch (Exception ex)
        {
            TaskCompletionSource? failed;
            bool terminal;
            lock (_deferredDecodeLock)
            {
                if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                    return;

                _decodeQueuedOrRunning = false;
                failed = _deferredDecodeCompletion;
                _deferredDecodeCompletion = null;

                // A decode that produced nothing must count against a bound, or a permanently
                // failing source has no terminal state at all: nothing is published, so nothing
                // looks saturated, and Image.OnRender re-arms a full native decode on every render
                // pass forever. It counts against the FAILURE bound rather than the unproductive
                // one because a throw is frequently transient and gets a delayed retry, while an
                // unproductive publish is deterministic and gets none — see
                // MaxFailedDecodeAttempts.
                _failedDecodeAttempts++;
                _lastDecodeFailureTimestamp = Stopwatch.GetTimestamp();
                unproductiveAttempts = _failedDecodeAttempts;
                terminal = DecodeChainSaturatedLocked();
                publishedWidth = _publishedBucketWidth;
                publishedHeight = _publishedBucketHeight;
            }

            failure = ex;
            failed?.TrySetException(ex);
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "deferred decode", ex);

            if (terminal)
            {
                // Terminal for this source version: RequestDecodeAsync short-circuits from here on
                // and the chain only re-arms when the source itself changes. Reported so a support
                // capture can tell "gave up" apart from "still trying".
                ImageDiagnostics.BucketSaturated(
                    DiagnosticSourceName,
                    publishedWidth,
                    publishedHeight,
                    unproductiveAttempts,
                    "decode chain stopped: consecutive decodes failed");
            }
        }

        // Everything below is outside the try, and that placement is load-bearing. These calls run
        // application code — a diagnostics subscriber, and a notification handler that
        // BitmapDecodeNotifier may deliver inline on this very thread — and a handler that throws
        // must not be able to unwind into the catch above and have a SUCCESSFUL decode re-reported
        // as a failure, which nulls Image.Source and destroys any binding to it. The decode worker
        // catches whatever escapes from here (BitmapDecodeScheduler.RunWorker).
        if (published)
        {
            ImageDiagnostics.DecodeCompleted(
                DiagnosticSourceName, publishedWidth, publishedHeight, attempt, elapsedMs);

            if (saturated)
            {
                ImageDiagnostics.BucketSaturated(
                    DiagnosticSourceName, publishedWidth, publishedHeight, unproductiveAttempts);
            }

            if (enqueueUpgrade)
            {
                ImageDiagnostics.UpgradeScheduled(
                    DiagnosticSourceName, publishedWidth, publishedHeight, attempt);
                BitmapDecodeScheduler.Enqueue(this, sourceVersion, BitmapDecodeJobKind.Pixels);
            }

            BitmapDecodeNotifier.Post(this, sourceVersion, success: true, error: null);

            // Multi-frame detection, from the bytes this decode already read. It belongs to the
            // metadata probe by design, but only an Image element that needs an intrinsic size
            // arms one — see ResolveMultiFrameSubstitute — so without this an animated GIF painted
            // through a brush, or one whose element already knows its size, would render exactly
            // one static frame forever. Once per source version: the resolved flag makes every
            // later upgrade decode skip it.
            //
            // Last, AFTER the completion post, because decoding every frame of a long animation
            // takes far longer than the single frame this job just published. Announcing the
            // static raster first lets the element paint frame zero immediately and switch to
            // animating when the substitute lands, instead of showing nothing until then.
            if (encoded is not null)
            {
                ResolveMultiFrameSubstitute(sourceVersion, encoded);
            }
        }
        else if (failure is not null)
        {
            BitmapDecodeNotifier.Post(this, sourceVersion, success: false, error: failure);
        }
    }

    /// <summary>
    /// Recovers from an exception that escaped a decode job entirely.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessDeferredDecode"/> handles its own failures, so reaching here means
    /// something outside its try/catch threw — an application handler running inline inside a
    /// notification post, or an allocation failure. The decode worker must not simply drop the
    /// source: <c>_decodeQueuedOrRunning</c> would stay set and the source would never be
    /// re-enqueued, leaving that one image blank forever with no record.
    /// </remarks>
    internal void AbandonDecodeAfterWorkerFault(long sourceVersion, Exception error)
    {
        TaskCompletionSource? failed;
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;

            _decodeQueuedOrRunning = false;
            _metadataQueuedOrRunning = false;
            failed = _deferredDecodeCompletion;
            _deferredDecodeCompletion = null;

            // Same bound and same delayed retry as a decode that threw inside its own try/catch:
            // the cause is outside the decoder either way, and it is just as likely to be
            // transient.
            _failedDecodeAttempts++;
            _lastDecodeFailureTimestamp = Stopwatch.GetTimestamp();
        }

        failed?.TrySetException(error);
        ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "decode worker", error);
    }

    /// <summary>
    /// Runs a metadata-only job: resolve the encoded bytes on a worker, read the natural dimensions
    /// without decoding pixels, and — when the payload turns out to be multi-frame — build the
    /// internal <see cref="AnimatedSubstitute"/>.
    /// </summary>
    /// <remarks>
    /// <para>Building the substitute here means one job decodes every frame of an animated source,
    /// which is far more than the header read the metadata queue is otherwise sized for. That is a
    /// deliberate trade against the two alternatives. Doing it on the UI thread (which is what
    /// <c>ImageSourceLoader.FromBytes</c> does, and what any dispatcher-side materialisation would
    /// do) puts a whole GIF's frame decoding on the thread that has to stay responsive. Deferring
    /// it to the pixel job introduces an ordering hazard: with two or more workers the pixel decode
    /// can finish before the probe, and once the bucket ladder is saturated no further pixel job
    /// runs, so the source would stay silently static. Here it happens exactly once per source
    /// version, ahead of the first draw, with the encoded bytes already in hand.</para>
    /// <para>A multi-frame source is rare, the scheduler has a worker floor of two plus a stall
    /// watchdog that can add one more, and the job is bounded by
    /// <see cref="MaxMetadataProbeAttempts"/> — so a slow one cannot wedge the queue.</para>
    /// </remarks>
    internal void ProcessDeferredMetadata(long sourceVersion)
    {
        Func<byte[]>? loader;
        byte[]? encoded;
        int attempt;
        lock (_deferredDecodeLock)
        {
            // A stale version's flag belongs to whoever bumped the version — ConfigureDeferredSource
            // and ResetDeferredSource already cleared it — so this must NOT clear it, or a job
            // queued by the NEW version loses its de-duplication and a second one is enqueued.
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;

            if (_metadataProbeAttempts >= MaxMetadataProbeAttempts)
            {
                _metadataQueuedOrRunning = false;
                return;
            }

            loader = _deferredSourceLoader;
            encoded = _imageData;
            attempt = ++_metadataProbeAttempts;
        }

        var exhausted = false;
        try
        {
            encoded ??= loader?.Invoke()
                ?? throw new InvalidOperationException("The deferred bitmap source has no byte loader.");
            if (encoded.Length == 0)
                throw new InvalidDataException("The deferred bitmap source is empty.");

            lock (_deferredDecodeLock)
            {
                if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                    return;

                // Cache the encoded bytes BEFORE probing them, exactly as ProcessDeferredDecode
                // does. Caching only on the success path made every retry — and every subsequent
                // pixel decode — re-open the file or the manifest resource for a source whose
                // header the decoder cannot read.
                _imageData = encoded;
            }

            var hasSize = ResolveDecoder().TryReadDimensions(encoded, out var width, out var height);

            var resolved = false;
            lock (_deferredDecodeLock)
            {
                if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                    return;

                _metadataQueuedOrRunning = false;
                if (hasSize && width > 0 && height > 0)
                {
                    _metadataNaturalWidth = width;
                    _metadataNaturalHeight = height;
                    resolved = true;
                }
                else
                {
                    // No throw, no dimensions: a container this decoder has no header reader for.
                    // It counts against the same bound as a throw, because the retry would read
                    // the same bytes and reach the same answer.
                    exhausted = _metadataProbeAttempts >= MaxMetadataProbeAttempts;
                }
            }

            if (resolved)
            {
                // The whole point of the lane. An intrinsic size that nobody is told about is one
                // the element only reads if it happens to measure again for an unrelated reason,
                // so without this the first layout pass still sizes to zero and the page still
                // reflows when the pixels land — which is the defect, one lane over.
                //
                // Announced ONLY when the dimensions actually resolved, so a probe that keeps
                // failing cannot drive a measure/probe cycle of its own; and on its own channel
                // rather than on OnImageLoaded, because that event tells an application there are
                // pixels and a header read has produced none.
                //
                // Inside the try, unlike the completion post at the end of ProcessDeferredDecode,
                // and deliberately: it must precede the multi-frame probe below, which decodes
                // every frame of an animation and can take orders of magnitude longer than this
                // header read. That is safe because the notifier isolates every handler it
                // delivers, and because the worst case if something did unwind into the catch is a
                // duplicate diagnostics record — the probe's own state is already committed, and
                // no raster and no Source can be lost by it.
                BitmapDecodeNotifier.PostMetadata(this, sourceVersion);
            }

            // Last, and outside the lock: it decodes every frame of a multi-frame payload through a
            // third-party INativeImageDecoder, and neither may happen while the decode lock — which
            // the render thread takes on every draw — is held. Placed after the dimensions are
            // published so a payload whose frames will not decode still delivers its intrinsic size.
            ResolveMultiFrameSubstitute(sourceVersion, encoded);
        }
        catch (Exception ex)
        {
            lock (_deferredDecodeLock)
            {
                if (_isDeferredSource && sourceVersion == _deferredSourceVersion)
                {
                    _metadataQueuedOrRunning = false;
                    exhausted = _metadataProbeAttempts >= MaxMetadataProbeAttempts;
                }
            }

            // Metadata is advisory — the pixel decode reports the same failure through the
            // completion task — but it must still be visible in a Release build.
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "metadata probe", ex);
        }

        if (exhausted)
        {
            // Terminal for this source version. Reported so a support capture can tell "the
            // intrinsic size is still coming" apart from "this source will never report one",
            // which is the difference between a layout that settles and one that does not.
            ImageDiagnostics.Degraded(
                DiagnosticSourceName,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"metadata probe stopped after {attempt} attempts; intrinsic size unavailable"),
                0,
                0);
        }
    }

    /// <summary>
    /// Resolves the payload's frame count once per source version and, when it turns out to be
    /// multi-frame, builds and publishes the animated substitute.
    /// </summary>
    /// <remarks>
    /// <para>Called from BOTH job kinds, and that is not redundancy. The metadata probe is the
    /// natural home — it is drained ahead of every pixel job, so the animation starts as early as
    /// possible — but it is armed by exactly one caller, <c>Image.TryGetSourceSize</c>, and only
    /// while that element has no intrinsic size to lay out with. A source painted by an
    /// <c>ImageBrush</c>, an <c>ImageDrawing</c> or a <c>Shape.Fill</c> is never measured by an
    /// <c>Image</c> at all, and one whose size is already resident stops asking. The pixel decode
    /// therefore resolves the frame count too, from the same encoded bytes it has already read, and
    /// whichever job arrives first does the work. Without the pixel-side call an animated GIF
    /// painted as a brush would stay on frame zero forever.</para>
    /// <para>The resolved flag is set BEFORE the probe runs, so a decoder that throws on
    /// <c>ReadFrameCount</c> costs one attempt rather than one per decode: the read is
    /// deterministic over the same bytes, so retrying it can only reach the same answer while
    /// re-entering a native codec on every render pass.</para>
    /// </remarks>
    private void ResolveMultiFrameSubstitute(long sourceVersion, byte[] encoded)
    {
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion || _frameCountResolved)
                return;

            _frameCountResolved = true;
        }

        int frameCount;
        try
        {
            frameCount = ResolveDecoder().ReadFrameCount(encoded);
        }
        catch (Exception ex)
        {
            // The source keeps its own single-frame raster, so this degrades rather than failing —
            // but it must be visible in a Release build, because "the GIF does not animate" has no
            // other symptom.
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "frame count probe", ex);
            return;
        }

        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;

            _metadataFrameCount = frameCount;
        }

        if (frameCount > 1)
        {
            PublishAnimatedSubstitute(sourceVersion, encoded);
        }
    }

    /// <summary>
    /// Decodes every frame of a multi-frame payload on the calling worker and publishes the result
    /// as this source's <see cref="AnimatedSubstitute"/>.
    /// </summary>
    /// <remarks>
    /// The substitute is published to the render thread here but deliberately NOT started here:
    /// starting it creates a <c>DispatcherTimer</c>, which binds to the CURRENT thread's dispatcher
    /// — on a pooled decode worker that is a dispatcher nothing will ever pump, and it can claim
    /// the process's main-dispatcher slot outright if it is the first to touch one. The start is
    /// posted to <see cref="BitmapDecodeNotifier"/>, which delivers on the main dispatcher. Until
    /// then the substitute still renders, frozen on frame zero, which is strictly better than the
    /// static frame it replaces.
    /// </remarks>
    private void PublishAnimatedSubstitute(long sourceVersion, byte[] encoded)
    {
        AnimatedBitmap? substitute;
        try
        {
            substitute = AnimatedBitmap.TryCreateDetached(encoded);
        }
        catch (Exception ex)
        {
            // A payload whose header claims several frames but whose frame data will not decode.
            // The source keeps its own single-frame raster, which is exactly today's behaviour, so
            // this degrades rather than failing — but it must be visible in a Release build,
            // because "the GIF does not animate" has no other symptom.
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "animated substitute", ex);
            return;
        }

        if (substitute is null)
        {
            // The frame-count probe and the frame decoder disagreed. Nothing is wrong with the
            // source; it simply is not animated after all.
            return;
        }

        bool published;
        lock (_deferredDecodeLock)
        {
            // The source may have been replaced while the frames were decoding. Publishing then
            // would attach an animation belonging to the previous URI to the new one.
            published = _isDeferredSource &&
                        sourceVersion == _deferredSourceVersion &&
                        Volatile.Read(ref _animatedSubstitute) is null;
            if (published)
            {
                _ownsAnimatedSubstitute = true;
                _animatedSubstituteStarted = false;
                Volatile.Write(ref _animatedSubstitute, substitute);
            }
        }

        if (!published)
        {
            RetireAnimatedSubstituteUnowned(substitute);
            return;
        }

        BitmapDecodeNotifier.PostAnimatedSubstitute(this, sourceVersion);
    }

    /// <summary>
    /// Disposes a substitute that was built but never published, so its frames are not left to the
    /// finalizer. Separate from <see cref="RetireAnimatedSubstitute"/> because there is no frame
    /// subscription to undo.
    /// </summary>
    private void RetireAnimatedSubstituteUnowned(AnimatedBitmap substitute)
    {
        try
        {
            substitute.Dispose();
        }
        catch (Exception ex)
        {
            ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "animated substitute teardown", ex);
        }
    }

    /// <summary>
    /// Completes the substitute's dispatcher-affine setup and announces it. Invoked by
    /// <see cref="BitmapDecodeNotifier"/> on the main dispatcher.
    /// </summary>
    internal void RaiseAnimatedSubstituteReady(long sourceVersion)
    {
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;
        }

        StartAnimatedSubstitute();

        // Every cache in the process keys its upload on the source's reference identity, and the
        // pixels behind this identity just became the substitute's current frame rather than this
        // instance's own raster. Dropping first and asking for the repaint second is the same order
        // a decode publish uses, and for the same reason.
        RaiseRasterChanged(this);
        RaiseImageLoadedPerSubscriber();
    }

    /// <summary>
    /// The hard bound. Runs before any loader invocation or decode so a saturated source costs
    /// nothing but a lock acquisition, no matter how often a renderer re-requests it.
    /// </summary>
    /// <returns><see langword="true"/> when the caller must abandon this decode.</returns>
    private bool TryStopSaturatedDecode(long sourceVersion)
    {
        TaskCompletionSource? completed;
        int publishedWidth;
        int publishedHeight;
        int attempts;
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return true;

            // NO exemption for "there is no snapshot yet". The first version of this guard skipped
            // the bound whenever _pixelSnapshot was null, on the theory that a decode either
            // publishes or throws and a throw is terminal. A throw is NOT terminal by itself: it
            // clears _decodeQueuedOrRunning and publishes nothing, and Image.OnRender re-requests
            // on every render pass, so an un-decodable source was re-decoded once per frame
            // forever. Counting failures (see the catch in ProcessDeferredDecode) plus this
            // unconditional bound is what gives a failing source a terminal state.
            if (!DecodeChainSaturatedLocked())
                return false;

            _decodeQueuedOrRunning = false;
            completed = _deferredDecodeCompletion;
            _deferredDecodeCompletion = null;
            publishedWidth = _publishedBucketWidth;
            publishedHeight = _publishedBucketHeight;
            attempts = _unproductiveDecodeAttempts;
        }

        // Whatever pixels are already in hand are a valid, if smaller, answer, and for a chain
        // that never published there is nothing left to wait for — complete rather than cancel so
        // an awaiter is released instead of hanging for the life of the source. The failure itself
        // was already delivered to the awaiter that was live at the time, and to ImageDiagnostics.
        completed?.TrySetResult();
        ImageDiagnostics.BucketSaturated(
            DiagnosticSourceName, publishedWidth, publishedHeight, attempts);
        return true;
    }

    /// <summary>
    /// The one publication point. Everything a consumer or the upgrade predicate can read about
    /// the current raster is written here, under <c>_deferredDecodeLock</c>, in one place.
    /// </summary>
    private void PublishDecodedPixelsLocked(
        DecodedImage decoded,
        DecodedImage displayDecode,
        byte[] pixels)
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        // Build first, mutate second. Create validates the geometry and throws on a mismatch, and
        // the throw must leave the mirrors describing the PREVIOUS publication rather than a
        // publication that does not exist — a non-zero _publishedBucketWidth with a null snapshot
        // would make the upgrade predicate and the saturation counter disagree about reality.
        var snapshot = BitmapPixelSnapshot.Create(
            pixels,
            displayDecode.Width,
            displayDecode.Height,
            displayDecode.Stride,
            displayDecode.Format,
            decoded.Width,
            decoded.Height);

        _canonicalPixelWidth = snapshot.CanonicalWidth;
        _canonicalPixelHeight = snapshot.CanonicalHeight;
        _publishedBucketWidth = snapshot.Width;
        _publishedBucketHeight = snapshot.Height;

        // Two mirrors, and keeping them apart is the point. The RASTER pair describes the buffer
        // in RawPixelData and therefore tracks the bucket; the INTRINSIC pair behind
        // Width/Height/PixelWidth/PixelHeight is the canonical size and does not move as the
        // bucket ladder climbs.
        //
        // Mirroring the bucket into the intrinsic pair — which is what this used to do — fed the
        // display bucket straight back into layout. With Stretch.None/UpOnly the draw rect IS the
        // intrinsic size and the renderer turns the draw rect into the next decode hint, so the
        // 25% headroom plus the next-power-of-two step answered with a bigger bucket every time:
        // an ordinary fixed-size grid cell cost three or more full native decodes plus a
        // measure/arrange of its subtree per publish, drew at the wrong scale in between, and
        // permanently defeated Image.OnSourceAsyncLoaded's measure-skip. It also made every public
        // reader — the clipboard, the OLE drag source, the window icon, notifications,
        // BitmapPalette — report thumbnail resolution for a full-resolution asset.
        _rawPixelData = snapshot.Pixels;
        _rasterPixelWidth = snapshot.Width;
        _rasterPixelHeight = snapshot.Height;
        _pixelStride = snapshot.Stride;
        _width = snapshot.CanonicalWidth;
        _height = snapshot.CanonicalHeight;
        _legacyPixelGeneration = snapshot.Generation;

        Volatile.Write(ref _pixelSnapshot, snapshot);
    }

    /// <summary>
    /// Decides whether another native decode can produce a strictly bigger raster than the one
    /// already published.
    /// </summary>
    /// <remarks>
    /// <para>This is the RC1 fix and it works for exactly one reason: it asks
    /// <see cref="BitmapPixelResampler.ResolveBucket"/> — the same function
    /// <see cref="BitmapPixelResampler.ResizeToDisplayBucket"/> resamples to — what the next
    /// decode WOULD publish, using the natural size the last decode actually reported. So the
    /// answer is "no" the instant another decode would be a no-op, whatever the request is.</para>
    /// <para>Do not replace this with a comparison against the REQUEST. A request can exceed the
    /// natural size and can exceed <see cref="BitmapPixelResampler.MaxBucketEdge"/>; neither is
    /// ever satisfiable, and demanding satisfaction is what made every image on the page go black
    /// on machines with few enough cores to run a single decode worker.</para>
    /// <para>A source carrying an explicit <c>DecodePixelWidth</c>/<c>DecodePixelHeight</c>
    /// terminates on its first decode: <see cref="ResolveBucketForRequest"/> answers the author's
    /// size for every request, so the target resolved here equals the one the producer already
    /// published and the strictly-greater test below is false forever after.</para>
    /// </remarks>
    private bool DecodeUpgradeNeededLocked()
    {
        Debug.Assert(Monitor.IsEntered(_deferredDecodeLock));

        // FIRST, ahead of the "nothing published yet" clause. A source whose decodes keep failing
        // never publishes a snapshot, so a null-snapshot shortcut placed above this would re-arm
        // an un-decodable source on every render pass for the life of the process.
        if (DecodeChainSaturatedLocked())
            return false;

        // Second, and for the same reason: a source inside its post-failure retry delay must not
        // be re-enqueued by the very next render pass, or the whole failure budget is spent in
        // three frames and a transient fault becomes a permanent blank.
        if (DecodeRetryDeferredLocked())
            return false;

        if (_pixelSnapshot is null)
            return true;

        if (_canonicalPixelWidth <= 0 || _canonicalPixelHeight <= 0)
            return false;

        // Test seam only. Deliberately placed BELOW the saturation check so a broken predicate —
        // which is what this seam simulates — still cannot outrun the hard bound.
        if (_upgradePredicateOverrideForTests is { } forced)
            return forced();

        var target = ResolveRequestedBucketLocked(_canonicalPixelWidth, _canonicalPixelHeight);

        // Strictly greater AREA, not "different": every candidate bucket is aspect-preserving and
        // monotonic in its power-of-two edge, so equal area means the same bucket and a repeat
        // decode. Comparing areas also collapses the degenerate cases (a 1x1 source, an axis that
        // clamps to 1) into "nothing to gain" instead of into an oscillation.
        return (long)target.Width * target.Height >
               (long)_publishedBucketWidth * _publishedBucketHeight;
    }

    /// <summary>
    /// Raises the load/failure events for one completed decode. Invoked by
    /// <see cref="BitmapDecodeNotifier"/> on the main dispatcher — never inline on a decode worker
    /// — because these handlers reach <c>Image.OnSourceAsyncLoaded</c> and therefore
    /// <c>InvalidateMeasure</c>/<c>InvalidateVisual</c>.
    /// </summary>
    internal void RaiseDecodeNotification(long sourceVersion, bool success, Exception? error)
    {
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;
        }

        if (success)
        {
            ClearLoadFailure();

            // Announced BEFORE any element-level handler runs, and before the batch-completed
            // signal that drives the repaint. A publish replaces the pixels behind an identity
            // that every GPU bitmap cache keys on by REFERENCE, so a cache that is never told
            // keeps serving the superseded texture: the upgrade decode runs, costs its full
            // native decode, and changes nothing on screen. Ordering matters — drop first, ask
            // for the repaint second, or the repaint re-reads the entry that is about to be
            // dropped.
            //
            // Raised from here rather than from the publish itself because this method runs on
            // the main dispatcher (BitmapDecodeNotifier guarantees it), so subscribers are reached
            // on one known thread instead of on whichever pool thread happened to finish the
            // decode. The main dispatcher is NOT the owner of every subscriber's state, though:
            // the GPU bitmap caches belong to the render thread for as long as one is running, so
            // a handler that reaches thread-affine state has to hand the work to its own owner
            // instead of doing it inline. RenderTargetDrawingContext queues the eviction and
            // applies it at the top of its next frame for exactly that reason.
            RaiseRasterChanged(this);

            RaiseImageLoadedPerSubscriber();
        }
        else if (error is not null)
        {
            // Diagnosed already, on the thread that failed: the decode worker reported this exact
            // exception with the stage and attempt it failed on (ProcessDeferredDecode's catch, and
            // AbandonDecodeAfterWorkerFault for a fault outside the decoder). This half of the
            // report is the APPLICATION channel — LoadFailed -> Image.OnSourceLoadFailed ->
            // ImageFailed — which has to run here, on the main dispatcher. Re-reporting it would
            // double-count DecodesFailed and make one failure read as two in a support capture.
            ReportDiagnosedLoadFailure(error);
            OnDecodeFailed(error);
        }
    }

    /// <summary>
    /// Raised on the main dispatcher once the header probe has resolved this source's natural
    /// size, before any pixels have been decoded.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="OnImageLoaded"/>. That event is the application's "the image is
    /// here" signal and a handler is entitled to read pixels from it; a metadata resolution has
    /// none, so raising it there would make <c>CopyPixels</c> throw for an application doing
    /// exactly what the event invites. This channel says only "the intrinsic size is known now",
    /// which is what an element needs in order to stop measuring at zero.
    /// </remarks>
    internal event EventHandler? MetadataResolved;

    /// <summary>
    /// Announces a resolved intrinsic size. Invoked by <see cref="BitmapDecodeNotifier"/> on the
    /// main dispatcher, because subscribers reach <c>InvalidateMeasure</c>.
    /// </summary>
    internal void RaiseMetadataResolved(long sourceVersion)
    {
        lock (_deferredDecodeLock)
        {
            if (!_isDeferredSource || sourceVersion != _deferredSourceVersion)
                return;
        }

        RaisePerSubscriber(MetadataResolved, "metadata-resolved handler");
    }

    /// <summary>
    /// Invokes <see cref="OnImageLoaded"/> one subscriber at a time so a throw from any of them
    /// cannot strand the others.
    /// </summary>
    /// <remarks>
    /// <para>A bare multicast invoke stops at the first exception and never reaches the later
    /// entries in the list, and the ORDER of this list is whatever order the subscribers happened
    /// to attach in. <c>Image.OnSourceAsyncLoaded</c> — the element's only
    /// <c>InvalidateMeasure</c>/<c>InvalidateVisual</c> — attaches when <c>Image.Source</c> is set,
    /// so any application handler attached to the source beforehand runs ahead of it. One throwing
    /// application handler therefore used to cost the element its repaint entirely: a successfully
    /// decoded, fully resident raster that nothing ever painted, with the decode chain already
    /// terminal so no later completion could repair it. That was originally reached through the
    /// framework's own decode-option re-application handler, which no longer exists — the options
    /// are applied inside the decode now — but the hazard is a property of multicast delegates,
    /// not of that one subscriber.</para>
    /// <para>Isolation is per subscriber and never silent: every failure is reported through the
    /// Release-live diagnostics channel. It deliberately does NOT route to
    /// <c>ReportLoadFailure</c> — a handler that threw is not a failure to load the source, and
    /// reporting it as one would take the element's whole image away over an application bug.</para>
    /// </remarks>
    private void RaiseImageLoadedPerSubscriber() =>
        RaisePerSubscriber(OnImageLoaded, "image-loaded handler");

    /// <summary>
    /// Walks <paramref name="handler"/>'s invocation list, isolating each subscriber. See
    /// <see cref="RaiseImageLoadedPerSubscriber"/> for why a bare multicast invoke is not enough.
    /// </summary>
    /// <param name="stage">Names the channel in a diagnostics record.</param>
    private void RaisePerSubscriber(EventHandler? handler, string stage)
    {
        if (handler is null)
            return;

        var subscribers = handler.GetInvocationList();
        for (var i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((EventHandler)subscribers[i])(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ImageDiagnostics.DecodeFailed(DiagnosticSourceName, stage, ex);
            }
        }
    }

    /// <summary>
    /// Stable identity used in diagnostics records. Reads the backing field rather than the
    /// <c>UriSource</c> dependency property because decode workers call this off the UI thread.
    /// </summary>
    internal string DiagnosticSourceName => _uriSource?.ToString() ?? "<bitmap>";

    /// <inheritdoc />
    /// <remarks>
    /// The URI is what a support capture is searched by, so the base class's type-name fallback
    /// would make every bitmap in the trace indistinguishable from every other.
    /// </remarks>
    private protected override string DiagnosticIdentity => DiagnosticSourceName;

    private void CancelDeferredDecode()
    {
        TaskCompletionSource? canceled;
        AnimatedBitmap? retiredSubstitute;
        lock (_deferredDecodeLock)
        {
            canceled = _deferredDecodeCompletion;
            _deferredDecodeCompletion = null;
            _deferredSourceVersion++;
            _deferredSourceLoader = null;
            _decodeQueuedOrRunning = false;
            _metadataQueuedOrRunning = false;
            _isDeferredSource = false;
            retiredSubstitute = DetachAnimatedSubstituteLocked();
        }

        RetireAnimatedSubstitute(retiredSubstitute);
        canceled?.TrySetCanceled();
    }

    /// <summary>
    /// Copies the deferred-decode half of this bitmap's state into a clone.
    /// </summary>
    /// <remarks>
    /// <para><b>What a clone shares, and why that is safe.</b> The published
    /// <see cref="BitmapPixelSnapshot"/> is shared BY REFERENCE. That is sound precisely because
    /// the snapshot is immutable — nothing ever writes to its buffer after
    /// <see cref="BitmapPixelSnapshot.Create"/> returns, so two owners cannot observe each other's
    /// changes. It also means a frozen clone reports the same
    /// <see cref="NaturalPixelWidth"/>/<see cref="NaturalPixelHeight"/> as its source and costs no
    /// second copy of what can be a multi-megabyte raster. The mirrored bucket and canonical
    /// numbers come along with it, so the clone's upgrade predicate starts from the same place
    /// rather than re-decoding from scratch.</para>
    /// <para><b>What a clone does NOT take.</b> The in-flight decode state:
    /// <c>_decodeQueuedOrRunning</c>, <c>_metadataQueuedOrRunning</c> and
    /// <c>_deferredDecodeCompletion</c> all describe work the SOURCE has queued, and a clone that
    /// inherited them would either wait forever on a completion nothing will ever set or, worse,
    /// believe a job is already queued and never enqueue its own. The clone starts with no queued
    /// work and no awaiter, and its first draw enqueues whatever it needs.</para>
    /// <para><b>The animated substitute is shared but NOT owned.</b> It carries a running
    /// <c>DispatcherTimer</c> and a set of decoded frames; disposing it from a clone would stop the
    /// original's animation and free frames the original is still drawing. The clone therefore
    /// renders through the same substitute and leaves its lifetime entirely to the source — see
    /// <see cref="DetachAnimatedSubstituteLocked"/>. It also does not subscribe its own frame
    /// handler: the source's subscription already drives the repaint, and a second one would keep
    /// the clone alive for as long as the animation runs.</para>
    /// </remarks>
    private void CopyDeferredStateFrom(BitmapImage source)
    {
        lock (source._deferredDecodeLock)
        {
            // Latched independently of _isDeferredSource: the options are authored on the facade,
            // not produced by the decoder, so a clone of an eagerly-loaded bitmap must carry them
            // too or a later LoadFromBytes on the clone would drop the author's crop.
            _decodeOptions = source._decodeOptions;

            if (!source._isDeferredSource)
                return;

            _deferredSourceVersion++;
            _deferredSourceLoader = source._deferredSourceLoader;
            _isDeferredSource = true;
            _requestedDecodeWidth = source._requestedDecodeWidth;
            _requestedDecodeHeight = source._requestedDecodeHeight;
            _requestedWidthOnlyMax = source._requestedWidthOnlyMax;
            _requestedHeightOnlyMax = source._requestedHeightOnlyMax;
            _requestedDecodeCover = source._requestedDecodeCover;
            _canonicalPixelWidth = source._canonicalPixelWidth;
            _canonicalPixelHeight = source._canonicalPixelHeight;
            _publishedBucketWidth = source._publishedBucketWidth;
            _publishedBucketHeight = source._publishedBucketHeight;
            _unproductiveDecodeAttempts = source._unproductiveDecodeAttempts;
            _failedDecodeAttempts = source._failedDecodeAttempts;
            _lastDecodeFailureTimestamp = source._lastDecodeFailureTimestamp;
            _decodeAttempts = source._decodeAttempts;
            _metadataNaturalWidth = source._metadataNaturalWidth;
            _metadataNaturalHeight = source._metadataNaturalHeight;
            _metadataFrameCount = source._metadataFrameCount;
            _metadataProbeAttempts = source._metadataProbeAttempts;
            _metadataQueuedOrRunning = false;

            // Shared, never owned, never started a second time. See the remarks above.
            Volatile.Write(ref _animatedSubstitute, Volatile.Read(ref source._animatedSubstitute));
            _ownsAnimatedSubstitute = false;
            _animatedSubstituteStarted = true;

            // The snapshot is immutable, so a clone may share the instance by reference. The
            // in-flight decode state deliberately does NOT come along: the clone owns no queued
            // work and no awaiter.
            var snapshot = Volatile.Read(ref source._pixelSnapshot);
            Volatile.Write(ref _pixelSnapshot, snapshot);
            _legacyPixelGeneration = source._legacyPixelGeneration;
            if (snapshot is not null)
            {
                // CopyBitmapStateFrom ran first and duplicated the pixel buffer. Re-point the
                // legacy mirror at the shared snapshot so the two can never describe different
                // buffers, and so the clone does not retain a second copy of the same raster.
                // Raster geometry from the buffer, intrinsic geometry from the canonical numbers —
                // crossing the two is what pairs a canonical dimension triple with a bucket.
                _rawPixelData = snapshot.Pixels;
                _rasterPixelWidth = snapshot.Width;
                _rasterPixelHeight = snapshot.Height;
                _pixelStride = snapshot.Stride;
                _width = snapshot.CanonicalWidth;
                _height = snapshot.CanonicalHeight;
            }

            _decodeQueuedOrRunning = false;
            _deferredDecodeCompletion = null;
        }
    }
}
