using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSPopupAccessibilityTests
{
    [Theory]
    [InlineData(false, "none", false)] [InlineData(true, "none", false)]
    [InlineData(false, "modal", false)] [InlineData(true, "modal", false)]
    [InlineData(false, "inplace", false)] [InlineData(true, "inplace", false)]
    [InlineData(false, "none", true)] [InlineData(true, "none", true)]
    [InlineData(false, "modal", true)] [InlineData(true, "modal", true)]
    [InlineData(false, "inplace", true)] [InlineData(true, "inplace", true)]
    public void PopupButtonsShareKeyboardAndPhysicalAXScope(bool constrain, string mode, bool cancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, mode);
        var button = cancel ? h.PopupCancel : h.PopupDefault;
        Assert.Equal(h.Id(h.Tree, button), h.WindowButton(h.Tree, cancel));
        h.Key(cancel);
        Assert.Equal(1, button.Clicks);
        Assert.Equal(0, h.OwnerDefault.Clicks + h.OwnerCancel.Clicks + h.DialogDefault.Clicks + h.DialogCancel.Clicks);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void InPlaceFallbackStaysInsideTheOwningDialog(bool constrain, bool cancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "inplace", localButtons: false);
        var button = cancel ? h.DialogCancel : h.DialogDefault;
        Assert.Equal(h.Id(h.OwnerTree, button), h.WindowButton(h.OwnerTree, cancel));
        Assert.Equal(constrain ? h.Id(h.Tree, button) : 0, h.WindowButton(h.Tree, cancel));
        h.Key(cancel);
        Assert.Equal(1, button.Clicks); Assert.Equal(0, h.OwnerDefault.Clicks + h.OwnerCancel.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ADisabledPopupDefaultDoesNotFallBackToTheBackground(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none");
        h.PopupDefault.IsEnabled = false;
        h.Key(cancel: false);
        Assert.Equal(0, h.PopupDefault.Clicks + h.OwnerDefault.Clicks);
        var info = h.Read(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXOperation.Info);
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Enabled));
    }

    [Theory]
    [InlineData(false, "none")] [InlineData(true, "none")]
    [InlineData(false, "modal")] [InlineData(true, "modal")]
    [InlineData(false, "inplace")] [InlineData(true, "inplace")]
    public void PopupFocusIsReadableAndClosedReferencesRetire(bool constrain, string mode)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, mode);
        ulong editor = h.Id(h.Tree, h.Editor);
        Assert.Equal(editor, h.Read(h.Tree, 1, MacOSAXOperation.Focus).ResultId);
        h.Popup.IsOpen = false;
        Assert.False(h.TryRead(h.Tree, editor, MacOSAXOperation.Info, out _));
        Assert.False(h.Action(h.Tree, editor, MacOSAXAction.SetFocus));
    }

    [Theory]
    [InlineData(false, "focus")] [InlineData(true, "focus")]
    [InlineData(false, "value")] [InlineData(true, "value")]
    [InlineData(false, "selection")] [InlineData(true, "selection")]
    [InlineData(false, "press")] [InlineData(true, "press")]
    public void ANewModalScopeDisablesOldPopupActionsButKeepsReadableContent(bool constrain, string action)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "modal");
        ulong editor = h.Id(h.Tree, h.Editor), button = h.Id(h.Tree, h.PopupDefault);
        var next = h.AddModal();
        Assert.True(next.Focus());
        var info = h.Read(h.Tree, editor, MacOSAXOperation.Info);
        Assert.False(info.Flags.HasFlag(MacOSAXFlags.Enabled));
        bool accepted;
        if (action == "focus") accepted = h.Action(h.Tree, editor, MacOSAXAction.SetFocus);
        else if (action == "press") { h.SetHandle(); accepted = h.Action(h.Tree, button, MacOSAXAction.Press); }
        else if (action == "selection")
        {
            var request = new MacOSAXRequest { NodeId = editor, Operation = MacOSAXOperation.SetTextSelection,
                TextStart = 1, TextLength = 1 };
            accepted = h.Tree.Handle(ref request);
        }
        else
        {
            fixed (char* text = "changed")
            {
                var request = new MacOSAXRequest { NodeId = editor, Operation = MacOSAXOperation.SetValue,
                    Text = text, TextCount = 7, TextCapacity = 7 };
                accepted = h.Tree.Handle(ref request);
            }
        }
        Assert.False(accepted);
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal("seed", h.Editor.Text); Assert.Equal(0, h.PopupDefault.Clicks);
        Assert.Same(next, Keyboard.FocusedElement);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ADisposedTreeCannotRunAnAlreadyAcceptedPress(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        h.Tree.Dispose(); Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal(0, h.PopupDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RetargetingRetiresTheAcceptedActionRoute(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        h.Popup.PlacementTarget = h.OtherAnchor;
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal(0, h.PopupDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ANewDialogPresentationRetiresAnAcceptedPress(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "inplace"); h.SetHandle();
        var field = typeof(ContentDialog).GetField("_showTaskSource", Private)!;
        field.SetValue(h.Dialog, new TaskCompletionSource<ContentDialogResult>());
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        field.SetValue(h.Dialog, new TaskCompletionSource<ContentDialogResult>());
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal(0, h.PopupDefault.Clicks);
        field.SetValue(h.Dialog, null);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PointerPassThroughDoesNotDisableAccessibilityActions(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none");
        h.Popup.IsHitTestVisible = false; h.SetHandle();
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal(1, h.PopupDefault.Clicks);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void PopupKeyboardDoesNotActivateItsPlacementButton(bool constrain, bool space)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none", buttonAnchor: true);
        h.KeyCode(space ? 0x20 : 0x0d);
        h.KeyCode(space ? 0x20 : 0x0d, up: true);
        Assert.Equal(0, ((ProbeButton)h.Anchor).Clicks);
        Assert.Equal(space ? 0 : 1, h.PopupDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ProviderLookupCannotRetargetAnAcceptedPress(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        h.PopupDefault.OnPattern = () => h.Popup.PlacementTarget = h.OtherAnchor;
        Assert.False(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(0, h.PopupDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DeferredProviderLookupCannotChangeTheModalScope(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        h.PopupDefault.OnPattern = () => h.AddModal();
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(0, h.PopupDefault.Clicks);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void FinalEnabledProbeCannotReviveARetiredPress(bool constrain, bool dispose)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        h.PopupDefault.OnPattern = () => h.PopupDefault.OnEnabled = () =>
        { if (dispose) h.Tree.Dispose(); else h.Popup.PlacementTarget = h.OtherAnchor; };
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(0, h.PopupDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void HidingAndShowingTheSameWindowRetiresItsAcceptedPress(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none");
        typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(h.Window, (nint)11);
        Assert.True(h.Action(h.OwnerTree, h.Id(h.OwnerTree, h.OwnerDefault), MacOSAXAction.Press));
        var visibility = typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!;
        visibility.Invoke(h.Window, [Visibility.Hidden]); visibility.Invoke(h.Window, [Visibility.Visible]);
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(0, h.OwnerDefault.Clicks);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AccessibilityMayActOnAnUnfocusedElement(bool constrain)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        Assert.True(h.Anchor.Focus());
        Assert.True(h.Action(h.Tree, h.Id(h.Tree, h.PopupDefault), MacOSAXAction.Press));
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(1, h.PopupDefault.Clicks);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void LogicalHiddenOrDisabledAnchorsRetireActions(bool constrain, bool hidden)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(constrain, "none"); h.SetHandle();
        ulong id = h.Id(h.Tree, h.PopupDefault);
        Assert.True(h.Action(h.Tree, id, MacOSAXAction.Press));
        if (hidden) h.Anchor.Visibility = Visibility.Hidden; else h.Anchor.IsEnabled = false;
        Assert.False(h.TryRead(h.Tree, id, MacOSAXOperation.Info, out _));
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Equal(0, h.PopupDefault.Clicks);
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeButton : Button
    {
        internal int Clicks;
        internal Action? OnPattern, OnEnabled;
        protected override void OnClick() => Clicks++;
        protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new ProbePeer(this);
        private sealed class ProbePeer(ProbeButton owner) : Jalium.UI.Automation.Peers.ButtonAutomationPeer(owner)
        {
            protected override bool IsEnabledCore()
            { var action = owner.OnEnabled; owner.OnEnabled = null; action?.Invoke(); return base.IsEnabledCore(); }
            protected override object? GetPatternCore(Jalium.UI.Automation.Peers.PatternInterface pattern)
            { var action = owner.OnPattern; owner.OnPattern = null; action?.Invoke(); return base.GetPatternCore(pattern); }
        }
    }

    private sealed class DisplayedWindow : Window
    {
        internal DisplayedWindow() => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!
            .Invoke(this, [Visibility.Visible]);
    }

    private sealed class DisplayedDialog : ContentDialog
    {
        private readonly UIElement _body;
        internal DisplayedDialog(UIElement body)
        {
            _body = body; Visibility = Visibility.Visible;
            AddVisualChild(body); SyncVisualChildrenCount();
        }
        protected override int VisualChildrenCount => _body == null ? 0 : 1;
        protected override Visual? GetVisualChild(int index) => index == 0 ? _body : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size size) { _body.Measure(size); return _body.DesiredSize; }
        protected override Size ArrangeOverride(Size size) { _body.Arrange(new Rect(new Point(), size)); return size; }
    }

    private sealed class ExternalPopup : Popup
    {
        internal HeadlessHost? Host;
        internal ExternalPopup() { PreferExternalWindow = true; PopupAnimation = PopupAnimation.None; }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) => Host = new(owner, this, root);
        internal override Rect GetMacOSPopupWorkArea() => new(0, 0, 1200, 800);
    }

    private sealed class HeadlessHost(Window owner, Popup popup, PopupRoot root) : MacOSPopupWindowHost(owner, popup, root)
    {
        public override void Show()
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!.Invoke(this, [Visibility.Visible]);
            Measure(new Size(Width, Height)); Arrange(new Rect(0, 0, Width, Height));
        }
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedWindow Window = new() { Width = 500, Height = 400, TitleBarStyle = WindowTitleBarStyle.Native };
        internal readonly Canvas Outer = new() { Width = 500, Height = 400 }, Inside = new() { Width = 400, Height = 250 }, Body = new() { Width = 200, Height = 160 };
        internal readonly ProbeButton OwnerDefault = new() { Content = "owner default", IsDefault = true }, OwnerCancel = new() { Content = "owner cancel", IsCancel = true };
        internal readonly ProbeButton DialogDefault = new() { Content = "dialog default", IsDefault = true }, DialogCancel = new() { Content = "dialog cancel", IsCancel = true };
        internal readonly ProbeButton PopupDefault = new() { Content = "popup default", IsDefault = true }, PopupCancel = new() { Content = "popup cancel", IsCancel = true };
        internal readonly Control Anchor;
        internal readonly Control OtherAnchor = new() { Width = 80, Height = 30, Focusable = true };
        internal readonly TextBox Editor = new() { Text = "seed", Width = 100, Height = 30 };
        internal readonly DisplayedDialog Dialog;
        internal readonly ExternalPopup Popup;
        internal readonly Window Source;
        internal readonly MacOSAccessibilityTree Tree, OwnerTree;
        internal Host(bool constrain, string mode, bool localButtons = true, bool buttonAnchor = false)
        {
            Anchor = buttonAnchor ? new ProbeButton() : new Control();
            Anchor.Width = 80; Anchor.Height = 30; Anchor.Focusable = true;
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Outer.Children.Add(OwnerDefault); Outer.Children.Add(OwnerCancel);
            Inside.Children.Add(Anchor); Inside.Children.Add(OtherAnchor);
            Inside.Children.Add(DialogDefault); Inside.Children.Add(DialogCancel);
            Dialog = new(Inside); Outer.Children.Add(Dialog); Window.Content = Outer;
            if (mode == "modal") Window.ActiveContentDialog = Dialog;
            if (mode == "inplace") Window.ActiveInPlaceDialogs.Add(Dialog);
            Body.Children.Add(Editor);
            if (localButtons) { Body.Children.Add(PopupDefault); Body.Children.Add(PopupCancel); }
            Canvas.SetTop(OwnerCancel, 35); Canvas.SetTop(Dialog, 80);
            Canvas.SetTop(OtherAnchor, 35); Canvas.SetTop(DialogDefault, 70); Canvas.SetTop(DialogCancel, 105);
            Canvas.SetTop(PopupDefault, 40); Canvas.SetTop(PopupCancel, 80);
            Window.Measure(new Size(500, 400)); Window.Arrange(new Rect(0, 0, 500, 400));
            Popup = new ExternalPopup { PlacementTarget = Anchor, Child = Body, ShouldConstrainToRootBounds = constrain, StaysOpen = true };
            Popup.IsOpen = true; Source = (Window?)Popup.Host ?? Window;
            if (constrain) { Window.Measure(new Size(500, 400)); Window.Arrange(new Rect(0, 0, 500, 400)); }
            Assert.True(Editor.Focus()); Tree = new(Source); OwnerTree = new(Window);
        }
        internal ulong Id(MacOSAccessibilityTree tree, UIElement element)
        { Assert.True(tree.TryGetId(element.GetAutomationPeer()!, out var id)); return id; }
        internal MacOSAXRequest Read(MacOSAccessibilityTree tree, ulong node, MacOSAXOperation operation)
        { Assert.True(TryRead(tree, node, operation, out var request)); return request; }
        internal bool TryRead(MacOSAccessibilityTree tree, ulong node, MacOSAXOperation operation, out MacOSAXRequest request)
        { request = new() { NodeId = node, Operation = operation }; return tree.Handle(ref request); }
        internal ulong WindowButton(MacOSAccessibilityTree tree, bool cancel)
        { var request = new MacOSAXRequest { NodeId = 1, Operation = MacOSAXOperation.WindowButton, Index = cancel ? 1 : 0 }; Assert.True(tree.Handle(ref request)); return request.ResultId; }
        internal bool Action(MacOSAccessibilityTree tree, ulong node, MacOSAXAction action)
        { var request = new MacOSAXRequest { NodeId = node, Operation = MacOSAXOperation.Action, Index = (int)action }; return tree.Handle(ref request); }
        internal void Key(bool cancel) => KeyCode(cancel ? 0x1b : 0x0d);
        internal void KeyCode(int code, bool up = false) => typeof(Window).GetMethod("OnPlatformEvent", Private)!.Invoke(Source,
            [new PlatformEvent { Type = up ? PlatformEventType.KeyUp : PlatformEventType.KeyDown, KeyCode = code }]);
        internal void SetHandle() => typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Source, (nint)11);
        internal Control AddModal()
        {
            var target = new Control { Width = 80, Height = 30, Focusable = true };
            var next = new DisplayedDialog(target); Outer.Children.Add(next); Window.ActiveContentDialog = next;
            return target;
        }
        public void Dispose()
        {
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Source, nint.Zero);
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Window, nint.Zero);
            Tree.Dispose(); OwnerTree.Dispose(); Popup.IsOpen = false;
            Window.ActiveContentDialog = null; Window.ActiveInPlaceDialogs.Clear();
            typeof(ContentDialog).GetField("_showTaskSource", Private)!.SetValue(Dialog, null);
            Keyboard.ClearFocus(); Window.Close(); Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }
}
