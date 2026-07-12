using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

// [Collection("Application")] is mandatory: these tests construct Window
// instances; running in parallel with other Window-constructing test classes
// races DependencyProperty metadata registration (random flaky failures).
[Collection("Application")]
public class PopupWindowRenderSchedulingTests
{
    private const int RenderFlag_Scheduled = 1 << 0;
    private const int RenderFlag_DirtyBetween = 1 << 3;

    [Fact]
    public void InvalidateWindow_BanksDirtyUntilFrameStartingSchedulesRender()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);

        SetPrivateField(popupWindow, "_hwnd", new nint(0x2201));

        try
        {
            popupWindow.InvalidateWindow();

            Assert.True(HasRenderFlag(popupWindow, RenderFlag_DirtyBetween));
            Assert.False(HasRenderFlag(popupWindow, RenderFlag_Scheduled));

            InvokePrivateMethod(popupWindow, "OnFrameStarting");

            Assert.False(HasRenderFlag(popupWindow, RenderFlag_DirtyBetween));
            Assert.True(HasRenderFlag(popupWindow, RenderFlag_Scheduled));
        }
        finally
        {
            SetPrivateField(popupWindow, "_hwnd", nint.Zero);
            popupWindow.Dispose();
        }
    }

    [Fact]
    public void Dispose_AbortsQueuedRenderAndRejectsItsStaleLifecycleToken()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);

        SetPrivateField(popupWindow, "_hwnd", new nint(0x2205));
        popupWindow.InvalidateWindow();
        InvokePrivateMethod(popupWindow, "OnFrameStarting");

        var generation = GetPrivateField<int>(popupWindow, "_renderLifecycleGeneration");
        var operation = GetPrivateField<DispatcherOperation>(popupWindow, "_scheduledRenderOperation");
        Assert.Equal(DispatcherOperationStatus.Pending, operation.Status);

        // Avoid calling DestroyWindow for the synthetic HWND; Dispose must still cancel the
        // already-queued dispatcher callback and invalidate its captured lifecycle generation.
        SetPrivateField(popupWindow, "_hwnd", nint.Zero);
        popupWindow.Dispose();

        Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status);
        Assert.Null(GetPrivateField<object?>(popupWindow, "_scheduledRenderOperation"));
        Assert.False(HasRenderFlag(popupWindow, RenderFlag_Scheduled | RenderFlag_DirtyBetween));

        // Simulate the narrow race where the dispatcher had already dequeued the callback and Abort
        // could no longer remove it. The stale token must return before target creation/native draw.
        InvokePrivateMethod(popupWindow, "ProcessRender", generation);
        Assert.Null(GetPrivateField<object?>(popupWindow, "_renderTarget"));
    }

    [Fact]
    public void NativeDestroy_TearsDownManagedLifecycleWithoutRecursivelyDestroyingHwnd()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);
        var hwnd = new nint(0x2206);
        var popupWindows = GetPrivateStaticField<Dictionary<nint, PopupWindow>>("_popupWindows");

        SetPrivateField(popupWindow, "_hwnd", hwnd);
        popupWindows.Add(hwnd, popupWindow);
        popupWindow.InvalidateWindow();
        InvokePrivateMethod(popupWindow, "OnFrameStarting");

        var generation = GetPrivateField<int>(popupWindow, "_renderLifecycleGeneration");
        var operation = GetPrivateField<DispatcherOperation>(popupWindow, "_scheduledRenderOperation");
        Assert.Equal(DispatcherOperationStatus.Pending, operation.Status);

        try
        {
            var result = InvokePrivateStaticMethod<nint>(
                typeof(PopupWindow),
                "PopupWndProc",
                hwnd,
                (uint)0x0002, // WM_DESTROY
                nint.Zero,
                nint.Zero);

            Assert.Equal(nint.Zero, result);
            Assert.True(GetPrivateField<bool>(popupWindow, "_disposed"));
            Assert.Equal(nint.Zero, GetPrivateField<nint>(popupWindow, "_hwnd"));
            Assert.False(popupWindows.ContainsKey(hwnd));
            Assert.Null(popupWindow.Child);
            Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status);
            Assert.Null(GetPrivateField<object?>(popupWindow, "_scheduledRenderOperation"));
            Assert.False(HasRenderFlag(popupWindow, RenderFlag_Scheduled | RenderFlag_DirtyBetween));
            Assert.NotEqual(generation, GetPrivateField<int>(popupWindow, "_renderLifecycleGeneration"));

            // Even if an already-dequeued callback still runs, its old lifecycle token
            // cannot recreate a render target after the native owner has gone away.
            InvokePrivateMethod(popupWindow, "ProcessRender", generation);
            Assert.Null(GetPrivateField<object?>(popupWindow, "_renderTarget"));
        }
        finally
        {
            _ = popupWindows.Remove(hwnd);
            SetPrivateField(popupWindow, "_hwnd", nint.Zero);
            popupWindow.Dispose();
        }
    }

    [Fact]
    public void UpdateRenderableRegistration_TracksVisiblePopupSurface()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);

        SetPrivateField(popupWindow, "_hwnd", new nint(0x2202));

        try
        {
            InvokePrivateMethod(popupWindow, "UpdateRenderableRegistration", true);

            Assert.True(GetPrivateField<bool>(popupWindow, "_registeredAsRenderable"));

            InvokePrivateMethod(popupWindow, "UpdateRenderableRegistration", false);

            Assert.False(GetPrivateField<bool>(popupWindow, "_registeredAsRenderable"));
        }
        finally
        {
            InvokePrivateMethod(popupWindow, "UpdateRenderableRegistration", false);
            SetPrivateField(popupWindow, "_hwnd", nint.Zero);
            popupWindow.Dispose();
        }
    }

    [Fact]
    public void RequestHostRender_OverlayPopupRequestsFullParentFrame()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();

        SetPrivateField(parentWindow, "<Handle>k__BackingField", new nint(0x2203));
        SetPrivateField(popup, "_parentWindow", parentWindow);
        SetPrivateField(popup, "_isUsingExternalWindow", false);

        try
        {
            InvokePrivateMethod(popup, "RequestHostRender");

            Assert.True(GetPrivateField<bool>(parentWindow, "_fullInvalidation"));
            Assert.True(HasWindowRenderFlag(parentWindow, RenderFlag_DirtyBetween));
        }
        finally
        {
            SetPrivateField(parentWindow, "<Handle>k__BackingField", nint.Zero);
        }
    }

    [Fact]
    public void RequestHostRender_ExternalPopupRequestsPopupWindowFrame()
    {
        var parentWindow = new Window
        {
            Width = 300,
            Height = 200
        };
        var popup = new Popup();
        var popupRoot = new PopupRoot(popup, new Border(), isLightDismiss: false);
        var popupWindow = new PopupWindow(parentWindow, popupRoot);

        SetPrivateField(popupWindow, "_hwnd", new nint(0x2204));
        SetPrivateField(popup, "_popupWindow", popupWindow);
        SetPrivateField(popup, "_isUsingExternalWindow", true);

        try
        {
            InvokePrivateMethod(popup, "RequestHostRender");

            Assert.True(HasRenderFlag(popupWindow, RenderFlag_DirtyBetween));
        }
        finally
        {
            SetPrivateField(popupWindow, "_hwnd", nint.Zero);
            popupWindow.Dispose();
        }
    }

    private static bool HasRenderFlag(PopupWindow popupWindow, int flag)
    {
        var state = GetPrivateField<int>(popupWindow, "_renderState");
        return (state & flag) != 0;
    }

    private static bool HasWindowRenderFlag(Window window, int flag)
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

    private static T GetPrivateStaticField<T>(string fieldName)
    {
        var field = typeof(PopupWindow).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(null)!;
    }

    private static T InvokePrivateStaticMethod<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        try
        {
            return (T)method!.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvokePrivateMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        try
        {
            method!.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvokePrivateMethod(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        try
        {
            method!.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
