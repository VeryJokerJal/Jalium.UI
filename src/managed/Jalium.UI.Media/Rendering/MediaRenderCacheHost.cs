using System;
using System.Collections.Generic;
using Jalium.UI.Rendering;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// The <see cref="IRenderCacheHost"/> implementation that wires
/// <see cref="DrawingRecorder"/> / <see cref="Drawing"/> / <see cref="DrawingReplayer"/>
/// together and installs itself into <c>Visual.RenderCacheHost</c>. Bootstrapped
/// once at startup by <c>RenderTargetDrawingContext</c>'s type initializer so
/// callers never need to call <see cref="Bootstrap"/> manually.
/// </summary>
/// <remarks>
/// <para>
/// Recorders are pooled process-wide. Render is single-threaded on the UI
/// thread, so the pool is guarded by a single lock rather than per-thread
/// storage — contention is only possible if a background thread drives an
/// out-of-band render, which currently no pipeline does.
/// </para>
/// <para>
/// Setting environment variable <c>JALIUM_DISABLE_RENDER_CACHE=1</c> before
/// the first render target is created skips the registration entirely and
/// preserves legacy immediate-mode <c>OnRender</c> dispatch, providing a
/// one-line bailout if the cache is suspected in a regression.
/// </para>
/// </remarks>
public sealed class MediaRenderCacheHost : IRenderCacheHost
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
    /// tree (including per-child offsets) as a self-contained <see cref="Drawing"/>
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
        DrawingReplayer.Replay((Drawing)drawing, targetDrawingContext);
    }

    /// <summary>
    /// Idempotent. True after <see cref="Bootstrap"/> has evaluated its
    /// registration policy (whether or not it actually installed the host).
    /// </summary>
    public static bool IsBootstrapped { get; private set; }

    /// <summary>
    /// Installs the host into <c>Visual.RenderCacheHost</c> unless one of:
    /// <list type="bullet">
    ///   <item><c>JALIUM_DISABLE_RENDER_CACHE=1</c> is set in the environment.</item>
    ///   <item><c>Visual.RenderCacheHost</c> has already been set by another
    ///   bootstrap path (e.g. a test harness installing a mock host).</item>
    /// </list>
    /// Safe to call repeatedly; only the first call performs work.
    /// </summary>
    public static void Bootstrap()
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
