using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

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
            BeginDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 300, height: 200, hwnd: new nint(0x2001));
        SetPrivateProperty(window, "RenderTarget", renderTarget);

        var exception = Assert.Throws<RenderPipelineException>(window.ForceRenderFrame);
        Assert.Equal("Begin", exception.Stage);
    }

    [Fact]
    public void PopupWindow_RenderFrame_WhenBeginDrawFails_Throws()
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
            BeginDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 160, height: 120, hwnd: new nint(0x2002));
        SetPrivateField(popupWindow, "_renderTarget", renderTarget);
        SetPrivateField(popupWindow, "_width", 160);
        SetPrivateField(popupWindow, "_height", 120);

        var exception = Assert.Throws<RenderPipelineException>(() => InvokePrivateMethod(popupWindow, "RenderFrame"));
        Assert.Equal("Begin", exception.Stage);
    }

    [Fact]
    public void DockIndicatorWindow_RenderFrame_WhenBeginDrawFails_Throws()
    {
        var dockWindow = new DockIndicatorWindow(showCenterCross: true, showEdgeButtons: true);
        var native = new RenderTargetTestNative
        {
            BeginDrawResult = (int)JaliumResult.DeviceLost
        };

        using var renderTarget = CreateRenderTarget(native, width: 200, height: 120, hwnd: new nint(0x2003));
        SetPrivateField(dockWindow, "_renderTarget", renderTarget);
        SetPrivateField(dockWindow, "_width", 200);
        SetPrivateField(dockWindow, "_height", 120);

        var exception = Assert.Throws<RenderPipelineException>(() => InvokePrivateMethod(dockWindow, "RenderFrame"));
        Assert.Equal("Begin", exception.Stage);
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
