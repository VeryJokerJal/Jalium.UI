using System.Reflection;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ContentElementWpfParityTests
{
    [Fact]
    public void Surface_UsesExactInputDelegatesAndDeclaredRoutedFields()
    {
        AssertEvent<MouseButtonEventHandler>(nameof(ContentElement.PreviewMouseLeftButtonDown));
        AssertEvent<MouseEventHandler>(nameof(ContentElement.MouseMove));
        AssertEvent<KeyEventHandler>(nameof(ContentElement.KeyDown));
        AssertEvent<KeyboardFocusChangedEventHandler>(nameof(ContentElement.GotKeyboardFocus));
        AssertEvent<StylusDownEventHandler>(nameof(ContentElement.StylusDown));
        AssertEvent<StylusSystemGestureEventHandler>(nameof(ContentElement.PreviewStylusSystemGesture));
        AssertEvent<EventHandler<TouchEventArgs>>(nameof(ContentElement.TouchDown));
        AssertEvent<DragEventHandler>(nameof(ContentElement.DragEnter));

        AssertRoutedField(nameof(ContentElement.PreviewMouseLeftButtonDownEvent), typeof(MouseButtonEventHandler));
        AssertRoutedField(nameof(ContentElement.KeyDownEvent), typeof(KeyEventHandler));
        AssertRoutedField(nameof(ContentElement.PreviewStylusButtonDownEvent), typeof(StylusButtonEventHandler));
        AssertRoutedField(nameof(ContentElement.TouchDownEvent), typeof(EventHandler<TouchEventArgs>));

        Assert.Same(typeof(ContentElement), typeof(ContentElement).GetConstructor(Type.EmptyTypes)!.DeclaringType);
        Assert.Contains(typeof(IInputElement), typeof(ContentElement).GetInterfaces());
    }

    [Fact]
    public void RoutedInput_InvokesVirtualHooksAndInstanceHandlers()
    {
        var element = new ProbeContentElement();
        var instanceCount = 0;
        element.KeyDown += (_, _) => instanceCount++;

        element.RaiseEvent(new KeyEventArgs(
            ContentElement.KeyDownEvent,
            Key.A,
            ModifierKeys.None,
            isDown: true,
            isRepeat: false,
            timestamp: 0));

        Assert.Equal(1, element.KeyDownCount);
        Assert.Equal(1, instanceCount);
    }

    [Fact]
    public void GenericMouseDown_ReRaisesTheMatchingDirectButtonEvent()
    {
        var element = new ProbeContentElement();
        var directCount = 0;
        element.MouseLeftButtonDown += (_, _) => directCount++;

        element.RaiseEvent(new MouseButtonEventArgs(
            ContentElement.MouseDownEvent,
            new Point(3, 4),
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0));

        Assert.Equal(1, element.MouseDownCount);
        Assert.Equal(1, element.MouseLeftButtonDownCount);
        Assert.Equal(1, directCount);
    }

    [Fact]
    public void MouseCapture_UpdatesEffectiveStateAndRaisesCaptureEvents()
    {
        var element = new ProbeContentElement { Focusable = true };
        var got = 0;
        var lost = 0;
        element.GotMouseCapture += (_, _) => got++;
        element.LostMouseCapture += (_, _) => lost++;

        Assert.True(element.CaptureMouse());
        Assert.True(element.IsMouseCaptured);
        Assert.Equal(1, got);

        element.ReleaseMouseCapture();
        Assert.False(element.IsMouseCaptured);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void ContentOperations_ManageTheNonvisualParentWithoutAllowingCycles()
    {
        var parent = new FrameworkContentElement();
        var child = new ProbeContentElement();

        ContentOperations.SetParent(child, parent);
        Assert.Same(parent, ContentOperations.GetParent(child));
        Assert.Throws<ArgumentException>(() => ContentOperations.SetParent(parent, child));

        ContentOperations.SetParent(child, null!);
        Assert.Null(ContentOperations.GetParent(child));
    }

    private static void AssertEvent<THandler>(string name) where THandler : Delegate
    {
        EventInfo? @event = typeof(ContentElement).GetEvent(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(@event);
        Assert.Equal(typeof(THandler), @event!.EventHandlerType);
    }

    private static void AssertRoutedField(string name, Type handlerType)
    {
        FieldInfo? field = typeof(ContentElement).GetField(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        var routedEvent = Assert.IsType<RoutedEvent>(field.GetValue(null));
        Assert.Equal(handlerType, routedEvent.HandlerType);
    }

    private sealed class ProbeContentElement : ContentElement
    {
        public int KeyDownCount { get; private set; }
        public int MouseDownCount { get; private set; }
        public int MouseLeftButtonDownCount { get; private set; }

        protected internal override void OnKeyDown(KeyEventArgs e) => KeyDownCount++;
        protected internal override void OnMouseDown(MouseButtonEventArgs e) => MouseDownCount++;
        protected internal override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => MouseLeftButtonDownCount++;
    }
}
