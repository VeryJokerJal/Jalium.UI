using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI.Media;

/// <summary>
/// The class of failure behind <see cref="ImageSource.LoadFailure"/>.
/// </summary>
/// <remarks>
/// <para>The distinction decides one thing: whether an element is entitled to stop painting the
/// source. It is not cosmetic. A <see cref="Load"/> failure means there are no pixels and no later
/// frame can invent any, so skipping the draw costs nothing and avoids painting a broken thing. An
/// <see cref="Upload"/> failure means the pixels are perfectly good and the DEVICE refused them —
/// VRAM pressure, a device-removed frame, a texture dimension over the adapter's limit on a
/// WARP/RDP adapter — and the only thing that ever clears one is RETRYING the upload on the next
/// frame.</para>
/// <para>Gating the draw on an upload failure therefore removes the very retry that would clear it,
/// so one bad frame on one machine becomes a permanently blank element for the life of the window:
/// the exact reported symptom this whole pipeline exists to eliminate, reintroduced through the
/// failure path. Anything deciding whether to paint must therefore take the failure and its class
/// together, from <see cref="ImageSource.TryGetLoadFailure"/>, rather than reading
/// <see cref="ImageSource.LoadFailure"/> on its own.</para>
/// </remarks>
internal enum ImageSourceFailureKind
{
    /// <summary>The source could not produce pixels: a missing file, a codec fault, a bad decode.</summary>
    Load,

    /// <summary>The pixels exist and the GPU refused them. Usually transient; always retryable.</summary>
    Upload,
}

/// <summary>
/// Represents the source of an image.
/// </summary>
public abstract class ImageSource : Animation.Animatable, IFormattable
{
    /// <summary>
    /// A latched failure together with its class, published as ONE object.
    /// </summary>
    /// <remarks>
    /// One reference rather than two fields is what makes the pair atomic. The render backend
    /// latches an upload failure from the render thread while an element reads the class of it on
    /// the UI thread, and two independent fields let that reader pair a live exception with the
    /// previous failure's class — i.e. suppress the draw for an upload failure, which is the
    /// permanently blank element this classification exists to prevent. A single reference also
    /// makes "claim the report" and "release it once the retry succeeded" plain compare-exchanges
    /// with no lock on any path.
    /// </remarks>
    private sealed class LoadFailureRecord
    {
        internal LoadFailureRecord(Exception error, ImageSourceFailureKind kind)
        {
            Error = error;
            Kind = kind;
        }

        internal Exception Error { get; }

        internal ImageSourceFailureKind Kind { get; }
    }

    private LoadFailureRecord? _loadFailure;

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public abstract double Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public abstract double Height { get; }

    /// <summary>
    /// Gets the native handle of the image (platform-specific).
    /// </summary>
    public abstract nint NativeHandle { get; }

    /// <summary>Gets image metadata when the source exposes any.</summary>
    public abstract ImageMetadata? Metadata { get; }

    /// <summary>Converts a physical pixel count at the specified DPI to device-independent units.</summary>
    protected static double PixelsToDIPs(double dpi, int pixels) =>
        dpi > 0 && double.IsFinite(dpi) ? pixels * 96d / dpi : pixels;

    /// <summary>Creates a modifiable copy of this image source.</summary>
    public new ImageSource Clone() => (ImageSource)base.Clone();

    /// <summary>Creates a modifiable copy using the current dependency-property values.</summary>
    public new ImageSource CloneCurrentValue() => (ImageSource)base.CloneCurrentValue();

    public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string ToString(IFormatProvider? provider) => GetType().Name;
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    /// <summary>
    /// Provides a compatibility factory for existing custom image-source implementations.
    /// Framework image sources with stateful construction override this method explicitly.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "This fallback only supports compatibility ImageSource implementations with a parameterless constructor; stateful framework types provide an explicit factory override.")]
    protected override Freezable CreateInstanceCore()
    {
        if (Activator.CreateInstance(GetType(), nonPublic: true) is Freezable instance)
        {
            return instance;
        }

        throw new InvalidOperationException($"Image source type '{GetType().FullName}' cannot be cloned.");
    }

    /// <summary>
    /// Raised internally when loading or decoding this source fails.
    /// </summary>
    internal event Action<ImageSource, Exception>? LoadFailed;

    /// <summary>Gets the most recent unresolved load failure.</summary>
    internal Exception? LoadFailure => Volatile.Read(ref _loadFailure)?.Error;

    /// <summary>
    /// Reads the latched failure and its class in ONE go.
    /// </summary>
    /// <remarks>
    /// There is deliberately no standalone <c>LoadFailureKind</c> to pair with
    /// <see cref="LoadFailure"/>. Reading them as two properties is two reads of a field a
    /// successful retry clears from the render thread, so a caller can observe a live failure and
    /// then the default class for the failure that is no longer there — and act as if a cleared
    /// upload fault were a load fault, which is a blank frame produced by the reader rather than by
    /// the pipeline. One read cannot disagree with itself.
    /// </remarks>
    internal bool TryGetLoadFailure(
        [NotNullWhen(true)] out Exception? error,
        out ImageSourceFailureKind kind)
    {
        var latched = Volatile.Read(ref _loadFailure);
        if (latched is null)
        {
            error = null;
            kind = ImageSourceFailureKind.Load;
            return false;
        }

        error = latched.Error;
        kind = latched.Kind;
        return true;
    }

    /// <summary>Reports a source loading or decoding failure.</summary>
    /// <remarks>
    /// <para>Latches the failure, puts it on the <b>Release-live</b> diagnostics channel, and
    /// announces it to <see cref="LoadFailed"/> — in that order, so a subscriber that throws cannot
    /// cost the failure its record.</para>
    /// <para>The diagnostics leg is not optional and not the caller's job to remember. Before it,
    /// the single most common real-world image bug — an asset that was not copied to the output
    /// directory — produced <c>Image.ImageFailed</c> and nothing else, so an application drawing
    /// that asset through an <see cref="ImageBrush"/>, a <c>DrawingImage</c> or a
    /// <c>Shape.Fill</c> (none of which is an <c>Image</c> element, and none of which raises that
    /// routed event) got a blank rectangle with no counter, no EventSource record and no line in a
    /// trace capture. Reporting from the choke point rather than from each call site is what makes
    /// that structural: a new failure path cannot be silent by omission.</para>
    /// <para>A site whose own record is RICHER — a decode worker that knows the stage and the
    /// attempt number, an SVG loader that knows the path it could not read — reports first and
    /// latches through <see cref="ReportDiagnosedLoadFailure"/> instead, so one failure produces
    /// one record rather than two.</para>
    /// </remarks>
    protected void ReportLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        LatchLoadFailure(exception);
        Jalium.UI.Diagnostics.ImageDiagnostics.DecodeFailed(
            DiagnosticIdentity, "source load", exception);
        LoadFailed?.Invoke(this, exception);
    }

    /// <summary>
    /// Latches and announces a load failure whose diagnostics record the discovering site has
    /// ALREADY written.
    /// </summary>
    /// <remarks>
    /// For the two places that report a strictly better record than
    /// <see cref="ReportLoadFailure"/> could: the deferred decode worker, which reports the stage
    /// and attempt on the thread that failed and only reaches the application chain later on the
    /// main dispatcher, and the SVG loader, which names the file path rather than the source's
    /// URI. Routing those through <see cref="ReportLoadFailure"/> would double-count
    /// <c>DecodesFailed</c> and make one failure read as two in a support capture.
    /// </remarks>
    private protected void ReportDiagnosedLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        LatchLoadFailure(exception);
        LoadFailed?.Invoke(this, exception);
    }

    private void LatchLoadFailure(Exception exception) =>
        Volatile.Write(
            ref _loadFailure, new LoadFailureRecord(exception, ImageSourceFailureKind.Load));

    /// <summary>
    /// Stable identity for this source in diagnostics records. Overridden by sources that know
    /// their URI; the type name is the fallback for one that does not.
    /// </summary>
    /// <remarks>
    /// Read from failure paths that may run on a decode worker, so an override must not touch
    /// dependency properties.
    /// </remarks>
    private protected virtual string DiagnosticIdentity => GetType().Name;

    /// <summary>
    /// Latches a GPU upload failure discovered by the render backend, and reports whether THIS
    /// caller is the one that owes the announcement.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the failure was latched by this call, i.e. it opened a new
    /// failure episode; <see langword="false"/> when one was already outstanding.
    /// </returns>
    /// <remarks>
    /// <para><see cref="ReportLoadFailure"/> is <see langword="protected"/>, so the render backend —
    /// the layer that discovers a GPU upload failure — cannot reach it. This seam exists so an
    /// upload failure reaches the same <see cref="LoadFailed"/> -&gt;
    /// <c>Image.OnSourceLoadFailed</c> -&gt; <c>ImageFailed</c> chain a decode failure already uses,
    /// instead of being reported only to <c>ImageDiagnostics</c>.</para>
    /// <para>It latches WITHOUT raising, and it is deliberately separate from
    /// <see cref="ReportLoadFailure"/> for two reasons. The claim has to happen on the discovering
    /// thread — the upload is retried every frame, so dozens of identical reports can pile up
    /// before the UI thread next runs, and a compare-exchange here makes the deduplication exact
    /// rather than merely collapsing it at delivery. And the raise must NOT happen here, because
    /// this runs on the render thread whenever one is active (the default on Windows) while the
    /// chain reaches a routed-event raise; <c>BitmapDecodeNotifier</c> carries that half to the UI
    /// thread.</para>
    /// </remarks>
    internal bool TryLatchUploadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Unsynchronised pre-check before the allocation. This is called from the catch of an
        // upload that is retried every frame, so the overwhelmingly common case while a failure is
        // outstanding is "already latched"; allocating a record to lose the exchange with it would
        // put a per-frame allocation on a path that runs precisely when the machine is already
        // under pressure. The exchange below is still the decision.
        if (Volatile.Read(ref _loadFailure) is not null)
        {
            return false;
        }

        return Interlocked.CompareExchange(
            ref _loadFailure,
            new LoadFailureRecord(exception, ImageSourceFailureKind.Upload),
            null) is null;
    }

    /// <summary>
    /// Announces a failure that <see cref="TryLatchUploadFailure"/> already latched, on a thread
    /// that may run application handlers.
    /// </summary>
    /// <remarks>
    /// Announces it only while it is still the source's live failure. A retry that succeeded before
    /// the UI thread got here has already cleared the latch, and reporting a failure the pipeline
    /// recovered from would hand the application a fallback image for a frame that painted
    /// correctly. The record itself is never re-latched from here, so a clear can never be undone
    /// by a stale queued announcement.
    /// </remarks>
    internal void RaiseLatchedLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Volatile.Read(ref _loadFailure) is { } latched &&
            ReferenceEquals(latched.Error, exception))
        {
            LoadFailed?.Invoke(this, exception);
        }
    }

    /// <summary>
    /// Drops a latched UPLOAD failure because a later upload of the same source succeeded.
    /// </summary>
    /// <remarks>
    /// <para>Called from the successful branch of the render backend's upload, so a transient
    /// device condition leaves no residue: the once-per-episode <c>ImageFailed</c> report re-arms,
    /// and <see cref="LoadFailure"/> stops describing a failure the GPU has demonstrably recovered
    /// from. Without it the latch is permanent, which silently downgrades the application channel
    /// to one report per source for the life of the process.</para>
    /// <para>A <see cref="ImageSourceFailureKind.Load"/> failure is deliberately untouched: a
    /// successful upload of an older resident raster says nothing about the decode that failed, so
    /// clearing it would un-suppress an element whose source really is broken. The unsynchronised
    /// pre-read keeps the common case — every successful upload in the process — at one volatile
    /// read.</para>
    /// </remarks>
    internal void ClearUploadFailure()
    {
        var latched = Volatile.Read(ref _loadFailure);
        if (latched is null || latched.Kind != ImageSourceFailureKind.Upload)
        {
            return;
        }

        Interlocked.CompareExchange(ref _loadFailure, null, latched);
    }

    /// <summary>Clears a previously reported failure after a successful load.</summary>
    protected void ClearLoadFailure() => Volatile.Write(ref _loadFailure, null);

    /// <summary>
    /// Raised when an image source wants every GPU-side bitmap cache (one per
    /// active <c>RenderTargetDrawingContext</c>) to drop its cached upload of
    /// the source so the underlying <c>NativeBitmap</c> texture is released.
    /// Used by the idle-resource reclaimer when an <c>IReclaimableResource</c>
    /// element decides its source has been off-screen long enough to free GPU
    /// memory; the upload is rebuilt from the bitmap's raw or encoded data on
    /// the next render.
    /// </summary>
    /// <remarks>
    /// <para>Each <c>RenderTargetDrawingContext</c> subscribes in its constructor and
    /// unsubscribes when the context closes. Handlers run synchronously on the
    /// thread that raised the event — typically the UI thread — and must be
    /// cheap; the source is the <see cref="ImageSource"/> whose GPU upload should
    /// be dropped.</para>
    /// <para>The raising thread is NOT the owner of the caches being asked to drop
    /// anything: a GPU bitmap cache belongs to the render thread for as long as one
    /// is running. A handler must therefore record the request and let its owner
    /// apply it, never mutate cache state or destroy a native texture inline — that
    /// races the render thread's own reads of the same dictionary and frees a
    /// texture it is about to sample.</para>
    /// </remarks>
    internal static event Action<ImageSource>? GpuCacheEvictionRequested;

    /// <summary>
    /// Asks every subscribed bitmap cache to drop its GPU upload of
    /// <paramref name="source"/>. No-op when nothing is subscribed.
    /// </summary>
    internal static void RaiseGpuCacheEviction(ImageSource source)
    {
        var handler = GpuCacheEvictionRequested;
        if (handler != null)
        {
            handler(source);
        }
    }

    /// <summary>
    /// Raised when an image source has replaced the pixels behind an identity that every cache
    /// keys on by reference — a deferred decode publishing a larger bucket, a reclaimed source
    /// being restored — so every cached upload and every derived thumbnail of that source is now
    /// stale even though the instance is unchanged.
    /// </summary>
    /// <remarks>
    /// <para>Distinct from <see cref="GpuCacheEvictionRequested"/>, which asks caches to release
    /// GPU memory for an image that is still correct. This one says the cached bytes are
    /// <i>wrong</i>: a cache that ignores it keeps serving the superseded raster forever, with no
    /// crash and no log line — the failure mode that makes a bucket upgrade look like it silently
    /// did nothing.</para>
    /// <para>Handlers run synchronously on the thread that raised the event and must be cheap.
    /// Because the repaint leg hangs off this event rather than off a dispatcher post, no
    /// dispatcher priority can starve it.</para>
    /// <para>Same ownership rule as <see cref="GpuCacheEvictionRequested"/>: a handler may only
    /// RECORD that a cached upload is now wrong. Dropping it inline would mutate a dictionary the
    /// render thread is concurrently inserting into and dispose a texture it is about to draw
    /// with. Deferring the drop is free here — a cache that compares
    /// <see cref="ContentGeneration"/> re-uploads the replaced raster on its next draw whether or
    /// not this event was ever delivered, so the event buys prompt release of the superseded
    /// texture, not correctness.</para>
    /// </remarks>
    internal static event Action<ImageSource>? RasterChanged;

    /// <summary>
    /// Announces that <paramref name="source"/>'s pixels were replaced. No-op when nothing is
    /// subscribed, which is the normal state in headless and unit-test hosts.
    /// </summary>
    internal static void RaiseRasterChanged(ImageSource source)
    {
        var handler = RasterChanged;
        if (handler != null)
        {
            handler(source);
        }
    }

    private long _lastDrawnTickMs;

    /// <summary>
    /// Wall-clock tick (<see cref="Environment.TickCount64"/>) of the last time any renderer
    /// realized this source's pixels for a draw, or 0 if it has never been drawn.
    /// </summary>
    /// <remarks>
    /// <para>An <see cref="ImageSource"/> is SHARED: the same instance routinely backs several
    /// elements (a wizard's preview pane and the banner of a later step, a list row and its detail
    /// view) and non-element consumers such as an image brush or an image drawing. The idle
    /// reclaimer, in contrast, decides per ELEMENT — so an off-screen element that happens to hold
    /// a reference to a source another element is painting every frame would drop that source's
    /// decoded pixels and GPU upload out from under the live consumer. The visible result is an
    /// image that blanks for the length of one re-decode and comes back, over and over, for as
    /// long as anything keeps rendering; see <c>Image.ReclaimIdleResources</c>, which uses this
    /// stamp to tell "nobody is drawing this any more" apart from "somebody else still is".</para>
    /// <para>Stamped by the drawing context at the single choke point every bitmap consumer funnels
    /// through, so brushes and drawings protect a source exactly as an element does. It is one
    /// volatile write per realized draw and is never read on the render hot path.</para>
    /// </remarks>
    internal long LastDrawnTickMs => Volatile.Read(ref _lastDrawnTickMs);

    /// <summary>
    /// Records that this source's pixels were just realized for a draw.
    /// </summary>
    /// <remarks>
    /// Called for the source the CALLER asked to draw, not for whatever derived raster the
    /// renderer substituted for it (a downscaled thumbnail, an animation frame): the substitute is
    /// a private, rebuildable artifact, while the source the application holds is the one an idle
    /// element may be about to reclaim.
    /// </remarks>
    internal void MarkDrawn()
    {
        Volatile.Write(ref _lastDrawnTickMs, Environment.TickCount64);
        if (this is DrawingImage { DrawOwner: { } owner } && !ReferenceEquals(owner, this)) owner.MarkDrawn();
    }

    private static long s_reclaimGraceMs = 2000;

    /// <summary>
    /// How recently this source must have been drawn for a reclaim to be refused, in milliseconds.
    /// Kept in step with the idle reclaimer's own idle window.
    /// </summary>
    /// <remarks>
    /// The reclaimer decides per element; the resource it frees belongs to a source that any number
    /// of elements, brushes and drawings may share. A source drawn within the same window the
    /// reclaimer calls "idle" therefore has a live consumer BY DEFINITION, whichever element the
    /// scan happened to arrive from — so this is the last word on reclamation, and it is a property
    /// of the source rather than of any one holder of it.
    /// </remarks>
    internal static long ReclaimGraceMs
    {
        get => Volatile.Read(ref s_reclaimGraceMs);
        set => Volatile.Write(ref s_reclaimGraceMs, value);
    }

    /// <summary>
    /// True when a renderer realized this source's pixels within <see cref="ReclaimGraceMs"/>.
    /// </summary>
    /// <remarks>
    /// A source that has never been drawn answers false: it holds no upload anything is sampling,
    /// and refusing to reclaim it would strand the decoded buffer of an image that was prepared and
    /// then never shown — the exact allocation the reclaimer exists to release.
    /// </remarks>
    internal bool WasDrawnRecently()
    {
        var drawn = LastDrawnTickMs;
        if (drawn == 0)
            return false;

        var since = Environment.TickCount64 - drawn;
        return since >= 0 && since < ReclaimGraceMs;
    }

    /// <summary>
    /// Monotonically-increasing stamp of the pixel content currently behind this source. A cache
    /// that recorded the value at upload time can detect that the source's raster was replaced
    /// under a reference-equal instance by comparing against it.
    /// </summary>
    /// <remarks>
    /// <para>Zero for sources whose content never changes under a stable reference, which is the
    /// safe default: a cache comparing 0 against 0 behaves exactly as it did before this member
    /// existed.</para>
    /// <para>Deliberately <see cref="long"/> rather than the <see cref="uint"/>
    /// <c>WriteableBitmap.ContentRevision</c> it generalises, so that an implementation stamping a
    /// process-wide generation counter can hand back the identical value it published rather than a
    /// truncation of it — a cache comparing a truncated stamp against an untruncated one never
    /// matches and re-uploads the texture every single frame.</para>
    /// </remarks>
    internal virtual long ContentGeneration => 0L;

    /// <summary>
    /// Raised once after a batch of image-content notifications has been delivered, so hosts can
    /// coalesce the repaint they owe into a single invalidation instead of one per image.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously on the thread that raised the event — the main dispatcher for
    /// decode completions — and must be allocation-free.
    /// </remarks>
    internal static event Action? ContentChangedBatchCompleted;

    /// <summary>
    /// Signals the end of a batch of image-content notifications. No-op when nothing is
    /// subscribed, which is the normal state in headless and unit-test hosts.
    /// </summary>
    internal static void RaiseContentChangedBatchCompleted() => ContentChangedBatchCompleted?.Invoke();
}
