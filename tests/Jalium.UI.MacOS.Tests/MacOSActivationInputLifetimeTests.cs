using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSActivationInputLifetimeTests
{
    [Theory]
    [InlineData("event", false)] [InlineData("event", true)]
    [InlineData("property", false)] [InlineData("property", true)]
    [InlineData("capture", false)] [InlineData("capture", true)]
    public void ReactivationKeepsItsNewMousePressAndCapture(string callback, bool reuseTarget)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Mouse(h.Target);
        Assert.True(h.Target.CaptureMouse());
        var next = reuseTarget ? h.Target : h.Other;
        bool ran = false;
        void Restart()
        {
            if (ran) return;
            ran = true;
            h.Send(PlatformEventType.Activate);
            h.Target.ReleaseMouseCapture();
            h.Mouse(next);
            Assert.True(next.CaptureMouse());
        }
        EventHandler onDeactivate = (_, _) => { if (callback == "event") Restart(); };
        Action<DependencyProperty, object?, object?> onProperty = (p, _, value) =>
        { if (callback == "property" && p == Window.IsActiveProperty && Equals(value, false)) Restart(); };
        MouseEventHandler onCapture = (_, _) => { if (callback == "capture") Restart(); };
        h.Window.Deactivated += onDeactivate;
        h.Window.PropertyChangedInternal += onProperty;
        h.Target.LostMouseCapture += onCapture;
        try
        {
            h.Send(PlatformEventType.Deactivate);
            Assert.True(ran);
            Assert.True(h.Window.IsActive);
            Assert.Same(next, UIElement.MouseCapturedElement);
            Assert.True(next.IsPressed);
            Assert.True(h.Panel.IsPressed);
            Assert.Equal(reuseTarget, h.Target.IsPressed);
            h.Mouse(next, up: true);
            Assert.False(next.IsPressed);
        }
        finally
        {
            h.Window.Deactivated -= onDeactivate;
            h.Window.PropertyChangedInternal -= onProperty;
            h.Target.LostMouseCapture -= onCapture;
        }
    }

    [Fact]
    public void AnInactiveOwnerDoesNotReleaseAnotherWindowsCapture()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); using var other = new Host();
        other.Mouse(other.Target); Assert.True(other.Target.CaptureMouse());
        h.Send(PlatformEventType.Deactivate);
        Assert.Same(other.Target, UIElement.MouseCapturedElement);
        Assert.True(other.Target.IsPressed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeactivatedCallbacksObserveRetiredMouseAndKeyboardPresses(bool keyboard)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        if (keyboard) h.Dispatcher.ActivateKeyboardPressedChain(h.Target); else h.Mouse(h.Target);
        Assert.True(h.Target.CaptureMouse());
        bool called = false;
        h.Window.Deactivated += (_, _) =>
        {
            called = true;
            Assert.False(h.Target.IsPressed);
            Assert.False(h.Panel.IsPressed);
            Assert.Null(UIElement.MouseCapturedElement);
            Assert.Same(h.Target, Keyboard.FocusedElement);
        };
        h.Send(PlatformEventType.Deactivate);
        Assert.True(called);
        Assert.False(h.Window.IsActive);
    }

    [Theory]
    [InlineData("query")] [InlineData("raw-key")] [InlineData("raw-text")]
    public void AReactivationCannotReviveAnOldKeyboardOrTextPacket(string phase)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        int executions = 0, later = 0;
        void Restart() { h.Send(PlatformEventType.Deactivate); h.Send(PlatformEventType.Activate); }
        var command = new Counter(() => { if (phase == "query") Restart(); return true; }, () => executions++);
        h.Target.InputBindings.Add(new KeyBinding(command, new KeyGesture(Key.K, ModifierKeys.Control)));
        if (phase == "raw-key") h.Target.PreviewKeyDown += (_, _) => Restart();
        if (phase == "raw-text") h.Target.PreviewTextInput += (_, _) => Restart();
        h.Target.KeyDown += (_, _) => later++;
        h.Target.TextInput += (_, _) => later++;
        if (phase == "raw-text")
            h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = 'k' });
        else h.SendKey();
        Assert.Equal(0, executions);
        Assert.Equal(phase == "query" ? 1 : 0, later);
        Assert.True(h.Window.IsActive);
        Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AReactivationCannotReviveAnOldMouseCommand(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int executions = 0;
        var command = new Counter(() => { h.Send(PlatformEventType.Deactivate); h.Send(PlatformEventType.Activate); return true; },
            () => executions++);
        h.Target.InputBindings.Add(new MouseBinding(command,
            new MouseGesture(wheel ? MouseAction.WheelClick : MouseAction.LeftClick, ModifierKeys.Control)));
        h.Mouse(h.Target, wheel: wheel, control: true);
        Assert.Equal(0, executions);
    }

    [Theory]
    [InlineData("deactivate")] [InlineData("close")]
    public void ActivatedHandlersCanSupersedeTheOldActivationTail(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Send(PlatformEventType.Deactivate);
        int active = 0, inactive = 0;
        h.Window.Activated += (_, _) =>
        {
            active++;
            if (change == "close") h.Window.Close();
            else h.Send(PlatformEventType.Deactivate);
        };
        h.Window.Deactivated += (_, _) => inactive++;
        h.Send(PlatformEventType.Activate);
        Assert.Equal(1, active);
        if (change == "deactivate") { Assert.False(h.Window.IsActive); Assert.Equal(1, inactive); }
        Assert.False(h.Window.IsMacOSPopupInputOwnerAlive && change == "close");
    }

    [Fact]
    public void ResponderFocusNotificationsDoNotRetireWindowActivationOrKeyCommands()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int executions = 0;
        h.Target.InputBindings.Add(new KeyBinding(new Counter(() =>
        {
            h.Send(PlatformEventType.FocusLost); h.Send(PlatformEventType.FocusGained); return true;
        }, () => executions++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.SendKey();
        Assert.Equal(1, executions); Assert.True(h.Window.IsActive);
    }

    [Fact]
    public void DuplicateActivationNotificationsDoNotRepeatEvents()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int activated = 0, deactivated = 0;
        h.Window.Activated += (_, _) => activated++;
        h.Window.Deactivated += (_, _) => deactivated++;
        h.Send(PlatformEventType.Activate); h.Send(PlatformEventType.Deactivate);
        h.Send(PlatformEventType.Deactivate); h.Send(PlatformEventType.Activate);
        Assert.Equal((1, 1), (activated, deactivated));
    }

    [Theory]
    [InlineData("activated")] [InlineData("deactivated")]
    [InlineData("active-property")] [InlineData("inactive-property")]
    [InlineData("other-window")] [InlineData("app-activated")] [InlineData("app-deactivated")]
    public void SupersededNotificationsCannotOverwriteApplicationActivation(string callback)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); using var other = new Host();
        h.Send(PlatformEventType.Deactivate);
        using var app = new ApplicationShell();
        bool fromActive = callback is "activated" or "active-property" or "app-activated";
        if (!fromActive) h.Send(PlatformEventType.Activate);
        bool ran = false;
        void Supersede()
        {
            if (ran) return; ran = true;
            if (callback == "other-window") other.Send(PlatformEventType.Activate);
            else h.Send(fromActive ? PlatformEventType.Deactivate : PlatformEventType.Activate);
        }
        h.Window.Activated += (_, _) => { if (callback == "activated") Supersede(); };
        h.Window.Deactivated += (_, _) => { if (callback is "deactivated" or "other-window") Supersede(); };
        h.Window.PropertyChangedInternal += (p, _, value) =>
        {
            if (p == Window.IsActiveProperty &&
                ((callback == "active-property" && Equals(value, true)) ||
                 (callback == "inactive-property" && Equals(value, false)))) Supersede();
        };
        app.Instance.Activated += (_, _) => { if (callback == "app-activated") Supersede(); };
        app.Instance.Deactivated += (_, _) => { if (callback == "app-deactivated") Supersede(); };
        h.Send(fromActive ? PlatformEventType.Activate : PlatformEventType.Deactivate);
        Assert.True(ran);
        Assert.Equal(!fromActive, app.IsActive);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeKeyboardAndTextLeasesEndEvenWhenFocusReturnsUnchanged(bool text)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var lifetime = ((IInputDispatcherHost)h.Window).CaptureNativeInputLifetime(h.Target, text)!;
        Assert.True(lifetime());
        h.Send(PlatformEventType.Deactivate); h.Send(PlatformEventType.Activate);
        Assert.Same(h.Target, Keyboard.FocusedElement);
        Assert.False(lifetime());
        Assert.True(((IInputDispatcherHost)h.Window).CaptureNativeInputLifetime(h.Target, text)!());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void APressedPropertyCallbackCanStartTheOtherInputKind(bool oldKeyboard)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        if (oldKeyboard) h.Dispatcher.ActivateKeyboardPressedChain(h.Target);
        else h.Dispatcher.ActivateMousePressedChain(h.Target);
        bool ran = false;
        Action<DependencyProperty, object?, object?> change = (p, _, value) =>
        {
            if (ran || p != UIElement.IsPressedProperty || !Equals(value, false)) return;
            ran = true; h.Send(PlatformEventType.Activate);
            if (oldKeyboard) h.Dispatcher.ActivateMousePressedChain(h.Other);
            else h.Dispatcher.ActivateKeyboardPressedChain(h.Other);
        };
        h.Target.PropertyChangedInternal += change;
        try
        {
            h.Send(PlatformEventType.Deactivate);
            Assert.True(ran); Assert.True(h.Other.IsPressed); Assert.True(h.Panel.IsPressed);
            Assert.False(h.Target.IsPressed);
        }
        finally { h.Target.PropertyChangedInternal -= change; }
    }

    [Fact]
    public void APopupCloseCallbackCanReactivateWithoutClosingTheRemainingPopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var first = new Popup { PlacementTarget = h.Target, Child = new Control { Width = 50, Height = 20 },
            StaysOpen = false, ShouldConstrainToRootBounds = true, PopupAnimation = PopupAnimation.None };
        var second = new Popup { PlacementTarget = h.Other, Child = new Control { Width = 50, Height = 20 },
            StaysOpen = false, ShouldConstrainToRootBounds = true, PopupAnimation = PopupAnimation.None };
        first.IsOpen = true; second.IsOpen = true;
        bool ran = false;
        first.Closed += (_, _) => { ran = true; h.Send(PlatformEventType.Activate); };
        try
        {
            h.Send(PlatformEventType.Deactivate);
            Assert.True(ran); Assert.False(first.IsOpen); Assert.True(second.IsOpen);
            Assert.True(h.Window.IsActive);
        }
        finally { second.IsOpen = false; first.IsOpen = false; }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AnEditingMenuRequestCannotSurviveWindowReactivation(bool reactivate)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var editor = new TextBox { Text = "alpha", Width = 100, Height = 30 };
        h.Panel.Children.Add(editor); Assert.True(editor.Focus());
        var handle = typeof(Window).GetField("<Handle>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        handle.SetValue(h.Window, (nint)0x4201);
        try
        {
            var request = h.Window.CreateMacOSTextContextMenuRequest(editor, default);
            Assert.NotNull(request); Assert.True(request.IsCurrent());
            Assert.True(request.CanExecute(PlatformTextEditingCommand.SelectAll));
            h.Send(PlatformEventType.Deactivate);
            if (reactivate) h.Send(PlatformEventType.Activate);
            Assert.False(request.IsCurrent());
            Assert.False(request.CanExecute(PlatformTextEditingCommand.SelectAll));
        }
        finally { handle.SetValue(h.Window, nint.Zero); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ImeGeometryCannotReturnFromAnOldActivation(int kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var editor = new GeometryControl { Width = 100, Height = 30 };
        h.Panel.Children.Add(editor);
        h.Window.Measure(new Size(300, 150)); h.Window.Arrange(new Rect(0, 0, 300, 150));
        Assert.True(editor.Focus());
        editor.Query = () => { h.Send(PlatformEventType.Deactivate); h.Send(PlatformEventType.Activate); };
        var request = new PlatformImeGeometryRequest(kind, 0, 1, default);
        typeof(Window).GetMethod("HandleMacOSImeGeometryRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.Window, [request]);
        Assert.False(request.Handled); Assert.Same(editor, Keyboard.FocusedElement);
        editor.Query = null;
        request = new PlatformImeGeometryRequest(kind, 0, 1, default);
        typeof(Window).GetMethod("HandleMacOSImeGeometryRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.Window, [request]);
        Assert.True(request.Handled);
    }

    private sealed class GeometryControl : Control, IImeSupport
    {
        internal Action? Query;
        internal GeometryControl() => Focusable = true;
        public Point GetImeCaretPosition() => new(0, 14);
        public void OnImeCompositionStart() { }
        public void OnImeCompositionUpdate(string text, int cursor) { }
        public void OnImeCompositionEnd(string? text) { }
        public bool TryGetImeTextRangeGeometry(int start, int length, bool composition, out ImeTextRangeGeometry geometry)
        { Query?.Invoke(); geometry = new(new Rect(0, 0, 10, 14), start, length); return true; }
        public bool TryGetImeCharacterIndex(Point point, bool composition, out int index)
        { Query?.Invoke(); index = 0; return true; }
    }


    private sealed class ApplicationShell : IDisposable
    {
        private static readonly FieldInfo Current = typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previous = Current.GetValue(null);
        internal readonly Application Instance = (Application)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Application));
        internal bool IsActive => (bool)typeof(Application).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Instance)!;
        internal ApplicationShell()
        {
            // No AppKit initialization on the xUnit worker. Supply only the
            // dispatcher and state consumed by managed activation notifications.
            typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Instance, Dispatcher.CurrentDispatcher);
            typeof(Application).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Instance, new WindowCollection(() => []));
            Instance.Resources = new ResourceDictionary();
            Instance.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Current.SetValue(null, Instance);
        }
        public void Dispose() => Current.SetValue(null, _previous);
    }


    private sealed class Counter(Func<bool> query, Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => query();
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    public static IEnumerable<object[]> WheelRestarts()
    {
        foreach (string callback in new[] { "property", "capture", "pressed" })
            foreach (bool same in new[] { false, true })
                foreach (bool momentum in new[] { false, true }) yield return [callback, same, momentum];
    }

    [Theory, MemberData(nameof(WheelRestarts))]
    public void DeactivationCannotCancelAWheelStartedByItsCleanup(string callback, bool same, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Mouse(h.Target); Assert.True(h.Target.CaptureMouse());
        void Wheel(UIElement target, int time, MouseWheelPhase phase, bool inertia = false)
            => h.Dispatcher.HandleMouseWheel(target.TranslatePoint(new Point(10, 10), h.Window), 0, -11, true,
                MouseButtonStates.AllReleased, ModifierKeys.None, time,
                inertia ? MouseWheelPhase.None : phase, inertia ? phase : MouseWheelPhase.None);
        Wheel(h.Target, 100, MouseWheelPhase.Began);
        var next = same ? h.Target : h.Other; bool restarted = false; int oldCancelled = 0, newCancelled = 0, changed = 0;
        h.Target.PreviewMouseWheel += (_, e) => { if (e.Phase == MouseWheelPhase.Cancelled) { if (restarted) newCancelled++; else oldCancelled++; } };
        if (!same) h.Other.PreviewMouseWheel += (_, e) => { if (e.Phase == MouseWheelPhase.Cancelled) newCancelled++; };
        next.MouseWheel += (_, e) => { if (e.Timestamp == 103) changed++; };
        void Restart()
        {
            if (restarted) return; restarted = true; h.Send(PlatformEventType.Activate);
            Wheel(next, 101, MouseWheelPhase.Began);
            if (momentum) { Wheel(next, 102, MouseWheelPhase.Ended); Wheel(next, 102, MouseWheelPhase.Began, true); }
        }
        h.Window.PropertyChangedInternal += (p, _, value) => { if (callback == "property" && p == Window.IsActiveProperty && Equals(value, false)) Restart(); };
        h.Target.LostMouseCapture += (_, _) => { if (callback == "capture") Restart(); };
        h.Target.PropertyChangedInternal += (p, _, value) => { if (callback == "pressed" && p == UIElement.IsPressedProperty && Equals(value, false)) Restart(); };
        h.Send(PlatformEventType.Deactivate);
        Assert.True(restarted); Assert.True(h.Window.IsActive); Assert.Equal(1, oldCancelled); Assert.Equal(0, newCancelled);
        Wheel(same ? h.Other : h.Target, 103, MouseWheelPhase.Changed, momentum);
        Assert.Equal(1, changed); Assert.Equal(0, newCancelled);
    }

    [Fact]
    public void NestedInputStopsTheRemainingOldCancellationHandlers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int later = 0;
        h.Dispatcher.HandleMouseWheel(new Point(10, 10), 0, -11, true, MouseButtonStates.AllReleased,
            ModifierKeys.None, 100, MouseWheelPhase.Began);
        h.Target.PreviewMouseWheel += (_, e) =>
        {
            if (e.Phase != MouseWheelPhase.Cancelled) return;
            h.Send(PlatformEventType.Activate);
            h.Dispatcher.HandleMouseWheel(new Point(10, 10), 0, -11, true, MouseButtonStates.AllReleased,
                ModifierKeys.None, 101, MouseWheelPhase.Began);
        };
        h.Target.PreviewMouseWheel += (_, e) => { if (e.Phase == MouseWheelPhase.Cancelled) later++; };
        h.Send(PlatformEventType.Deactivate);
        Assert.True(h.Window.IsActive); Assert.Equal(0, later);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ThrowingRetirementStillClearsTheOldMouseAndKeyboardPresses(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Mouse(h.Target); h.Dispatcher.ActivateKeyboardPressedChain(h.Other);
        Assert.True(h.Target.CaptureMouse());
        h.Dispatcher.HandleMouseWheel(new Point(10, 10), 0, -11, true, MouseButtonStates.AllReleased,
            ModifierKeys.None, 100, MouseWheelPhase.Began);
        h.Target.PreviewMouseWheel += (_, e) => { if (wheel && e.Phase == MouseWheelPhase.Cancelled) throw new ApplicationException("cancel"); };
        h.Target.PropertyChangedInternal += (p, _, value) => { if (!wheel && p == UIElement.IsPressedProperty && Equals(value, false)) throw new ApplicationException("pressed"); };
        Assert.ThrowsAny<Exception>(() => h.Send(PlatformEventType.Deactivate));
        Assert.False(h.Window.IsActive); Assert.Null(UIElement.MouseCapturedElement);
        Assert.False(h.Target.IsPressed); Assert.False(h.Other.IsPressed); Assert.False(h.Panel.IsPressed);
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly Canvas Panel = new();
        internal readonly Control Target = new() { Width = 100, Height = 50, Focusable = true, Background = Brushes.White };
        internal readonly Control Other = new() { Width = 100, Height = 50, Focusable = true, Background = Brushes.White };
        internal readonly WindowInputDispatcher Dispatcher;
        internal Host()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); UIElement.MouseCapturedElement?.ReleaseMouseCapture();
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Panel.Children.Add(Target); Panel.Children.Add(Other); Canvas.SetLeft(Other, 150);
            Window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Width = 300, Height = 150, Content = Panel };
            Window.Measure(new Size(300, 150)); Window.Arrange(new Rect(0, 0, 300, 150));
            Dispatcher = (WindowInputDispatcher)typeof(Window).GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
            Assert.True(Target.Focus()); Send(PlatformEventType.Activate);
        }
        internal void Send(PlatformEventType type) => Send(new PlatformEvent { Type = type });
        internal void Send(PlatformEvent evt) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [evt]);
        internal void SendKey() => Send(new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(Key.K), Modifiers = 8 });
        internal void Mouse(UIElement target, bool up = false, bool wheel = false, bool control = false)
        {
            var p = target.TranslatePoint(new Point(10, 10), Window);
            Send(new PlatformEvent { Type = wheel ? PlatformEventType.MouseWheel : up ? PlatformEventType.MouseUp : PlatformEventType.MouseDown,
                MouseX = (int)Math.Round(p.X * Window.DpiScale), MouseY = (int)Math.Round(p.Y * Window.DpiScale),
                Button = 0, ClickCount = 1, Modifiers = control ? 8 : 0, WheelDeltaY = 1,
                HasMouseButtonStates = true, MouseButtons = up || wheel ? 0u : 1u });
        }
        public void Dispose()
        {
            UIElement.MouseCapturedElement?.ReleaseMouseCapture(); Keyboard.ClearFocus();
            Window.Close(); Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
        }
    }
}
