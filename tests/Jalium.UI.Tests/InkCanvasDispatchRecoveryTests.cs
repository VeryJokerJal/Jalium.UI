using System.Collections;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Drives the GPU ink-layer brush-dispatch failure-recovery state machine in
/// <see cref="InkCanvas"/> (the <c>_inkRebuildPending</c> → rebuild →
/// heal / <c>_inkRebuildSuppressed</c> latch) deterministically and headlessly.
/// </summary>
/// <remarks>
/// <para>
/// The recovery path only fires for a Vulkan device-generation mismatch
/// (dispatch <c>-6</c>/<c>-7</c>), which a real GPU cannot be coerced into on
/// demand and which CI has no Vulkan ICD to produce at all. Two injection points
/// make it testable without a device: <c>InkCanvas._inkNativeOpsOverride</c>
/// substitutes the native ink/brush ops through the existing
/// <see cref="IInkNativeOps"/> seam (so <c>DispatchBrush</c> returns a scripted
/// return code and layer/shader construction succeeds with fake handles), and
/// <c>InkCanvas._inkBackendOverride</c> makes the failure classifier treat the
/// context as a chosen backend. The owning <see cref="RenderContext"/> is still a
/// real one (cheap D3D12), so the layer's context-pin lifetime stays faithful.
/// </para>
/// <para>
/// This is the same fake-seam strategy <see cref="InkLayerBitmapContextLifetimeTests"/>
/// uses for the context-pin contract; here it gives the recovery FSM real
/// regression bite (flipping the Vulkan gate to D3D12, or removing the
/// suppression latch, turns these red).
/// </para>
/// </remarks>
[Collection("Application")]
public sealed class InkCanvasDispatchRecoveryTests : IDisposable
{
    private readonly List<InkCanvas> _canvases = new();

    public InkCanvasDispatchRecoveryTests() => DrainAllContexts();

    public void Dispose()
    {
        // Release the fake layers/shaders (and thus their context pins) before
        // draining the contexts, so finalizers never run against a disposed
        // context.
        foreach (var c in _canvases)
            InvokeDisposeInkLayerResources(c);
        DrainAllContexts();
    }

    /// <summary>
    /// A Vulkan generational failure (-7) schedules a deferred whole-layer
    /// rebuild; once the next frame's replay succeeds, the latches return to a
    /// clean baseline.
    /// </summary>
    [Fact]
    public void VulkanGenerationalFailure_SchedulesRebuild_ThenSelfHeals()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = -7 };
        var canvas = CreateCanvas(ops, RenderBackend.Vulkan);
        canvas.Strokes.Add(MakeStroke());

        // Frame 1: build the layer + replay the committed stroke, whose dispatch
        // returns -7 → a rebuild is scheduled but not yet abandoned.
        canvas.EnsureInkLayer(ctx);
        Assert.True(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));

        // The device generation re-aligns (rebuilt bitmap + shaders re-pair):
        // the next dispatch succeeds.
        ops.DispatchRc = 0;

        // Frame 2: the pending rebuild runs, replay succeeds → clean baseline.
        canvas.EnsureInkLayer(ctx);
        Assert.False(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));
    }

    /// <summary>
    /// If the rebuild itself still hits a generational failure during its replay,
    /// the canvas latches suppression and stops rebuilding (storm guard),
    /// dropping to the CPU ink path.
    /// </summary>
    [Fact]
    public void VulkanGenerationalFailure_Persistent_LatchesSuppressed_AndStopsRebuilding()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = -7 }; // never heals
        var canvas = CreateCanvas(ops, RenderBackend.Vulkan);
        canvas.Strokes.Add(MakeStroke());

        canvas.EnsureInkLayer(ctx);                 // frame 1: schedule rebuild
        Assert.True(RebuildPending(canvas));

        canvas.EnsureInkLayer(ctx);                 // frame 2: rebuild replay still -7 → suppress
        Assert.True(RebuildSuppressed(canvas));
        Assert.False(RebuildPending(canvas));
        Assert.True(InkLayerIsNull(canvas));        // dropped → committed ink goes CPU

        int createsBefore = ops.InkCreates;
        canvas.EnsureInkLayer(ctx);                 // frame 3+: must not rebuild
        canvas.EnsureInkLayer(ctx);
        Assert.Equal(createsBefore, ops.InkCreates); // no further layer construction
    }

    /// <summary>
    /// On D3D12 the dispatch codes -6/-7 are transient upload-buffer OOM, not a
    /// generation mismatch, so they must NOT be amplified into a whole-layer
    /// rebuild — the central no-amplification guarantee of the change.
    /// </summary>
    [Fact]
    public void D3D12GenerationalCodes_DoNotTriggerRebuild()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = -7 };
        var canvas = CreateCanvas(ops, RenderBackend.D3D12);
        canvas.Strokes.Add(MakeStroke());

        canvas.EnsureInkLayer(ctx);
        Assert.False(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));
    }

    /// <summary>
    /// Vulkan's transient codes (here -3, a buffer-allocation hiccup) are not
    /// deterministic generational failures, so they are logged but never trigger
    /// the rebuild.
    /// </summary>
    [Fact]
    public void VulkanTransientCode_DoesNotTriggerRebuild()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = -3 };
        var canvas = CreateCanvas(ops, RenderBackend.Vulkan);
        canvas.Strokes.Add(MakeStroke());

        canvas.EnsureInkLayer(ctx);
        Assert.False(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private InkCanvas CreateCanvas(IInkNativeOps ops, RenderBackend backend)
    {
        var canvas = new InkCanvas();
        canvas.Arrange(new Rect(0, 0, 64, 64));
        canvas._inkNativeOpsOverride = ops;
        canvas._inkBackendOverride = backend;
        _canvases.Add(canvas);
        return canvas;
    }

    private static Stroke MakeStroke()
    {
        var pts = new StylusPointCollection();
        pts.Add(new StylusPoint(0, 0));
        pts.Add(new StylusPoint(10, 10));
        return new Stroke(pts, new DrawingAttributes());
    }

    private static bool RebuildPending(InkCanvas c) => GetBool(c, "_inkRebuildPending");
    private static bool RebuildSuppressed(InkCanvas c) => GetBool(c, "_inkRebuildSuppressed");

    private static bool GetBool(InkCanvas c, string field) =>
        (bool)typeof(InkCanvas)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(c)!;

    private static bool InkLayerIsNull(InkCanvas c) =>
        typeof(InkCanvas)
            .GetField("_inkLayer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(c) is null;

    private static void InvokeDisposeInkLayerResources(InkCanvas c) =>
        typeof(InkCanvas)
            .GetMethod("DisposeInkLayerResources", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(c, null);

    /// <summary>
    /// Scriptable fake ink/brush ops: layer + shader construction always succeed
    /// (non-zero handles → IsValid), and every brush dispatch returns the
    /// currently configured <see cref="DispatchRc"/>. No GPU is touched.
    /// </summary>
    private sealed class ScriptableInkNativeOps : IInkNativeOps
    {
        private nint _next = 0x5000;

        /// <summary>Return code every <see cref="DispatchBrush"/> hands back.</summary>
        public int DispatchRc;
        /// <summary>Count of ink-layer bitmap allocations (storm-guard assertion).</summary>
        public int InkCreates;

        public nint CreateInkLayerBitmap(nint context, int width, int height)
        {
            InkCreates++;
            return _next++;
        }

        public void DestroyInkLayerBitmap(nint handle) { }

        public int ResizeInkLayerBitmap(nint handle, int width, int height) => 0;

        public void ClearInkLayerBitmap(nint handle, float r, float g, float b, float a) { }

        public int DispatchBrush(
            nint bitmap, nint shader,
            ReadOnlySpan<BrushStrokePoint> points,
            in BrushConstantsNative constants,
            ReadOnlySpan<byte> extraParams) => DispatchRc;

        public nint CreateBrushShader(nint context, string shaderKey, string brushMainHlsl, int blendMode)
            => _next++;

        public void DestroyBrushShader(nint handle) { }
    }

    /// <summary>
    /// Disposes the current and any retired contexts so the shared static state
    /// does not leak across tests in the Application collection (mirrors
    /// <see cref="InkLayerBitmapContextLifetimeTests"/>).
    /// </summary>
    private static void DrainAllContexts()
    {
        RenderContext.Current?.Dispose();

        var field = typeof(RenderContext).GetField(
            "_retiredContexts", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is IEnumerable retired)
        {
            foreach (var ctx in retired.Cast<RenderContext>().ToList())
            {
                ctx.Dispose();
            }
        }
    }
}
