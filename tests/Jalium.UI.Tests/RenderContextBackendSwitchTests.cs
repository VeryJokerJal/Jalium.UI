using System.Collections;
using System.Reflection;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Verifies the explicit-backend enforcement in
/// <see cref="RenderContext.GetOrCreateCurrent(RenderBackend, GpuPreference, bool)"/>:
/// an explicit (non-Auto) backend that is available retires + replaces a cached
/// context running a different backend; an Auto request defers to whatever
/// context is current; and an explicit request for an UNAVAILABLE backend leaves
/// the working context untouched (no software-rasterizer downgrade, no
/// re-creation churn).
/// </summary>
/// <remarks>
/// These exercise real native contexts, so they share the process-wide
/// <see cref="RenderContext.Current"/> + retired-set static state. The
/// <c>[Collection("Application")]</c> attribute serializes them and
/// <see cref="DrainAllContexts"/> resets that static state around each test.
/// </remarks>
[Collection("Application")]
public sealed class RenderContextBackendSwitchTests : IDisposable
{
    public RenderContextBackendSwitchTests() => DrainAllContexts();

    public void Dispose() => DrainAllContexts();

    /// <summary>
    /// A single <c>GetOrCreateCurrent(Vulkan)</c> — WITHOUT <c>forceReplace</c> —
    /// must switch the cached (non-Vulkan) current context to Vulkan when Vulkan
    /// is available. This is the user-facing contract: one explicit call switches
    /// the backend even though the prewarm already installed the platform default.
    /// Pre-fix this returned the cached non-Vulkan context unchanged (a no-op).
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void ExplicitVulkan_SwitchesCachedContext_WhenVulkanAvailable()
    {
        // Install a non-Vulkan current context (platform default, or Software on
        // a GPU-less host — either way not Vulkan).
        var initial = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);
        Assert.NotEqual(RenderBackend.Vulkan, initial.Backend);

        // One explicit Vulkan request, no forceReplace.
        var switched = RenderContext.GetOrCreateCurrent(RenderBackend.Vulkan);

        Assert.Equal(RenderBackend.Vulkan, switched.Backend);
        Assert.NotSame(initial, switched);
        Assert.Same(switched, RenderContext.Current);
    }

    /// <summary>
    /// An Auto request must reuse whatever context an explicit caller installed,
    /// instead of re-resolving to the platform default and clobbering it. This is
    /// what lets the GPU prewarm and <c>Window.EnsureRenderTarget</c> (both issue
    /// Auto) honor a context the app installed explicitly at startup.
    /// </summary>
    [Fact]
    public void AutoRequest_DefersToExplicitlyInstalledContext()
    {
        var installed = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);

        var viaAuto = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.Same(installed, viaAuto);
        Assert.Same(installed, RenderContext.Current);
    }

    [RequiresBackendFact(RenderBackend.D3D12)]
    public void ExplicitGpuPreference_ReplacesIncompatibleCachedContext()
    {
        var automatic = RenderContext.GetOrCreateCurrent(
            RenderBackend.D3D12,
            GpuPreference.Auto,
            forceReplace: true);

        var integrated = RenderContext.GetOrCreateCurrent(
            RenderBackend.D3D12,
            GpuPreference.MinimumPower);

        Assert.NotSame(automatic, integrated);
        Assert.Equal(GpuPreference.MinimumPower, integrated.GpuPreference);
        Assert.Same(integrated, RenderContext.Current);
    }

    /// <summary>
    /// Regression guard for the software-downgrade / re-creation-churn defect:
    /// requesting an UNAVAILABLE explicit backend must leave the working current
    /// context untouched — it must NOT retire it and fall back to the software
    /// rasterizer (nor create a fresh fallback context on every call). Metal is
    /// unavailable on the Windows test host, so it is the probe backend. Without
    /// the availability guard this returns a brand-new (Software) context, so the
    /// <see cref="Assert.Same(object, object)"/> below is the regression bite.
    /// </summary>
    [RequiresBackendAbsentFact(RenderBackend.Metal)]
    public void ExplicitUnavailableBackend_PreservesCurrentContext_NoSoftwareDowngrade()
    {
        var working = RenderContext.GetOrCreateCurrent();
        var workingBackend = working.Backend;

        // Request a backend that cannot be created on this host.
        var afterUnavailable = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);

        // The working context is preserved verbatim — not retired, not replaced
        // by a Software fallback.
        Assert.Same(working, afterUnavailable);
        Assert.Equal(workingBackend, afterUnavailable.Backend);
        Assert.Same(working, RenderContext.Current);
    }

    /// <summary>
    /// The regression that made <c>GetOrCreateCurrent(RenderBackend.Vulkan)</c>
    /// look like a no-op on Windows: an Auto request normalizes to the platform
    /// default (D3D12) and then normalizes its Auto GPU preference to
    /// HighPerformance, while the explicitly installed Vulkan context carries
    /// GpuPreference.Auto. Comparing those two normalized defaults made every
    /// Auto caller — the GPU prewarm first of all — retire the Vulkan context
    /// and install a fresh D3D12 one. A normalized default must never act as a
    /// reuse requirement, so the Auto request has to hand back the very same
    /// Vulkan instance.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void AutoRequest_DefersToExplicitlyInstalledVulkanContext()
    {
        var installed = RenderContext.GetOrCreateCurrent(RenderBackend.Vulkan, forceReplace: true);
        Assert.Equal(RenderBackend.Vulkan, installed.Backend);

        var viaAuto = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.Same(installed, viaAuto);
        Assert.Equal(RenderBackend.Vulkan, RenderContext.Current!.Backend);
    }

    /// <summary>
    /// An honored explicit selection is sticky for the rest of the process: the
    /// device-lost call sites (PopupWindow, DockIndicatorWindow,
    /// <c>TryRecoverFromDeviceLost</c>) all pass <c>RenderBackend.Auto</c> with
    /// <c>forceReplace: true</c>, and must rebuild on the application's backend
    /// instead of silently dropping back to the platform default.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void ForcedAutoReplace_RebuildsOnExplicitlySelectedBackend()
    {
        var installed = RenderContext.GetOrCreateCurrent(RenderBackend.Vulkan, forceReplace: true);
        Assert.Equal(RenderBackend.Vulkan, installed.Backend);

        var rebuilt = RenderContext.GetOrCreateCurrent(RenderBackend.Auto, forceReplace: true);

        Assert.NotSame(installed, rebuilt);
        Assert.Equal(RenderBackend.Vulkan, rebuilt.Backend);
        Assert.Same(rebuilt, RenderContext.Current);
    }

    /// <summary>
    /// Disposing the current context ends the sticky selection: a later Auto
    /// request resolves the platform default again instead of resurrecting a
    /// choice that belonged to a torn-down application.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void DisposingCurrentContext_ClearsStickyBackendSelection()
    {
        var installed = RenderContext.GetOrCreateCurrent(RenderBackend.Vulkan, forceReplace: true);
        Assert.Equal(RenderBackend.Vulkan, installed.Backend);
        installed.Dispose();

        var expected = RenderBackendSelector.GetPreferredBackend();
        var fresh = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.Equal(expected, fresh.Backend);
    }

    /// <summary>
    /// Requests disposal of the current and any retired contexts so the shared
    /// static state (<see cref="RenderContext.Current"/> + retired set) does not
    /// leak across tests in the Application collection. Any still-pinned native
    /// backend is reclaimed by its final resource release.
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
