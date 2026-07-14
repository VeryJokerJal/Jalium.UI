using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class LinuxPopupInputBridgeTests
{
    [Fact]
    public void PlatformTouchEvents_RouteTouchPointerAndManipulationThroughPopupChild()
    {
        var child = new Border { IsManipulationEnabled = true };
        using PopupWindow popupWindow = CreateArrangedPopup(child);

        int touchDown = 0;
        int touchMove = 0;
        int touchUp = 0;
        int pointerDown = 0;
        int pointerMove = 0;
        int pointerUp = 0;
        int manipulationStarted = 0;
        int manipulationDelta = 0;
        int manipulationCompleted = 0;
        PointerPoint? pressedPoint = null;

        child.TouchDown += (_, _) => touchDown++;
        child.TouchMove += (_, _) => touchMove++;
        child.TouchUp += (_, _) => touchUp++;
        child.PointerDown += (_, e) =>
        {
            pointerDown++;
            pressedPoint = Assert.IsType<PointerDownEventArgs>(e).Pointer;
        };
        child.PointerMove += (_, _) => pointerMove++;
        child.PointerUp += (_, _) => pointerUp++;
        child.ManipulationStarted += (_, _) => manipulationStarted++;
        child.ManipulationDelta += (_, _) => manipulationDelta++;
        child.ManipulationCompleted += (_, _) => manipulationCompleted++;

        DispatchPointer(popupWindow, PlatformEventType.PointerDown, 101, 1, 30, 24, 0.42f);
        DispatchPointer(popupWindow, PlatformEventType.PointerMove, 101, 1, 50, 40, 0.64f);
        DispatchPointer(popupWindow, PlatformEventType.PointerUp, 101, 1, 50, 40, 0.0f);

        Assert.Equal(1, touchDown);
        Assert.Equal(1, touchMove);
        Assert.Equal(1, touchUp);
        Assert.Equal(1, pointerDown);
        Assert.Equal(1, pointerMove);
        Assert.Equal(1, pointerUp);
        Assert.Equal(1, manipulationStarted);
        Assert.Equal(1, manipulationDelta);
        Assert.Equal(1, manipulationCompleted);
        Assert.NotNull(pressedPoint);
        Assert.Equal(PointerDeviceType.Touch, pressedPoint!.PointerDeviceType);
        Assert.Equal(0.42f, pressedPoint.Properties.Pressure, 3);
        Assert.True(pressedPoint.Properties.IsPrimary);
    }

    [Fact]
    public void PlatformPenAndCancelEvents_PreservePenMetricsAndTerminateSessions()
    {
        var child = new Border();
        using PopupWindow popupWindow = CreateArrangedPopup(child);

        int stylusDown = 0;
        int stylusMove = 0;
        int stylusUp = 0;
        int pointerCancel = 0;
        float stylusPressure = 0;
        PointerPoint? penPoint = null;

        child.StylusDown += (_, e) =>
        {
            stylusDown++;
            StylusPointCollection points = e.GetStylusPoints(child);
            stylusPressure = points[0].PressureFactor;
        };
        child.StylusMove += (_, _) => stylusMove++;
        child.StylusUp += (_, _) => stylusUp++;
        child.PointerDown += (_, e) =>
        {
            PointerDownEventArgs args = Assert.IsType<PointerDownEventArgs>(e);
            if (args.Pointer.PointerDeviceType == PointerDeviceType.Pen)
                penPoint = args.Pointer;
        };
        child.PointerCancel += (_, _) => pointerCancel++;

        DispatchPointer(
            popupWindow, PlatformEventType.PointerDown, 202, 2, 40, 28, 0.73f,
            tiltX: 14, tiltY: -9, twist: 37);
        DispatchPointer(
            popupWindow, PlatformEventType.PointerMove, 202, 2, 48, 34, 0.81f,
            tiltX: 15, tiltY: -8, twist: 38);
        DispatchPointer(popupWindow, PlatformEventType.PointerUp, 202, 2, 48, 34, 0.0f);

        Assert.Equal(1, stylusDown);
        Assert.Equal(1, stylusMove);
        Assert.Equal(1, stylusUp);
        Assert.Equal(0.73f, stylusPressure, 3);
        Assert.NotNull(penPoint);
        Assert.Equal(14, penPoint!.Properties.XTilt);
        Assert.Equal(-9, penPoint.Properties.YTilt);
        Assert.Equal(37, penPoint.Properties.Twist);

        DispatchPointer(popupWindow, PlatformEventType.PointerDown, 303, 1, 20, 20, 1.0f);
        DispatchPointer(popupWindow, PlatformEventType.PointerCancel, 303, 1, 22, 22, 0.0f);
        Assert.Equal(1, pointerCancel);
    }

    private static PopupWindow CreateArrangedPopup(FrameworkElement child)
    {
        var owner = new Window { Width = 320, Height = 240 };
        var popup = new Popup();
        var root = new PopupRoot(popup, child, isLightDismiss: false);
        var popupWindow = new PopupWindow(owner, root);
        popupWindow.Measure(new Size(200, 120));
        popupWindow.Arrange(new Rect(0, 0, 200, 120));
        popupWindow.SetVisualBounds(new Rect(0, 0, 200, 120));
        root.SetVisualBounds(new Rect(0, 0, 200, 120));
        child.SetVisualBounds(new Rect(0, 0, 200, 120));
        return popupWindow;
    }

    private static void DispatchPointer(
        PopupWindow popupWindow,
        PlatformEventType type,
        uint pointerId,
        int pointerType,
        float x,
        float y,
        float pressure,
        float tiltX = 0,
        float tiltY = 0,
        float twist = 0)
    {
        MethodInfo method = typeof(PopupWindow).GetMethod(
            "OnPlatformPointerEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Popup platform pointer bridge was not found.");
        method.Invoke(popupWindow, [new PlatformEvent
        {
            Type = type,
            PointerId = pointerId,
            PointerType = pointerType,
            PointerX = x,
            PointerY = y,
            Pressure = pressure,
            TiltX = tiltX,
            TiltY = tiltY,
            Twist = twist,
        }]);
    }
}
