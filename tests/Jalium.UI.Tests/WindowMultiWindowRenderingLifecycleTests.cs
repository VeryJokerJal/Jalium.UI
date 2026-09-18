using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Exercises the empty-window optimization through real Window/RenderTarget
/// lifecycles. Successful-present counters are used as the frame-completion
/// witness so a context/target reference alone cannot make these tests pass.
/// </summary>
[Collection("Application")]
public sealed class WindowMultiWindowRenderingLifecycleTests : IDisposable
{
    private const int RoundTripIterations = 6;
    private static readonly TimeSpan PresentTimeout = TimeSpan.FromSeconds(10);

    private static readonly FieldInfo SuccessfulPresentCountField =
        typeof(Window).GetField(
            "_successfulPresentCount",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Window._successfulPresentCount was not found.");

    private static readonly FieldInfo EmptyWindowLeaseCountField =
        GetRenderContextField("_emptyWindowLeaseCount");

    private static readonly FieldInfo AutomaticWindowConsumerCountField =
        GetRenderContextField("_automaticWindowConsumerCount");

    private static readonly FieldInfo ActiveRenderTargetCountField =
        GetRenderContextField("_activeRenderTargetCount");

    private static readonly FieldInfo RetiredContextsField =
        typeof(RenderContext).GetField(
            "_retiredContexts",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RenderContext._retiredContexts was not found.");

    private readonly List<Window> _windows = [];
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal)
    {
        [RenderBackendSelector.BackendOverrideEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.BackendOverrideEnvironmentVariable),
        [RenderBackendSelector.GpuPreferenceEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.GpuPreferenceEnvironmentVariable),
        [RenderBackendSelector.EngineOverrideEnvironmentVariable] =
            Environment.GetEnvironmentVariable(RenderBackendSelector.EngineOverrideEnvironmentVariable),
    };

    public WindowMultiWindowRenderingLifecycleTests()
    {
        foreach (var name in _environment.Keys)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        RenderContextEmptyWindowTests.DrainAllContexts();
    }

    public void Dispose()
    {
        try
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                _windows[i].Close();
            }
        }
        finally
        {
            _windows.Clear();
            RenderContextEmptyWindowTests.DrainAllContexts();

            foreach (var (name, value) in _environment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void TwoEmptyWindows_ShareOneSoftwareContext_AndBothPresentFrames()
        => VerifyEmptyWindowGroup(windowCount: 2);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void FourEmptyWindows_ShareOneSoftwareContext_AndAllPresentFrames()
        => VerifyEmptyWindowGroup(windowCount: 4);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void FourWindows_TwoContentOwnersShareAutomaticGpu_AndLastClearRetiresIt()
    {
        var windows = CreateAndShowWindows(4);
        var software = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
        Assert.All(windows, window => AssertSoftwareWindow(window, software));

        var firstContent = CreateContent(0);
        var secondContent = CreateContent(1);
        windows[0].Content = firstContent;
        windows[1].Content = secondContent;

        var gpu = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
        Assert.Equal(RenderBackend.D3D12, gpu.Backend);
        AssertAutomaticGpuWindow(windows[0], gpu);
        AssertAutomaticGpuWindow(windows[1], gpu);
        AssertSoftwareWindow(windows[2], software);
        AssertSoftwareWindow(windows[3], software);
        Assert.Same(gpu, RenderContext.Current);
        Assert.Equal(2, GetContextCount(gpu, AutomaticWindowConsumerCountField));
        Assert.True(software.IsValid);

        AssertNewFramePresented(windows[0]);
        AssertNewFramePresented(windows[1]);

        windows[0].Content = null;

        AssertSoftwareWindow(windows[0], software);
        AssertAutomaticGpuWindow(windows[1], gpu);
        Assert.True(gpu.IsValid);
        Assert.Same(gpu, RenderContext.Current);
        Assert.Equal(1, GetContextCount(gpu, AutomaticWindowConsumerCountField));

        AssertNewFramePresented(
            windows[1],
            () => secondContent.Background = Brushes.Blue);

        windows[1].Content = null;

        Assert.False(gpu.IsValid);
        AssertContextFullyDrained(gpu);
        Assert.Same(software, RenderContext.Current);
        Assert.True(RenderContext.IsEmptyWindowProvisionalCurrent(software));
        Assert.All(windows, window => AssertSoftwareWindow(window, software));

        AssertNewFramePresented(windows[0]);
        AssertNewFramePresented(windows[2]);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ClosingOneOfTwoAutomaticGpuWindows_KeepsPeerAliveForAnotherFrame()
    {
        var windows = CreateAndShowWindows(2);
        var firstContent = CreateContent(0);
        var secondContent = CreateContent(1);

        windows[0].Content = firstContent;
        windows[1].Content = secondContent;

        var gpu = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
        AssertAutomaticGpuWindow(windows[0], gpu);
        AssertAutomaticGpuWindow(windows[1], gpu);
        Assert.Equal(2, GetContextCount(gpu, AutomaticWindowConsumerCountField));

        AssertNewFramePresented(windows[0]);
        AssertNewFramePresented(windows[1]);

        CloseTracked(windows[0]);

        Assert.Equal(nint.Zero, windows[0].Handle);
        Assert.Null(windows[0].RenderTarget);
        Assert.True(gpu.IsValid);
        Assert.Same(gpu, RenderContext.Current);
        AssertAutomaticGpuWindow(windows[1], gpu);
        Assert.Equal(1, GetContextCount(gpu, AutomaticWindowConsumerCountField));

        AssertNewFramePresented(
            windows[1],
            () => secondContent.Background = Brushes.Blue);

        windows[1].Content = null;

        var restoredSoftware = Assert.IsType<RenderContext>(windows[1].RenderTarget?.OwnerContext);
        AssertSoftwareWindow(windows[1], restoredSoftware);
        Assert.Same(restoredSoftware, RenderContext.Current);
        Assert.False(gpu.IsValid);
        AssertContextFullyDrained(gpu);
        AssertNewFramePresented(windows[1]);

        CloseTracked(windows[1]);
        Assert.False(restoredSoftware.IsValid);
        Assert.Null(RenderContext.Current);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExternalBitmapBrushAndRenderTarget_KeepRetiredGpuAliveUntilFinalRelease()
    {
        var windows = CreateAndShowWindows(2);
        var software = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
        var gpuContent = CreateContent(0);
        windows[1].Content = gpuContent;

        var gpu = Assert.IsType<RenderContext>(windows[1].RenderTarget?.OwnerContext);
        AssertAutomaticGpuWindow(windows[1], gpu);
        AssertSoftwareWindow(windows[0], software);
        AssertNewFramePresented(windows[1]);

        using var nativeWindow = new HiddenNativeWindow(width: 64, height: 64);
        RenderTarget? externalTarget = null;
        NativeBitmap? bitmap = null;
        NativeBrush? brush = null;

        try
        {
            externalTarget = gpu.CreateRenderTarget(nativeWindow.Hwnd, width: 64, height: 64);
            bitmap = gpu.CreateBitmapFromPixels(
                [
                    0x00, 0x00, 0xFF, 0xFF,
                    0x00, 0xFF, 0x00, 0xFF,
                    0xFF, 0x00, 0x00, 0xFF,
                    0xFF, 0xFF, 0xFF, 0xFF,
                ],
                width: 2,
                height: 2,
                stride: 8);
            brush = gpu.CreateSolidBrush(0.2f, 0.4f, 0.8f, 1f);

            PresentExternalFrame(externalTarget, bitmap, brush);

            CloseTracked(windows[1]);

            Assert.Same(software, RenderContext.Current);
            Assert.True(gpu.IsValid);
            Assert.True(gpu.IsNativeBackendAlive);
            Assert.True(externalTarget.IsValid);
            Assert.True(bitmap.IsValid);
            Assert.True(brush.IsValid);
            Assert.Equal(0, GetContextCount(gpu, AutomaticWindowConsumerCountField));
            Assert.True(GetContextCount(gpu, ActiveRenderTargetCountField) >= 3);
            Assert.Contains(gpu, GetRetiredContexts());

            // The context has left Current and has no Window consumers. This real
            // target Present proves the remaining resource pins still protect a
            // usable native backend, rather than merely preserving managed fields.
            PresentExternalFrame(externalTarget, bitmap, brush);
            AssertNewFramePresented(windows[0]);

            bitmap.Dispose();
            bitmap = null;
            Assert.True(gpu.IsValid);

            brush.Dispose();
            brush = null;
            Assert.True(gpu.IsValid);
            Assert.True(externalTarget.IsValid);

            externalTarget.Dispose();
            externalTarget = null;

            Assert.False(gpu.IsValid);
            Assert.False(gpu.IsNativeBackendAlive);
            AssertContextFullyDrained(gpu);
        }
        finally
        {
            brush?.Dispose();
            bitmap?.Dispose();
            externalTarget?.Dispose();
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void RepeatedDualWindowAutomaticGpuRoundTrips_LeaveNoConsumersOrPins()
    {
        var windows = CreateAndShowWindows(2);
        int previousGpuGeneration = 0;

        for (int round = 0; round < RoundTripIterations; round++)
        {
            var softwareBeforePromotion =
                Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
            AssertSoftwareWindow(windows[0], softwareBeforePromotion);
            AssertSoftwareWindow(windows[1], softwareBeforePromotion);

            var firstContent = CreateContent(round);
            var secondContent = CreateContent(round + 1);
            windows[0].Content = firstContent;
            windows[1].Content = secondContent;

            var gpu = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
            Assert.True(
                gpu.Generation > previousGpuGeneration,
                $"Round {round}: expected a new GPU generation after the prior one retired.");
            previousGpuGeneration = gpu.Generation;

            AssertAutomaticGpuWindow(windows[0], gpu);
            AssertAutomaticGpuWindow(windows[1], gpu);
            Assert.Same(gpu, RenderContext.Current);
            Assert.False(softwareBeforePromotion.IsValid);
            Assert.Equal(2, GetContextCount(gpu, AutomaticWindowConsumerCountField));

            AssertNewFramePresented(windows[0]);
            AssertNewFramePresented(windows[1]);

            windows[0].Content = null;

            var restoredSoftware =
                Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);
            AssertSoftwareWindow(windows[0], restoredSoftware);
            AssertAutomaticGpuWindow(windows[1], gpu);
            Assert.True(gpu.IsValid);
            Assert.Same(gpu, RenderContext.Current);
            Assert.Equal(1, GetContextCount(gpu, AutomaticWindowConsumerCountField));

            AssertNewFramePresented(
                windows[1],
                () => secondContent.Background = Brushes.White);

            windows[1].Content = null;

            AssertSoftwareWindow(windows[1], restoredSoftware);
            Assert.Same(restoredSoftware, RenderContext.Current);
            Assert.False(gpu.IsValid);
            AssertContextFullyDrained(gpu);

            AssertNewFramePresented(windows[0]);
            AssertNewFramePresented(windows[1]);
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ExplicitGpuSelection_RemainsCurrentAcrossMultiWindowClearAndClose()
    {
        var explicitGpu = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        var first = CreateWindow(CreateContent(0));
        var secondContent = CreateContent(1);
        var second = CreateWindow(secondContent);

        first.Show();
        second.Show();

        AssertExplicitGpuWindow(first, explicitGpu);
        AssertExplicitGpuWindow(second, explicitGpu);
        Assert.Same(explicitGpu, RenderContext.Current);
        Assert.Equal(0, GetContextCount(explicitGpu, AutomaticWindowConsumerCountField));

        AssertNewFramePresented(first);
        AssertNewFramePresented(second);

        CloseTracked(first);

        Assert.True(explicitGpu.IsValid);
        Assert.Same(explicitGpu, RenderContext.Current);
        AssertExplicitGpuWindow(second, explicitGpu);
        AssertNewFramePresented(
            second,
            () => secondContent.Background = Brushes.Blue);

        second.Content = null;

        AssertExplicitGpuWindow(second, explicitGpu);
        Assert.Equal(Window.EmptyRenderingState.Full, second.CurrentEmptyRenderingState);
        AssertNewFramePresented(second);

        CloseTracked(second);

        Assert.True(explicitGpu.IsValid);
        Assert.Same(explicitGpu, RenderContext.Current);
        Assert.Equal(0, GetContextCount(explicitGpu, AutomaticWindowConsumerCountField));
        Assert.Equal(0, GetContextCount(explicitGpu, ActiveRenderTargetCountField));

        explicitGpu.Dispose();
        Assert.False(explicitGpu.IsValid);
        Assert.Null(RenderContext.Current);
    }

    private void VerifyEmptyWindowGroup(int windowCount)
    {
        var windows = CreateAndShowWindows(windowCount);
        var software = Assert.IsType<RenderContext>(windows[0].RenderTarget?.OwnerContext);

        Assert.Equal(RenderBackend.Software, software.Backend);
        Assert.Same(software, RenderContext.Current);
        Assert.True(RenderContext.IsEmptyWindowProvisionalCurrent(software));
        Assert.Equal(windowCount, GetContextCount(software, EmptyWindowLeaseCountField));

        foreach (var window in windows)
        {
            AssertSoftwareWindow(window, software);
            Assert.True(
                GetSuccessfulPresentCount(window) > 0,
                "Window.Show must complete an initial Present before returning.");
            AssertNewFramePresented(window);
        }

        for (int i = 0; i < windows.Length - 1; i++)
        {
            CloseTracked(windows[i]);
            Assert.True(software.IsValid);
            Assert.Same(software, RenderContext.Current);
            Assert.Equal(
                windows.Length - i - 1,
                GetContextCount(software, EmptyWindowLeaseCountField));
        }

        CloseTracked(windows[^1]);

        Assert.False(software.IsValid);
        Assert.Null(RenderContext.Current);
        AssertContextFullyDrained(software);
    }

    private Window[] CreateAndShowWindows(int count)
    {
        var windows = new Window[count];
        for (int i = 0; i < count; i++)
        {
            windows[i] = CreateWindow();
            windows[i].Show();
        }

        return windows;
    }

    private Window CreateWindow(object? content = null)
    {
        var window = new Window
        {
            Width = 240,
            Height = 160,
            ShowActivated = false,
            Content = content,
        };
        _windows.Add(window);
        return window;
    }

    private void CloseTracked(Window window)
    {
        window.Close();
        _windows.Remove(window);
    }

    private static Border CreateContent(int seed)
        => new()
        {
            Background = (seed % 3) switch
            {
                0 => Brushes.Red,
                1 => Brushes.Green,
                _ => Brushes.Blue,
            },
        };

    private static void AssertSoftwareWindow(Window window, RenderContext expectedContext)
    {
        Assert.NotNull(window.RenderTarget);
        Assert.True(window.RenderTarget!.IsValid);
        Assert.Equal(RenderBackend.Software, window.RenderTarget.Backend);
        Assert.Same(expectedContext, window.RenderTarget.OwnerContext);
        Assert.True(window.UsesAutomaticEmptySoftwareContext);
        Assert.False(window.UsesAutomaticGpuContext);
        Assert.Equal(Window.EmptyRenderingState.Software, window.CurrentEmptyRenderingState);
    }

    private static void AssertAutomaticGpuWindow(Window window, RenderContext expectedContext)
    {
        Assert.NotNull(window.RenderTarget);
        Assert.True(window.RenderTarget!.IsValid);
        Assert.Equal(RenderBackend.D3D12, window.RenderTarget.Backend);
        Assert.Same(expectedContext, window.RenderTarget.OwnerContext);
        Assert.True(RenderContext.IsBackendSelectionAutomatic(expectedContext));
        Assert.False(window.UsesAutomaticEmptySoftwareContext);
        Assert.True(window.UsesAutomaticGpuContext);
        Assert.Equal(Window.EmptyRenderingState.Full, window.CurrentEmptyRenderingState);
    }

    private static void AssertExplicitGpuWindow(Window window, RenderContext expectedContext)
    {
        Assert.NotNull(window.RenderTarget);
        Assert.True(window.RenderTarget!.IsValid);
        Assert.Equal(RenderBackend.D3D12, window.RenderTarget.Backend);
        Assert.Same(expectedContext, window.RenderTarget.OwnerContext);
        Assert.False(RenderContext.IsBackendSelectionAutomatic(expectedContext));
        Assert.False(window.UsesAutomaticEmptySoftwareContext);
        Assert.False(window.UsesAutomaticGpuContext);
    }

    private static void AssertNewFramePresented(Window window, Action? mutate = null)
    {
        long before = GetSuccessfulPresentCount(window);
        mutate?.Invoke();
        window.ForceRenderFrame();

        bool presented = GetSuccessfulPresentCount(window) > before;
        var stopwatch = Stopwatch.StartNew();
        while (!presented && stopwatch.Elapsed < PresentTimeout)
        {
            window.Dispatcher.ProcessQueue();
            presented = GetSuccessfulPresentCount(window) > before;
            if (!presented)
            {
                Thread.Sleep(1);
            }
        }

        Assert.True(
            presented,
            $"Window did not complete another Present within {PresentTimeout}. " +
            $"Backend={window.RenderTarget?.Backend}, before={before}, " +
            $"after={GetSuccessfulPresentCount(window)}.");
    }

    private static void PresentExternalFrame(
        RenderTarget target,
        NativeBitmap bitmap,
        NativeBrush brush)
    {
        bool began = target.TryBeginDraw();
        var stopwatch = Stopwatch.StartNew();
        while (!began && stopwatch.Elapsed < PresentTimeout)
        {
            Thread.Sleep(1);
            began = target.TryBeginDraw();
        }

        Assert.True(began, "External render target never accepted a BeginDraw.");
        try
        {
            target.Clear(0.05f, 0.05f, 0.08f, 1f);
            target.FillRectangle(4, 4, 56, 56, brush);
            target.DrawBitmap(bitmap, 16, 16, 32, 32);
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }
        finally
        {
            if (target.IsDrawing)
            {
                _ = target.TryEndDraw();
            }
        }
    }

    private static long GetSuccessfulPresentCount(Window window)
        => (long)SuccessfulPresentCountField.GetValue(window)!;

    private static int GetContextCount(RenderContext context, FieldInfo field)
        => (int)field.GetValue(context)!;

    private static IReadOnlyList<RenderContext> GetRetiredContexts()
    {
        var retired = Assert.IsAssignableFrom<IEnumerable>(RetiredContextsField.GetValue(null));
        return retired.Cast<RenderContext>().ToArray();
    }

    private static void AssertContextFullyDrained(RenderContext context)
    {
        Assert.Equal(0, GetContextCount(context, EmptyWindowLeaseCountField));
        Assert.Equal(0, GetContextCount(context, AutomaticWindowConsumerCountField));
        Assert.Equal(0, GetContextCount(context, ActiveRenderTargetCountField));
        Assert.DoesNotContain(context, GetRetiredContexts());
    }

    private static FieldInfo GetRenderContextField(string fieldName)
        => typeof(RenderContext).GetField(
               fieldName,
               BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException(
               $"RenderContext.{fieldName} was not found.");
}
