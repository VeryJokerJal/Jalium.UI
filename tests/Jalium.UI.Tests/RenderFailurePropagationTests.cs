using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Guards how a non-recoverable BeginDraw failure is surfaced, which differs by window kind:
/// <list type="bullet">
/// <item><see cref="Window"/> renders from the top-level loop, so the failure must
/// PROPAGATE as a <see cref="RenderPipelineException"/> (Stage "Begin").</item>
/// <item><see cref="PopupWindow"/> / <see cref="DockIndicatorWindow"/> render inside a
/// native WM_PAINT / dispatcher-critical callback, where an escaping exception triggers a
/// 0xC000041D process crash — so their RenderFrame must SWALLOW the failure and never throw.</item>
/// </list>
/// </summary>
/// <remarks>
/// All three use the non-recoverable <see cref="JaliumResult.InitializationFailed"/> so the
/// behavior is exercised at full strength. DeviceLost / InvalidState / ResourceCreationFailed
/// are classified recoverable by IsRecoverableRenderPipelineException and are intentionally
/// recovered (device re-create / GPU-busy retry); those recoverable paths are covered by
/// WindowRenderSchedulingTests.
/// </remarks>
// [Collection("Application")]: constructs Window/PopupWindow — running in
// parallel with other Window-constructing classes races DP-metadata
// registration (random flaky failures).
[Collection("Application")]
public class RenderFailurePropagationTests
{
    [Fact]
    public void Window_ForceRenderFrame_WhenBeginDrawFails_Throws()
    {
        var window = new Window
        {
            Width = 300,
            Height = 200
        };

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InitializationFailed
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2001));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        var exception = Assert.Throws<RenderPipelineException>(window.ForceRenderFrame);
        Assert.Equal("Begin", exception.Stage);
    }

    [Fact]
    public void PopupWindow_RenderFrame_WhenBeginDrawFails_DoesNotThrow()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);

        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InitializationFailed
        };

        using var renderTarget = CreateRenderTarget(native, width: 160, height: 120, hwnd: new nint(0x2002));
        SetPrivateField(popupWindow, "_renderTarget", renderTarget);
        SetPrivateField(popupWindow, "_width", 160);
        SetPrivateField(popupWindow, "_height", 120);

        // PopupWindow.RenderFrame runs inside a native WM_PAINT / dispatcher-critical
        // callback; letting a RenderPipelineException escape would trigger a 0xC000041D
        // process crash. Even a non-recoverable BeginDraw failure must be swallowed.
        var exception = Record.Exception(() => InvokePrivateMethod(popupWindow, "RenderFrame"));
        Assert.Null(exception);
    }

    [Fact]
    public void DockIndicatorWindow_RenderFrame_WhenBeginDrawFails_DoesNotThrow()
    {
        var dockWindow = new DockIndicatorWindow(showCenterCross: true, showEdgeButtons: true);
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.InitializationFailed
        };

        using var renderTarget = CreateRenderTarget(native, width: 200, height: 120, hwnd: new nint(0x2003));
        SetPrivateField(dockWindow, "_renderTarget", renderTarget);
        SetPrivateField(dockWindow, "_width", 200);
        SetPrivateField(dockWindow, "_height", 120);

        // Same native-callback constraint as PopupWindow: a non-recoverable BeginDraw
        // failure must be swallowed, never thrown (an escaping exception → 0xC000041D).
        var exception = Record.Exception(() => InvokePrivateMethod(dockWindow, "RenderFrame"));
        Assert.Null(exception);
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

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(instance, value);
    }

    private static void InvokePrivateMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        try
        {
            method.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
