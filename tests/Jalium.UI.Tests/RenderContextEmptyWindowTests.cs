using System.Collections;
using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class RenderContextEmptyWindowTests : IDisposable
{
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal)
    {
        [RenderBackendSelector.BackendOverrideEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.BackendOverrideEnvironmentVariable),
        [RenderBackendSelector.GpuPreferenceEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.GpuPreferenceEnvironmentVariable),
        [RenderBackendSelector.EngineOverrideEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.EngineOverrideEnvironmentVariable),
    };

    public RenderContextEmptyWindowTests()
    {
        foreach (var name in _environment.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        DrainAllContexts();
    }

    [RequiresWindowsFact]
    public void FirstLease_PublishesProvisionalSoftware_AndLastReleaseReclaimsIt()
    {
        var lease = AcquireLease();
        var software = lease.Context;

        Assert.Equal(RenderBackend.Software, software.Backend);
        Assert.Same(software, RenderContext.Current);
        Assert.True(RenderContext.IsEmptyWindowProvisionalCurrent(software));

        lease.Dispose();

        Assert.Null(RenderContext.Current);
        Assert.False(software.IsValid);
    }

    [RequiresWindowsFact]
    public void MultipleLeases_ShareOneSoftwareContext_AndKeepItUntilTheLastRelease()
    {
        var first = AcquireLease();
        var second = AcquireLease();
        var software = first.Context;

        Assert.Same(software, second.Context);

        first.Dispose();
        Assert.True(software.IsValid);
        Assert.Same(software, RenderContext.Current);

        second.Dispose();
        Assert.False(software.IsValid);
        Assert.Null(RenderContext.Current);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void AutoDemand_PromotesProvisionalCurrent_WhileLeaseKeepsOldTargetContextAlive()
    {
        var lease = AcquireLease();
        var software = lease.Context;

        var promoted = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.NotSame(software, promoted);
        Assert.Equal(RenderBackend.D3D12, promoted.Backend);
        Assert.Same(promoted, RenderContext.Current);
        Assert.True(software.IsValid);
        Assert.False(RenderContext.IsEmptyWindowProvisionalCurrent(software));

        lease.Dispose();

        Assert.False(software.IsValid);
        Assert.True(promoted.IsValid);
        Assert.Same(promoted, RenderContext.Current);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExistingAutomaticGpuCurrent_IsNeverReplacedByAnEmptyWindowLease()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        Assert.Equal(RenderBackend.D3D12, gpu.Backend);

        var lease = AcquireLease();
        var software = lease.Context;

        Assert.NotSame(gpu, software);
        Assert.Same(gpu, RenderContext.Current);

        lease.Dispose();

        Assert.False(software.IsValid);
        Assert.True(gpu.IsValid);
        Assert.Same(gpu, RenderContext.Current);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void LastAutomaticWindowConsumer_ReturnsCurrentToLeasedSoftwareContext()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        using var softwareLease = AcquireLease();
        var software = softwareLease.Context;
        var windowLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));

        Assert.Same(gpu, RenderContext.Current);
        Assert.NotSame(gpu, software);

        windowLease.Dispose();

        Assert.False(gpu.IsValid);
        Assert.Same(software, RenderContext.Current);
        Assert.True(RenderContext.IsEmptyWindowProvisionalCurrent(software));
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void MultipleAutomaticWindowConsumers_KeepGpuCurrentUntilLastRelease()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        using var softwareLease = AcquireLease();
        var first = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));
        var second = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));

        first.Dispose();
        Assert.True(gpu.IsValid);
        Assert.Same(gpu, RenderContext.Current);

        second.Dispose();
        Assert.False(gpu.IsValid);
        Assert.Same(softwareLease.Context, RenderContext.Current);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitGpuCurrent_DoesNotOfferAutomaticWindowOwnership()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        Assert.Null(RenderContext.TryAcquireAutomaticWindowContext(gpu));
        Assert.Same(gpu, RenderContext.Current);
        Assert.True(gpu.IsValid);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExternalBitmapAndBrush_PinRetiredGenerationUntilTheirNativeDestroy()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        int gpuGeneration = gpu.Generation;
        using var softwareLease = AcquireLease();
        var windowLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));
        var bitmap = gpu.CreateBitmapFromPixels(
            [0x10, 0x20, 0x30, 0xFF],
            width: 1,
            height: 1,
            stride: 4);
        var brush = gpu.CreateSolidBrush(0.1f, 0.2f, 0.3f, 1f);

        windowLease.Dispose();

        Assert.Same(softwareLease.Context, RenderContext.Current);
        Assert.True(gpu.IsValid);
        Assert.Equal(gpuGeneration, gpu.Generation);
        Assert.True(bitmap.IsValid);
        Assert.True(brush.IsValid);

        bitmap.Dispose();
        Assert.True(gpu.IsValid); // the external brush is now the final consumer

        brush.Dispose();
        Assert.False(gpu.IsValid);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExternalTextFormat_PinsRetiredGenerationUntilItsNativeDestroy()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        using var softwareLease = AcquireLease();
        var windowLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));
        var format = gpu.CreateTextFormat("Segoe UI", 12f);

        windowLease.Dispose();

        Assert.Same(softwareLease.Context, RenderContext.Current);
        Assert.True(gpu.IsValid);
        Assert.True(format.IsValid);

        var metrics = format.MeasureText("Pinned format", 200f, 100f);
        Assert.True(metrics.LineHeight > 0f);

        format.Dispose();

        Assert.False(gpu.IsValid);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExternalRenderTarget_PinsRetiredGenerationUntilTargetDestroy()
    {
        var gpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        using var softwareLease = AcquireLease();
        var windowLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(gpu));
        var native = new RenderTargetTestNative();
        bool backendAliveAtDestroy = false;
        native.OnDestroy = () => backendAliveAtDestroy = gpu.Handle != nint.Zero;
        var target = new RenderTarget(
            backend: gpu.Backend,
            contextHandle: gpu.Handle,
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x4410)),
            width: 24,
            height: 24,
            useComposition: false,
            native: native,
            ownerContext: gpu);

        windowLease.Dispose();

        Assert.Same(softwareLease.Context, RenderContext.Current);
        Assert.True(gpu.IsValid);
        Assert.True(target.IsValid);

        target.Dispose();

        Assert.Equal(1, native.DestroyCalls);
        Assert.True(backendAliveAtDestroy);
        Assert.False(gpu.IsValid);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void AutoGpuCanBeCreatedAgainAfterReturningToSoftware_WithNewGeneration()
    {
        var firstGpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        using var softwareLease = AcquireLease();
        var windowLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(firstGpu));

        windowLease.Dispose();
        Assert.Same(softwareLease.Context, RenderContext.Current);

        var secondGpu = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.Equal(RenderBackend.D3D12, secondGpu.Backend);
        Assert.NotSame(firstGpu, secondGpu);
        Assert.True(secondGpu.Generation > firstGpu.Generation);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ForcedAutoReplacement_KeepsOldGenerationAliveUntilWindowLeaseMoves()
    {
        var first = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        var firstLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(first));

        var second = RenderContext.GetOrCreateCurrent(
            RenderBackend.Auto,
            forceReplace: true);

        Assert.NotSame(first, second);
        Assert.True(first.IsValid);
        Assert.True(second.Generation > first.Generation);

        var secondLease = Assert.IsType<RenderContext.AutomaticWindowContextLease>(
            RenderContext.TryAcquireAutomaticWindowContext(second));
        firstLease.Dispose();

        Assert.False(first.IsValid);
        Assert.True(second.IsValid);
        secondLease.Dispose();
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitBackendCurrent_DisablesEmptyWindowOptimization()
    {
        var explicitContext = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);

        var lease = RenderContext.TryAcquireEmptyWindowSoftwareContext();

        Assert.Null(lease);
        Assert.Same(explicitContext, RenderContext.Current);
        Assert.False(RenderContext.IsBackendSelectionAutomatic(explicitContext));
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitRenderingEngineCurrent_DisablesEmptyWindowOptimization()
    {
        var explicitContext = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        explicitContext.DefaultRenderingEngine = RenderingEngine.Vello;

        var lease = RenderContext.TryAcquireEmptyWindowSoftwareContext();

        Assert.Null(lease);
        Assert.Same(explicitContext, RenderContext.Current);
        Assert.False(RenderContext.IsBackendSelectionAutomatic(explicitContext));
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitGpuPreferenceCurrent_DisablesEmptyWindowOptimization()
    {
        var explicitContext = RenderContext.GetOrCreateCurrent(
            RenderBackend.Auto,
            GpuPreference.MinimumPower,
            forceReplace: true);

        var lease = RenderContext.TryAcquireEmptyWindowSoftwareContext();

        Assert.Null(lease);
        Assert.Same(explicitContext, RenderContext.Current);
    }

    [RequiresWindowsFact]
    public void ExplicitSoftwareRequest_NormalizesProvisionalCurrentWithoutDisposingIt()
    {
        var lease = AcquireLease();
        var software = lease.Context;

        var explicitSoftware = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

        Assert.Same(software, explicitSoftware);
        Assert.False(RenderContext.IsEmptyWindowProvisionalCurrent(software));
        Assert.False(RenderContext.IsBackendSelectionAutomatic(software));

        lease.Dispose();

        Assert.True(software.IsValid);
        Assert.Same(software, RenderContext.Current);
    }

    [RequiresWindowsFact]
    public void ExplicitEnvironmentSelections_DisableEmptyWindowOptimization()
    {
        var cases = new (string Name, string Value)[]
        {
            (RenderBackendSelector.BackendOverrideEnvironmentVariable, "software"),
            (RenderBackendSelector.GpuPreferenceEnvironmentVariable, "auto"),
            (RenderBackendSelector.EngineOverrideEnvironmentVariable, "vello"),
        };

        foreach (var (name, value) in cases)
        {
            Environment.SetEnvironmentVariable(name, value);
            try
            {
                Assert.Null(RenderContext.TryAcquireEmptyWindowSoftwareContext());
                Assert.Null(RenderContext.Current);
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Promotion_ClearsTextFormatsOwnedByTheProvisionalContext()
    {
        using var lease = AcquireLease();
        var software = lease.Context;
        var text = new FormattedText("Jalium", "Segoe UI", 13);

        Assert.True(TextMeasurement.MeasureText(text));
        Assert.NotEmpty(GetTextFormatCache());

        var promoted = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);

        Assert.NotSame(software, promoted);
        Assert.Empty(GetTextFormatCache());
    }

    [RequiresWindowsFact]
    public void ConcurrentAcquire_UsesOneSharedContext_AndBalancesEveryLease()
    {
        const int leaseCount = 16;
        var leases = new RenderContext.EmptyWindowSoftwareContextLease[leaseCount];

        Parallel.For(0, leaseCount, i =>
        {
            leases[i] = AcquireLease();
        });

        var software = leases[0].Context;
        Assert.All(leases, lease => Assert.Same(software, lease.Context));

        Parallel.ForEach(leases, lease => lease.Dispose());

        Assert.False(software.IsValid);
        Assert.Null(RenderContext.Current);
    }

    public void Dispose()
    {
        TextMeasurement.ClearCache();
        DrainAllContexts();

        foreach (var (name, value) in _environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static RenderContext.EmptyWindowSoftwareContextLease AcquireLease()
        => Assert.IsType<RenderContext.EmptyWindowSoftwareContextLease>(
            RenderContext.TryAcquireEmptyWindowSoftwareContext());

    private static IDictionary GetTextFormatCache()
    {
        var field = typeof(TextMeasurement).GetField(
            "_formatCache",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IDictionary>(field!.GetValue(null));
    }

    internal static void DrainAllContexts()
    {
        RenderContext.Current?.Dispose();

        var poolField = typeof(RenderContext).GetField(
            "_emptyWindowSoftwareContext",
            BindingFlags.NonPublic | BindingFlags.Static);
        (poolField?.GetValue(null) as RenderContext)?.Dispose();

        var retiredField = typeof(RenderContext).GetField(
            "_retiredContexts",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (retiredField?.GetValue(null) is IEnumerable retired)
        {
            foreach (var context in retired.Cast<RenderContext>().ToList())
            {
                context.Dispose();
            }
        }
    }
}
