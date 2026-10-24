using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSMousePressRetirementTests
{

    public static IEnumerable<object[]> RetiredPresses()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (bool custom in new[] { false, true })
        foreach (string phase in new[] { "preview", "bubble", "pointer-preview", "pointer-bubble" })
        foreach (string mutation in depth == 0 ? new[] { "move", "wheel", "release", "reparent" } : new[] { "move", "wheel", "release", "reparent", "policy" })
        foreach (bool throwing in new[] { false, true }) yield return [depth, custom, phase, mutation, throwing];
        for (int depth = 0; depth < 3; depth++)
        foreach (bool custom in new[] { false, true })
        foreach (string phase in new[] { "preview", "bubble", "pointer-preview", "pointer-bubble" })
            yield return [depth, custom, phase, "exception", true];
    }
    private static RoutedEvent Route(string phase) => phase switch
    {
        "preview" => UIElement.PreviewMouseDownEvent, "bubble" => UIElement.MouseDownEvent,
        "pointer-preview" => PointerEvents.PreviewPointerDownEvent, _ => PointerEvents.PointerDownEvent
    };
    private static Delegate Handler(string phase, Action action) => phase.StartsWith("pointer")
        ? new RoutedEventHandler((_, _) => action()) : new MouseButtonEventHandler((_, _) => action());

    [Theory, MemberData(nameof(RetiredPresses))]
    public void RetiredPressClearsItsStateAndCannotProduceAnOrphanRelease(int depth, bool custom, string phase, string mutation, bool throwing)
    {
        using var h = new Scene(depth, custom); bool entered = false, injecting = false; int late = 0, ups = 0;
        var error = new InvalidOperationException("retired down");
        h.Target.PointerUp += (_, _) => ups++;
        h.Target.AddHandler(Route(phase), Handler(phase, () =>
        {
            if (entered) return; entered = true; injecting = true;
            try
            {
                if (mutation == "exception") { }
                else if (mutation == "reparent") h.Reparent();
                else if (mutation == "policy") h.Policy();
                else h.Packet(mutation == "move" ? PlatformEventType.MouseMove : mutation == "wheel" ? PlatformEventType.MouseWheel : PlatformEventType.MouseUp,
                    h.Target, mutation == "release" ? 0u : 1u);
            }
            finally { injecting = false; }
            if (throwing) throw error;
        }));
        h.Target.AddHandler(Route(phase), Handler(phase, () => { if (!injecting) late++; }));
        if (throwing) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => h.Down(h.Target)));
        else h.Down(h.Target);
        Assert.True(entered); Assert.Equal(0, late); h.AssertRetired();
        int releasedInsidePartialPointerDown = mutation == "release" && phase.StartsWith("pointer") ? 1 : 0;
        Assert.Equal(releasedInsidePartialPointerDown, ups);
        if (mutation != "release") h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.Equal(releasedInsidePartialPointerDown, ups); h.AssertRetired();
        // The suppression belongs to the old button. A new physical Down must pair normally.
        int before = ups; h.Down(h.Target); h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.Equal(before + 1, ups); Assert.False(Pressed(h.Target)); Assert.False(Pressed(h.OldParent));
    }

    public static IEnumerable<object[]> Successors()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (string phase in new[] { "preview", "bubble", "pointer-preview", "pointer-bubble", "pressed", "cleanup" })
        foreach (string destination in new[] { "target", "same", "sibling", "foreign-reuse" })
        foreach (bool throwing in new[] { false, true }) yield return [depth, phase, destination, throwing];
    }
    [Theory, MemberData(nameof(Successors))]
    public void OldPressUnwindingPreservesANewerPress(int depth, string phase, string destination, bool throwing)
    {
        using var h = new Scene(depth, false); bool entered = false; Border next = h.Successor(destination == "foreign-reuse" ? "sibling" : destination);
        var error = new InvalidOperationException("old press"); int successorUps = 0;
        void Change()
        {
            if (entered) return; entered = true;
            if (destination == "foreign-reuse")
            {
                var parent = (Panel)next.VisualParent!; parent.Children.Remove(next);
                ((Panel)h.Target.VisualParent!).Children.Remove(h.Target); parent.Children.Add(h.Target); next = h.Target;
                var physical = Window.GetMacOSPhysicalWindow(next)!; physical.Measure(new Size(440, 300)); physical.Arrange(new Rect(0, 0, 440, 300));
            }
            h.Down(next); Assert.True(next.CaptureMouse()); if (throwing) throw error;
        }
        if (phase is "pressed" or "cleanup")
        {
            h.Target.PropertyChangedInternal += (property, _, value) =>
            { if (property == UIElement.IsPressedProperty && Equals(value, phase == "pressed")) Change(); };
            if (phase == "cleanup") h.Target.MouseDown += (_, _) => { if (!entered) h.Packet(PlatformEventType.MouseMove, h.Target, 1); };
        }
        else h.Target.AddHandler(Route(phase), Handler(phase, Change));
        if (throwing) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => h.Down(h.Target)));
        else h.Down(h.Target);
        Assert.True(entered); Assert.True(Pressed(next)); Assert.Same(next, Mouse.Captured);
        next.PointerUp += (_, _) => successorUps++;
        h.Packet(PlatformEventType.MouseUp, next); Assert.Equal(1, successorUps); Assert.False(Pressed(next)); Assert.Same(next, Mouse.Captured); next.ReleaseMouseCapture();
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void PressedPropertyNestedMoveCannotLeaveAnUnpairedPress(int depth)
    {
        using var h = new Scene(depth, false); bool entered = false; int pointers = 0;
        h.Target.PropertyChangedInternal += (property, _, value) =>
        {
            if (!entered && property == UIElement.IsPressedProperty && Equals(value, true))
            { entered = true; h.Packet(PlatformEventType.MouseMove, h.Target, 1); }
        };
        h.Target.PointerDown += (_, _) => pointers++; h.Target.PointerUp += (_, _) => pointers++;
        h.Down(h.Target); Assert.True(entered); h.AssertRetired(); h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.Equal(0, pointers); h.AssertRetired();
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CleanupFinishesAllAncestorsAndRethrowsTheOriginalHandlerFailure(int depth)
    {
        using var h = new Scene(depth, false); var original = new InvalidOperationException("first failure"); bool armed = true;
        h.Target.PreviewMouseDown += (_, _) => { h.Packet(PlatformEventType.MouseMove, h.Target, 1); throw original; };
        h.Target.PropertyChangedInternal += (property, _, value) =>
        { if (armed && property == UIElement.IsPressedProperty && Equals(value, false)) throw new ArgumentException("cleanup failure"); };
        try { Assert.Same(original, Assert.Throws<InvalidOperationException>(() => h.Down(h.Target))); } finally { armed = false; } h.AssertRetired();
    }

    [Theory] [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public void AnOrdinaryPressKeepsItsPressedStateUntilTheMatchingRelease(int depth, bool handled)
    {
        using var h = new Scene(depth, false); int down = 0, up = 0;
        h.Target.MouseDown += (_, e) => e.Handled = handled;
        h.Target.PointerDown += (_, _) => down++; h.Target.PointerUp += (_, _) => up++;
        h.Down(h.Target); Assert.True(Pressed(h.Target)); Assert.True(Pressed(h.OldParent));
        h.Packet(PlatformEventType.MouseUp, h.Target); Assert.Equal(handled ? 0 : 1, down); Assert.Equal(down, up); Assert.False(Pressed(h.Target)); Assert.False(Pressed(h.OldParent));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RetiringOneButtonSuppressesOrphanReleasesForEveryHeldButton(int depth)
    {
        using var h = new Scene(depth, false); int down = 0, up = 0; bool entered = false;
        h.Target.PointerDown += (_, _) => down++; h.Target.PointerUp += (_, _) => up++;
        h.Target.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && !entered)
            { entered = true; h.Packet(PlatformEventType.MouseMove, h.Target, 3); }
        };
        h.Packet(PlatformEventType.MouseDown, h.Target, 2, 1);
        h.Packet(PlatformEventType.MouseDown, h.Target, 3); Assert.True(entered); h.AssertRetired();
        h.Packet(PlatformEventType.MouseUp, h.Target, 2); h.Packet(PlatformEventType.MouseUp, h.Target, 0, 1);
        Assert.Equal(1, down); Assert.Equal(0, up);
        h.Packet(PlatformEventType.MouseDown, h.Target, 2, 1); h.Packet(PlatformEventType.MouseUp, h.Target, 0, 1);
        Assert.Equal(2, down); Assert.Equal(1, up);
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
