using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPhysicalPopupDragTests
{
    public static IEnumerable<object[]> Surfaces()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (bool custom in new[] { false, true }) yield return [depth, custom];
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void AVisitRoutesEnterOverAndDropThroughItsPresentingSurface(int depth, bool custom)
    {
        using var h = new Scene(depth, custom); var events = new List<string>(); IDataObject? data = null;
        void Seen(string name, DragEventArgs e)
        {
            events.Add(name); data ??= e.Data; Assert.Same(data, e.Data);
            if (name.StartsWith("preview", StringComparison.Ordinal)) e.Effects = DragDropEffects.Copy;
            else Assert.Equal(DragDropEffects.Copy, e.Effects);
        }
        h.Target.PreviewDragEnter += (_, e) => Seen("preview-enter", e);
        h.Target.DragEnter += (_, e) => Seen("enter", e);
        h.Target.PreviewDragOver += (_, e) => Seen("preview-over", e);
        h.Target.DragOver += (_, e) => Seen("over", e);
        h.Target.PreviewDrop += (_, e) => Seen("preview-drop", e);
        h.Target.Drop += (_, e) => Seen("drop", e);
        h.Drag(PlatformEventType.DragEnter); h.Drag(PlatformEventType.DragOver); h.Drag(PlatformEventType.Drop);
        Assert.Equal(new[] { "preview-enter", "enter", "preview-over", "over", "preview-drop", "drop" }, events);
        Assert.Same(h.Owner, Window.GetWindow(h.Target)); Assert.Same(h.Surface, Window.GetMacOSPhysicalWindow(h.Target));
        h.Drag(PlatformEventType.Drop); Assert.Equal(6, events.Count);
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void DropPositionsAreRelativeToTheRequestedElementIncludingTransforms(int depth, bool custom)
    {
        using var h = new Scene(depth, custom); h.Target.RenderTransform = new ScaleTransform(1.5, 1.25);
        int calls = 0;
        void Check(DragEventArgs e)
        {
            calls++; var local = e.GetPosition(h.Target);
            Assert.Equal(5, local.X, 4); Assert.Equal(5, local.Y, 4);
            var root = h.Target.TranslatePoint(new Point(5, 5), h.Surface);
            Assert.Equal(root.X, e.GetPosition(h.Surface).X, 4); Assert.Equal(root.Y, e.GetPosition(null).Y, 4);
            e.Effects = DragDropEffects.Copy;
        }
        h.Target.PreviewDragEnter += (_, e) => Check(e); h.Target.DragEnter += (_, e) => Check(e);
        h.Target.PreviewDragOver += (_, e) => Check(e); h.Target.DragOver += (_, e) => Check(e);
        h.Target.PreviewDrop += (_, e) => Check(e); h.Target.Drop += (_, e) => Check(e);
        h.Drag(PlatformEventType.DragEnter); h.Drag(PlatformEventType.DragOver); h.Drag(PlatformEventType.Drop);
        Assert.Equal(6, calls);
    }

    public static IEnumerable<object[]> Releases()
    {
        foreach (var row in Surfaces())
        foreach (bool throwing in new[] { false, true }) yield return [row[0], row[1], throwing];
    }

    [Theory, MemberData(nameof(Releases))]
    public void CompletionRetiresOnlyItsOwnCaptureAndNeverClicks(int depth, bool custom, bool throwing)
    {
        using var h = new Scene(depth, custom); int lost = 0, ups = 0, cancels = 0; var failure = new InvalidOperationException("capture callback");
        h.Target.PointerCancel += (_, _) => cancels++;
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        Assert.True(Pressed(h.Target)); Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
        MouseEventHandler handler = (_, _) => { lost++; if (throwing) throw failure; };
        h.Target.LostMouseCapture += handler; h.Target.MouseUp += (_, _) => ups++;
        try
        {
            if (throwing) Assert.Same(failure, Assert.Throws<InvalidOperationException>(h.Surface.CompletePlatformDragInput));
            else h.Surface.CompletePlatformDragInput();
            Assert.Equal(1, lost); Assert.Null(Mouse.Captured); Assert.False(Pressed(h.Target));
            Assert.Equal(MouseButtonState.Released, Mouse.LeftButton); Assert.Equal(0, ups);
            Assert.Equal(1, cancels);
        }
        finally { h.Target.LostMouseCapture -= handler; }
    }

    public static IEnumerable<object[]> Successors()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (string destination in new[] { "same", "owner", "sibling" }) yield return [depth, destination];
    }

    public static IEnumerable<object[]> ForeignSurfaces()
    {
        yield return [0, "sibling"];
        foreach (int depth in new[] { 1, 2 })
        foreach (string destination in new[] { "owner", "sibling" }) yield return [depth, destination];
    }

    [Theory, MemberData(nameof(ForeignSurfaces))]
    public void CompletionDoesNotReleaseAnotherSurfaceWithTheSameLogicalOwner(int depth, string destination)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination);
        Assert.NotSame(Window.GetMacOSPhysicalWindow(next), h.Surface);
        h.Down(h.Target); h.Down(next); Assert.True(next.CaptureMouse());
        h.Surface.CompletePlatformDragInput();
        Assert.Same(next, Mouse.Captured); Assert.True(Pressed(next)); Assert.False(Pressed(h.Target));
        Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
    }

    public static IEnumerable<object[]> CallbackSuccessors()
    {
        foreach (var row in Successors())
        foreach (bool throwing in new[] { false, true }) yield return [row[0], row[1], throwing];
    }

    public static IEnumerable<object[]> CaptureCallbackSuccessors()
    {
        foreach (var row in CallbackSuccessors()) yield return row;
        for (int depth = 0; depth < 3; depth++)
        foreach (bool throwing in new[] { false, true }) yield return [depth, "target", throwing];
    }

    [Theory, MemberData(nameof(CaptureCallbackSuccessors))]
    public void LostCaptureCanStartANewPressWithoutOldCompletionClearingIt(int depth, string destination, bool throwing)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination); int callbacks = 0, staleCancels = 0;
        h.Target.PointerCancel += (_, _) => staleCancels++;
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse()); var failure = new InvalidOperationException("successor capture");
        MouseEventHandler handler = (_, _) =>
        {
            callbacks++; h.Down(next); Assert.True(next.CaptureMouse()); if (throwing) throw failure;
        };
        h.Target.LostMouseCapture += handler;
        try
        {
            if (throwing) Assert.Same(failure, Assert.Throws<InvalidOperationException>(h.Surface.CompletePlatformDragInput));
            else h.Surface.CompletePlatformDragInput();
            Assert.Equal(1, callbacks); Assert.Same(next, Mouse.Captured); Assert.True(Pressed(next));
            if (!ReferenceEquals(next, h.Target)) Assert.False(Pressed(h.Target));
            Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
            Assert.Equal(0, staleCancels);
        }
        finally { h.Target.LostMouseCapture -= handler; }
    }

    [Theory, MemberData(nameof(Successors))]
    public void TheTrackingLoopsOldFinallyCannotRetireALaterPress(int depth, string destination)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination);
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        // Bind the parent's direct completion, or the current source token, so
        // both products execute their actual completion behavior after tracking.
        var method = typeof(Window).GetMethod("CapturePlatformDragInputCompletion", BindingFlags.Instance | BindingFlags.NonPublic);
        Action complete = method is null ? h.Surface.CompletePlatformDragInput : (Action)method.Invoke(h.Surface, null)!;
        h.Target.ReleaseMouseCapture(); h.Down(next); Assert.True(next.CaptureMouse());
        complete();
        Assert.Same(next, Mouse.Captured); Assert.True(Pressed(next)); Assert.False(Pressed(h.Target));
        Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void FinishingTrackingInsideMouseDownCannotPublishAPointerDownAfterItReturns(int depth, bool custom)
    {
        using var h = new Scene(depth, custom); int downs = 0, ups = 0;
        h.Target.MouseDown += (_, _) => h.Surface.CompletePlatformDragInput();
        h.Target.PointerDown += (_, _) => downs++; h.Target.MouseUp += (_, _) => ups++;
        h.Down(h.Target);
        Assert.Equal(0, downs); Assert.Equal(0, ups); Assert.False(Pressed(h.Target));
        Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
    }

    [Theory, MemberData(nameof(CallbackSuccessors))]
    public void PointerCancellationCanStartANewPressWithoutClearingItsSession(int depth, string destination, bool throwing)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination); int callbacks = 0;
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse()); var failure = new InvalidOperationException("pointer cancellation");
        RoutedEventHandler handler = (_, _) => { callbacks++; h.Down(next); Assert.True(next.CaptureMouse()); if (throwing) throw failure; };
        h.Target.PointerCancel += handler;
        try
        {
            if (throwing) Assert.Same(failure, Assert.Throws<InvalidOperationException>(h.Surface.CompletePlatformDragInput));
            else h.Surface.CompletePlatformDragInput();
            Assert.Equal(1, callbacks); Assert.Same(next, Mouse.Captured); Assert.True(Pressed(next));
            Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
            h.Surface.CompletePlatformDragInput();
        }
        finally { h.Target.PointerCancel -= handler; }
    }

    [Theory, MemberData(nameof(Successors))]
    public void ANewPressDuringPreviewPointerCancellationStopsTheRetiredBubble(int depth, string destination)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination); int previews = 0, bubbles = 0;
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        RoutedEventHandler handler = (_, _) => { previews++; h.Down(next); Assert.True(next.CaptureMouse()); };
        h.Target.PreviewPointerCancel += handler; h.Target.PointerCancel += (_, _) => bubbles++;
        try
        {
            h.Surface.CompletePlatformDragInput();
            Assert.Equal(1, previews); Assert.Equal(0, bubbles); Assert.Same(next, Mouse.Captured); Assert.True(Pressed(next));
            Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
        }
        finally { h.Target.PreviewPointerCancel -= handler; }
    }

    [Theory, MemberData(nameof(Successors))]
    public void CaptureTransferWithoutANewPressStillPublishesTheOldRelease(int depth, string destination)
    {
        using var h = new Scene(depth, false); var next = h.Successor(destination);
        h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        MouseEventHandler handler = (_, _) => Assert.True(next.CaptureMouse()); h.Target.LostMouseCapture += handler;
        try
        {
            h.Surface.CompletePlatformDragInput();
            Assert.Same(next, Mouse.Captured); Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
            Assert.False(Pressed(h.Target)); Assert.False(Pressed(next));
        }
        finally { h.Target.LostMouseCapture -= handler; }
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void ACompletedTrackingScopeCannotReleaseCaptureAcquiredAfterItsFirstCompletion(int depth, bool custom)
    {
        using var h = new Scene(depth, custom); h.Down(h.Target); Assert.True(h.Target.CaptureMouse());
        var method = typeof(Window).GetMethod("CapturePlatformDragInputCompletion", BindingFlags.Instance | BindingFlags.NonPublic);
        Action complete = method is null ? h.Surface.CompletePlatformDragInput : (Action)method.Invoke(h.Surface, null)!;
        complete(); Assert.Null(Mouse.Captured); Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
        Assert.True(h.Same.CaptureMouse()); complete(); Assert.Same(h.Same, Mouse.Captured);
    }

    public static IEnumerable<object[]> Retirements()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (string phase in new[] { "enter", "over", "drop" })
        foreach (string action in new[] { "detach", "reparent", "disable", "hide", "close", "reopen", "replace" })
            yield return [depth, phase, action];
    }

    [Theory, MemberData(nameof(Retirements))]
    public void PreviewRetirementCannotPublishTheOldBubbleOrRestoreTheOldVisit(int depth, string phase, string action)
    {
        using var h = new Scene(depth, false); bool changed = false; IDataObject? original = null; int bubbles = 0;
        DragEventHandler mutate = (_, e) =>
        {
            original ??= e.Data;
            if (changed) return; changed = true;
            if (action == "detach") h.Body.Children.Remove(h.Target);
            else if (action == "reparent")
            {
                var sibling = h.Successor("sibling"); h.Body.Children.Remove(h.Target);
                Assert.IsType<Canvas>(sibling.VisualParent).Children.Add(h.Target);
            }
            else if (action == "disable") h.Target.IsEnabled = false;
            else if (action == "hide") h.Target.Visibility = Visibility.Hidden;
            else if (action == "close") h.CloseSurface(false);
            else if (action == "reopen") h.CloseSurface(true);
            else h.Drag(PlatformEventType.DragEnter, 2);
            e.Effects = DragDropEffects.Copy;
        };
        var preview = phase == "enter" ? DragDrop.PreviewDragEnterEvent : phase == "over" ? DragDrop.PreviewDragOverEvent : DragDrop.PreviewDropEvent;
        var bubble = phase == "enter" ? DragDrop.DragEnterEvent : phase == "over" ? DragDrop.DragOverEvent : DragDrop.DropEvent;
        h.Target.AddHandler(preview, mutate);
        h.Target.AddHandler(bubble, new DragEventHandler((_, e) => { if (ReferenceEquals(e.Data, original)) bubbles++; }));
        if (phase != "enter") h.Drag(PlatformEventType.DragEnter);
        h.Drag(phase == "enter" ? PlatformEventType.DragEnter : phase == "over" ? PlatformEventType.DragOver : PlatformEventType.Drop);
        Assert.True(changed); Assert.Equal(0, bubbles);
        if (action == "replace") { h.Drag(PlatformEventType.DragOver, 1); h.Drag(PlatformEventType.Drop, 1); Assert.Equal(0, bubbles); }
    }

    private static bool Pressed(UIElement element) => element.GetValue(UIElement.IsPressedProperty) is true;

    private sealed class SurfaceWindow : Window
    {
        internal SurfaceWindow() => Display(this);
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
                var body = new Canvas { Width = 240, Height = 180 }; var next = TargetBox(); body.Children.Add(next);
                Canvas.SetLeft(next, 10); Canvas.SetTop(next, 10);
                var popup = new ExternalPopup { PlacementTarget = anchor, Child = body }; _popups.Add(popup); popup.IsOpen = true;
                Surface = popup.Host!; Body = body; anchor = next;
            }
            Body.Children.Add(Target); Body.Children.Add(Same); Canvas.SetLeft(Target, 10); Canvas.SetTop(Target, 60);
            Canvas.SetLeft(Same, 130); Canvas.SetTop(Same, 60); Layout(Surface);
            Assert.Same(Owner, Window.GetWindow(Target)); Assert.Same(Surface, Window.GetMacOSPhysicalWindow(Target));
        }
        internal Border Successor(string destination)
        {
            if (destination == "target") return Target;
            if (destination == "same") return Same;
            if (destination == "owner") return OwnerTarget;
            if (_sibling is not null) return _sibling;
            _sibling = TargetBox(); var body = new Canvas { Width = 240, Height = 180 }; body.Children.Add(_sibling);
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
