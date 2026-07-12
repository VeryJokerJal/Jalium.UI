using System.Reflection;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class InputElementContractCollection
{
    public const string CollectionName = "Input element global state";
}

[Collection(InputElementContractCollection.CollectionName)]
public sealed class InputElementContractWpfParityTests
{
    [Fact]
    public void IInputElement_CoreContractMatchesWpfSignatures()
    {
        var type = typeof(IInputElement);

        Assert.Equal(
            new[]
            {
                nameof(IInputElement.Focusable),
                nameof(IInputElement.IsEnabled),
                nameof(IInputElement.IsKeyboardFocused),
                nameof(IInputElement.IsKeyboardFocusWithin),
                nameof(IInputElement.IsMouseCaptured),
                nameof(IInputElement.IsMouseDirectlyOver),
                nameof(IInputElement.IsMouseOver),
                nameof(IInputElement.IsStylusCaptured),
                nameof(IInputElement.IsStylusDirectlyOver),
                nameof(IInputElement.IsStylusOver),
            },
            type.GetProperties().Select(static property => property.Name).Order());

        var focusable = type.GetProperty(nameof(IInputElement.Focusable));
        Assert.NotNull(focusable);
        Assert.True(focusable!.CanRead);
        Assert.True(focusable.CanWrite);
        Assert.Equal(typeof(bool), focusable.PropertyType);

        Assert.Equal(
            new[]
            {
                nameof(IInputElement.AddHandler),
                nameof(IInputElement.CaptureMouse),
                nameof(IInputElement.CaptureStylus),
                nameof(IInputElement.Focus),
                nameof(IInputElement.RaiseEvent),
                nameof(IInputElement.ReleaseMouseCapture),
                nameof(IInputElement.ReleaseStylusCapture),
                nameof(IInputElement.RemoveHandler),
            },
            type.GetMethods()
                .Where(static method => !method.IsSpecialName)
                .Select(static method => method.Name)
                .Order());

        var expectedEvents = new Dictionary<string, Type>
        {
            [nameof(IInputElement.PreviewMouseLeftButtonDown)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.MouseLeftButtonDown)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.PreviewMouseLeftButtonUp)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.MouseLeftButtonUp)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.PreviewMouseRightButtonDown)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.MouseRightButtonDown)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.PreviewMouseRightButtonUp)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.MouseRightButtonUp)] = typeof(MouseButtonEventHandler),
            [nameof(IInputElement.PreviewMouseMove)] = typeof(MouseEventHandler),
            [nameof(IInputElement.MouseMove)] = typeof(MouseEventHandler),
            [nameof(IInputElement.PreviewMouseWheel)] = typeof(MouseWheelEventHandler),
            [nameof(IInputElement.MouseWheel)] = typeof(MouseWheelEventHandler),
            [nameof(IInputElement.MouseEnter)] = typeof(MouseEventHandler),
            [nameof(IInputElement.MouseLeave)] = typeof(MouseEventHandler),
            [nameof(IInputElement.GotMouseCapture)] = typeof(MouseEventHandler),
            [nameof(IInputElement.LostMouseCapture)] = typeof(MouseEventHandler),
            [nameof(IInputElement.PreviewKeyDown)] = typeof(KeyEventHandler),
            [nameof(IInputElement.KeyDown)] = typeof(KeyEventHandler),
            [nameof(IInputElement.PreviewKeyUp)] = typeof(KeyEventHandler),
            [nameof(IInputElement.KeyUp)] = typeof(KeyEventHandler),
            [nameof(IInputElement.PreviewGotKeyboardFocus)] = typeof(KeyboardFocusChangedEventHandler),
            [nameof(IInputElement.GotKeyboardFocus)] = typeof(KeyboardFocusChangedEventHandler),
            [nameof(IInputElement.PreviewLostKeyboardFocus)] = typeof(KeyboardFocusChangedEventHandler),
            [nameof(IInputElement.LostKeyboardFocus)] = typeof(KeyboardFocusChangedEventHandler),
            [nameof(IInputElement.GotStylusCapture)] = typeof(StylusEventHandler),
            [nameof(IInputElement.LostStylusCapture)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewStylusButtonDown)] = typeof(StylusButtonEventHandler),
            [nameof(IInputElement.PreviewStylusButtonUp)] = typeof(StylusButtonEventHandler),
            [nameof(IInputElement.PreviewStylusDown)] = typeof(StylusDownEventHandler),
            [nameof(IInputElement.PreviewStylusInAirMove)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewStylusInRange)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewStylusMove)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewStylusOutOfRange)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewStylusSystemGesture)] = typeof(StylusSystemGestureEventHandler),
            [nameof(IInputElement.PreviewStylusUp)] = typeof(StylusEventHandler),
            [nameof(IInputElement.PreviewTextInput)] = typeof(TextCompositionEventHandler),
            [nameof(IInputElement.StylusButtonDown)] = typeof(StylusButtonEventHandler),
            [nameof(IInputElement.StylusButtonUp)] = typeof(StylusButtonEventHandler),
            [nameof(IInputElement.StylusDown)] = typeof(StylusDownEventHandler),
            [nameof(IInputElement.StylusEnter)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusInAirMove)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusInRange)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusLeave)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusMove)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusOutOfRange)] = typeof(StylusEventHandler),
            [nameof(IInputElement.StylusSystemGesture)] = typeof(StylusSystemGestureEventHandler),
            [nameof(IInputElement.StylusUp)] = typeof(StylusEventHandler),
            [nameof(IInputElement.TextInput)] = typeof(TextCompositionEventHandler),
        };

        var actualEvents = type.GetEvents().ToDictionary(
            static eventInfo => eventInfo.Name,
            static eventInfo => eventInfo.EventHandlerType!);
        Assert.Equal(expectedEvents.Count, actualEvents.Count);
        foreach (var expected in expectedEvents)
        {
            Assert.True(actualEvents.TryGetValue(expected.Key, out var actualHandler));
            Assert.Equal(expected.Value, actualHandler);
        }
    }

    [Fact]
    public void IFrameworkInputElement_OnlyAddsWritableNameAndFrameworkElementImplementsIt()
    {
        var type = typeof(IFrameworkInputElement);

        Assert.Equal(new[] { typeof(IInputElement) }, type.GetInterfaces());
        var property = Assert.Single(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(IFrameworkInputElement.Name), property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.True(type.IsAssignableFrom(typeof(FrameworkElement)));

        IFrameworkInputElement element = new FrameworkElement();
        element.Name = "contract-name";
        Assert.Equal("contract-name", element.Name);
    }

    [Fact]
    public void InterfaceEventAccessors_UseExistingRoutedEventStore()
    {
        IInputElement element = new FrameworkElement();
        var calls = 0;
        MouseButtonEventHandler handler = (_, args) =>
        {
            Assert.Equal(MouseButton.Left, args.ChangedButton);
            calls++;
        };

        element.MouseLeftButtonDown += handler;
        element.RaiseEvent(CreateMouseButtonArgs(UIElement.MouseLeftButtonDownEvent));
        element.MouseLeftButtonDown -= handler;
        element.RaiseEvent(CreateMouseButtonArgs(UIElement.MouseLeftButtonDownEvent));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void StylusAndTextInterfaceEvents_UseEachImplementersRoutedEventStore()
    {
        IInputElement[] elements =
        [
            new UIElement(),
            new ContentElement(),
            new InputElement3DProbe(),
        ];

        foreach (IInputElement element in elements)
        {
            var stylusCalls = 0;
            var textCalls = 0;
            StylusEventHandler stylusHandler = (sender, args) =>
            {
                Assert.Same(element, sender);
                Assert.Equal(73, args.Timestamp);
                stylusCalls++;
            };
            TextCompositionEventHandler textHandler = (sender, args) =>
            {
                Assert.Same(element, sender);
                Assert.Equal("contract", args.Text);
                textCalls++;
            };

            RoutedEvent stylusEvent = GetDeclaredRoutedEvent(element.GetType(), "PreviewStylusInRangeEvent");
            RoutedEvent textEvent = GetDeclaredRoutedEvent(element.GetType(), "TextInputEvent");
            var device = new PointerStylusDevice(4721, "IInputElement event probe");

            element.PreviewStylusInRange += stylusHandler;
            element.TextInput += textHandler;
            element.RaiseEvent(new StylusEventArgs(device, 73) { RoutedEvent = stylusEvent });
            element.RaiseEvent(new TextCompositionEventArgs(textEvent, "contract", 74));

            element.PreviewStylusInRange -= stylusHandler;
            element.TextInput -= textHandler;
            element.RaiseEvent(new StylusEventArgs(device, 75) { RoutedEvent = stylusEvent });
            element.RaiseEvent(new TextCompositionEventArgs(textEvent, "ignored", 76));

            Assert.Equal(1, stylusCalls);
            Assert.Equal(1, textCalls);
        }
    }

    [Fact]
    public void AllThreeInputElementFamilies_MapTheCompleteInterfaceAndUIElementIsConcrete()
    {
        Type interfaceType = typeof(IInputElement);
        Type[] implementers = [typeof(UIElement), typeof(ContentElement), typeof(UIElement3D)];

        Assert.False(typeof(UIElement).IsAbstract);
        Assert.NotNull(typeof(UIElement).GetConstructor(Type.EmptyTypes));

        foreach (Type implementer in implementers)
        {
            Assert.True(interfaceType.IsAssignableFrom(implementer));
            InterfaceMapping map = implementer.GetInterfaceMap(interfaceType);
            Assert.Equal(interfaceType.GetMethods().Length, map.InterfaceMethods.Length);
            Assert.All(map.TargetMethods, static method => Assert.True(method.IsPublic));
        }
    }

    [Fact]
    public void DirectMouseAndStylusStateBackTheInterfacePropertiesAndCaptureMethods()
    {
        var element = new FrameworkElement();
        IInputElement input = element;
        var previousStylus = Tablet.CurrentStylusDevice;
        var stylus = new PointerStylusDevice(9173, "Contract probe");

        try
        {
            UIElement.SetMouseDirectlyOverElement(element);
            Assert.True(input.IsMouseDirectlyOver);

            Tablet.CurrentStylusDevice = stylus;
            stylus.UpdateState(
                Point.Zero,
                new StylusPointCollection(),
                inAir: false,
                inverted: false,
                inRange: true,
                barrelPressed: false,
                eraserPressed: false,
                directlyOver: element);

            Assert.True(input.IsStylusDirectlyOver);
            Assert.True(input.IsStylusOver);
            Assert.True(input.CaptureStylus());
            Assert.True(input.IsStylusCaptured);
            Assert.Same(element, stylus.Captured);

            input.ReleaseStylusCapture();
            Assert.False(input.IsStylusCaptured);
            Assert.Null(stylus.Captured);
        }
        finally
        {
            UIElement.SetMouseDirectlyOverElement(null);
            stylus.Capture(null);
            Tablet.CurrentStylusDevice = previousStylus;
        }
    }

    [Fact]
    public void InputContractsLiveInUnifiedManagedAssemblyWithoutFacadeDependencies()
    {
        var managedAssembly = typeof(IInputElement).Assembly;

        Assert.Equal("Jalium.UI.Managed", managedAssembly.GetName().Name);
        Assert.Equal(managedAssembly, typeof(Keyboard).Assembly);
        Assert.Equal(managedAssembly, typeof(StylusEventHandler).Assembly);
        Assert.Equal(managedAssembly, typeof(TextCompositionEventHandler).Assembly);
        Assert.DoesNotContain(
            managedAssembly.GetReferencedAssemblies(),
            reference => reference.Name is
                "Jalium.UI.Core" or
                "Jalium.UI.Media" or
                "Jalium.UI.Input" or
                "Jalium.UI.Interop" or
                "Jalium.UI.Controls");
    }

    private static MouseButtonEventArgs CreateMouseButtonArgs(RoutedEvent routedEvent) => new(
        routedEvent,
        Point.Zero,
        MouseButton.Left,
        MouseButtonState.Pressed,
        clickCount: 1,
        MouseButtonState.Pressed,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        ModifierKeys.None,
        timestamp: 1);

    private static RoutedEvent GetDeclaredRoutedEvent(Type type, string fieldName)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field?.GetValue(null) is RoutedEvent routedEvent)
            {
                return routedEvent;
            }
        }

        throw new InvalidOperationException($"No routed-event field named '{fieldName}' exists on '{type}'.");
    }

    private sealed class InputElement3DProbe : UIElement3D
    {
    }
}
