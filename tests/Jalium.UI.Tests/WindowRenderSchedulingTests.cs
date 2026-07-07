using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

// [Collection("Application")] is mandatory: these tests construct Window
// instances; running in parallel with other Window-constructing test classes
// races DependencyProperty metadata registration (random flaky failures).
[Collection("Application")]
public class WindowRenderSchedulingTests
{
    private const int RenderFlag_Scheduled = 1 << 0;
    private const int RenderFlag_DirtyBetween = 1 << 3;

    [Fact]
    public void RenderTarget_TryBeginDraw_WhenGpuBusy_ReturnsFalse()
    {
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 320, height: 240, hwnd: new nint(0x2101));

        Assert.False(renderTarget.TryBeginDraw());
    }

    [Fact]
    public void RenderTarget_TryBeginDraw_WhenBeginFails_Throws()
    {
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 320, height: 240, hwnd: new nint(0x2102));

        var exception = Assert.Throws<RenderPipelineException>(() => renderTarget.TryBeginDraw());
        Assert.Equal("Begin", exception.Stage);
    }

    [Fact]
    public void Window_ForceRenderFrame_WhenGpuBusy_ArmsRetryWithoutUpdatingLastRenderTimestamp()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2103));

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2103));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        window.ForceRenderFrame();

        Assert.Equal(0L, GetPrivateField<long>(window, "_lastRenderTicks"));
        // The retry is an ARMED deferred timer; RenderFlag_Scheduled is claimed only
        // when it fires. Claiming at arm time blocked every animation tick's
        // InvalidateWindow for the timer's ~15.6 ms resolution window after each
        // present — the present-cadence stutter fixed with the animation rewrite.
        Assert.NotNull(GetPrivateField<Timer?>(window, "_renderThrottleTimer"));

        GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
    }

    [Fact]
    public void Window_InvalidateWindow_WhenCompositionTargetIsUncapped_SchedulesImmediateRender()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2104));

        EventHandler noop = static (_, _) => { };
        CompositionTarget.Rendering += noop;
        CompositionTarget.Subscribe();

        try
        {
            window.InvalidateWindow();

            Assert.True(HasRenderFlag(window, RenderFlag_Scheduled));
            Assert.False(HasRenderFlag(window, RenderFlag_DirtyBetween));
        }
        finally
        {
            CompositionTarget.Unsubscribe();
            CompositionTarget.Rendering -= noop;
            GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
        }
    }

    [Fact]
    public void Window_ForceRenderFrame_WhenGpuBusyAndCompositionTargetIsUncapped_SchedulesDeferredRetry()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateField(window, "<Handle>k__BackingField", new nint(0x2105));

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InvalidState
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2105));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        EventHandler noop = static (_, _) => { };
        CompositionTarget.Rendering += noop;
        CompositionTarget.Subscribe();

        try
        {
            window.ForceRenderFrame();

            // Deferred retry armed as a timer; the Scheduled flag must stay CLEAR
            // until the timer fires so that real invalidations (animation ticks)
            // arriving in the interim can schedule a render immediately instead
            // of being blocked behind the ~15.6 ms coarse timer.
            Assert.NotNull(GetPrivateField<Timer?>(window, "_renderThrottleTimer"));
            Assert.False(HasRenderFlag(window, RenderFlag_Scheduled));
            Assert.False(HasRenderFlag(window, RenderFlag_DirtyBetween));
        }
        finally
        {
            CompositionTarget.Unsubscribe();
            CompositionTarget.Rendering -= noop;
            GetPrivateField<Timer?>(window, "_renderThrottleTimer")?.Dispose();
        }
    }

    private static RenderTarget CreateRenderTarget(RenderTargetTestNative native, int width, int height, nint hwnd)
    {
        return new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1234),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(hwnd),
            width: width,
            height: height,
            useComposition: false,
            native: native);
    }

    private static bool HasRenderFlag(Window window, int flag)
    {
        var state = GetPrivateField<int>(window, "_renderState");
        return (state & flag) != 0;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }
}
