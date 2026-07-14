using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Data;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection(nameof(ParityFoundationBehaviorCollection))]
public sealed class LostFocusEventManagerParityTests
{
    [Fact]
    public void SurfaceMatchesWpfShape()
    {
        var managerType = typeof(LostFocusEventManager);
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        const BindingFlags protectedInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        Assert.Equal(typeof(WeakEventManager), managerType.BaseType);
        Assert.False(managerType.IsSealed);
        Assert.Empty(managerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Contains(
            managerType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => constructor.GetParameters().Length == 0);

        Assert.NotNull(managerType.GetMethod(
            nameof(LostFocusEventManager.AddHandler),
            publicStatic,
            binder: null,
            types: [typeof(DependencyObject), typeof(EventHandler<RoutedEventArgs>)],
            modifiers: null));
        Assert.NotNull(managerType.GetMethod(
            nameof(LostFocusEventManager.RemoveHandler),
            publicStatic,
            binder: null,
            types: [typeof(DependencyObject), typeof(EventHandler<RoutedEventArgs>)],
            modifiers: null));
        Assert.NotNull(managerType.GetMethod(
            nameof(LostFocusEventManager.AddListener),
            publicStatic,
            binder: null,
            types: [typeof(DependencyObject), typeof(IWeakEventListener)],
            modifiers: null));
        Assert.NotNull(managerType.GetMethod(
            nameof(LostFocusEventManager.RemoveListener),
            publicStatic,
            binder: null,
            types: [typeof(DependencyObject), typeof(IWeakEventListener)],
            modifiers: null));

        AssertProtectedOverride(managerType, "NewListenerList", Type.EmptyTypes);
        AssertProtectedOverride(managerType, "StartListening", [typeof(object)]);
        AssertProtectedOverride(managerType, "StopListening", [typeof(object)]);

        Assert.NotNull(typeof(UIElement).GetMethod(
            "OnGotFocus",
            protectedInstance,
            binder: null,
            types: [typeof(RoutedEventArgs)],
            modifiers: null));
        Assert.NotNull(typeof(UIElement).GetMethod(
            "OnLostFocus",
            protectedInstance,
            binder: null,
            types: [typeof(RoutedEventArgs)],
            modifiers: null));
    }

    [Fact]
    public void HandlerReceivesRealLogicalFocusTransitionAndRemoveStopsDelivery()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        var first = new FocusProbe();
        var second = new FocusProbe();
        var calls = 0;
        object? senderSeen = null;
        RoutedEventArgs? argsSeen = null;
        EventHandler<RoutedEventArgs> handler = (sender, e) =>
        {
            calls++;
            senderSeen = sender;
            argsSeen = e;
        };

        LostFocusEventManager.AddHandler(first, handler);
        try
        {
            Assert.True(first.Focus());
            Assert.True(first.Focus());
            Assert.True(second.Focus());

            Assert.Equal(1, calls);
            Assert.Same(first, senderSeen);
            Assert.NotNull(argsSeen);
            Assert.Same(UIElement.LostFocusEvent, argsSeen!.RoutedEvent);
            Assert.Same(first, argsSeen.Source);
            Assert.Equal(1, first.GotFocusCalls);
            Assert.Equal(1, first.LostFocusCalls);

            LostFocusEventManager.RemoveHandler(first, handler);
            Assert.True(first.Focus());
            Assert.True(second.Focus());

            Assert.Equal(1, calls);
            Assert.Equal(2, first.GotFocusCalls);
            Assert.Equal(2, first.LostFocusCalls);
        }
        finally
        {
            LostFocusEventManager.RemoveHandler(first, handler);
            Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void WeakListenerReceivesManagerTypeAndCanBeRemoved()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        var first = new FocusProbe();
        var second = new FocusProbe();
        var listener = new CountingWeakListener();

        LostFocusEventManager.AddListener(first, listener);
        try
        {
            Assert.True(first.Focus());
            Assert.True(second.Focus());

            Assert.Equal(1, listener.Calls);
            Assert.Same(typeof(LostFocusEventManager), listener.ManagerType);
            Assert.Same(first, listener.Sender);
            Assert.Same(UIElement.LostFocusEvent, listener.Args?.RoutedEvent);

            LostFocusEventManager.RemoveListener(first, listener);
            Assert.True(first.Focus());
            Assert.True(second.Focus());

            Assert.Equal(1, listener.Calls);
        }
        finally
        {
            LostFocusEventManager.RemoveListener(first, listener);
            Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void ReentrantFocusRequestIsQueuedUntilCurrentTransitionCompletes()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        var first = new FocusProbe();
        var second = new FocusProbe();
        var third = new FocusProbe();
        var order = new List<string>();
        EventHandler<RoutedEventArgs> firstLost = (_, _) =>
        {
            order.Add("first-lost");
            Assert.True(third.Focus());
        };
        RoutedEventHandler secondGot = (_, _) => order.Add("second-got");
        RoutedEventHandler secondLost = (_, _) => order.Add("second-lost");
        RoutedEventHandler thirdGot = (_, _) => order.Add("third-got");

        LostFocusEventManager.AddHandler(first, firstLost);
        second.GotFocus += secondGot;
        second.LostFocus += secondLost;
        third.GotFocus += thirdGot;
        try
        {
            Assert.True(first.Focus());
            Assert.True(second.Focus());

            Assert.Same(third, Keyboard.FocusedElement);
            Assert.Equal(
                ["first-lost", "second-got", "second-lost", "third-got"],
                order);
        }
        finally
        {
            LostFocusEventManager.RemoveHandler(first, firstLost);
            second.GotFocus -= secondGot;
            second.LostFocus -= secondLost;
            third.GotFocus -= thirdGot;
            Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void LostFocusUpdateSourceTriggerCommitsOnlyWhenFocusLeavesTarget()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        var source = new BindingSource { Value = "initial" };
        var target = new FocusBindingTarget();
        var next = new FocusProbe();
        target.SetBinding(
            FocusBindingTarget.ValueProperty,
            new Binding(nameof(BindingSource.Value))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            });

        try
        {
            Assert.True(target.Focus());
            target.Value = "edited";

            Assert.Equal("initial", source.Value);

            Assert.True(next.Focus());

            Assert.Equal("edited", source.Value);
        }
        finally
        {
            target.ClearBinding(FocusBindingTarget.ValueProperty);
            Keyboard.ClearFocus();
        }
    }

    [Fact]
    public void NullHandlersAndListenersAreRejected()
    {
        var source = new FocusProbe();

        Assert.Throws<ArgumentNullException>(() => LostFocusEventManager.AddHandler(source, null!));
        Assert.Throws<ArgumentNullException>(() => LostFocusEventManager.RemoveHandler(source, null!));
        Assert.Throws<ArgumentNullException>(() => LostFocusEventManager.AddListener(source, null!));
        Assert.Throws<ArgumentNullException>(() => LostFocusEventManager.RemoveListener(source, null!));
    }

    private static void AssertProtectedOverride(Type managerType, string name, Type[] parameterTypes)
    {
        var method = managerType.GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.Equal(managerType, method.DeclaringType);
        Assert.Equal(typeof(WeakEventManager), method.GetBaseDefinition().DeclaringType);
    }

    private sealed class FocusProbe : FrameworkElement
    {
        public FocusProbe()
        {
            Focusable = true;
        }

        public int GotFocusCalls { get; private set; }

        public int LostFocusCalls { get; private set; }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            GotFocusCalls++;
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            LostFocusCalls++;
        }
    }

    private sealed class FocusBindingTarget : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(FocusBindingTarget),
            new PropertyMetadata(string.Empty));

        public FocusBindingTarget()
        {
            Focusable = true;
        }

        public string Value
        {
            get => (string)(GetValue(ValueProperty) ?? string.Empty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class BindingSource : INotifyPropertyChanged
    {
        private string _value = string.Empty;

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CountingWeakListener : IWeakEventListener
    {
        public int Calls { get; private set; }

        public Type? ManagerType { get; private set; }

        public object? Sender { get; private set; }

        public RoutedEventArgs? Args { get; private set; }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            Calls++;
            ManagerType = managerType;
            Sender = sender;
            Args = Assert.IsType<RoutedEventArgs>(e);
            return true;
        }
    }
}
