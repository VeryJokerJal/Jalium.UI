using System.Reflection;
using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowRenderWorkerDiagnosticsTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void WorkerPresentPublishesNativePathStatsWhenSelected() => Verify(enabled: true, selected: true);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void WorkerPresentDoesNotPublishWithDiagnosticsDisabled() => Verify(enabled: false, selected: true);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void WorkerPresentDoesNotOverwriteAnotherWindowsPathStats() => Verify(enabled: true, selected: false);

    private static void Verify(bool enabled, bool selected)
    {
        MediaRenderCacheHost.Bootstrap();
        using var nativeWindow = new HiddenNativeWindow(32, 32);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(nativeWindow.Hwnd, 32, 32);
        var window = new Window { Width = 32, Height = 32 };
        typeof(Window).GetProperty("RenderTarget")!.SetValue(window, target);
        var captureType = typeof(Window).GetNestedType("FrameCapture", BindingFlags.NonPublic)!;
        var capture = Activator.CreateInstance(captureType, nonPublic: true)!;
        void Field(string name, object value) => captureType.GetField(name)!.SetValue(capture, value);
        Field("Drawing", RecordedDrawing.Empty);
        Field("Target", target);
        Field("OwnerContext", context);
        Field("ContextGeneration", target.OwnerContextGeneration);
        Field("LifecycleGeneration", typeof(Window).GetField("_renderLifecycleGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
        bool previousEnabled = RenderDiagnostics.ApiStatsEnabled;
        object? previousOwner = RenderDiagnostics.StatsOwner;
        try
        {
            RenderDiagnostics.ApiStatsEnabled = enabled;
            RenderDiagnostics.StatsOwner = selected ? window : new object();
            var before = RenderDiagnostics.LatestPathCacheStats;
            typeof(Window).GetMethod("PresentCaptureOnRenderThreadCore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [capture]);
            Assert.False(target.IsDrawing);
            if (enabled && selected)
            {
                Assert.NotNull(RenderDiagnostics.LatestPathCacheStats);
                Assert.NotSame(before, RenderDiagnostics.LatestPathCacheStats);
            }
            else
            {
                Assert.Same(before, RenderDiagnostics.LatestPathCacheStats);
            }
        }
        finally
        {
            RenderDiagnostics.ApiStatsEnabled = previousEnabled;
            RenderDiagnostics.StatsOwner = previousOwner;
            window.Close();
        }
    }
}
