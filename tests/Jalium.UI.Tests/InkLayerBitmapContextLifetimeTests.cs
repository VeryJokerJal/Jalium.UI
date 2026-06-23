using System.Collections;
using System.Reflection;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Guards the device-lost use-after-free where an <see cref="InkLayerBitmap"/> /
/// <see cref="BrushShaderHandle"/> outlives the context whose backend its native
/// destroy routes through.
/// </summary>
/// <remarks>
/// <para>
/// Recovery (Window.cs) disposes the old render target, then forces a new
/// context. Forcing the new context retires the old one and — because the
/// retired context's active-resource count is already zero — destroys its
/// backend immediately. InkCanvas only drops its stale ink handles on the
/// <em>following</em> frame, so those destroys used to route through a freed
/// backend (a virtual call on deleted memory). The fix pins the owning context
/// as an active dependent resource for the lifetime of every ink handle, so the
/// retired context defers backend destruction until the handles are disposed.
/// </para>
/// <para>
/// The real native ink/brush pipeline needs a GPU compute path unavailable in
/// headless / WARP CI (the allocation returns 0 there), so the handle tests
/// inject a fake <see cref="IInkNativeOps"/>. The owning RenderContext is still
/// a real one — its IsValid faithfully reflects whether it has been disposed —
/// only the ink/brush native handles are faked. This keeps the pin / unpin /
/// destroy-ordering contract verifiable deterministically and with real
/// regression bite (removing the pin turns these tests red).
/// </para>
/// </remarks>
[Collection("Application")]
public sealed class InkLayerBitmapContextLifetimeTests : IDisposable
{
    public InkLayerBitmapContextLifetimeTests() => DrainAllContexts();

    public void Dispose() => DrainAllContexts();

    /// <summary>
    /// Mechanism invariant — proves the dependent-resource count gates retired-
    /// context disposal: a live dependent keeps a retired context (and its
    /// backend) alive until released. This is the machinery the ink-handle fix
    /// relies on.
    /// </summary>
    [Fact]
    public void RegisteredDependent_KeepsRetiredContextAlive_UntilUnregistered()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        first.RegisterRenderTarget(); // stand in for a live ink handle

        var second = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(first, second);
        Assert.True(first.IsValid); // a live dependent keeps the retired context alive

        first.UnregisterRenderTarget();
        Assert.False(first.IsValid); // drained → retired context disposed
    }

    /// <summary>
    /// A live <see cref="InkLayerBitmap"/> must keep its retired context (and
    /// backend) alive across a forced context replacement, and its native
    /// destroy must run while the backend is still alive. Pre-fix the
    /// <c>Assert.True(first.IsValid)</c> after the forced replace fails (the
    /// retired context disposes immediately) — the regression bite.
    /// </summary>
    [Fact]
    public void InkLayerBitmap_PinsRetiredContext_AndDestroysBeforeBackendReclaim()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var fake = new FakeInkNativeOps();
        bool backendAliveAtDestroy = false;
        fake.OnDestroy = () => backendAliveAtDestroy = first.IsValid;

        var ink = new InkLayerBitmap(first, 64, 64, fake);
        Assert.True(ink.IsValid);

        // Force a context replacement while the ink handle is still alive — the
        // device-lost-recovery moment.
        var second = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(first, second);
        Assert.True(first.IsValid); // FIX: pinned by the live ink handle

        ink.Dispose();

        Assert.Equal(1, fake.InkDestroys);
        Assert.True(backendAliveAtDestroy); // native destroy ran before backend reclaim (order contract)
        Assert.False(first.IsValid);        // retired context reclaimed only after the last unpin
    }

    /// <summary>Same contract for <see cref="BrushShaderHandle"/>.</summary>
    [Fact]
    public void BrushShaderHandle_PinsRetiredContext_AndDestroysBeforeBackendReclaim()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var fake = new FakeInkNativeOps();
        bool backendAliveAtDestroy = false;
        fake.OnDestroy = () => backendAliveAtDestroy = first.IsValid;

        var shader = BrushShaderHandle.Create(first, "probe", "brush-main", 0, fake);
        Assert.NotNull(shader);

        var second = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(first, second);
        Assert.True(first.IsValid); // FIX: pinned by the live shader handle

        shader!.Dispose();

        Assert.Equal(1, fake.ShaderDestroys);
        Assert.True(backendAliveAtDestroy);
        Assert.False(first.IsValid);
    }

    /// <summary>
    /// Mirrors the exact recovery ordering in Window.cs: the render target is
    /// released (its Unregister drops the count) BEFORE the context is forcibly
    /// replaced. Across that gap the ink handle is the only thing pinning the
    /// backend — it alone must keep the retired context alive.
    /// </summary>
    [Fact]
    public void RecoverySequence_InkHandleAloneHoldsBackend_AcrossRtReleaseThenReplace()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var fake = new FakeInkNativeOps();
        var ink = new InkLayerBitmap(first, 32, 32, fake);

        // Simulate the window's render target existing and then being released
        // during recovery (RenderTarget ctor RegisterRenderTarget /
        // RenderTarget.Dispose UnregisterRenderTarget).
        first.RegisterRenderTarget();
        first.UnregisterRenderTarget(); // RenderTarget.Dispose() during recovery
        Assert.True(first.IsValid);     // the ink handle still pins it

        // Now the forced replacement (EnsureRenderTarget(forceNewContext:true)).
        var second = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(first, second);
        Assert.True(first.IsValid);     // survived the forced replacement (FIX)

        ink.Dispose();                  // next-frame InkCanvas teardown
        Assert.False(first.IsValid);    // backend reclaimed only now — safely
    }

    /// <summary>
    /// A failed native allocation (backend without the brush-shader pipeline
    /// returns 0) must not leave a dangling context pin. After the throwing ctor
    /// a forced replacement should retire AND dispose the context at once,
    /// proving the construction-failure path released its pin.
    /// </summary>
    [Fact]
    public void FailedInkAllocation_ReleasesContextPin()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var fake = new FakeInkNativeOps { FailCreate = true };

        Assert.Throws<InvalidOperationException>(() => new InkLayerBitmap(first, 64, 64, fake));

        var second = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotSame(first, second);
        Assert.False(first.IsValid); // no live dependent → reclaimed immediately
    }

    // ── Fake native ink/brush ops (no GPU) ──────────────────────────────────
    private sealed class FakeInkNativeOps : IInkNativeOps
    {
        private nint _next;

        public int InkCreates;
        public int InkDestroys;
        public int ShaderCreates;
        public int ShaderDestroys;
        public bool FailCreate;
        public Action? OnDestroy;

        public FakeInkNativeOps(nint firstHandle = 0x4000) => _next = firstHandle;

        public nint CreateInkLayerBitmap(nint context, int width, int height)
        {
            InkCreates++;
            return FailCreate ? nint.Zero : _next++;
        }

        public void DestroyInkLayerBitmap(nint handle)
        {
            InkDestroys++;
            OnDestroy?.Invoke();
        }

        public nint CreateBrushShader(nint context, string shaderKey, string brushMainHlsl, int blendMode)
        {
            ShaderCreates++;
            return FailCreate ? nint.Zero : _next++;
        }

        public void DestroyBrushShader(nint handle)
        {
            ShaderDestroys++;
            OnDestroy?.Invoke();
        }

        // These lifetime tests never resize/clear/dispatch — default no-ops keep
        // the fake conformant with the seam without touching the real GPU path.
        public int ResizeInkLayerBitmap(nint handle, int width, int height) => 0;

        public void ClearInkLayerBitmap(nint handle, float r, float g, float b, float a) { }

        public int DispatchBrush(
            nint bitmap, nint shader,
            ReadOnlySpan<BrushStrokePoint> points,
            in BrushConstantsNative constants,
            ReadOnlySpan<byte> extraParams) => 0;
    }

    /// <summary>
    /// Disposes the current and any retired contexts so the shared static state
    /// (<see cref="RenderContext.Current"/> + retired set) does not leak across
    /// tests in the Application collection. Uses direct Dispose to force cleanup
    /// regardless of pin counts.
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
