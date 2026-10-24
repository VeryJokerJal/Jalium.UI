using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupMouseCommandRouteTests
{
    [Theory]
    [InlineData("popup", false)] [InlineData("anchor", false)] [InlineData("dialog", false)]
    [InlineData("popup", true)] [InlineData("anchor", true)] [InlineData("dialog", true)]
    public void NativeMouseCommandsReachTheOwnedPopupRoute(string owner, bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter();
        h.Owner(owner).InputBindings.Add(Bind(count, wheel)); h.Mouse(wheel);
        Assert.Equal((1, 1), (count.Queries, count.Executions));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NestedPopupMouseCommandsCanReachTheOuterOwner(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: true); var count = new Counter();
        h.Popups[0].InputBindings.Add(Bind(count, wheel)); h.Mouse(wheel);
        Assert.Equal(1, count.Executions);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AModalPopupCannotInvokeBackgroundWindowMouseBindings(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter();
        h.Window.InputBindings.Add(Bind(count, wheel)); h.Mouse(wheel);
        Assert.Equal((0, 0), (count.Queries, count.Executions));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AWindowBindingOutsideAModalKeepsTheOriginalHitAsItsDefaultTarget(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); var command = Command(); int executions = 0;
        h.Window.InputBindings.Add(Bind(command, wheel));
        h.Window.CommandBindings.Add(new CommandBinding(command, (_, e) =>
        { Assert.Same(h.Target, e.Source); Assert.Same(h.Target, e.OriginalSource); executions++; }, (_, e) => e.CanExecute = true));
        h.Mouse(wheel); Assert.Equal(1, executions);
    }

    [Theory]
    [InlineData("preview-query")] [InlineData("query")]
    [InlineData("preview-execute")] [InlineData("execute")]
    public void MouseCommandEventsDoNotEscapeTheActiveDialog(string phase)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = Command(); int leaked = 0, executed = 0;
        h.Target.InputBindings.Add(Bind(command));
        h.Dialog.CommandBindings.Add(new CommandBinding(command, (_, _) => executed++, (_, e) => e.CanExecute = true));
        if (phase is "query" or "preview-query")
            h.Window.AddHandler(phase == "query" ? CommandManager.CanExecuteEvent : CommandManager.PreviewCanExecuteEvent,
                new CanExecuteRoutedEventHandler((_, _) => leaked++), handledEventsToo: true);
        else h.Window.AddHandler(phase == "execute" ? CommandManager.ExecutedEvent : CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler((_, _) => leaked++), handledEventsToo: true);
        h.Mouse(); Assert.Equal(1, executed); Assert.Equal(0, leaked);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void OnlyProgrammaticCommandsMayUseAnExplicitBackgroundModalTarget(bool programmatic)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = Command(); int executions = 0, queries = 0;
        h.Target.InputBindings.Add(Bind(command, target: h.Background));
        h.Background.CommandBindings.Add(new CommandBinding(command, (_, _) => executions++, (_, e) => { queries++; e.CanExecute = true; }));
        if (programmatic) command.Execute(null, h.Background); else h.Mouse();
        Assert.Equal(programmatic ? (1, 1) : (0, 0), (queries, executions));
    }

    [Fact]
    public void AnExplicitTargetInsideTheDialogStillReceivesTheCommand()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = Command(); int executions = 0;
        h.Target.InputBindings.Add(Bind(command, target: h.Other));
        h.Other.CommandBindings.Add(new CommandBinding(command, (_, e) =>
        { Assert.Same(h.Other, e.OriginalSource); Assert.Equal(42, e.Parameter); executions++; }, (_, e) => e.CanExecute = true));
        h.Mouse(); Assert.Equal(1, executions);
    }

    [Theory]
    [InlineData("retarget", false)] [InlineData("reparent-anchor", false)] [InlineData("hide-anchor", false)]
    [InlineData("disable-anchor", false)] [InlineData("close", false)] [InlineData("reopen", false)]
    [InlineData("focus", false)] [InlineData("reparent-target", false)]
    [InlineData("retarget", true)] [InlineData("reparent-anchor", true)] [InlineData("hide-anchor", true)]
    [InlineData("disable-anchor", true)] [InlineData("reopen", true)] [InlineData("focus", true)]
    public void ACommandQueryCannotExecuteAfterChangingItsInputLifetime(string change, bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter { Query = () => { h.Mutate(change); return true; } };
        h.Target.InputBindings.Add(Bind(count, wheel)); h.Mouse(wheel);
        Assert.Equal(1, count.Queries); Assert.Equal(0, count.Executions);
    }

    [Theory]
    [InlineData("preview")] [InlineData("direct-preview")] [InlineData("bubble")] [InlineData("direct-bubble")]
    public void RetargetingDuringRawMouseRoutingStopsLaterHandlersAndPointerPromotion(string phase)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int later = 0, pointers = 0;
        var routed = phase switch
        {
            "preview" => UIElement.PreviewMouseDownEvent,
            "direct-preview" => UIElement.PreviewMouseLeftButtonDownEvent,
            "bubble" => UIElement.MouseDownEvent,
            _ => UIElement.MouseLeftButtonDownEvent
        };
        h.Target.AddHandler(routed, new MouseButtonEventHandler((_, _) => h.Popups[0].PlacementTarget = h.Other));
        h.Target.AddHandler(routed, new MouseButtonEventHandler((_, _) => later++));
        h.Target.PointerPressed += (_, _) => pointers++;
        h.Mouse(); Assert.Equal((0, 0), (later, pointers));
    }

    [Fact]
    public void AHandledControlPressPrecedesAnOwnerMouseBinding()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter(); h.Target.HandleDown = true;
        h.Anchor.InputBindings.Add(Bind(count)); h.Mouse(); Assert.Equal((0, 0), (count.Queries, count.Executions));
    }

    [Fact]
    public void OrdinaryClickFocusMayChangeBeforeAnAncestorBindingIsMatched()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Other.Focus()); h.Target.FocusOnDown = true;
        var count = new Counter { Query = () => { Assert.Same(h.Target, Keyboard.FocusedElement); return true; } };
        h.Anchor.InputBindings.Add(Bind(count)); h.Mouse(); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void APreviewFocusTransferDoesNotInvalidateThePointerHit()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter();
        h.Target.PreviewMouseDown += (_, _) => Assert.True(h.Other.Focus());
        h.Target.InputBindings.Add(Bind(count)); h.Mouse(); Assert.Equal(1, count.Executions);
    }

    [Fact]
    public void AnOwnerMouseCommandConsumesItsPressAndMatchingPointerRelease()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter(); int downs = 0, ups = 0;
        h.Anchor.InputBindings.Add(Bind(count));
        h.Target.PointerPressed += (_, _) => downs++; h.Target.PointerReleased += (_, _) => ups++;
        h.Mouse(); h.Mouse(up: true);
        Assert.Equal(1, count.Executions); Assert.Equal((0, 0), (downs, ups));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ADeclinedLocalMappingKeepsOrdinaryClicksBeforeTheOwnerBinding(bool continueRouting)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var command = Command(); var ancestor = new Counter(); int pointers = 0, clicks = 0;
        h.Target.InputBindings.Add(Bind(command)); h.Anchor.InputBindings.Add(Bind(ancestor));
        h.Target.CommandBindings.Add(new CommandBinding(command, (_, _) => throw new InvalidOperationException(),
            (_, e) => { e.CanExecute = false; e.ContinueRouting = continueRouting; }));
        h.Target.PointerPressed += (_, _) => pointers++;
        h.Target.MouseDown += (_, _) => clicks++;
        h.Mouse(); Assert.Equal(1, ancestor.Executions); Assert.Equal(0, pointers); Assert.Equal(1, clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ADeclinedVirtualOwnerMayFallThroughToTheNextOwner(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var declined = new Counter { Query = () => false }; var ancestor = new Counter();
        h.Popups[0].InputBindings.Add(Bind(declined, wheel)); h.Anchor.InputBindings.Add(Bind(ancestor, wheel));
        h.Mouse(wheel); Assert.Equal(1, declined.Queries); Assert.Equal(0, declined.Executions); Assert.Equal(1, ancestor.Executions);
    }

    [Fact]
    public void UnhandledRawMouseEventsKeepTheirPhysicalOverlayRoute()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int logical = 0; var seen = new List<string>();
        h.Target.MouseDown += (_, e) => { Assert.Same(h.Target, e.OriginalSource); seen.Add("target"); };
        h.Window.OverlayLayer.MouseDown += (_, _) => seen.Add("overlay"); h.Window.MouseDown += (_, _) => seen.Add("window");
        h.Anchor.MouseDown += (_, _) => logical++; h.Dialog.MouseDown += (_, _) => logical++;
        h.Mouse(); Assert.Equal(["target", "overlay", "window"], seen); Assert.Equal(0, logical);
    }

    [Fact]
    public void AnOldPressCannotSuppressTheReleaseOfAReopenedHost()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int downs = 0, ups = 0; var count = new Counter();
        h.Target.PointerPressed += (_, _) => downs++; h.Target.PointerReleased += (_, _) => ups++;
        count.Query = () =>
        {
            if (count.Queries == 1)
            { h.Mutate("reopen"); h.Target.InputBindings.Clear(); h.Mouse(); }
            return true;
        };
        h.Target.InputBindings.Add(Bind(count)); h.Mouse();
        Assert.True(h.Target.IsPressed); h.Mouse(up: true);
        Assert.Equal((1, 1), (downs, ups)); Assert.Equal(0, count.Executions);
        Assert.False(h.Target.IsPressed);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AnOldReleaseCannotClearANewerPressStartedByItsCallback(bool pointerCallback)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int downs = 0, ups = 0; bool restarted = false;
        h.Target.PointerPressed += (_, _) => downs++;
        void Restart() { if (!restarted) { restarted = true; h.Mouse(); } }
        h.Target.PointerReleased += (_, _) => { ups++; if (pointerCallback) Restart(); };
        h.Target.MouseUp += (_, _) => { if (!pointerCallback) Restart(); };
        h.Mouse(); h.Mouse(up: true);
        Assert.True(restarted); Assert.True(h.Target.IsPressed);
        h.Mouse(up: true); Assert.False(h.Target.IsPressed);
        Assert.Equal(2, downs); Assert.Equal(pointerCallback ? 2 : 1, ups);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RetainedMouseArgumentsReleaseTheNativeInputContext(bool throwing)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); MouseButtonEventArgs? retained = null, direct = null;
        h.Window.PreviewMouseDown += (_, e) => retained = e;
        h.Target.PreviewMouseLeftButtonDown += (_, e) => { direct = e; if (throwing) throw new InvalidOperationException("probe"); };
        if (throwing) Assert.Throws<TargetInvocationException>(() => h.Mouse()); else h.Mouse();
        Assert.NotNull(retained); Assert.NotNull(direct);
        Assert.Null(retained.CommandInputLifetime); Assert.Null(direct.CommandInputLifetime);
        var property = typeof(MouseEventArgs).GetProperty("CommandInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Null(property?.GetValue(retained)); Assert.Null(property?.GetValue(direct));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void MouseUpDoesNotContinueAfterItsNativeWindowOrPopupHostCloses(bool wholeWindow)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int later = 0, ups = 0;
        h.Target.MouseUp += (_, _) => { if (wholeWindow) h.Window.Close(); else h.Popups[0].IsOpen = false; };
        h.Target.MouseUp += (_, _) => later++; h.Target.PointerReleased += (_, _) => ups++;
        h.Mouse(); h.Mouse(up: true); Assert.Equal((0, 0), (later, ups));
    }

    [Fact]
    public void PromotedWheelHandlersStopAfterTheirPopupOwnerChanges()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int later = 0;
        h.Target.AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelChangedEventHandler((_, _) =>
            h.Popups[0].PlacementTarget = h.Other));
        h.Target.AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelChangedEventHandler((_, _) => later++));
        h.Mouse(wheel: true); Assert.Equal(0, later);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DeactivationDuringAQueryRetiresTheOldMouseCommand(bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var count = new Counter
        { Query = () => { h.Send(new PlatformEvent { Type = PlatformEventType.Deactivate }); return true; } };
        h.Target.InputBindings.Add(Bind(count, wheel)); h.Mouse(wheel);
        Assert.Equal(1, count.Queries); Assert.Equal(0, count.Executions);
        Assert.True(h.Popups[0].IsOpen);
    }

    private static RoutedCommand Command() => new(Guid.NewGuid().ToString("N"), typeof(MacOSPopupMouseCommandRouteTests));
    private static MouseBinding Bind(ICommand command, bool wheel = false, UIElement? target = null) =>
        new(command, new MouseGesture(wheel ? MouseAction.WheelClick : MouseAction.LeftClick, ModifierKeys.Control))
        { CommandTarget = target, CommandParameter = 42 };
    private sealed class Counter : ICommand
    {
        internal int Queries, Executions; internal Func<bool>? Query;
        public bool CanExecute(object? p) { Queries++; return Query?.Invoke() ?? true; }
        public void Execute(object? p) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class MouseProbe : Control
    {
        internal bool HandleDown, FocusOnDown;
        protected override void OnMouseDown(MouseButtonEventArgs e)
        { if (FocusOnDown) Focus(); if (HandleDown) e.Handled = true; else base.OnMouseDown(e); }
    }
    private sealed class DialogContent : ContentDialog
    {
        private readonly UIElement _child;
        internal DialogContent(UIElement child) { _child = child; Visibility = Visibility.Visible; AddVisualChild(child); }
        protected override Size MeasureOverride(Size size) { _child.Measure(size); return _child.DesiredSize; }
        protected override Size ArrangeOverride(Size size) { _child.Arrange(new Rect(new Point(), size)); return size; }
    }
    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly Canvas Root = new(), Inside = new(), Replacement = new();
        internal readonly Control Anchor = new() { Focusable = true, Width = 120, Height = 30 }, Other = new() { Focusable = true, Width = 120, Height = 30 };
        internal readonly Control Background = new() { Focusable = true, Width = 100, Height = 30 };
        internal readonly ContentDialog Dialog;
        internal readonly List<Popup> Popups = [];
        internal MouseProbe Target = null!;
        internal Canvas Panel = null!, OtherPanel = null!;
        internal Host(bool modal = true, bool nested = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Jalium.UI.Input.Mouse.Capture(null);
            Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Inside.Width = 500; Inside.Height = 300; Inside.Children.Add(Anchor); Inside.Children.Add(Other); Inside.Children.Add(Replacement);
            Canvas.SetLeft(Anchor, 20); Canvas.SetTop(Anchor, 20); Canvas.SetLeft(Other, 300); Canvas.SetTop(Other, 20);
            Dialog = new DialogContent(Inside) { Width = 500, Height = 300 }; Root.Children.Add(Dialog);
            Root.Children.Add(Background); Canvas.SetTop(Background, 350);
            Window = new DisplayedTestWindow { Width = 500, Height = 450, TitleBarStyle = WindowTitleBarStyle.Native, Content = Root };
            Layout(); if (modal) Window.ActiveContentDialog = Dialog; Assert.True(Other.Focus()); UIElement anchor = Anchor;
            for (int i = 0; i <= (nested ? 1 : 0); i++)
            {
                Panel = new Canvas { Width = 220, Height = 100 }; OtherPanel = new Canvas();
                Target = new MouseProbe { Width = 80, Height = 30, Focusable = true, Background = Brushes.White };
                Panel.Children.Add(Target); Panel.Children.Add(OtherPanel); Canvas.SetLeft(Target, 20); Canvas.SetTop(Target, 20);
                var popup = new Popup { PlacementTarget = anchor, Child = Panel, StaysOpen = true,
                    ShouldConstrainToRootBounds = true, PopupAnimation = PopupAnimation.None };
                Popups.Add(popup); popup.IsOpen = true; Layout(); Assert.IsType<PopupRoot>(Panel.VisualParent); anchor = Target;
            }
            Assert.True(Target.Focus());
        }
        private void Layout() { Window.Measure(new Size(500,450)); Window.Arrange(new Rect(0,0,500,450)); }
        internal UIElement Owner(string name) => name == "popup" ? Popups[0] : name == "anchor" ? Anchor : Dialog;
        internal void Mouse(bool wheel = false, bool up = false)
        {
            var point = Target.TranslatePoint(new Point(10, 10), Window);
            Assert.Same(Target, Window.HitTest(point)?.VisualHit);
            var e = new PlatformEvent { Type = wheel ? PlatformEventType.MouseWheel : up ? PlatformEventType.MouseUp : PlatformEventType.MouseDown,
                MouseX = (int)Math.Round(point.X * Window.DpiScale), MouseY = (int)Math.Round(point.Y * Window.DpiScale),
                Modifiers = 8, Button = 0, ClickCount = 1, WheelDeltaY = 1, HasMouseButtonStates = true, MouseButtons = wheel || up ? 0u : 1u };
            Send(e);
        }
        internal void Send(PlatformEvent e) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [e]);
        internal void Mutate(string change)
        {
            if (change == "retarget") Popups[0].PlacementTarget = Other;
            else if (change == "reparent-anchor") { Inside.Children.Remove(Anchor); Replacement.Children.Add(Anchor); }
            else if (change == "hide-anchor") Anchor.Visibility = Visibility.Collapsed;
            else if (change == "disable-anchor") Anchor.IsEnabled = false;
            else if (change == "focus") Assert.True(Other.Focus());
            else if (change == "reparent-target") { Panel.Children.Remove(Target); OtherPanel.Children.Add(Target); }
            else { Popups[0].IsOpen = false; if (change == "reopen") { Popups[0].IsOpen = true; Layout(); Assert.True(Target.Focus()); } }
        }
        public void Dispose()
        {
            Jalium.UI.Input.Mouse.Capture(null); Jalium.UI.Input.Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            foreach (var popup in Popups.AsEnumerable().Reverse()) popup.IsOpen = false;
            Window.ActiveContentDialog = null; Keyboard.ClearFocus(); Window.Close(); Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }
}
