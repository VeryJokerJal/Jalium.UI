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
/// Dispatch return codes are backend-agnostic (<see cref="InkDispatchResult"/>):
/// every native backend classifies its failures into the same semantic
/// categories before returning, so the recovery FSM keys on the code alone —
/// there is no per-backend branch to exercise. The rebuild path only fires for
/// <see cref="InkDispatchResult.StaleContext"/> (a device-generation loss /
/// mismatch), which a real GPU cannot be coerced into on demand and which CI
/// has no ICD to produce at all. The injection point that makes it testable
/// without a device is <c>InkCanvas._inkNativeOpsOverride</c>: it substitutes
/// the native ink/brush ops through the existing <see cref="IInkNativeOps"/>
/// seam (so <c>DispatchBrush</c> returns a scripted return code and
/// layer/shader construction succeeds with fake handles). The owning
/// <see cref="RenderContext"/> is still a real one (cheap D3D12), so the
/// layer's context-pin lifetime stays faithful.
/// </para>
/// <para>
/// This is the same fake-seam strategy <see cref="InkLayerBitmapContextLifetimeTests"/>
/// uses for the context-pin contract; here it gives the recovery FSM real
/// regression bite (widening the StaleContext gate to other codes, or removing
/// the suppression latch, turns these red).
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
    /// A stale-context failure schedules a deferred whole-layer rebuild; once
    /// the next frame's replay succeeds, the latches return to a clean
    /// baseline. Backend-independent: the FSM reacts to the semantic code, not
    /// to which backend produced it.
    /// </summary>
    [Fact]
    public void StaleContextFailure_SchedulesRebuild_ThenSelfHeals()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = InkDispatchResult.StaleContext };
        var canvas = CreateCanvas(ops);
        canvas.Strokes.Add(MakeStroke());

        // Frame 1: build the layer + replay the committed stroke, whose dispatch
        // reports StaleContext → a rebuild is scheduled but not yet abandoned.
        canvas.EnsureInkLayer(ctx);
        Assert.True(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));

        // The device generation re-aligns (rebuilt bitmap + shaders re-pair):
        // the next dispatch succeeds.
        ops.DispatchRc = InkDispatchResult.Ok;

        // Frame 2: the pending rebuild runs, replay succeeds → clean baseline.
        canvas.EnsureInkLayer(ctx);
        Assert.False(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));
    }

    /// <summary>
    /// If the rebuild itself still hits a stale-context failure during its
    /// replay, the canvas latches suppression and stops rebuilding (storm
    /// guard), dropping to the CPU ink path.
    /// </summary>
    [Fact]
    public void StaleContextFailure_Persistent_LatchesSuppressed_AndStopsRebuilding()
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = InkDispatchResult.StaleContext }; // never heals
        var canvas = CreateCanvas(ops);
        canvas.Strokes.Add(MakeStroke());

        canvas.EnsureInkLayer(ctx);                 // frame 1: schedule rebuild
        Assert.True(RebuildPending(canvas));

        canvas.EnsureInkLayer(ctx);                 // frame 2: rebuild replay still stale → suppress
        Assert.True(RebuildSuppressed(canvas));
        Assert.False(RebuildPending(canvas));
        Assert.True(InkLayerIsNull(canvas));        // dropped → committed ink goes CPU

        int createsBefore = ops.InkCreates;
        canvas.EnsureInkLayer(ctx);                 // frame 3+: must not rebuild
        canvas.EnsureInkLayer(ctx);
        Assert.Equal(createsBefore, ops.InkCreates); // no further layer construction
    }

    /// <summary>
    /// Every non-stale failure class must NOT be amplified into a whole-layer
    /// rebuild — the central no-amplification guarantee. Covers the semantic
    /// codes (Transient: retry the same handles; InvalidArg / InvalidState:
    /// skip the stroke) and the retired legacy raw codes (-3..-7, which the
    /// backends no longer emit): if a stale comparison against a historical
    /// per-backend code ever resurfaces in the classifier, or a backend leaks
    /// an unclassified raw code, the safe outcome is "log and skip", never a
    /// teardown storm.
    /// </summary>
    [Theory]
    [InlineData(InkDispatchResult.Transient)]
    [InlineData(InkDispatchResult.InvalidArg)]
    [InlineData(InkDispatchResult.InvalidState)]
    [InlineData(-3)] // legacy raw codes — retired, must stay inert
    [InlineData(-4)]
    [InlineData(-5)]
    [InlineData(-6)]
    [InlineData(-7)]
    public void NonStaleCode_DoesNotTriggerRebuild(int dispatchRc)
    {
        var ctx = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var ops = new ScriptableInkNativeOps { DispatchRc = dispatchRc };
        var canvas = CreateCanvas(ops);
        canvas.Strokes.Add(MakeStroke());

        canvas.EnsureInkLayer(ctx);
        Assert.False(RebuildPending(canvas));
        Assert.False(RebuildSuppressed(canvas));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private InkCanvas CreateCanvas(IInkNativeOps ops)
    {
        var canvas = new InkCanvas();
        canvas.Arrange(new Rect(0, 0, 64, 64));
        canvas._inkNativeOpsOverride = ops;
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
