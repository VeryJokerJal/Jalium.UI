using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSMouseModifierTests
{
    [Theory]
    [InlineData(0, ModifierKeys.None)]
    [InlineData(2, ModifierKeys.Control)]
    [InlineData(8, ModifierKeys.Control)]
    [InlineData(9, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(12, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(10, ModifierKeys.Control | ModifierKeys.Windows)]
    public void PlatformClickRetainsLogicalInputBindingModifiers(int native, ModifierKeys logical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        MouseButtonEventArgs? input = null;
        host.Target.AddHandler(UIElement.MouseDownEvent, new MouseButtonEventHandler((_, e) => input = e), true);
        host.Send(PlatformEventType.MouseDown, native);
        Assert.NotNull(input);
        Assert.Equal(logical, input.KeyboardModifiers);
        var command = new Counter();
        host.Target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.LeftClick, logical)));
        Assert.True(CommandManager.ProcessInput(host.Target, input));
        Assert.Equal(1, command.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlatformCommandRetainsDoubleClickAndWheelGestureMatching(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        MouseEventArgs? input = null;
        host.Target.AddHandler(wheel ? UIElement.MouseWheelEvent : UIElement.MouseDownEvent,
            wheel ? new MouseWheelEventHandler((_, e) => input = e) : new MouseButtonEventHandler((_, e) => input = e), true);
        host.Send(wheel ? PlatformEventType.MouseWheel : PlatformEventType.MouseDown, 8, clicks: 2);
        Assert.NotNull(input);
        var command = new Counter();
        host.Target.InputBindings.Add(new MouseBinding(command,
            new MouseGesture(wheel ? MouseAction.WheelClick : MouseAction.LeftDoubleClick, ModifierKeys.Control)));
        Assert.True(CommandManager.ProcessInput(host.Target, input));
        Assert.Equal(1, command.Count);
    }

    public static IEnumerable<object[]> Packets()
    {
        foreach (var kind in new[] { PlatformEventType.MouseDown, PlatformEventType.MouseUp,
            PlatformEventType.MouseMove, PlatformEventType.MouseWheel })
        foreach (var entry in new (int Native, ModifierKeys Physical, ModifierKeys Logical)[]
        {
            (0, ModifierKeys.None, ModifierKeys.None),
            (2, ModifierKeys.Control, ModifierKeys.Control),
            (8, ModifierKeys.Windows, ModifierKeys.Control),
            (10, ModifierKeys.Control | ModifierKeys.Windows, ModifierKeys.Control | ModifierKeys.Windows),
            (13, ModifierKeys.Windows | ModifierKeys.Shift | ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt),
        })
            yield return [(int)kind, entry.Native, entry.Physical, entry.Logical];
    }

    [Theory]
    [MemberData(nameof(Packets))]
    public void WindowMouseRoutesRetainLogicalAndPhysicalSnapshots(
        int kindValue, int native, ModifierKeys physical, ModifierKeys logical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var seen = Observe(host.Target, (PlatformEventType)kindValue);
        host.Send((PlatformEventType)kindValue, native);
        AssertSnapshots(seen, physical, logical);
    }

    [Theory]
    [MemberData(nameof(Packets))]
    public void PopupMouseRoutesRetainLogicalAndPhysicalSnapshots(
        int kindValue, int native, ModifierKeys physical, ModifierKeys logical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new PopupHost();
        var seen = Observe(host.Target, (PlatformEventType)kindValue);
        host.Send((PlatformEventType)kindValue, native);
        AssertSnapshots(seen, physical, logical);
    }

    [Fact]
    public void DoubleClickClonesRetainBothModifierSnapshots()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var seen = new List<MouseEventArgs>();
        host.Target.PreviewMouseDoubleClick += (_, e) => seen.Add(e);
        host.Target.MouseDoubleClick += (_, e) => seen.Add(e);
        host.Send(PlatformEventType.MouseDown, 8, clicks: 2);
        AssertSnapshots(seen, ModifierKeys.Windows, ModifierKeys.Control);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void PointerPromotionRetainsPhysicalCommandAndExposesLogicalControl(int device, bool mouseExpected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        ModifierKeys? pointer = null;
        MouseEventArgs? mouse = null;
        host.Target.PointerDown += (_, e) => pointer = Assert.IsAssignableFrom<PointerEventArgs>(e).KeyModifiers;
        host.Target.MouseDown += (_, e) => mouse = e;
        var packet = new PlatformEvent
        {
            Type = PlatformEventType.PointerDown, PointerId = 31, PointerType = device,
            PointerX = 50, PointerY = 50, PointerFlags = 7, PointerButtons = 1, Modifiers = 8,
        };
        Assert.Equal(ModifierKeys.Windows, host.Window.BuildPointerInputData(packet).Modifiers);
        host.Send(packet);
        Assert.Equal(ModifierKeys.Control, pointer);
        Assert.Equal(mouseExpected, mouse != null);
        if (mouse != null)
        {
            Assert.Equal(ModifierKeys.Control, mouse.KeyboardModifiers);
            Assert.Equal(ModifierKeys.Windows, mouse.PhysicalModifiers);
        }
    }

    [Theory]
    [InlineData(0, 1, false, ModifierKeys.None)]
    [InlineData(8, 1, false, ModifierKeys.Control)]
    [InlineData(8, 2, false, ModifierKeys.Control)]
    [InlineData(8, 1, true, ModifierKeys.Control)]
    public void RegisteredMouseBindingExecutesFromPlatformPacket(
        int native, int clicks, bool wheel, ModifierKeys logical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter();
        var action = wheel ? MouseAction.WheelClick : clicks == 2 ? MouseAction.LeftDoubleClick : MouseAction.LeftClick;
        host.Target.InputBindings.Add(new MouseBinding(command, new MouseGesture(action, logical)));
        host.Send(wheel ? PlatformEventType.MouseWheel : PlatformEventType.MouseDown, native, clicks);
        Assert.Equal(1, command.Count);
        if (!wheel)
        {
            host.Send(PlatformEventType.MouseUp, native);
            Assert.Equal(1, command.Count);
        }
    }

    [Fact]
    public void ButtonSpecificRouteRetainsPhysicalModifiers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        MouseEventArgs? seen = null;
        host.Target.MouseLeftButtonDown += (_, e) => seen = e;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.NotNull(seen);
        Assert.Equal(ModifierKeys.Control, seen.KeyboardModifiers);
        Assert.Equal(ModifierKeys.Windows, seen.PhysicalModifiers);
    }

    [Fact]
    public void HandledPreviewDoesNotQueryTheMouseCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = BindClick(host.Target);
        host.Target.PreviewMouseDown += (_, e) => e.Handled = true;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(0, command.QueryCount);
        Assert.Equal(0, command.Count);
    }

    [Fact]
    public void DisabledCommandLeavesTheOrdinaryMouseRouteAvailable()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = BindClick(host.Target);
        command.Query = () => false;
        int clicks = 0;
        host.Target.MouseDown += (_, _) => clicks++;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(1, command.QueryCount);
        Assert.Equal(0, command.Count);
        Assert.Equal(1, clicks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingDuringTheCommandDoesNotContinueTheOldMouseRoute(bool duringQuery)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = BindClick(host.Target);
        if (duringQuery) command.Query = () => { host.Window.Close(); return true; };
        else command.OnExecute = () => host.Window.Close();
        int clicks = 0;
        host.Target.MouseDown += (_, _) => clicks++;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(duringQuery ? 0 : 1, command.Count);
        Assert.Equal(0, clicks);
        Assert.False(host.Target.IsPressed);
        Assert.Null(Jalium.UI.Input.Mouse.Captured);
        Assert.Empty(host.Dispatcher.ActivePointerTargets);
        Assert.Empty(host.Dispatcher.LastPointerPoints);
    }

    [Fact]
    public void ReparentingDuringCanExecuteDoesNotRunInTheNewWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        using var destination = new Host();
        var command = BindClick(host.Target);
        command.Query = () => { host.Window.Content = null; destination.Window.Content = host.Target; return true; };
        int clicks = 0;
        host.Target.MouseDown += (_, _) => clicks++;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(0, command.Count);
        Assert.Equal(0, clicks);
        Assert.Same(host.Target, destination.Window.Content);
        Assert.Empty(host.Dispatcher.ActivePointerTargets);
    }

    [Fact]
    public void CanExecuteChangingTheBindingDoesNotReplaceTheCurrentCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var first = new Counter();
        var second = new Counter();
        var binding = new MouseBinding(first, new MouseGesture(MouseAction.LeftClick, ModifierKeys.Control));
        host.Target.InputBindings.Add(binding);
        first.Query = () => { binding.Command = second; return true; };
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(1, first.Count);
        Assert.Equal(0, second.Count);
        host.Send(PlatformEventType.MouseUp, 8);
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(1, first.Count);
        Assert.Equal(1, second.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseBindingsRouteToTheNearestAvailableElement(bool childBound)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var child = new Control { Background = Brushes.White };
        var parent = new ContentControl { Background = Brushes.White, Content = child };
        using var host = new Host(parent);
        var parentCommand = BindClick(parent);
        var childCommand = childBound ? BindClick(child) : null;
        host.Send(PlatformEventType.MouseDown, 8);
        Assert.Equal(childBound ? 0 : 1, parentCommand.Count);
        if (childCommand != null) Assert.Equal(1, childCommand.Count);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public void PopupRegisteredMouseBindingExecutesOnce(int clicks, bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new PopupHost();
        var command = new Counter();
        host.Target.InputBindings.Add(new MouseBinding(command, new MouseGesture(
            wheel ? MouseAction.WheelClick : clicks == 2 ? MouseAction.LeftDoubleClick : MouseAction.LeftClick,
            ModifierKeys.Control)));
        host.Send(wheel ? PlatformEventType.MouseWheel : PlatformEventType.MouseDown, 8, clicks);
        Assert.Equal(1, command.Count);
        if (!wheel) host.Send(PlatformEventType.MouseUp, 8);
        Assert.Equal(1, command.Count);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(8, false)]
    public void PhysicalControlTextMenuRetainsPriorityOverLogicalMouseBinding(int native, bool menuExpected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(new TextBox { Text = "alpha beta", Background = Brushes.White });
        Assert.True(host.Target.Focus());
        var command = BindClick(host.Target);
        int menus = 0;
        var previous = PlatformTextContextMenus.Show;
        typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)123);
        PlatformTextContextMenus.Show = _ => { menus++; return true; };
        try
        {
            host.Send(PlatformEventType.MouseDown, native);
            Assert.Equal(menuExpected ? 1 : 0, menus);
            Assert.Equal(menuExpected ? 0 : 1, command.Count);
        }
        finally
        {
            PlatformTextContextMenus.Show = previous;
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)0);
        }
    }

    [Theory]
    [InlineData(0f, 0f, MouseWheelPhase.Began, 0)]
    [InlineData(0f, 0f, MouseWheelPhase.Ended, 0)]
    [InlineData(.001f, 0f, MouseWheelPhase.Changed, 1)]
    [InlineData(0f, .001f, MouseWheelPhase.Changed, 1)]
    public void WheelCommandRequiresMovementButRetainsPreciseSubunitDeltas(
        float x, float y, MouseWheelPhase phase, int expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter();
        host.Target.InputBindings.Add(new MouseBinding(command,
            new MouseGesture(MouseAction.WheelClick, ModifierKeys.Control)));
        int packets = 0;
        host.Target.AddHandler(UIElement.MouseWheelEvent, new MouseWheelEventHandler((_, _) => packets++), true);
        host.Send(new PlatformEvent { Type = PlatformEventType.MouseWheel, MouseX = 50, MouseY = 50,
            Modifiers = 8, WheelDeltaX = x, WheelDeltaY = y, WheelHasPreciseScrollingDeltas = true, WheelPhase = phase });
        Assert.Equal(expected, command.Count);
        Assert.Equal(expected, command.QueryCount);
        Assert.Equal(1, packets);
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    public void WheelCommandUsesOnlyUnconsumedAxes(bool xHandled, bool yHandled, int expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var command = new Counter();
        host.Target.InputBindings.Add(new MouseBinding(command,
            new MouseGesture(MouseAction.WheelClick, ModifierKeys.Control)));
        host.Target.PreviewMouseWheel += (_, e) =>
        { e.IsHorizontalDeltaHandled = xHandled; e.IsVerticalDeltaHandled = yHandled; };
        host.Send(new PlatformEvent { Type = PlatformEventType.MouseWheel, MouseX = 50, MouseY = 50,
            Modifiers = 8, WheelDeltaX = .25f, WheelDeltaY = -.5f, WheelHasPreciseScrollingDeltas = true });
        Assert.Equal(expected, command.Count);
        Assert.Equal(expected, command.QueryCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TextMenuFailureAndCancellationRetainMouseCommandAndPreviewSemantics(int mode)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(new TextBox { Text = "alpha beta", Background = Brushes.White });
        Assert.True(host.Target.Focus());
        var command = BindClick(host.Target);
        int menus = 0;
        var previous = PlatformTextContextMenus.Show;
        typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)123);
        PlatformTextContextMenus.Show = _ => { menus++; return false; };
        if (mode == 1) host.Target.PreviewMouseDown += (_, e) => e.Handled = true;
        if (mode == 2) host.Target.AddHandler(ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler((_, e) => e.Handled = true));
        try
        {
            host.Send(PlatformEventType.MouseDown, 2);
            Assert.Equal(mode == 0 ? 1 : 0, menus);
            Assert.Equal(mode == 1 ? 0 : 1, command.Count);
        }
        finally
        {
            PlatformTextContextMenus.Show = previous;
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)0);
        }
    }

    private static Counter BindClick(Control target)
    {
        var command = new Counter();
        target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.LeftClick, ModifierKeys.Control)));
        return command;
    }

    private static List<MouseEventArgs> Observe(Control target, PlatformEventType kind)
    {
        var seen = new List<MouseEventArgs>();
        switch (kind)
        {
            case PlatformEventType.MouseDown:
                target.PreviewMouseDown += (_, e) => seen.Add(e);
                target.MouseDown += (_, e) => seen.Add(e);
                break;
            case PlatformEventType.MouseUp:
                target.PreviewMouseUp += (_, e) => seen.Add(e);
                target.MouseUp += (_, e) => seen.Add(e);
                break;
            case PlatformEventType.MouseMove:
                target.PreviewMouseMove += (_, e) => seen.Add(e);
                target.MouseMove += (_, e) => seen.Add(e);
                break;
            case PlatformEventType.MouseWheel:
                target.PreviewMouseWheel += (_, e) => seen.Add(e);
                target.MouseWheel += (_, e) => seen.Add(e);
                break;
        }
        return seen;
    }

    private static void AssertSnapshots(List<MouseEventArgs> seen, ModifierKeys physical, ModifierKeys logical)
    {
        Assert.Equal(2, seen.Count);
        Assert.All(seen, e =>
        {
            Assert.Equal(logical, e.KeyboardModifiers);
            Assert.Equal(physical, e.PhysicalModifiers);
        });
    }

    private sealed class Counter : ICommand
    {
        internal int Count;
        internal int QueryCount;
        internal Func<bool>? Query;
        internal Action? OnExecute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) { QueryCount++; return Query?.Invoke() ?? true; }
        public void Execute(object? parameter) { Count++; OnExecute?.Invoke(); }
    }

    private sealed class Host : IDisposable
    {
        internal readonly Control Target;
        internal readonly DisplayedTestWindow Window;
        internal WindowInputDispatcher Dispatcher => (WindowInputDispatcher)typeof(Window)
            .GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        internal Host(Control? target = null)
        {
            Target = target ?? new Control { Background = Brushes.White, Focusable = true };
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Jalium.UI.Input.Mouse.Capture(null);
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Window = new DisplayedTestWindow { Width = 240, Height = 180,
                TitleBarStyle = WindowTitleBarStyle.Native, Content = Target };
            Window.Measure(new Size(240, 180)); Window.Arrange(new Rect(0, 0, 240, 180));
        }
        internal void Send(PlatformEventType type, int modifiers, int clicks = 1) => Send(new PlatformEvent
        {
            Type = type, MouseX = 50, MouseY = 50, Modifiers = modifiers, ClickCount = clicks,
            HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0, WheelDeltaY = 1,
        });
        internal void Send(PlatformEvent packet) => typeof(Window)
            .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [packet]);
        public void Dispose()
        {
            Jalium.UI.Input.Mouse.Capture(null);
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Keyboard.ClearFocus(); Window.Close();
        }
    }

    private sealed class PopupHost : IDisposable
    {
        internal readonly Control Target = new() { Background = Brushes.White, Focusable = true };
        private readonly Window _owner;
        private readonly PopupWindow _popup;
        internal PopupHost()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Jalium.UI.Input.Mouse.Capture(null);
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            _owner = new DisplayedTestWindow { Width = 320, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native };
            var root = new PopupRoot(new Popup(), Target, isLightDismiss: false);
            _popup = new PopupWindow(_owner, root);
            // Contract fixture: the bridge owns no native handle. AppKit menu and
            // window integration run separately in the native tests and HostSmoke.
            typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_popup, RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow)));
            _popup.Measure(new Size(200, 120)); _popup.Arrange(new Rect(0, 0, 200, 120));
            _popup.SetVisualBounds(new Rect(0, 0, 200, 120));
            root.SetVisualBounds(new Rect(0, 0, 200, 120)); Target.SetVisualBounds(new Rect(0, 0, 200, 120));
        }
        internal void Send(PlatformEventType type, int modifiers, int clicks = 1) => Send(new PlatformEvent
        {
            Type = type, MouseX = 50, MouseY = 50, Modifiers = modifiers, ClickCount = clicks,
            HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0, WheelDeltaY = 1,
        });
        internal void Send(PlatformEvent packet) => typeof(PopupWindow)
            .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_popup, [packet]);
        public void Dispose()
        {
            Jalium.UI.Input.Mouse.Capture(null);
            typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_popup, null);
            _popup.Dispose(); _owner.Close(); Keyboard.ClearFocus();
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
        }
    }

}
