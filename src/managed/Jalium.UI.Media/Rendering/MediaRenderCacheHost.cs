using System;
using System.Collections.Generic;
using Jalium.UI.Rendering;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// The <see cref="IRenderCacheHost"/> implementation that wires
/// <see cref="DrawingRecorder"/> / <see cref="RecordedDrawing"/> / <see cref="DrawingReplayer"/>
/// together and installs itself into <c>Visual.RenderCacheHost</c>. Bootstrapped
/// once at startup by <c>RenderTargetDrawingContext</c>'s type initializer so
/// callers never need to call <see cref="Bootstrap"/> manually.
/// </summary>
/// <remarks>
/// <para>
/// Recorders are pooled process-wide under a single lock. Even on the
/// render-thread path (JALIUM_RENDER_THREAD) the pool stays single-producer:
/// <see cref="CreateFrameRecorder"/> + <see cref="FinishRecord"/> both run on the
/// UI thread inside <c>Window.PublishFrameToRenderThread</c>; the render thread
/// only calls <see cref="Replay"/>, which is read-only over the published
/// <see cref="RecordedDrawing"/> and never touches the pool. So the single lock suffices —
/// do not add per-thread pools unless a future path lets a non-UI thread
/// allocate/commit a recorder. (Whole-frame freeze-clone snapshots, written by
/// <see cref="DrawInputSnapshotter"/> during record on the UI thread, are likewise
/// only dereferenced — never mutated — on the render thread via the published Drawing.)
/// </para>
/// <para>
/// Setting environment variable <c>JALIUM_DISABLE_RENDER_CACHE=1</c> before
/// the first render target is created skips the registration entirely and
/// preserves legacy immediate-mode <c>OnRender</c> dispatch, providing a
/// one-line bailout if the cache is suspected in a regression.
/// </para>
/// </remarks>
internal sealed class MediaRenderCacheHost : IRenderCacheHost
{
    private readonly Stack<DrawingRecorder> _pool = new();
    private readonly object _poolLock = new();

    public DrawingContext CreateRecorder(DrawingContext targetDrawingContext)
    {
        DrawingRecorder recorder;
        lock (_poolLock)
        {
            recorder = _pool.Count > 0 ? _pool.Pop() : new DrawingRecorder();
        }
        recorder.Bind(targetDrawingContext);
        return recorder;
    }

    /// <summary>
    /// Whole-frame recorder for the render-thread path: captures the entire visual
    /// tree (including per-child offsets) as a self-contained <see cref="RecordedDrawing"/>
    /// with no live target. Released via <see cref="FinishRecord"/> like a normal
    /// recorder; replayed via <see cref="Replay"/>.
    /// </summary>
    public DrawingContext CreateFrameRecorder()
    {
        DrawingRecorder recorder;
        lock (_poolLock)
        {
            recorder = _pool.Count > 0 ? _pool.Pop() : new DrawingRecorder();
        }
        recorder.BindWholeFrame();
        return recorder;
    }

    public object FinishRecord(DrawingContext recorder)
    {
        var r = (DrawingRecorder)recorder;
        var drawing = r.Commit();
        lock (_poolLock)
        {
            _pool.Push(r);
        }
        return drawing;
    }

    public void Replay(object drawing, DrawingContext targetDrawingContext)
    {
        DrawingReplayer.Replay((RecordedDrawing)drawing, targetDrawingContext);
    }

    /// <summary>
    /// Idempotent. True after <see cref="Bootstrap"/> has evaluated its
    /// registration policy (whether or not it actually installed the host).
    /// </summary>
    internal static bool IsBootstrapped { get; private set; }

    /// <summary>
    /// Installs the host into <c>Visual.RenderCacheHost</c> unless one of:
    /// <list type="bullet">
    ///   <item><c>JALIUM_DISABLE_RENDER_CACHE=1</c> is set in the environment.</item>
    ///   <item><c>Visual.RenderCacheHost</c> has already been set by another
    ///   bootstrap path (e.g. a test harness installing a mock host).</item>
    /// </list>
    /// Safe to call repeatedly; only the first call performs work.
    /// </summary>
    internal static void Bootstrap()
    {
        if (IsBootstrapped)
        {
            return;
        }
        IsBootstrapped = true;

        if (string.Equals(
            Environment.GetEnvironmentVariable("JALIUM_DISABLE_RENDER_CACHE"),
            "1",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Do not overwrite a host installed explicitly (tests, alternate impls).
        Visual.RenderCacheHost ??= new MediaRenderCacheHost();
    }
}
