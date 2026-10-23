using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSMouseReleaseRetirementTests
{
    public static IEnumerable<object[]> StaleReleases()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (bool custom in new[] { false, true })
        foreach (string phase in new[] { "preview", "bubble", "pointer-preview", "pointer-bubble" })
        foreach (string mutation in depth == 0 ? new[] { "reparent" } : new[] { "reparent", "policy" })
        foreach (bool throwing in new[] { false, true }) yield return [depth, custom, phase, mutation, throwing];
    }
    private static RoutedEvent Route(string phase) => phase switch
    {
        "preview" => UIElement.PreviewMouseUpEvent, "bubble" => UIElement.MouseUpEvent,
        "pointer-preview" => PointerEvents.PreviewPointerUpEvent, _ => PointerEvents.PointerUpEvent
    };
    private static Delegate Handler(string phase, Action action) => phase.StartsWith("pointer")
        ? new RoutedEventHandler((_, _) => action()) : new MouseButtonEventHandler((_, _) => action());

    [Theory, MemberData(nameof(StaleReleases))]
    public void AReleasedPressCannotSurviveItsRetiredRoute(int depth, bool custom, string phase, string mutation, bool throwing)
    {
        using var h = new Scene(depth, custom); int reached = 0, late = 0, cancels = 0;
        var error = new InvalidOperationException("released route");
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        h.Target.PointerCancel += (_, _) => cancels++;
        h.Target.AddHandler(Route(phase), Handler(phase, () =>
        {
            reached++; if (mutation == "reparent") h.Reparent(); else h.Policy(); if (throwing) throw error;
        }));
        h.Target.AddHandler(Route(phase), Handler(phase, () => late++));
        if (throwing) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => h.Packet(PlatformEventType.MouseUp, h.Target)));
        else h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.Equal(1, reached); Assert.Equal(0, late); Assert.Equal(1, cancels);
        Assert.Null(Mouse.Captured); Assert.Equal(MouseButtonState.Released, Mouse.LeftButton); h.AssertRetired();
    }

    public static IEnumerable<object[]> NestedPackets()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (string phase in new[] { "preview", "bubble" })
        foreach (int kind in new[] { (int)PlatformEventType.MouseMove, (int)PlatformEventType.MouseWheel, (int)PlatformEventType.MouseUp })
            yield return [depth, phase, kind];
    }
    [Theory, MemberData(nameof(NestedPackets))]
    public void ANewHoverOrWheelCannotResurrectTheReleasedGesture(int depth, string phase, int kind)
    {
        using var h = new Scene(depth, false); bool entered = false; int cancels = 0, ups = 0;
        h.Target.PointerCancel += (_, _) => cancels++; h.Target.PointerUp += (_, _) => ups++;
        h.Target.AddHandler(Route(phase), Handler(phase, () =>
        {
            if (entered) return; entered = true; h.Reparent(); h.Packet((PlatformEventType)kind, h.Target);
        }));
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse()); h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.True(entered); Assert.Equal(kind == (int)PlatformEventType.MouseUp ? 1 : 0, ups);
        Assert.Equal(kind == (int)PlatformEventType.MouseUp ? 0 : 1, cancels);
        Assert.Null(Mouse.Captured); Assert.Equal(MouseButtonState.Released, Mouse.LeftButton); h.AssertRetired();
    }

    public static IEnumerable<object[]> SuccessorCases()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (string callback in new[] { "raw", "capture", "pressed", "cancel" })
        foreach (string destination in new[] { "target", "same", "sibling", "foreign-reuse" })
        foreach (bool throwing in new[] { false, true }) yield return [depth, callback, destination, throwing];
    }
    [Theory, MemberData(nameof(SuccessorCases))]
    public void CleanupPreservesASuccessorEvenWhenItReusesTheElementInAnotherSurface(int depth, string callback, string destination, bool throwing)
    {
        using var h = new Scene(depth, false); bool restarted = false; int staleCancel = 0;
        var sibling = destination is "sibling" or "foreign-reuse" ? h.Successor("sibling") : h.Same;
        UIElement next = destination == "target" || destination == "foreign-reuse" ? h.Target : sibling;
        var error = new InvalidOperationException("successor callback");
        void Restart()
        {
            if (restarted) return; restarted = true;
            if (destination == "foreign-reuse")
            { ((Panel)h.Target.VisualParent!).Children.Remove(h.Target); ((Panel)sibling.VisualParent!).Children.Add(h.Target); }
            h.Target.ReleaseMouseCapture(); h.Down(next); Assert.True(next.CaptureMouse()); if (throwing) throw error;
        }
        h.Owner.SwallowRelease = callback != "raw";
        h.Target.MouseUp += (_, _) => { if (callback == "raw") Restart(); };
        h.Target.LostMouseCapture += (_, _) => { if (callback == "capture") Restart(); };
        h.Target.PropertyChangedInternal += (property, _, value) =>
        { if (callback == "pressed" && property == UIElement.IsPressedProperty && Equals(value, false)) Restart(); };
        h.Target.PointerCancel += (_, _) => { if (restarted) staleCancel++; else if (callback == "cancel") Restart(); };
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        if (throwing) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => h.Packet(PlatformEventType.MouseUp, h.Target)));
        else h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.True(restarted); Assert.True(Pressed(next));
        bool adoptedParent = false;
        for (Visual? node = next; node is not null; node = node.VisualParent) adoptedParent |= ReferenceEquals(node, h.OldParent);
        Assert.Equal(adoptedParent, Pressed(h.OldParent));
        Assert.Same(next, Mouse.Captured); Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton); Assert.Equal(0, staleCancel);
        var input = h.Input(Window.GetMacOSPhysicalWindow(next));
        var active = (System.Collections.IDictionary)typeof(WindowInputDispatcher).GetField("_activePointerTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        Assert.Same(next, active[(uint)1]);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void CleanupPreservesTheFirstFailureAndClearsEveryAncestor(int depth)
    {
        using var h = new Scene(depth, false); var first = new InvalidOperationException("mouse route");
        h.Target.MouseUp += (_, _) => throw first;
        h.Target.LostMouseCapture += (_, _) => throw new ApplicationException("capture observer");
        h.Target.PropertyChangedInternal += (property, _, value) =>
        { if (property == UIElement.IsPressedProperty && Equals(value, false)) throw new ApplicationException("pressed observer"); };
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        Assert.Same(first, Assert.Throws<InvalidOperationException>(() => h.Packet(PlatformEventType.MouseUp, h.Target)));
        Assert.Null(Mouse.Captured); h.AssertRetired();
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void NormalReleaseAlsoClearsAncestorsWhenAPressedObserverThrows(int depth)
    {
        using var h = new Scene(depth, false); var error = new InvalidOperationException("pressed observer"); bool armed = true;
        h.Down(h.Target);
        h.Target.PropertyChangedInternal += (property, _, value) =>
        { if (armed && property == UIElement.IsPressedProperty && Equals(value, false)) { armed = false; throw error; } };
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => h.Packet(PlatformEventType.MouseUp, h.Target)));
        h.AssertRetired();
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ReacquiredCaptureWithoutANewPressRemainsOwned(int depth)
    {
        using var h = new Scene(depth, false); h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        h.Target.MouseUp += (_, _) => { h.Reparent(); h.Target.ReleaseMouseCapture(); Assert.True(h.Target.CaptureMouse()); };
        h.Packet(PlatformEventType.MouseUp, h.Target); Assert.Same(h.Target, Mouse.Captured); h.AssertRetired();
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void RetiringOneReleasePreservesOtherPhysicalButtonsAndSuppressesTheirOrphanPointerUp(int depth)
    {
        using var h = new Scene(depth, false); bool retired = false; int ups = 0;
        h.Target.MouseUp += (_, _) => { if (!retired) { retired = true; h.Reparent(); } };
        h.Target.PointerUp += (_, _) => ups++;
        h.Down(h.Target); h.Packet(PlatformEventType.MouseDown, h.Target, 3, 1); Assert.True(h.Target.CaptureMouse());
        h.Packet(PlatformEventType.MouseUp, h.Target, 2); Assert.Equal(MouseButtonState.Pressed, Mouse.RightButton);
        Assert.Equal(MouseButtonState.Released, Mouse.LeftButton); h.AssertRetired(); Assert.Null(Mouse.Captured);
        h.Packet(PlatformEventType.MouseUp, h.Target, 0, 1); Assert.Equal(0, ups); Assert.Equal(MouseButtonState.Released, Mouse.RightButton);
    }

    public static IEnumerable<object[]> DelayedOrphanReleaseCases()
    {
        for (int depth = 0; depth < 3; depth++)
        foreach (bool custom in new[] { false, true })
        foreach (bool sameElement in new[] { false, true })
        for (int oldButton = 1; oldButton <= 4; oldButton++)
        for (int nextButton = 0; nextButton <= 4; nextButton++)
            if (nextButton != oldButton) yield return [depth, custom, sameElement, oldButton, nextButton];
    }
    [Theory, MemberData(nameof(DelayedOrphanReleaseCases))]
    public void ADelayedOrphanReleaseCannotRemoveTheSuccessorPointerContact(int depth, bool custom, bool sameElement, int oldButton, int nextButton)
    {
        using var h = new Scene(depth, custom); bool retired = false;
        h.Target.MouseUp += (_, e) => { if (!retired && e.ChangedButton == MouseButton.Left) { retired = true; h.Reparent(); } };
        uint oldMask = ButtonMask(oldButton), nextMask = ButtonMask(nextButton);
        h.Down(h.Target); h.Packet(PlatformEventType.MouseDown, h.Target, 1 | oldMask, oldButton);
        h.Packet(PlatformEventType.MouseUp, h.Target, oldMask, 0); h.AssertRetired(); Assert.True(retired);
        var next = sameElement ? h.Target : h.Same; int ups = 0;
        next.PointerUp += (_, _) => ups++;
        h.Packet(PlatformEventType.MouseDown, next, oldMask | nextMask, nextButton); Assert.True(next.CaptureMouse());
        var input = h.Input(Window.GetMacOSPhysicalWindow(next));
        var active = (System.Collections.IDictionary)typeof(WindowInputDispatcher).GetField("_activePointerTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        var points = (System.Collections.IDictionary)typeof(WindowInputDispatcher).GetField("_lastPointerPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        Assert.Same(next, active[(uint)1]); Assert.True(points.Contains((uint)1));
        h.Packet(PlatformEventType.MouseUp, next, nextMask, oldButton);
        Assert.Equal(0, ups); Assert.Same(next, active[(uint)1]); Assert.True(points.Contains((uint)1));
        Assert.Same(next, Mouse.Captured); if (nextButton == 0) Assert.True(Pressed(next));
        Assert.Equal(MouseButtonState.Pressed, ButtonState(nextButton)); Assert.Equal(MouseButtonState.Released, ButtonState(oldButton));
        h.Packet(PlatformEventType.MouseUp, next, 0, nextButton); Assert.Equal(1, ups); Assert.False(active.Contains((uint)1));
    }

    [Theory, InlineData(0, false), InlineData(1, false), InlineData(2, false), InlineData(0, true), InlineData(1, true), InlineData(2, true)]
    public void AHandledAdditionalButtonDoesNotOwnTheExistingPointerContact(int depth, bool move)
    {
        using var h = new Scene(depth, false); int ups = 0;
        h.Target.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Right) e.Handled = true; };
        h.Target.PointerUp += (_, _) => ups++;
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        var active = (System.Collections.IDictionary)typeof(WindowInputDispatcher).GetField("_activePointerTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(h.Input())!;
        Assert.Same(h.Target, active[(uint)1]);
        h.Packet(PlatformEventType.MouseDown, h.Target, 3, 1);
        if (move) h.Packet(PlatformEventType.MouseMove, h.Target, 3);
        h.Packet(PlatformEventType.MouseUp, h.Target, 1, 1);
        Assert.Equal(0, ups); Assert.Same(h.Target, active[(uint)1]); Assert.Same(h.Target, Mouse.Captured);
        h.Packet(PlatformEventType.MouseUp, h.Target, 0, 0); Assert.Equal(1, ups); Assert.False(active.Contains((uint)1));
    }

    [Theory, InlineData(0, false), InlineData(1, false), InlineData(2, false), InlineData(0, true), InlineData(1, true), InlineData(2, true)]
    public void ACanceledPressStillCleansItsOwnContactOnMatchingRelease(int depth, bool move)
    {
        using var h = new Scene(depth, false); int cancels = 0, ups = 0;
        h.Target.MouseDown += (_, e) => e.Cancel = true;
        h.Target.PointerCancel += (_, _) => cancels++; h.Target.PointerUp += (_, _) => ups++;
        h.Down(h.Target); Assert.Equal(1, cancels);
        if (move) h.Packet(PlatformEventType.MouseMove, h.Target, 1);
        h.Packet(PlatformEventType.MouseUp, h.Target); Assert.Equal(0, ups); h.AssertRetired();
    }

    [Theory, InlineData(0, false), InlineData(1, false), InlineData(2, false), InlineData(0, true), InlineData(1, true), InlineData(2, true)]
    public void AHandledPressCleansHoverStateAfterAnEarlierContactEnds(int depth, bool earlierContact)
    {
        using var h = new Scene(depth, false); int downs = 0, ups = 0;
        h.Target.PointerDown += (_, _) => downs++; h.Target.PointerUp += (_, _) => ups++;
        if (earlierContact) { h.Down(h.Target); h.Packet(PlatformEventType.MouseUp, h.Target); }
        int expected = earlierContact ? 1 : 0;
        h.Target.MouseDown += (_, e) => e.Handled = true;
        h.Down(h.Target); h.Packet(PlatformEventType.MouseMove, h.Target, 1);
        h.Packet(PlatformEventType.MouseUp, h.Target);
        Assert.Equal((expected, expected), (downs, ups)); h.AssertRetired();
    }

    private static uint ButtonMask(int button) => 1u << button;
    private static MouseButtonState ButtonState(int button) => button switch
    { 0 => Mouse.LeftButton, 1 => Mouse.RightButton, 2 => Mouse.MiddleButton, 3 => Mouse.XButton1, _ => Mouse.XButton2 };

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
