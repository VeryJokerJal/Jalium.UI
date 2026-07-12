using System.Reflection;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class UIElementStylusManipulationWpfParityTests
{
    [Fact]
    public void InputContracts_LiveInCoreSoUIElementUsesExactTypes()
    {
        var core = typeof(UIElement).Assembly;
        Assert.Same(core, typeof(InputDevice).Assembly);
        Assert.Same(core, typeof(StylusDevice).Assembly);
        Assert.Same(core, typeof(StylusEventArgs).Assembly);
        Assert.Same(core, typeof(TextCompositionEventArgs).Assembly);
        Assert.Same(core, typeof(ManipulationStartingEventArgs).Assembly);
        Assert.Same(core, typeof(ManipulationDeltaEventArgs).Assembly);
    }

    [Fact]
    public void StylusSurface_HasExactHandlersFieldsAndVirtualHooks()
    {
        AssertEvent<StylusEventHandler>(nameof(UIElement.GotStylusCapture));
        AssertEvent<StylusEventHandler>(nameof(UIElement.LostStylusCapture));
        AssertEvent<StylusButtonEventHandler>(nameof(UIElement.PreviewStylusButtonDown));
        AssertEvent<StylusButtonEventHandler>(nameof(UIElement.PreviewStylusButtonUp));
        AssertEvent<StylusEventHandler>(nameof(UIElement.PreviewStylusInAirMove));
        AssertEvent<StylusEventHandler>(nameof(UIElement.PreviewStylusInRange));
        AssertEvent<StylusEventHandler>(nameof(UIElement.PreviewStylusOutOfRange));
        AssertEvent<StylusSystemGestureEventHandler>(nameof(UIElement.PreviewStylusSystemGesture));

        AssertRoutedField(nameof(UIElement.GotStylusCaptureEvent), typeof(StylusEventHandler));
        AssertRoutedField(nameof(UIElement.LostStylusCaptureEvent), typeof(StylusEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusButtonDownEvent), typeof(StylusButtonEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusButtonUpEvent), typeof(StylusButtonEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusInAirMoveEvent), typeof(StylusEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusInRangeEvent), typeof(StylusEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusOutOfRangeEvent), typeof(StylusEventHandler));
        AssertRoutedField(nameof(UIElement.PreviewStylusSystemGestureEvent), typeof(StylusSystemGestureEventHandler));

        AssertProtectedVirtual("OnGotStylusCapture", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnLostStylusCapture", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnPreviewStylusButtonDown", typeof(StylusButtonEventArgs));
        AssertProtectedVirtual("OnPreviewStylusButtonUp", typeof(StylusButtonEventArgs));
        AssertProtectedVirtual("OnPreviewStylusInAirMove", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnPreviewStylusInRange", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnPreviewStylusOutOfRange", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnPreviewStylusSystemGesture", typeof(StylusSystemGestureEventArgs));
        AssertProtectedVirtual("OnPreviewStylusDown", typeof(StylusDownEventArgs));
        AssertProtectedVirtual("OnStylusDown", typeof(StylusDownEventArgs));
        AssertProtectedVirtual("OnPreviewStylusMove", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusMove", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnPreviewStylusUp", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusUp", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusInAirMove", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusEnter", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusLeave", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusInRange", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusOutOfRange", typeof(StylusEventArgs));
        AssertProtectedVirtual("OnStylusButtonDown", typeof(StylusButtonEventArgs));
        AssertProtectedVirtual("OnStylusButtonUp", typeof(StylusButtonEventArgs));
        AssertProtectedVirtual("OnStylusSystemGesture", typeof(StylusSystemGestureEventArgs));
        AssertProtectedVirtual("OnPreviewTextInput", typeof(TextCompositionEventArgs));
        AssertProtectedVirtual("OnTextInput", typeof(TextCompositionEventArgs));
    }

    [Fact]
    public void ManipulationSurface_UsesEventHandlerAndInvokesVirtualHooks()
    {
        AssertEvent<EventHandler<ManipulationStartingEventArgs>>(nameof(UIElement.ManipulationStarting));
        AssertEvent<EventHandler<ManipulationStartedEventArgs>>(nameof(UIElement.ManipulationStarted));
        AssertEvent<EventHandler<ManipulationDeltaEventArgs>>(nameof(UIElement.ManipulationDelta));
        AssertEvent<EventHandler<ManipulationInertiaStartingEventArgs>>(nameof(UIElement.ManipulationInertiaStarting));
        AssertEvent<EventHandler<ManipulationBoundaryFeedbackEventArgs>>(nameof(UIElement.ManipulationBoundaryFeedback));
        AssertEvent<EventHandler<ManipulationCompletedEventArgs>>(nameof(UIElement.ManipulationCompleted));

        AssertProtectedVirtual("OnManipulationStarting", typeof(ManipulationStartingEventArgs));
        AssertProtectedVirtual("OnManipulationStarted", typeof(ManipulationStartedEventArgs));
        AssertProtectedVirtual("OnManipulationDelta", typeof(ManipulationDeltaEventArgs));
        AssertProtectedVirtual("OnManipulationInertiaStarting", typeof(ManipulationInertiaStartingEventArgs));
        AssertProtectedVirtual("OnManipulationBoundaryFeedback", typeof(ManipulationBoundaryFeedbackEventArgs));
        AssertProtectedVirtual("OnManipulationCompleted", typeof(ManipulationCompletedEventArgs));

        var element = new ProbeElement();
        element.RaiseEvent(new ManipulationDeltaEventArgs
        {
            RoutedEvent = UIElement.ManipulationDeltaEvent,
        });
        Assert.Equal(1, element.ManipulationDeltaCount);

        var device = new PointerStylusDevice(42);
        element.RaiseEvent(new StylusEventArgs(device, 123)
        {
            RoutedEvent = UIElement.PreviewStylusInRangeEvent,
        });
        Assert.Equal(1, element.PreviewStylusInRangeCount);
    }

    private static void AssertEvent<THandler>(string name) where THandler : Delegate
    {
        var @event = typeof(UIElement).GetEvent(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(@event);
        Assert.Equal(typeof(THandler), @event!.EventHandlerType);
    }

    private static void AssertRoutedField(string name, Type handlerType)
    {
        var field = typeof(UIElement).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        var routedEvent = Assert.IsType<RoutedEvent>(field.GetValue(null));
        Assert.Equal(handlerType, routedEvent.HandlerType);
    }

    private static void AssertProtectedVirtual(string name, Type parameterType)
    {
        var method = typeof(UIElement).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: [parameterType],
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private sealed class ProbeElement : UIElement
    {
        public int ManipulationDeltaCount { get; private set; }
        public int PreviewStylusInRangeCount { get; private set; }

        protected override void OnManipulationDelta(ManipulationDeltaEventArgs e) => ManipulationDeltaCount++;
        protected override void OnPreviewStylusInRange(StylusEventArgs e) => PreviewStylusInRangeCount++;
    }
}
