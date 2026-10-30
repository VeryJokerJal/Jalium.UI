using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class RenderTargetMemoryPressureTests
{
    [Fact]
    public void SoftwareCreateAndDispose_ReportsLogicalBgraBytesAfterAllocationAndRemovalAfterDestroy()
    {
        var events = new ConcurrentQueue<string>();
        var native = new RenderTargetTestNative
        {
            OnCreate = () => events.Enqueue("create"),
            OnDestroy = () => events.Enqueue("destroy")
        };
        var pressure = new RecordingMemoryPressure(events);

        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.Equal(
            new[]
            {
                "create",
                "add:1920000"
            },
            events.ToArray());

        renderTarget.Dispose();

        Assert.Equal(
            new[]
            {
                "create",
                "add:1920000",
                "destroy",
                "remove:1920000"
            },
            events.ToArray());
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_920_000),
                new PressureCall(PressureOperation.Remove, 1_920_000)
            },
            pressure.Calls);
    }

    [Fact]
    public void SoftwareResize_TracksHistoricalMaximumUntilDestroy()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 64,
            height: 32);

        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(128, 64));
        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(128, 64));
        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(16, 8));
        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(100, 60));
        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(160, 64));

        Assert.Equal(5, native.ResizeCalls);
        Assert.Equal(160, renderTarget.Width);
        Assert.Equal(64, renderTarget.Height);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 8_192),
                new PressureCall(PressureOperation.Add, 24_576),
                new PressureCall(PressureOperation.Add, 8_192)
            },
            pressure.Calls);

        renderTarget.Dispose();

        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 8_192),
                new PressureCall(PressureOperation.Add, 24_576),
                new PressureCall(PressureOperation.Add, 8_192),
                new PressureCall(PressureOperation.Remove, 40_960)
            },
            pressure.Calls);
    }

    [Fact]
    public void SoftwareCreate_WithZeroDimension_DoesNotReportPressure()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 0,
            height: 32);

        Assert.Equal(1, native.CreateCalls);
        Assert.Empty(pressure.Calls);

        renderTarget.Dispose();

        Assert.Equal(1, native.DestroyCalls);
        Assert.Empty(pressure.Calls);
    }

    [Fact]
    public void SoftwareResize_WithZeroDimension_IsNoOp()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 8,
            height: 8);

        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(0, 16));
        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(16, 0));

        Assert.Equal(0, native.ResizeCalls);
        Assert.Equal(8, renderTarget.Width);
        Assert.Equal(8, renderTarget.Height);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 256)
            },
            pressure.Calls);

        renderTarget.Dispose();

        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 256),
                new PressureCall(PressureOperation.Remove, 256)
            },
            pressure.Calls);
    }

    [Fact]
    public void SoftwareResize_BusyAndFailure_DoNotChangePressureOrDimensions()
    {
        var native = new RenderTargetTestNative
        {
            ResizeResult = (int)JaliumResult.Busy
        };
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 320,
            height: 240);

        Assert.Equal(JaliumResult.Busy, renderTarget.Resize(640, 480));
        Assert.Equal(320, renderTarget.Width);
        Assert.Equal(240, renderTarget.Height);

        native.ResizeResult = (int)JaliumResult.DeviceLost;
        Assert.Throws<RenderPipelineException>(() => renderTarget.Resize(800, 600));
        Assert.Equal(320, renderTarget.Width);
        Assert.Equal(240, renderTarget.Height);

        Assert.Equal(2, native.ResizeCalls);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 307_200)
            },
            pressure.Calls);

        renderTarget.Dispose();

        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 307_200),
                new PressureCall(PressureOperation.Remove, 307_200)
            },
            pressure.Calls);
    }

    [Fact]
    public void NonSoftwareTarget_DoesNotReportMemoryPressure()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            backend: RenderBackend.D3D12,
            width: 800,
            height: 600);

        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(1024, 768));
        renderTarget.Dispose();

        Assert.Empty(pressure.Calls);
        Assert.Equal(1, native.DestroyCalls);
    }

    [Fact]
    public void OwnedStorageQuery_CompactMaterializeAndDispose_TracksExactCapacity()
    {
        const ulong DenseBytes = 2_097_152;
        const ulong CompactBytes = 131_072;
        var native = new RenderTargetTestNative
        {
            FramebufferStorageAbiAvailable = true,
            MainFramebufferOwnedBytes = DenseBytes
        };
        native.OnCompactIdleStorage = () =>
            native.MainFramebufferOwnedBytes = CompactBytes;
        native.OnBeginDraw = () =>
            native.MainFramebufferOwnedBytes = DenseBytes;
        var pressure = new RecordingMemoryPressure();
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.True(renderTarget.TryCompactIdleStorage());
        renderTarget.BeginDraw();
        renderTarget.EndDraw();
        renderTarget.Dispose();

        Assert.Equal(1, native.CompactIdleStorageCalls);
        Assert.Equal(4, native.QueryMainFramebufferOwnedBytesCalls);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 2_097_152),
                new PressureCall(PressureOperation.Remove, 1_966_080),
                new PressureCall(PressureOperation.Add, 1_966_080),
                new PressureCall(PressureOperation.Remove, 2_097_152)
            },
            pressure.Calls);
    }

    [Fact]
    public void OwnedStorageQuery_ResizeCanReducePressureBelowHistoricalPeak()
    {
        var native = new RenderTargetTestNative
        {
            FramebufferStorageAbiAvailable = true,
            MainFramebufferOwnedBytes = 1_920_000
        };
        native.OnResize = (_, _) =>
            native.MainFramebufferOwnedBytes = 262_144;
        var pressure = new RecordingMemoryPressure();
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.Equal(JaliumResult.Ok, renderTarget.Resize(256, 256));
        Assert.Equal(256, renderTarget.Width);
        Assert.Equal(256, renderTarget.Height);
        renderTarget.Dispose();

        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_920_000),
                new PressureCall(PressureOperation.Remove, 1_657_856),
                new PressureCall(PressureOperation.Remove, 262_144)
            },
            pressure.Calls);
    }

    [Fact]
    public void TryCompactIdleStorage_NoActualSaving_ReturnsFalseAndKeepsPressure()
    {
        var native = new RenderTargetTestNative
        {
            FramebufferStorageAbiAvailable = true,
            MainFramebufferOwnedBytes = 1_920_000
        };
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.False(renderTarget.TryCompactIdleStorage());

        Assert.Equal(1, native.CompactIdleStorageCalls);
        Assert.Equal(3, native.QueryMainFramebufferOwnedBytesCalls);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_920_000)
            },
            pressure.Calls);
    }

    [Fact]
    public void TryCompactIdleStorage_NativeOutOfMemory_ReturnsFalseAndKeepsPressure()
    {
        var native = new RenderTargetTestNative
        {
            FramebufferStorageAbiAvailable = true,
            MainFramebufferOwnedBytes = 1_920_000,
            CompactIdleStorageResult = (int)JaliumResult.OutOfMemory
        };
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.False(renderTarget.TryCompactIdleStorage());

        Assert.Equal(1, native.CompactIdleStorageCalls);
        Assert.Equal(2, native.QueryMainFramebufferOwnedBytesCalls);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_920_000)
            },
            pressure.Calls);
    }

    [Fact]
    public void TryCompactIdleStorage_WhileDrawing_DoesNotCallNative()
    {
        var native = new RenderTargetTestNative
        {
            FramebufferStorageAbiAvailable = true,
            MainFramebufferOwnedBytes = 1_920_000
        };
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        renderTarget.BeginDraw();
        Assert.False(renderTarget.TryCompactIdleStorage());
        renderTarget.EndDraw();

        Assert.Equal(0, native.CompactIdleStorageCalls);
        Assert.Equal(2, native.QueryMainFramebufferOwnedBytesCalls);
    }

    [Fact]
    public void TryCompactIdleStorage_MissingStorageAbi_UsesLegacyPressureAndReturnsFalse()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 800,
            height: 600);

        Assert.False(renderTarget.TryCompactIdleStorage());

        Assert.Equal(0, native.CompactIdleStorageCalls);
        Assert.Equal(0, native.QueryMainFramebufferOwnedBytesCalls);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_920_000)
            },
            pressure.Calls);
    }

    [Fact]
    public void CreateFailure_DoesNotReportMemoryPressureOrDestroyNullHandle()
    {
        var native = new RenderTargetTestNative
        {
            CreatedHandle = nint.Zero,
            ContextLastError = (int)JaliumResult.ResourceCreationFailed
        };
        var pressure = new RecordingMemoryPressure();

        Assert.Throws<RenderPipelineException>(() =>
            CreateRenderTarget(native, pressure, width: 40, height: 30));

        Assert.Equal(1, native.CreateCalls);
        Assert.Equal(0, native.DestroyCalls);
        Assert.Empty(pressure.Calls);
    }

    [Fact]
    public void PostCreateConstructorFailure_RollsBackAfterDestroy()
    {
        var events = new ConcurrentQueue<string>();
        var native = new RenderTargetTestNative
        {
            ThrowOnSupportsPartialPresentation = true,
            OnCreate = () => events.Enqueue("create"),
            OnDestroy = () => events.Enqueue("destroy")
        };
        var pressure = new RecordingMemoryPressure(events);

        Assert.Throws<InvalidOperationException>(() =>
            CreateRenderTarget(native, pressure, width: 40, height: 30));

        Assert.Equal(1, native.DestroyCalls);
        Assert.Equal(
            new[]
            {
                "create",
                "add:4800",
                "destroy",
                "remove:4800"
            },
            events.ToArray());
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 4_800),
                new PressureCall(PressureOperation.Remove, 4_800)
            },
            pressure.Calls);
    }

    [Fact]
    public void RepeatedDispose_DestroysAndRemovesPressureOnce()
    {
        var events = new ConcurrentQueue<string>();
        var native = new RenderTargetTestNative
        {
            OnDestroy = () => events.Enqueue("destroy")
        };
        var pressure = new RecordingMemoryPressure(events);
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 20,
            height: 10);

        renderTarget.Dispose();
        renderTarget.Dispose();

        Assert.Equal(1, native.DestroyCalls);
        Assert.Equal(
            new[]
            {
                "add:800",
                "destroy",
                "remove:800"
            },
            events.ToArray());
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 800),
                new PressureCall(PressureOperation.Remove, 800)
            },
            pressure.Calls);
    }

    [Fact]
    public void Finalizer_DestroysBeforeRemovingPressureExactlyOnce()
    {
        var events = new ConcurrentQueue<string>();
        var native = new RenderTargetTestNative
        {
            OnDestroy = () => events.Enqueue("destroy")
        };
        var pressure = new RecordingMemoryPressure(events);
        WeakReference<RenderTarget> renderTarget = CreateAbandonedRenderTarget(
            native,
            pressure,
            width: 24,
            height: 12);

        ForceFinalizers();

        Assert.False(renderTarget.TryGetTarget(out _));
        Assert.Equal(1, native.DestroyCalls);
        Assert.Equal(
            new[]
            {
                "add:1152",
                "destroy",
                "remove:1152"
            },
            events.ToArray());
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 1_152),
                new PressureCall(PressureOperation.Remove, 1_152)
            },
            pressure.Calls);

        GC.KeepAlive(native);
        GC.KeepAlive(pressure);
    }

    [Fact]
    public void SoftwareDimensionsThatOverflowInt64_DoNotInvokeNativeOrPressure()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();

        Assert.Throws<OverflowException>(() =>
            CreateRenderTarget(
                native,
                pressure,
                width: int.MaxValue,
                height: int.MaxValue));

        Assert.Equal(0, native.CreateCalls);
        Assert.Empty(pressure.Calls);
    }

    [Fact]
    public void SoftwareResizeThatOverflowsInt64_DoesNotInvokeNativeOrChangePressure()
    {
        var native = new RenderTargetTestNative();
        var pressure = new RecordingMemoryPressure();
        using var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: 8,
            height: 8);

        Assert.Throws<OverflowException>(() =>
            renderTarget.Resize(int.MaxValue, int.MaxValue));

        Assert.Equal(0, native.ResizeCalls);
        Assert.Equal(8, renderTarget.Width);
        Assert.Equal(8, renderTarget.Height);
        Assert.Equal(
            new[]
            {
                new PressureCall(PressureOperation.Add, 256)
            },
            pressure.Calls);
    }

    private static RenderTarget CreateRenderTarget(
        RenderTargetTestNative native,
        RecordingMemoryPressure pressure,
        RenderBackend backend = RenderBackend.Software,
        int width = 320,
        int height = 240,
        bool useComposition = false)
    {
        return new RenderTarget(
            backend: backend,
            contextHandle: new nint(0x1111),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x1234)),
            width: width,
            height: height,
            useComposition: useComposition,
            native: native,
            memoryPressure: pressure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<RenderTarget> CreateAbandonedRenderTarget(
        RenderTargetTestNative native,
        RecordingMemoryPressure pressure,
        int width,
        int height)
    {
        var renderTarget = CreateRenderTarget(
            native,
            pressure,
            width: width,
            height: height);
        return new WeakReference<RenderTarget>(renderTarget);
    }

    private static void ForceFinalizers()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private enum PressureOperation
    {
        Add,
        Remove
    }

    private readonly record struct PressureCall(
        PressureOperation Operation,
        long Bytes);

    private sealed class RecordingMemoryPressure : IRenderTargetMemoryPressure
    {
        private readonly ConcurrentQueue<PressureCall> _calls = new();
        private readonly ConcurrentQueue<string>? _events;

        internal RecordingMemoryPressure(ConcurrentQueue<string>? events = null)
        {
            _events = events;
        }

        internal PressureCall[] Calls => _calls.ToArray();

        public void Add(long bytes)
        {
            _calls.Enqueue(new PressureCall(PressureOperation.Add, bytes));
            _events?.Enqueue($"add:{bytes}");
        }

        public void Remove(long bytes)
        {
            _calls.Enqueue(new PressureCall(PressureOperation.Remove, bytes));
            _events?.Enqueue($"remove:{bytes}");
        }
    }
}
