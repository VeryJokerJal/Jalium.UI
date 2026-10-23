using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTabletRouteLifetimeTests
{

    private sealed record Stage(string Name, RoutedEvent Event, int Phase, SystemGesture? Gesture = null, bool Barrel = false);
    private static readonly Stage[] Stages =
    [
        new("enter", UIElement.StylusEnterEvent, 0),
        new("range-preview", UIElement.PreviewStylusInRangeEvent, 0), new("range-bubble", UIElement.StylusInRangeEvent, 0),
        new("hover-enter-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.HoverEnter),
        new("hover-enter-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.HoverEnter),
        new("barrel-down-preview", UIElement.PreviewStylusButtonDownEvent, 0, Barrel: true),
        new("barrel-down-bubble", UIElement.StylusButtonDownEvent, 0, Barrel: true),
        new("tap-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.Tap),
        new("tap-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.Tap),
        new("hold-enter-preview", UIElement.PreviewStylusSystemGestureEvent, 0, SystemGesture.HoldEnter),
        new("hold-enter-bubble", UIElement.StylusSystemGestureEvent, 0, SystemGesture.HoldEnter),
        new("down-preview", UIElement.PreviewStylusDownEvent, 0), new("down-bubble", UIElement.StylusDownEvent, 0),
        new("barrel-up-preview", UIElement.PreviewStylusButtonUpEvent, 1), new("barrel-up-bubble", UIElement.StylusButtonUpEvent, 1),
        new("drag-preview", UIElement.PreviewStylusSystemGestureEvent, 1, SystemGesture.Drag),
        new("drag-bubble", UIElement.StylusSystemGestureEvent, 1, SystemGesture.Drag),
        new("move-preview", UIElement.PreviewStylusMoveEvent, 1), new("move-bubble", UIElement.StylusMoveEvent, 1),
        new("hold-leave-preview", UIElement.PreviewStylusSystemGestureEvent, 2, SystemGesture.HoldLeave),
        new("hold-leave-bubble", UIElement.StylusSystemGestureEvent, 2, SystemGesture.HoldLeave),
        new("up-preview", UIElement.PreviewStylusUpEvent, 2), new("up-bubble", UIElement.StylusUpEvent, 2),
        new("air-preview", UIElement.PreviewStylusInAirMoveEvent, 3), new("air-bubble", UIElement.StylusInAirMoveEvent, 3),
        new("leave", UIElement.StylusLeaveEvent, 5),
        new("out-preview", UIElement.PreviewStylusOutOfRangeEvent, 4), new("out-bubble", UIElement.StylusOutOfRangeEvent, 4),
        new("hover-leave-preview", UIElement.PreviewStylusSystemGestureEvent, 4, SystemGesture.HoverLeave),
        new("hover-leave-bubble", UIElement.StylusSystemGestureEvent, 4, SystemGesture.HoverLeave)
    ];
    public static IEnumerable<object[]> Cases()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (bool threaded in new[] { false, true })
        foreach (var stage in Stages)
        foreach (string mutation in new[] { "lifetime", "replacement", "cancel", "other-pen", "none" })
            yield return [depth, threaded, stage.Name, mutation];
    }
    [Theory, MemberData(nameof(Cases))]
    public void ATabletRouteCannotContinueAfterItsOriginalPacketRetires(int depth, bool threaded, string name, string mutation)
    {
        using var h = new Scene(depth, false); var stage = Stages.Single(s => s.Name == name);
        var input = h.Input(); ((IInputDispatcherHost)h.Surface).RealTimeStylus.UseRealTimeThread = threaded;
        bool alive = true, entered = false; int later = 0, ancestors = 0, mouse = 0, pointers = 0, replacement = 0;
        RoutedEventArgs? saved = null;
        PointerInputData Data(int phase, int time, uint id = 917, bool barrel = false)
        {
            var at = (phase == 5 ? h.Same : h.Target).TranslatePoint(new Point(5, 5), h.Surface);
            bool contact = phase is 0 or 1;
            return h.Surface.BuildPointerInputData(new MacOSTabletSnapshot
            {
                Size = 96, Type = phase == 0 ? 50 : phase == 2 ? 51 : 52, PointerId = id, PointerType = 2,
                X = (float)at.X, Y = (float)at.Y, Flags = (phase == 4 ? 0u : 1u) | (contact ? 2u : 0u) | (barrel ? 32u : 0u),
                Pressure = contact ? .6f : 0, Buttons = contact ? 1u | (barrel ? 8u : 0u) : 0u, TimestampMillis = time
            }.ToPointerEvent());
        }
        void Send(int phase, int time, uint id = 917, bool barrel = false, Func<bool>? current = null, Action? promote = null) =>
            input.HandleMacOSTabletInput(Data(phase, time, id, barrel), phase == 0, phase == 2, time, current ?? (() => true), promote);
        bool Outer(RoutedEventArgs e) => e is StylusEventArgs stylus && stylus.Timestamp == 100 &&
            (stage.Gesture == null || e is StylusSystemGestureEventArgs system && system.SystemGesture == stage.Gesture);
        h.Target.AddHandler(stage.Event, new RoutedEventHandler((_, e) =>
        {
            if (!Outer(e) || entered) return; entered = true; saved = e;
            if (mutation == "lifetime") alive = false;
            else if (mutation == "replacement") Send(0, 101);
            else if (mutation == "cancel") input.HandleMacOSTabletCancel(Data(4, 101), 101);
            else if (mutation == "other-pen") Send(0, 101, 918);
        }));
        h.Target.AddHandler(stage.Event, new RoutedEventHandler((_, e) => { if (Outer(e)) later++; }));
        h.OldParent.AddHandler(stage.Event, new RoutedEventHandler((_, e) => { if (Outer(e)) ancestors++; }));
        void Pointer(PointerEventArgs e) { if (e.Timestamp == 100) pointers++; else if (e.Timestamp == 101) replacement++; }
        h.Target.AddHandler(UIElement.PointerDownEvent, new PointerDownEventHandler((_, e) => Pointer(e)));
        h.Target.AddHandler(UIElement.PointerMoveEvent, new PointerMoveEventHandler((_, e) => Pointer(e)));
        h.Target.AddHandler(UIElement.PointerUpEvent, new PointerUpEventHandler((_, e) => Pointer(e)));
        h.Same.AddHandler(UIElement.PointerMoveEvent, new PointerMoveEventHandler((_, e) => Pointer(e)));
        try
        {
            if (stage.Phase is 1 or 2 or 4 or 5) Send(0, 10, barrel: name.StartsWith("barrel-up"));
            Send(stage.Phase, 100, barrel: stage.Barrel, current: () => alive, promote: stage.Phase == 4 ? null : () => mouse++);
            Assert.True(entered, "stage must be exercised: " + name);
            bool retired = mutation is "lifetime" or "replacement" or "cancel";
            Assert.Equal(retired ? 0 : 1, later);
            // Direct enter/leave events intentionally have no ancestor route.
            if (stage.Event.RoutingStrategy == RoutingStrategy.Bubble) Assert.Equal(retired ? 0 : 1, ancestors);
            Assert.Equal(retired || stage.Phase == 4 ? 0 : 1, mouse);
            Assert.Equal(retired || stage.Phase == 4 ? 0 : 1, pointers);
            if (mutation is "replacement" or "other-pen") Assert.Equal(1, replacement);
            Assert.NotNull(saved); Assert.Null(saved.CommandInputLifetime);
        }
        finally
        {
            input.HandleMacOSTabletCancel(Data(4, 200), 200); input.HandleMacOSTabletCancel(Data(4, 200, 918), 200);
        }
    }

    public static IEnumerable<object[]> CancellationCases()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (bool threaded in new[] { false, true })
        foreach (string stage in new[] { "out-preview", "out-bubble", "hover-leave-preview", "hover-leave-bubble" })
        foreach (string mutation in new[] { "replacement", "finished-replacement", "other-pen", "nested-cancel", "none" })
            yield return [depth, threaded, stage, mutation];
    }
    [Theory, MemberData(nameof(CancellationCases))]
    public void CancellationCannotResumeAfterAReplacementEvenWhenTheReplacementHasEnded(int depth, bool threaded, string name, string mutation)
    {
        using var h = new Scene(depth, false); var stage = Stages.Single(s => s.Name == name); var input = h.Input();
        ((IInputDispatcherHost)h.Surface).RealTimeStylus.UseRealTimeThread = threaded;
        PointerInputData Data(bool contact, int time, uint id = 917)
        {
            var at = h.Target.TranslatePoint(new Point(5, 5), h.Surface);
            return h.Surface.BuildPointerInputData(new MacOSTabletSnapshot { Size = 96, Type = contact ? 50 : 53,
                PointerId = id, PointerType = 2, X = (float)at.X, Y = (float)at.Y, Flags = contact ? 3u : 0u,
                Pressure = contact ? .7f : 0, Buttons = contact ? 1u : 0, TimestampMillis = time }.ToPointerEvent());
        }
        void Down(int time, uint id = 917) => input.HandleMacOSTabletInput(Data(true, time, id), true, false, time, () => true, null);
        bool Outer(RoutedEventArgs e) => e is StylusEventArgs stylus && stylus.Timestamp == 100 &&
            (stage.Gesture == null || e is StylusSystemGestureEventArgs system && system.SystemGesture == stage.Gesture);
        bool entered = false; int later = 0, cancels = 0; RoutedEventArgs? saved = null;
        h.Target.AddHandler(stage.Event, new RoutedEventHandler((_, e) =>
        {
            if (!Outer(e) || entered) return; entered = true; saved = e;
            if (mutation is "replacement" or "finished-replacement")
            {
                Down(101);
                if (mutation == "finished-replacement") input.HandleMacOSTabletCancel(Data(false, 102), 102);
            }
            else if (mutation == "other-pen") Down(101, 918);
            else if (mutation == "nested-cancel") input.HandleMacOSTabletCancel(Data(false, 101), 101);
        }));
        h.Target.AddHandler(stage.Event, new RoutedEventHandler((_, e) => { if (Outer(e)) later++; }));
        h.Target.PointerCancel += (_, e) => { if (((PointerEventArgs)e).Timestamp == 100) cancels++; };
        try
        {
            Down(10); input.HandleMacOSTabletCancel(Data(false, 100), 100);
            Assert.True(entered, "range cancellation must emit " + name);
            bool retired = mutation is "replacement" or "finished-replacement";
            Assert.Equal(retired ? 0 : 1, later); Assert.Equal(retired ? 0 : 1, cancels);
            Assert.NotNull(saved); Assert.Null(saved.CommandInputLifetime);
        }
        finally { input.HandleMacOSTabletCancel(Data(false, 200), 200); input.HandleMacOSTabletCancel(Data(false, 200, 918), 200); }
    }

    private static bool Pressed(UIElement element) => element.GetValue(UIElement.IsPressedProperty) is true;

    private sealed class SurfaceWindow : Window
    {
        internal SurfaceWindow() => Display(this);
        internal bool SwallowRelease;
        protected override bool OnPreviewWindowMouseUp(MouseButton button, Point position) =>
            SwallowRelease || base.OnPreviewWindowMouseUp(button, position);
    }
    private sealed class ExternalPopup : Popup
    {
        internal ModelPopupHost? Host;
        internal ExternalPopup() { PreferExternalWindow = true; PopupAnimation = PopupAnimation.None; StaysOpen = true; }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) => Host = new(owner, this, root);
        internal override Rect GetMacOSPopupWorkArea() => new(-1000, -1000, 4000, 3000);
    }
    private sealed class ModelPopupHost(Window owner, Popup popup, PopupRoot root) : MacOSPopupWindowHost(owner, popup, root)
    {
        public override void Show()
        {
            Display(this); Measure(new Size(Width, Height)); Arrange(new Rect(0, 0, Width, Height));
        }
    }
    private static void Display(Window window) => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Visibility.Visible]);

    private sealed class Scene : IDisposable
    {
        internal readonly SurfaceWindow Owner;
        internal Window Surface;
        internal readonly Border Target = TargetBox(), Same = TargetBox(), OwnerTarget = TargetBox();
        internal Canvas Body;
        internal readonly Canvas OldParent = new() { Width = 220, Height = 140 };
        internal readonly Canvas NextParent = new() { Width = 220, Height = 140 };
        private readonly List<ExternalPopup> _popups = new();
        private readonly Canvas _ownerBody;
        private Border? _sibling;
        internal Scene(int depth, bool custom)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); UIElement.MouseCapturedElement?.ReleaseMouseCapture();
            _ownerBody = Body = new Canvas { Width = 440, Height = 300 };
            _ownerBody.Children.Add(OwnerTarget); Canvas.SetLeft(OwnerTarget, 260); Canvas.SetTop(OwnerTarget, 40);
            Owner = new SurfaceWindow { Width = 440, Height = 300, Left = 40, Top = 40,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = Body };
            Surface = Owner; Layout(Owner); UIElement anchor = OwnerTarget;
            for (int i = 0; i < depth; i++)
            {
                var body = new Canvas { Width = 440, Height = 300 }; var next = TargetBox(); body.Children.Add(next);
                Canvas.SetLeft(next, 10); Canvas.SetTop(next, 10);
                var popup = new ExternalPopup { PlacementTarget = anchor, Child = body }; _popups.Add(popup); popup.IsOpen = true;
                Surface = popup.Host!; Body = body; anchor = next;
            }
            Body.Children.Add(OldParent); Body.Children.Add(NextParent); Canvas.SetLeft(NextParent, 220);
            OldParent.Children.Add(Target); NextParent.Children.Add(Same); Canvas.SetLeft(Target, 10); Canvas.SetTop(Target, 60);
            Canvas.SetLeft(Same, 10); Canvas.SetTop(Same, 5); Layout(Surface);
            Assert.Same(Owner, Window.GetWindow(Target)); Assert.Same(Surface, Window.GetMacOSPhysicalWindow(Target));
        }
        internal Border Successor(string destination)
        {
            if (destination == "target") return Target;
            if (destination == "same") return Same;
            if (destination == "owner") return OwnerTarget;
            if (_sibling is not null) return _sibling;
            _sibling = TargetBox(); var body = new Canvas { Width = 440, Height = 300 }; body.Children.Add(_sibling);
            Canvas.SetLeft(_sibling, 20); Canvas.SetTop(_sibling, 30);
            var popup = new ExternalPopup { PlacementTarget = OwnerTarget, Child = body }; _popups.Add(popup); popup.IsOpen = true;
            Layout(popup.Host!); return _sibling;
        }
        internal void Drag(PlatformEventType type, ulong visit = 1)
        {
            var at = Target.TranslatePoint(new Point(5, 5), Surface);
            NativeDropTarget.ProcessEvent(Surface, new PlatformEvent { Type = type, DragSessionId = visit,
                MouseX = (float)(at.X * Surface.DpiScale), MouseY = (float)(at.Y * Surface.DpiScale),
                DragAllowedEffects = (uint)DragDropEffects.Copy, DragMimeTypes = ["text/plain"], DragKeyStates = 1 });
        }
        internal WindowInputDispatcher Input(Window? window = null) => (WindowInputDispatcher)typeof(Window)
            .GetField("_inputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window ?? Surface)!;
        internal void Reparent()
        {
            if (Target.VisualParent is Panel parent) parent.Children.Remove(Target);
            NextParent.Children.Add(Target); Layout(Surface);
        }
        internal void Policy()
        {
            var popup = _popups.First(value => ReferenceEquals(value.Host, Surface)); popup.StaysOpen = !popup.StaysOpen;
        }
        internal void Packet(PlatformEventType type, UIElement target, uint buttons = 0, int button = 0)
        {
            var window = Window.GetMacOSPhysicalWindow(target)!; var point = target.TranslatePoint(new Point(5, 5), window);
            try { typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,
                [new PlatformEvent { Type = type, MouseX = (float)(point.X * window.DpiScale), MouseY = (float)(point.Y * window.DpiScale),
                    Button = button, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = buttons, WheelDeltaY = 1 }]); }
            catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }
        internal void AssertRetired()
        {
            Assert.False(Pressed(Target)); Assert.False(Pressed(OldParent));
            foreach (string name in new[] { "_activePointerTargets", "_lastPointerPoints", "_gestureTrackers", "_pointerSessionTokens" })
                Assert.False(((System.Collections.IDictionary)typeof(WindowInputDispatcher).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(Input())!).Contains((uint)1), name);
        }
        internal void Down(UIElement target)
        {
            var window = Window.GetMacOSPhysicalWindow(target)!; var point = target.TranslatePoint(new Point(5, 5), window);
            var method = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try { method.Invoke(window, [new PlatformEvent { Type = PlatformEventType.MouseDown,
                MouseX = (float)(point.X * window.DpiScale), MouseY = (float)(point.Y * window.DpiScale),
                Button = 0, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = 1 }]); }
            catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }
        internal void CloseSurface(bool reopen)
        {
            var popup = _popups.FirstOrDefault(value => ReferenceEquals(value.Host, Surface));
            if (popup is null) { Owner.Visibility = Visibility.Hidden; if (reopen) Owner.Visibility = Visibility.Visible; }
            else { popup.IsOpen = false; if (reopen) popup.IsOpen = true; }
        }
        private static Border TargetBox() => new() { Width = 100, Height = 40, AllowDrop = true, Focusable = true, Background = Brushes.White };
        private static void Layout(Window window) { window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); }
        public void Dispose()
        {
            UIElement.MouseCapturedElement?.ReleaseMouseCapture();
            foreach (var popup in _popups.AsEnumerable().Reverse()) { if (popup.Host is { } host) NativeDropTarget.RevokeWindow(host); popup.IsOpen = false; }
            NativeDropTarget.RevokeWindow(Owner); Owner.Close(); Keyboard.ClearFocus();
            Mouse.UpdateState(Mouse.Position, null, MouseButtonStates.AllReleased); Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }
}
