using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class InputEventArgsWpfParityTests
{
    [Fact]
    public void Surface_ExposesTheWpfDeviceAwareConstructorsAndMembers()
    {
        AssertConstructor<InputEventArgs>(typeof(InputDevice), typeof(int));
        AssertConstructor<KeyboardFocusChangedEventArgs>(
            typeof(KeyboardDevice),
            typeof(int),
            typeof(IInputElement),
            typeof(IInputElement));
        AssertConstructor<MouseButtonEventArgs>(typeof(MouseDevice), typeof(int), typeof(MouseButton));
        AssertConstructor<MouseButtonEventArgs>(
            typeof(MouseDevice),
            typeof(int),
            typeof(MouseButton),
            typeof(StylusDevice));
        AssertConstructor<MouseWheelEventArgs>(typeof(MouseDevice), typeof(int), typeof(int));
        AssertConstructor<QueryCursorEventArgs>(typeof(MouseDevice), typeof(int));
        AssertConstructor<QueryCursorEventArgs>(typeof(MouseDevice), typeof(int), typeof(StylusDevice));
        AssertConstructor<TextCompositionEventArgs>(typeof(InputDevice), typeof(TextComposition));
        AssertConstructor<AccessKeyPressedEventArgs>();

        var device = typeof(InputEventArgs).GetProperty(
            nameof(InputEventArgs.Device),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(device);
        Assert.Equal(typeof(InputDevice), device!.PropertyType);
        Assert.NotNull(device.GetMethod);
        Assert.Null(device.GetSetMethod(nonPublic: false));
        Assert.NotNull(device.GetSetMethod(nonPublic: true));

        var systemCompositionText = typeof(TextComposition).GetProperty(
            nameof(TextComposition.SystemCompositionText),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(systemCompositionText);
        Assert.Equal(typeof(string), systemCompositionText!.PropertyType);
        Assert.True(systemCompositionText.GetMethod!.IsPublic);
        Assert.True(systemCompositionText.SetMethod!.IsFamily);
        Assert.False(systemCompositionText.GetCustomAttribute<CLSCompliantAttribute>()!.IsCompliant);

        var invoke = typeof(AccessKeyPressedEventArgs).GetMethod(
            "InvokeEventHandler",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(invoke);
        Assert.True(invoke!.IsFamily);
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal(new[] { typeof(Delegate), typeof(object) }, invoke.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void InputEventArgs_PreservesTheOriginatingDeviceAndTimestamp()
    {
        var device = new ProbeInputDevice();

        var args = new InputEventArgs(device, 123456);

        Assert.Same(device, args.Device);
        Assert.Equal(123456, args.Timestamp);
    }

    [Fact]
    public void KeyboardFocusChangedEventArgs_PreservesDeviceTimestampAndFocusEndpoints()
    {
        var keyboard = new ProbeKeyboardDevice();
        var oldFocus = new Button();
        var newFocus = new Button();

        var args = new KeyboardFocusChangedEventArgs(keyboard, 734, oldFocus, newFocus);

        Assert.Same(keyboard, args.Device);
        Assert.Equal(734, args.Timestamp);
        Assert.Same(oldFocus, args.OldFocus);
        Assert.Same(newFocus, args.NewFocus);
    }

    [Fact]
    public void MouseButtonEventArgs_UsesTheDeviceSnapshotAndStartsAtOneClick()
    {
        var mouse = new ProbeMouseDevice(
            new Point(12, 34),
            new MouseButtonStates
            {
                Left = MouseButtonState.Released,
                Middle = MouseButtonState.Released,
                Right = MouseButtonState.Pressed,
                XButton1 = MouseButtonState.Released,
                XButton2 = MouseButtonState.Released,
            });

        var args = new MouseButtonEventArgs(mouse, 99, MouseButton.Right);

        Assert.Same(mouse, args.Device);
        Assert.Equal(99, args.Timestamp);
        Assert.Equal(new Point(12, 34), args.Position);
        Assert.Equal(MouseButton.Right, args.ChangedButton);
        Assert.Equal(MouseButtonState.Pressed, args.ButtonState);
        Assert.Equal(1, args.ClickCount);

        mouse.Buttons = mouse.Buttons with { Right = MouseButtonState.Released };
        Assert.Equal(MouseButtonState.Released, args.ButtonState);

        var invalid = Assert.Throws<InvalidEnumArgumentException>(() =>
            new MouseButtonEventArgs(mouse, 99, (MouseButton)int.MaxValue));
        Assert.Equal("button", invalid.ParamName);
        Assert.Equal("mouse", Assert.Throws<ArgumentNullException>(() =>
            new MouseButtonEventArgs(null!, 99, MouseButton.Left)).ParamName);
    }

    [Fact]
    public void PromotedMouseButtonEvent_PreservesItsStylusAssociation()
    {
        var mouse = new ProbeMouseDevice(Point.Zero, MouseButtonStates.AllReleased);
        var stylus = new PointerStylusDevice(42, "Test stylus");

        var args = new MouseButtonEventArgs(mouse, 7, MouseButton.Left, stylus);

        var associatedStylus = typeof(MouseEventArgs).GetProperty(
            "AssociatedStylusDevice",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Same(stylus.Device, associatedStylus!.GetValue(args));
    }

    [Fact]
    public void MouseWheelAndQueryCursor_ConstructorsPreserveDeviceState()
    {
        var mouse = new ProbeMouseDevice(new Point(5, 8), MouseButtonStates.AllReleased);
        var wheel = new MouseWheelEventArgs(mouse, 414, -120);

        Assert.Same(mouse, wheel.Device);
        Assert.Equal(414, wheel.Timestamp);
        Assert.Equal(new Point(5, 8), wheel.Position);
        Assert.Equal(-120, wheel.Delta);

        var query = new QueryCursorEventArgs(mouse, 415);
        Assert.Equal(typeof(MouseEventArgs), typeof(QueryCursorEventArgs).BaseType);
        Assert.Same(mouse, query.Device);
        Assert.Equal(415, query.Timestamp);
        Assert.Null(query.Cursor);

        query.Cursor = null;
        Assert.Same(Cursors.None, query.Cursor);

        Assert.Equal("mouse", Assert.Throws<ArgumentNullException>(() =>
            new QueryCursorEventArgs(null!, 0)).ParamName);
    }

    [Fact]
    public void TextCompositionEventArgs_UsesTheLiveCompositionAndCurrentTick()
    {
        var device = new ProbeInputDevice();
        var composition = new ProbeTextComposition("initial");
        composition.SetSystemText("system");
        composition.SetControlText("control");
        composition.SetSystemCompositionText("system composition");
        var before = Environment.TickCount;

        var args = new TextCompositionEventArgs(device, composition);
        var after = Environment.TickCount;

        Assert.Same(device, args.Device);
        Assert.Same(composition, args.TextComposition);
        Assert.Equal("initial", args.Text);
        Assert.Equal("system", args.SystemText);
        Assert.Equal("control", args.ControlText);
        Assert.Equal("system composition", composition.SystemCompositionText);
        Assert.True(IsBetweenTickCounts(args.Timestamp, before, after));

        composition.SetResultText("updated");
        Assert.Equal("updated", args.Text);

        Assert.Equal("composition", Assert.Throws<ArgumentNullException>(() =>
            new TextCompositionEventArgs(device, null!)).ParamName);
    }

    [Fact]
    public void AccessKeyPressedEventArgs_DefaultsTheRoutedEventAndDispatchesTypedHandlers()
    {
        var target = new Button();
        object? observedSender = null;
        AccessKeyPressedEventArgs? observedArgs = null;
        target.AddHandler(
            AccessKeyManager.AccessKeyPressedEvent,
            new AccessKeyPressedEventHandler((sender, args) =>
            {
                observedSender = sender;
                observedArgs = args;
            }));

        var args = new AccessKeyPressedEventArgs();
        target.RaiseEvent(args);

        Assert.Same(AccessKeyManager.AccessKeyPressedEvent, args.RoutedEvent);
        Assert.Null(args.Key);
        Assert.Same(target, observedSender);
        Assert.Same(args, observedArgs);

        var keyed = new AccessKeyPressedEventArgs("F");
        Assert.Same(AccessKeyManager.AccessKeyPressedEvent, keyed.RoutedEvent);
        Assert.Equal("F", keyed.Key);
    }

    private static bool IsBetweenTickCounts(int value, int start, int end)
    {
        return unchecked((uint)(value - start)) <= unchecked((uint)(end - start));
    }

    private static void AssertConstructor<T>(params Type[] parameterTypes)
    {
        Assert.NotNull(typeof(T).GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: parameterTypes,
            modifiers: null));
    }

    private sealed class ProbeInputDevice : InputDevice
    {
        public override UIElement? Target => null;

        public override PresentationSource? ActiveSource => null;
    }

    private sealed class ProbeKeyboardDevice : KeyboardDevice
    {
        public override UIElement? Target => null;

        public override PresentationSource? ActiveSource => null;

        protected override KeyStates GetKeyStatesFromSystem(Key key) => KeyStates.None;
    }

    private sealed class ProbeMouseDevice : MouseDevice
    {
        private readonly Point _position;
        public MouseButtonStates Buttons { get; set; }

        public ProbeMouseDevice(Point position, MouseButtonStates buttons)
        {
            _position = position;
            Buttons = buttons;
        }

        public override UIElement? Target => null;

        public override PresentationSource? ActiveSource => null;

        protected override MouseButtonState GetButtonState(MouseButton mouseButton) => mouseButton switch
        {
            MouseButton.Left => Buttons.Left,
            MouseButton.Middle => Buttons.Middle,
            MouseButton.Right => Buttons.Right,
            MouseButton.XButton1 => Buttons.XButton1,
            MouseButton.XButton2 => Buttons.XButton2,
            _ => MouseButtonState.Released,
        };

        protected override Point GetPositionCore(IInputElement? relativeTo) => _position;
    }

    private sealed class ProbeTextComposition : TextComposition
    {
        public ProbeTextComposition(string resultText)
            : base(InputManager.Current, source: null, resultText)
        {
        }

        public void SetSystemCompositionText(string value)
        {
            SystemCompositionText = value;
        }

        public void SetSystemText(string value) => SystemText = value;
        public void SetControlText(string value) => ControlText = value;
        public void SetResultText(string value) => Text = value;
    }
}
