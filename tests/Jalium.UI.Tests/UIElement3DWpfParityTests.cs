using System.Reflection;
using Jalium.UI.Input;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class UIElement3DWpfParityTests
{
    [Fact]
    public void Surface_ExposesTheWpfTierOneContract()
    {
        var type = typeof(UIElement3D);
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        Assert.Equal(typeof(Visual3D), type.BaseType);
        Assert.Contains(typeof(IInputElement), type.GetInterfaces());
        Assert.Equal(89, type.GetEvents(declared).Count(static @event => @event.AddMethod?.IsPublic == true));
        Assert.Equal(98, type.GetFields(declared).Count(static field => field.IsPublic && field.IsStatic));

        Assert.Equal(typeof(EventHandler<TouchEventArgs>), type.GetEvent(nameof(UIElement3D.TouchDown))!.EventHandlerType);
        Assert.Equal(typeof(StylusDownEventHandler), type.GetEvent(nameof(UIElement3D.StylusDown))!.EventHandlerType);
        Assert.Equal(typeof(KeyboardFocusChangedEventHandler), type.GetEvent(nameof(UIElement3D.GotKeyboardFocus))!.EventHandlerType);

        foreach (var name in RequiredProperties)
        {
            Assert.NotNull(type.GetProperty(name, declared));
        }

        foreach (var name in RequiredRoutedEvents)
        {
            var field = type.GetField(name + "Event", declared);
            Assert.NotNull(field);
            Assert.True(field!.IsPublic && field.IsStatic && field.IsInitOnly);
            Assert.IsType<RoutedEvent>(field.GetValue(null));
        }

        foreach (var name in RequiredDependencyProperties)
        {
            var field = type.GetField(name + "Property", declared);
            Assert.NotNull(field);
            Assert.True(field!.IsPublic && field.IsStatic && field.IsInitOnly);
            Assert.IsType<DependencyProperty>(field.GetValue(null));
        }
    }

    [Fact]
    public void Methods_HaveWpfCompatibleVisibilityAndVirtualShape()
    {
        var type = typeof(UIElement3D);
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var invalidateModel = type.GetMethod(nameof(UIElement3D.InvalidateModel), declared)!;
        Assert.True(invalidateModel.IsPublic);
        Assert.False(invalidateModel.IsVirtual);

        var updateModel = type.GetMethod("OnUpdateModel", declared)!;
        Assert.True(updateModel.IsFamily);
        Assert.True(updateModel.IsVirtual);

        var mouseDown = type.GetMethod("OnMouseDown", declared, null, [typeof(MouseButtonEventArgs)], null)!;
        Assert.True(mouseDown.IsFamilyOrAssembly);
        Assert.True(mouseDown.IsVirtual);

        var uiParent = type.GetMethod("GetUIParentCore", declared)!;
        Assert.True(uiParent.IsFamilyOrAssembly);
        Assert.False(uiParent.IsVirtual);

        var parentChanged = type.GetMethod("OnVisualParentChanged", declared, null, [typeof(DependencyObject)], null)!;
        Assert.True(parentChanged.IsFamilyOrAssembly);
        Assert.True(parentChanged.IsVirtual);

        Assert.NotNull(type.GetMethod(nameof(UIElement3D.AddHandler), declared, null, [typeof(RoutedEvent), typeof(Delegate)], null));
        Assert.NotNull(type.GetMethod(nameof(UIElement3D.AddHandler), declared, null, [typeof(RoutedEvent), typeof(Delegate), typeof(bool)], null));
        Assert.NotNull(type.GetMethod(nameof(UIElement3D.CaptureTouch), declared, null, [typeof(TouchDevice)], null));
        Assert.NotNull(type.GetMethod(nameof(UIElement3D.PredictFocus), declared, null, [typeof(FocusNavigationDirection)], null));
    }

    [Fact]
    public void DependencyState_PropagatesVisibilityAndInvalidatesTheModel()
    {
        var parent = new ContainerUIElement3D();
        var child = new ProbeElement3D();
        parent.Children.Add(child);

        Assert.True(child.IsVisible);
        Assert.True(child.IsEnabled);
        Assert.True(child.IsHitTestVisible);
        Assert.Equal(1, child.UpdateModelCount);

        var visibleChanges = 0;
        child.IsVisibleChanged += (_, _) => visibleChanges++;
        parent.Visibility = Visibility.Collapsed;

        Assert.False(child.IsVisible);
        Assert.Equal(1, visibleChanges);

        child.Visibility = Visibility.Hidden;
        Assert.True(child.UpdateModelCount >= 2);

        parent.IsEnabled = false;
        parent.IsHitTestVisible = false;
        Assert.False(child.IsEnabled);
        Assert.False(child.IsHitTestVisible);
    }

    [Fact]
    public void RoutedInput_BubblesThroughTheVisual3DTreeAndInvokesOverrides()
    {
        var parent = new ContainerUIElement3D();
        var child = new ProbeElement3D();
        parent.Children.Add(child);

        var childCount = 0;
        var parentCount = 0;
        child.KeyDown += (_, _) => childCount++;
        parent.KeyDown += (_, _) => parentCount++;

        child.RaiseEvent(new KeyEventArgs(
            UIElement3D.KeyDownEvent,
            Key.A,
            ModifierKeys.None,
            isDown: true,
            isRepeat: false,
            timestamp: 1));

        Assert.Equal(1, child.KeyDownOverrideCount);
        Assert.Equal(1, childCount);
        Assert.Equal(1, parentCount);
    }

    [Fact]
    public void CaptureApis_UpdateReadOnlyStateAndRaiseCaptureEvents()
    {
        var element = new ProbeElement3D
        {
            IsEnabled = true,
            IsHitTestVisible = true,
        };

        var gotMouseCapture = 0;
        var lostMouseCapture = 0;
        element.GotMouseCapture += (_, _) => gotMouseCapture++;
        element.LostMouseCapture += (_, _) => lostMouseCapture++;

        Assert.True(element.CaptureMouse());
        Assert.True(element.IsMouseCaptured);
        Assert.True(element.IsMouseCaptureWithin);
        Assert.Equal(1, gotMouseCapture);

        element.ReleaseMouseCapture();
        Assert.False(element.IsMouseCaptured);
        Assert.False(element.IsMouseCaptureWithin);
        Assert.Equal(1, lostMouseCapture);

        var touch = Touch.RegisterTouchPoint(42, Point.Zero, null);
        Assert.True(element.CaptureTouch(touch));
        Assert.True(element.AreAnyTouchesCaptured);
        Assert.Contains(touch, element.TouchesCaptured);
        Assert.True(element.ReleaseTouchCapture(touch));
        Assert.False(element.AreAnyTouchesCaptured);
        Assert.Empty(element.TouchesCaptured);
        Touch.UnregisterTouchPoint(touch.Id);
    }

    private static readonly string[] RequiredProperties =
    [
        nameof(UIElement3D.AllowDrop),
        nameof(UIElement3D.AreAnyTouchesCaptured),
        nameof(UIElement3D.AreAnyTouchesCapturedWithin),
        nameof(UIElement3D.AreAnyTouchesDirectlyOver),
        nameof(UIElement3D.AreAnyTouchesOver),
        nameof(UIElement3D.CommandBindings),
        nameof(UIElement3D.Focusable),
        nameof(UIElement3D.InputBindings),
        nameof(UIElement3D.IsEnabled),
        "IsEnabledCore",
        nameof(UIElement3D.IsFocused),
        nameof(UIElement3D.IsHitTestVisible),
        nameof(UIElement3D.IsInputMethodEnabled),
        nameof(UIElement3D.IsKeyboardFocused),
        nameof(UIElement3D.IsKeyboardFocusWithin),
        nameof(UIElement3D.IsMouseCaptured),
        nameof(UIElement3D.IsMouseCaptureWithin),
        nameof(UIElement3D.IsMouseDirectlyOver),
        nameof(UIElement3D.IsMouseOver),
        nameof(UIElement3D.IsStylusCaptured),
        nameof(UIElement3D.IsStylusCaptureWithin),
        nameof(UIElement3D.IsStylusDirectlyOver),
        nameof(UIElement3D.IsStylusOver),
        nameof(UIElement3D.IsVisible),
        nameof(UIElement3D.TouchesCaptured),
        nameof(UIElement3D.TouchesCapturedWithin),
        nameof(UIElement3D.TouchesDirectlyOver),
        nameof(UIElement3D.TouchesOver),
        nameof(UIElement3D.Visibility),
    ];

    private static readonly string[] RequiredDependencyProperties =
    [
        nameof(UIElement3D.AllowDrop),
        nameof(UIElement3D.AreAnyTouchesCaptured),
        nameof(UIElement3D.AreAnyTouchesCapturedWithin),
        nameof(UIElement3D.AreAnyTouchesDirectlyOver),
        nameof(UIElement3D.AreAnyTouchesOver),
        nameof(UIElement3D.Focusable),
        nameof(UIElement3D.IsEnabled),
        nameof(UIElement3D.IsFocused),
        nameof(UIElement3D.IsHitTestVisible),
        nameof(UIElement3D.IsKeyboardFocused),
        nameof(UIElement3D.IsKeyboardFocusWithin),
        nameof(UIElement3D.IsMouseCaptured),
        nameof(UIElement3D.IsMouseCaptureWithin),
        nameof(UIElement3D.IsMouseDirectlyOver),
        nameof(UIElement3D.IsMouseOver),
        nameof(UIElement3D.IsStylusCaptured),
        nameof(UIElement3D.IsStylusCaptureWithin),
        nameof(UIElement3D.IsStylusDirectlyOver),
        nameof(UIElement3D.IsStylusOver),
        nameof(UIElement3D.IsVisible),
        nameof(UIElement3D.Visibility),
    ];

    private static readonly string[] RequiredRoutedEvents =
    [
        nameof(UIElement3D.PreviewKeyDown), nameof(UIElement3D.KeyDown),
        nameof(UIElement3D.PreviewKeyUp), nameof(UIElement3D.KeyUp),
        nameof(UIElement3D.PreviewTextInput), nameof(UIElement3D.TextInput),
        nameof(UIElement3D.PreviewGotKeyboardFocus), nameof(UIElement3D.GotKeyboardFocus),
        nameof(UIElement3D.PreviewLostKeyboardFocus), nameof(UIElement3D.LostKeyboardFocus),
        nameof(UIElement3D.GotFocus), nameof(UIElement3D.LostFocus),
        nameof(UIElement3D.PreviewMouseDown), nameof(UIElement3D.MouseDown),
        nameof(UIElement3D.PreviewMouseUp), nameof(UIElement3D.MouseUp),
        nameof(UIElement3D.PreviewMouseMove), nameof(UIElement3D.MouseMove),
        nameof(UIElement3D.MouseEnter), nameof(UIElement3D.MouseLeave),
        nameof(UIElement3D.PreviewMouseWheel), nameof(UIElement3D.MouseWheel),
        nameof(UIElement3D.PreviewMouseLeftButtonDown), nameof(UIElement3D.MouseLeftButtonDown),
        nameof(UIElement3D.PreviewMouseLeftButtonUp), nameof(UIElement3D.MouseLeftButtonUp),
        nameof(UIElement3D.PreviewMouseRightButtonDown), nameof(UIElement3D.MouseRightButtonDown),
        nameof(UIElement3D.PreviewMouseRightButtonUp), nameof(UIElement3D.MouseRightButtonUp),
        nameof(UIElement3D.GotMouseCapture), nameof(UIElement3D.LostMouseCapture),
        nameof(UIElement3D.PreviewStylusDown), nameof(UIElement3D.StylusDown),
        nameof(UIElement3D.PreviewStylusMove), nameof(UIElement3D.StylusMove),
        nameof(UIElement3D.PreviewStylusUp), nameof(UIElement3D.StylusUp),
        nameof(UIElement3D.PreviewStylusInAirMove), nameof(UIElement3D.StylusInAirMove),
        nameof(UIElement3D.StylusEnter), nameof(UIElement3D.StylusLeave),
        nameof(UIElement3D.PreviewStylusInRange), nameof(UIElement3D.StylusInRange),
        nameof(UIElement3D.PreviewStylusOutOfRange), nameof(UIElement3D.StylusOutOfRange),
        nameof(UIElement3D.PreviewStylusButtonDown), nameof(UIElement3D.StylusButtonDown),
        nameof(UIElement3D.PreviewStylusButtonUp), nameof(UIElement3D.StylusButtonUp),
        nameof(UIElement3D.PreviewStylusSystemGesture), nameof(UIElement3D.StylusSystemGesture),
        nameof(UIElement3D.GotStylusCapture), nameof(UIElement3D.LostStylusCapture),
        nameof(UIElement3D.PreviewTouchDown), nameof(UIElement3D.TouchDown),
        nameof(UIElement3D.PreviewTouchMove), nameof(UIElement3D.TouchMove),
        nameof(UIElement3D.PreviewTouchUp), nameof(UIElement3D.TouchUp),
        nameof(UIElement3D.TouchEnter), nameof(UIElement3D.TouchLeave),
        nameof(UIElement3D.GotTouchCapture), nameof(UIElement3D.LostTouchCapture),
        nameof(UIElement3D.PreviewDragEnter), nameof(UIElement3D.DragEnter),
        nameof(UIElement3D.PreviewDragOver), nameof(UIElement3D.DragOver),
        nameof(UIElement3D.PreviewDragLeave), nameof(UIElement3D.DragLeave),
        nameof(UIElement3D.PreviewDrop), nameof(UIElement3D.Drop),
        nameof(UIElement3D.PreviewGiveFeedback), nameof(UIElement3D.GiveFeedback),
        nameof(UIElement3D.PreviewQueryContinueDrag), nameof(UIElement3D.QueryContinueDrag),
        nameof(UIElement3D.QueryCursor),
    ];

    private sealed class ProbeElement3D : UIElement3D
    {
        public int UpdateModelCount { get; private set; }
        public int KeyDownOverrideCount { get; private set; }

        protected override void OnUpdateModel() => UpdateModelCount++;
        protected internal override void OnKeyDown(KeyEventArgs e) => KeyDownOverrideCount++;
    }
}
