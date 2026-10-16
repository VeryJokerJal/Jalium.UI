using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSExternalPopupHostTests
{
    [Fact]
    public void ExternalPopupKeepsItsPhysicalHostAndLogicalWindowOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        Assert.NotNull(h.Popup.Host);
        Assert.Same(h.Window, Window.GetWindow(h.Target));
        Assert.Same(h.Popup.Host, h.Target.GetWindowHostOrNull());
        Assert.True(InputRoute.IsWithin(h.Target, h.Popup));
        Assert.True(InputRoute.IsWithin(h.Target, h.Window));
        Assert.Contains(h.Popup, h.Window.ActiveExternalPopups);
        Assert.DoesNotContain(h.Root, h.Window.OverlayLayer.Children.Cast<UIElement>());
        Assert.Equal(new Point(120, 150), new Point(h.Popup.Host.Left, h.Popup.Host.Top));
        Assert.Equal((200d, 100d), (h.Popup.Host.Width, h.Popup.Host.Height));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExternalHostUsesPopupParticipationAndPointerPassThrough(bool interactive)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(interactive: interactive);
        var w = h.Popup.Host!;
        var style = (uint)typeof(Window).GetMethod("ApplyMacOSPopupStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, [(uint)0x7f])!;
        Assert.Equal(1u | 128u | 256u | (interactive ? 0u : 1024u), style);
        Assert.False(w.ShowActivated); Assert.False(w.ShowInTaskbar);
        Assert.False(w.IsShowTitleBar); Assert.True(w.AllowsTransparency);
    }

    [Fact]
    public void ConstrainedPopupStaysInTheOwnersOverlay()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain: true);
        Assert.Null(h.Popup.Host); Assert.Contains(h.Root, h.Window.OverlayLayer.Children.Cast<UIElement>());
    }

    [Theory] [InlineData("popup")] [InlineData("anchor")] [InlineData("window")]
    public void NativeKeyboardCommandsReachTheOwningInputRoute(string bindingOwner)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        int ran = 0, raw = 0;
        h.Owner(bindingOwner).InputBindings.Add(new KeyBinding(new Counter(() => ran++),
            new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Target.KeyDown += (_, e) => { Assert.Same(h.Target, e.OriginalSource); raw++; };
        h.Key();
        Assert.Equal((1, 1), (ran, raw));
    }

    [Theory] [InlineData("popup", false)] [InlineData("anchor", false)] [InlineData("window", false)]
    [InlineData("popup", true)] [InlineData("anchor", true)] [InlineData("window", true)]
    public void NativeMouseCommandsReachOwnersWhileRawMouseKeepsThePhysicalRoute(string bindingOwner, bool wheel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int ran = 0, ownerRaw = 0, hostRaw = 0;
        h.Owner(bindingOwner).InputBindings.Add(new MouseBinding(new Counter(() => ran++),
            new MouseGesture(wheel ? MouseAction.WheelClick : MouseAction.LeftClick, ModifierKeys.Control)));
        h.Window.MouseDown += (_, _) => ownerRaw++;
        h.Popup.Host!.MouseDown += (_, _) => hostRaw++;
        h.Mouse(wheel);
        Assert.Equal(1, ran); Assert.Equal(0, ownerRaw);
        // A handled input binding consumes the physical bubbling event.
        Assert.Equal(0, hostRaw);
    }

    [Theory] [InlineData("popup")] [InlineData("host")] [InlineData("destroy")]
    public void ClosingTheExternalHostRestoresThePriorFocusAndRetiresTheSurface(string close)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var host = h.Popup.Host!;
        Assert.True(h.Target.Focus());
        if (close == "popup") h.Popup.IsOpen = false;
        else if (close == "host") host.Close();
        else Send(host, new PlatformEvent { Type = PlatformEventType.Destroyed });
        Assert.False(h.Popup.IsOpen); Assert.Same(h.Anchor, Keyboard.FocusedElement);
        Assert.Empty(h.Window.ActiveExternalPopups); Assert.Null(h.Root.Child);
        Assert.True(host.IsClosedForPlatformTermination);
    }

    [Fact]
    public void NestedExternalPopupsShareTheInputOwnerAndCloseWithTheParent()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var childTarget = new Control { Width = 80, Height = 30, Focusable = true, Background = Brushes.White };
        var child = new ExternalPopup { PlacementTarget = h.Target, Child = childTarget, StaysOpen = true };
        child.IsOpen = true;
        try
        {
            Assert.NotNull(child.Host); Assert.Same(h.Window, child.Host.MacOSInputOwner);
            Assert.Same(h.Window, Window.GetWindow(childTarget));
            Assert.True(childTarget.Focus());
            h.Popup.IsOpen = false;
            Assert.False(child.IsOpen); Assert.Same(h.Anchor, Keyboard.FocusedElement);
        }
        finally { child.IsOpen = false; }
    }

    [Theory] [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)]
    public void ImeGeometryUsesTheReceivingNativeWindowsCoordinates(int kind, bool ownerReceives)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var host = h.Popup.Host!;
        typeof(Window).GetField("_dpiScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, 1d);
        Assert.True(h.Target.Focus());
        Send(host, new PlatformEvent { Type = PlatformEventType.Activate });
        Window receiver = ownerReceives ? h.Window : host;
        var point = ownerReceives ? new Point(60, 120) : new Point(10, 10);
        var request = new PlatformImeGeometryRequest(kind, 0, 1, point);
        Send(receiver, new PlatformEvent { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
        Assert.True(request.Handled);
        if (kind is 0 or 2)
            Assert.Equal(ownerReceives ? new Rect(60, 120, 20, 28) : new Rect(10, 10, 10, 14), request.Geometry.Rectangle);
        else Assert.Equal(new Point(), h.Target.QueryPoint);
    }

    [Fact]
    public void TransferIntoAnExternalPopupDoesNotDeactivateOrDismissTheInputOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(staysOpen: false); int deactivated = 0;
        h.Window.Deactivated += (_, _) => deactivated++;
        Assert.True(h.Target.Focus());
        Send(h.Window, new PlatformEvent { Type = PlatformEventType.Deactivate });
        Send(h.Popup.Host!, new PlatformEvent { Type = PlatformEventType.Activate });
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.True(h.Popup.IsOpen); Assert.True(h.Window.IsActive);
        Assert.Equal(0, deactivated); Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Fact]
    public void LeavingThePopupGroupClosesLightDismissSurfacesOnce()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(staysOpen: false); int deactivated = 0;
        h.Window.Deactivated += (_, _) => deactivated++;
        var host = h.Popup.Host!;
        Send(h.Window, new PlatformEvent { Type = PlatformEventType.Deactivate });
        Send(host, new PlatformEvent { Type = PlatformEventType.Activate });
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.True(h.Popup.IsOpen); Assert.True(h.Window.IsActive);
        Send(host, new PlatformEvent { Type = PlatformEventType.Deactivate });
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.False(h.Popup.IsOpen); Assert.False(h.Window.IsActive);
        Assert.Equal(1, deactivated);
    }

    [Theory] [InlineData("popup")] [InlineData("host")] [InlineData("destroy")]
    public void NativeCloseActivationSeesTheAlreadyRestoredOwnerFocus(string close)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Popup.Host!.PlatformTeardownCompleted += (_, _) => Send(h.Window, new PlatformEvent { Type = PlatformEventType.Activate });
        if (close == "popup") h.Popup.IsOpen = false;
        else if (close == "host") h.Popup.Host.Close();
        else Send(h.Popup.Host, new PlatformEvent { Type = PlatformEventType.Destroyed });
        Assert.Same(h.Anchor, Keyboard.FocusedElement);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AnExternalPopupCanInvokeTheOwnersDefaultButton(bool modal)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: modal); int clicked = 0;
        var button = new Button { IsDefault = true, Width = 80, Height = 30 }; button.Click += (_, _) => clicked++;
        h.Panel.Children.Add(button); h.Layout();
        Assert.True(button.IsVisible, $"button visibility={button.Visibility}, panel={h.Panel.Visibility}/{h.Panel.IsVisible}, dialog={h.Window.ActiveContentDialog?.Visibility}/{h.Window.ActiveContentDialog?.IsVisible}");
        Assert.Same(button, ((IInputDispatcherHost)h.Popup.Host!).FindButton(h.Window.ActiveContentDialog ?? (UIElement)h.Window, b => b.IsDefault));
        h.Key(Key.Enter, control: false);
        Assert.Equal(1, clicked);
    }

    [Fact]
    public void EscapeClosesTheFocusedPopupBeforeTheContainingDialog()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: true, staysOpen: false); int cancel = 0;
        var button = new Button { IsCancel = true, Width = 80, Height = 30 }; button.Click += (_, _) => cancel++;
        h.Panel.Children.Add(button); h.Layout();
        h.Key(Key.Escape, control: false);
        Assert.False(h.Popup.IsOpen); Assert.Equal(0, cancel);
        Assert.NotNull(h.Window.ActiveContentDialog); Assert.Same(h.Anchor, Keyboard.FocusedElement);
        Send(h.Window, new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(Key.Escape) });
        Assert.Equal(1, cancel);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ClickingAPopupClosesUnrelatedSurfacesWithoutClosingItself(bool siblingOverlay)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(staysOpen: false); int clicked = 0;
        Popup sibling = siblingOverlay ? new Popup() : new ExternalPopup();
        sibling.PlacementTarget = h.Anchor; sibling.Child = new Control { Width = 50, Height = 30 };
        sibling.StaysOpen = false; sibling.PopupAnimation = PopupAnimation.None;
        sibling.ShouldConstrainToRootBounds = siblingOverlay; sibling.IsOpen = true;
        try
        {
            h.Target.MouseDown += (_, _) => clicked++;
            h.Mouse(wheel: false);
            Assert.True(h.Popup.IsOpen); Assert.False(sibling.IsOpen); Assert.Equal(1, clicked);
        }
        finally { sibling.IsOpen = false; }
    }

    [Fact]
    public void OwnerPreviewHooksReceiveExternalKeysAndOwnerLocalMousePositions()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int key = 0, mouse = 0, raw = 0;
        h.Window.KeyPreview = () => { key++; return true; };
        h.Window.MousePreview = point => { mouse++; Assert.Equal(new Point(35, 65), point); return true; };
        h.Target.KeyDown += (_, _) => raw++; h.Target.MouseDown += (_, _) => raw++;
        h.Key(); h.Mouse(wheel: false);
        Assert.Equal((1, 1, 0), (key, mouse, raw));
    }

    [Fact]
    public void ABackgroundBindingDoesNotEscapeTheModalThroughAnExternalHost()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: true); int ran = 0;
        h.Window.InputBindings.Add(new KeyBinding(new Counter(() => ran++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key();
        Assert.Equal(0, ran);
    }

    [Fact]
    public void ClosingTheInputOwnerClosesExternalPopupsAndClearsTheirFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Window.Close(); Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.False(h.Popup.IsOpen); Assert.Null(Keyboard.FocusedElement);
        Assert.True(h.Popup.Host!.IsClosedForPlatformTermination);
    }

    [Fact]
    public void FractionalContentSizeRoundsTheHostOutwardAtTheOwnerScale()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popup.IsOpen = false;
        h.Child.Width = 200.25; h.Child.Height = 100.25; h.Popup.IsOpen = true;
        Assert.Equal((200.5, 100.5), (h.Popup.Host!.Width, h.Popup.Host.Height));
    }

    [Fact]
    public void AFailedNativeShowDoesNotLeaveAnOpenPopupOrRegisteredHost()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popup.IsOpen = false; h.Popup.FailShow = true;
        Assert.Throws<InvalidOperationException>(() => h.Popup.IsOpen = true);
        Assert.False(h.Popup.IsOpen); Assert.Empty(h.Window.ActiveExternalPopups);
        Assert.True(h.Popup.Host!.IsClosedForPlatformTermination);
        Assert.Null(h.Child.VisualParent);
    }

    [Fact]
    public void AClosedNativeHostCannotCloseThePopupReopenedByItsClosedHandler()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var old = h.Popup.Host!;
        bool reopened = false;
        h.Popup.Closed += (_, _) => { if (reopened) return; reopened = true; h.Popup.IsOpen = true; };
        h.Popup.IsOpen = false;
        Assert.True(h.Popup.IsOpen); Assert.NotSame(old, h.Popup.Host);
        Send(old, new PlatformEvent { Type = PlatformEventType.Destroyed });
        Assert.True(h.Popup.IsOpen); Assert.Same(h.Window, Window.GetWindow(h.Target));
    }

    [Fact]
    public void ASyntheticNonzeroHandleDoesNotAuthorizeCreatingAnAppKitHost()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var handle = typeof(Window).GetField("<Handle>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        handle.SetValue(h.Window, (nint)0x4201);
        try { Assert.False(h.Window.CanCreateMacOSNativePopup); }
        finally { handle.SetValue(h.Window, nint.Zero); }
    }

    [Fact]
    public void ExternalPopupRetainsTheOwnersTextProperties()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popup.IsOpen = false;
        h.Window.FontSize = 24; h.Window.FontWeight = FontWeights.Bold;
        h.Popup.IsOpen = true;
        Assert.Equal(24, h.Target.FontSize); Assert.Equal(FontWeights.Bold, h.Target.FontWeight);
    }

    [Fact]
    public void RetargetingAcrossNativeHostsKeepsTheLogicalOwnerAndCommandRoute()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var target = new Control { Width = 80, Height = 30, Focusable = true };
        var sibling = new ExternalPopup { PlacementTarget = h.Anchor, Child = target, StaysOpen = true };
        sibling.IsOpen = true;
        try
        {
            h.Popup.PlacementTarget = target; Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.True(h.Popup.IsOpen); Assert.True(InputRoute.IsWithin(h.Target, h.Window));
            Assert.Same(h.Window, h.Popup.Host!.MacOSInputOwner);
            Assert.Equal(180, h.Popup.Host.Top);
            int ran = 0;
            sibling.InputBindings.Add(new KeyBinding(new Counter(() => ran++), new KeyGesture(Key.K, ModifierKeys.Control)));
            h.Key(); Assert.Equal(1, ran);
            sibling.IsOpen = false;
            Assert.False(h.Popup.IsOpen);
        }
        finally { sibling.IsOpen = false; }
    }

    [Fact]
    public void AnExternalPopupMayGrowBeyondTheOwnersClientHeight()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popup.IsOpen = false;
        h.Child.Height = 500; h.Popup.IsOpen = true;
        Assert.Equal(500, h.Popup.Host!.Height);
    }

    private sealed class Counter(Action run) : ICommand
    {
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => run();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class Probe : Control, IImeSupport
    {
        internal Point QueryPoint;
        public Point GetImeCaretPosition() => new(0, 14);
        public void OnImeCompositionStart() { }
        public void OnImeCompositionUpdate(string text, int cursor) { }
        public void OnImeCompositionEnd(string? text) { }
        public bool TryGetImeTextRangeGeometry(int start, int length, bool composition, out ImeTextRangeGeometry geometry)
        { geometry = new(new Rect(0, 0, 10, 14), start, length); return true; }
        public bool TryGetImeCharacterIndex(Point point, bool composition, out int index)
        { QueryPoint = point; index = 0; return true; }
    }

    private sealed class ExternalPopup : Popup
    {
        internal HeadlessHost? Host;
        internal bool FailShow;
        internal ExternalPopup()
        {
            PreferExternalWindow = true; PopupAnimation = PopupAnimation.None;
        }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) =>
            Host = new HeadlessHost(owner, this, root) { FailShow = FailShow };
        internal override Rect GetMacOSPopupWorkArea() => new(0, 0, 1200, 800);
    }

    private sealed class HeadlessHost(Window owner, Popup popup, PopupRoot root) : MacOSPopupWindowHost(owner, popup, root)
    {
        internal bool FailShow;
        public override void Show()
        {
            if (FailShow) throw new InvalidOperationException("Native show failed in the contract host.");
            // Headless contract: no AppKit UI is created on an xUnit worker.
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [Visibility.Visible]);
            Measure(new Size(Width, Height)); Arrange(new Rect(0, 0, Width, Height));
        }
    }

    private sealed class OwnerWindow : Window
    {
        internal Func<bool>? KeyPreview;
        internal Func<Point, bool>? MousePreview;
        internal OwnerWindow() => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [Visibility.Visible]);
        protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => KeyPreview?.Invoke() ?? false;
        protected override bool OnPreviewWindowMouseDown(MouseButton button, Point point, int count) => MousePreview?.Invoke(point) ?? false;
    }

    private sealed class DialogContent : ContentDialog
    {
        private readonly UIElement _child;
        // Expose a displayed logical dialog without invoking ShowAsync on an xUnit worker.
        internal DialogContent(UIElement child) { _child = child; Visibility = Visibility.Visible; AddVisualChild(child); SyncVisualChildrenCount(); }
        protected override int VisualChildrenCount => _child == null ? 0 : 1;
        protected override Visual? GetVisualChild(int index) => index == 0 ? _child : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size available) { _child.Measure(available); return _child.DesiredSize; }
        protected override Size ArrangeOverride(Size size) { _child.Arrange(new Rect(default(Point), size)); return size; }
    }

    private sealed class Host : IDisposable
    {
        internal readonly OwnerWindow Window;
        internal readonly Canvas Panel = new(), Child = new() { Width = 200, Height = 100 };
        internal readonly Control Anchor = new() { Width = 100, Height = 30, Focusable = true };
        internal readonly Probe Target = new() { Width = 80, Height = 30, Focusable = true, Background = Brushes.White };
        internal readonly ExternalPopup Popup;
        internal readonly PopupRoot Root;
        internal Host(bool constrain = false, bool interactive = true, bool staysOpen = true, bool modal = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); UIElement.MouseCapturedElement?.ReleaseMouseCapture();
            Panel.Children.Add(Anchor); Canvas.SetLeft(Anchor, 20); Canvas.SetTop(Anchor, 20);
            Child.Children.Add(Target); Canvas.SetLeft(Target, 10); Canvas.SetTop(Target, 10);
            Window = new OwnerWindow { Width = 400, Height = 250, Left = 100, Top = 100,
                TitleBarStyle = WindowTitleBarStyle.Native, Content = modal ? new DialogContent(Panel) : Panel };
            typeof(Window).GetField("_dpiScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, 2d);
            if (modal) Window.ActiveContentDialog = (ContentDialog)Window.Content!;
            Window.Measure(new Size(400, 250)); Window.Arrange(new Rect(0, 0, 400, 250));
            Assert.True(Anchor.Focus()); Send(Window, new PlatformEvent { Type = PlatformEventType.Activate });
            Popup = new ExternalPopup { PlacementTarget = Anchor, Child = Child, StaysOpen = staysOpen,
                ShouldConstrainToRootBounds = constrain, IsHitTestVisible = interactive };
            Popup.IsOpen = true;
            Root = Assert.IsType<PopupRoot>(Child.VisualParent);
            if (constrain) { Window.Measure(new Size(400, 250)); Window.Arrange(new Rect(0, 0, 400, 250)); }
            if (interactive) Assert.True(Target.Focus());
        }
        internal UIElement Owner(string name) => name == "popup" ? Popup : name == "anchor" ? Anchor : Window;
        internal void Layout() { Window.Measure(new Size(400, 250)); Window.Arrange(new Rect(0, 0, 400, 250)); }
        internal void Key(Jalium.UI.Input.Key key = Jalium.UI.Input.Key.K, bool control = true) => Send(Popup.Host!, new PlatformEvent { Type = PlatformEventType.KeyDown,
            KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = control ? 8 : 0 });
        internal void Mouse(bool wheel)
        {
            var host = Popup.Host!; var point = Target.TranslatePoint(new Point(5, 5), host);
            Send(host, new PlatformEvent { Type = wheel ? PlatformEventType.MouseWheel : PlatformEventType.MouseDown,
                MouseX = (int)(point.X * host.DpiScale), MouseY = (int)(point.Y * host.DpiScale),
                Button = 0, ClickCount = 1, Modifiers = 8, WheelDeltaY = 1,
                HasMouseButtonStates = true, MouseButtons = wheel ? 0u : 1u });
        }
        public void Dispose()
        {
            Popup.IsOpen = false; UIElement.MouseCapturedElement?.ReleaseMouseCapture();
            Window.ActiveContentDialog = null; Keyboard.ClearFocus(); Window.Close(); Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }

    private static void Send(Window window, PlatformEvent e) => typeof(Window)
        .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [e]);
}
