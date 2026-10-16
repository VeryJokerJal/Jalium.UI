using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSAccessibilityFocusHostTests
{
    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void FocusRequestReturnsBeforeActivatingItsPhysicalWindow(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        h.Accept();
        Assert.Equal(0, h.ActivationCalls);
        h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Target, Keyboard.FocusedElement);
        if (kind == "native") Assert.Equal(0, h.Owner.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void RefusedActivationDoesNotMoveLogicalFocus(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); h.SetActivation(() => false);
        h.Accept(); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Before, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("owner", "focus")] [InlineData("overlay", "focus")] [InlineData("native", "focus")]
    [InlineData("owner", "away-back")] [InlineData("overlay", "away-back")] [InlineData("native", "away-back")]
    [InlineData("owner", "hide-show")] [InlineData("overlay", "hide-show")] [InlineData("native", "hide-show")]
    [InlineData("owner", "dispose")] [InlineData("overlay", "dispose")] [InlineData("native", "dispose")]
    [InlineData("owner", "detach")] [InlineData("overlay", "detach")] [InlineData("native", "detach")]
    [InlineData("owner", "modal")] [InlineData("overlay", "modal")] [InlineData("native", "modal")]
    [InlineData("owner", "provider")] [InlineData("overlay", "provider")] [InlineData("native", "provider")]
    [InlineData("owner", "handle")] [InlineData("overlay", "handle")] [InlineData("native", "handle")]
    [InlineData("owner", "disabled")] [InlineData("overlay", "disabled")] [InlineData("native", "disabled")]
    [InlineData("owner", "deactivate")] [InlineData("overlay", "deactivate")] [InlineData("native", "deactivate")]
    [InlineData("owner", "activation-away-back")] [InlineData("overlay", "activation-away-back")] [InlineData("native", "activation-away-back")]
    public void QueuedFocusCannotOverrideANewerContext(string kind, string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); h.Accept();
        h.Change(change); h.Process();
        Assert.Equal(0, h.ActivationCalls); Assert.NotSame(h.Target, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("owner", "focus")] [InlineData("overlay", "focus")] [InlineData("native", "focus")]
    [InlineData("owner", "away-back")] [InlineData("overlay", "away-back")] [InlineData("native", "away-back")]
    [InlineData("owner", "hide-show")] [InlineData("overlay", "hide-show")] [InlineData("native", "hide-show")]
    [InlineData("owner", "dispose")] [InlineData("overlay", "dispose")] [InlineData("native", "dispose")]
    [InlineData("owner", "modal")] [InlineData("overlay", "modal")] [InlineData("native", "modal")]
    [InlineData("owner", "provider")] [InlineData("overlay", "provider")] [InlineData("native", "provider")]
    [InlineData("owner", "handle")] [InlineData("overlay", "handle")] [InlineData("native", "handle")]
    [InlineData("owner", "disabled")] [InlineData("overlay", "disabled")] [InlineData("native", "disabled")]
    [InlineData("owner", "deactivate")] [InlineData("overlay", "deactivate")] [InlineData("native", "deactivate")]
    public void ActivationCallbacksCannotReviveTheOldFocusRequest(string kind, string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); h.SetActivation(() => { h.Change(change); return true; });
        h.Accept(); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.NotSame(h.Target, Keyboard.FocusedElement);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void ANewerAXFocusRequestSupersedesTheQueuedOne(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); h.Accept(); Assert.True(h.Focus(h.Other)); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Other, Keyboard.FocusedElement);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void ActivationCanQueueANewerFocusRequestWithoutBeingOverwritten(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        h.SetActivation(() => { h.SetActivation(() => true); Assert.True(h.Focus(h.Other)); return true; });
        h.Accept(); h.Process();
        Assert.Equal(2, h.ActivationCalls); Assert.Same(h.Other, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("owner", false)] [InlineData("overlay", false)] [InlineData("native", false)]
    [InlineData("owner", true)] [InlineData("overlay", true)] [InlineData("native", true)]
    public void CustomProviderFocusAwayAndBackRetiresTheOldRequest(string kind, bool duringActivation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        FocusService.Provider = new CustomFocusProvider { Current = h.Before };
        void Change() { FocusService.Focus(h.Other); FocusService.Focus(h.Before); }
        if (duringActivation) h.SetActivation(() => { Change(); return true; });
        h.Accept(); if (!duringActivation) Change(); h.Process();
        Assert.Equal(duringActivation ? 1 : 0, h.ActivationCalls);
        Assert.Same(h.Before, FocusService.FocusedElement);
    }

    [Theory]
    [InlineData("owner", false)] [InlineData("overlay", false)] [InlineData("native", false)]
    [InlineData("owner", true)] [InlineData("overlay", true)] [InlineData("native", true)]
    public void ARepeatedRequestForTheCurrentElementIsStillANewerIntent(string kind, bool duringActivation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        if (duringActivation) h.SetActivation(() => { Assert.True(h.Before.Focus()); return true; });
        h.Accept(); if (!duringActivation) Assert.True(h.Before.Focus()); h.Process();
        Assert.Equal(duringActivation ? 1 : 0, h.ActivationCalls);
        Assert.Same(h.Before, Keyboard.FocusedElement);
    }

    [Fact]
    public void FocusRequestsFromDifferentWindowsShareTheLatestIntent()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var first = new Host("owner"); using var second = new Host("native");
        Assert.True(first.Focus(first.Target)); Assert.True(second.Focus(second.Target)); second.Process();
        Assert.Equal(0, first.ActivationCalls); Assert.Equal(1, second.ActivationCalls);
        Assert.Same(second.Target, Keyboard.FocusedElement);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void ActivationRestorationDoesNotReplaceTheRequestedTarget(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); using var foreign = new Host("owner");
        FocusManager.SetFocusedElement(h.Owner, h.Before);
        h.SetActivation(() =>
        {
            typeof(Window).GetMethod("RestoreMacOSKeyboardFocus", Private)!.Invoke(h.Owner, null);
            return true;
        });
        Assert.True(h.Focus(h.Target)); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void AnAlreadyFocusedElementStillSelectsItsPhysicalHost(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); Assert.True(h.Target.Focus());
        Assert.True(h.Focus(h.Target)); Assert.Equal(0, h.ActivationCalls); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void ProviderActivationExceptionsStayInsideTheAsyncBoundary(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind); h.SetActivation(() => throw new InvalidOperationException("activation probe"));
        h.Accept(); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Before, Keyboard.FocusedElement);
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Visible(Window window, Visibility value = Visibility.Visible) =>
        typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!.Invoke(window, [value]);
    private static void Handle(Window window, nint value) =>
        typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(window, value);

    private sealed class CustomFocusProvider : IFocusProvider
    {
        internal IInputElement? Current;
        public IInputElement? FocusedElement => Current;
        public IInputElement? Focus(IInputElement? element) => Current = element;
        public void ClearFocus() => Current = null;
        public bool MoveFocus(UIElement element, FocusNavigationDirection direction) => false;
    }
    private sealed class OwnerWindow : Window
    {
        internal int ActivationCalls;
        internal Func<bool>? Activation;
        public override bool Activate() { ActivationCalls++; return Activation?.Invoke() ?? true; }
    }
    private sealed class ExternalPopup : Popup
    {
        internal PopupHost? Host;
        internal ExternalPopup() { PreferExternalWindow = true; PopupAnimation = PopupAnimation.None; }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) => Host = new(owner, this, root);
        internal override Rect GetMacOSPopupWorkArea() => new(0, 0, 1200, 800);
    }
    private sealed class PopupHost(Window owner, Popup popup, PopupRoot root) : MacOSPopupWindowHost(owner, popup, root)
    {
        internal int ActivationCalls;
        internal Func<bool>? Activation;
        public override bool Activate() { ActivationCalls++; return Activation?.Invoke() ?? true; }
        public override void Show() { Visible(this); Measure(new Size(Width, Height)); Arrange(new Rect(0, 0, Width, Height)); }
    }
    private sealed class Host : IDisposable
    {
        internal readonly OwnerWindow Owner = new() { Width = 500, Height = 400, TitleBarStyle = WindowTitleBarStyle.Native };
        internal readonly TextBox Before = new() { Text = "before", Width = 100, Height = 30 };
        internal readonly TextBox Target = new() { Text = "target", Width = 100, Height = 30 };
        internal readonly TextBox Other = new() { Text = "other", Width = 100, Height = 30 };
        internal readonly Canvas Content = new() { Width = 500, Height = 400 }, Body = new() { Width = 220, Height = 140 };
        internal readonly ExternalPopup? Popup;
        internal readonly Window Source;
        internal readonly MacOSAccessibilityTree Tree;
        internal int ActivationCalls => Popup?.Host?.ActivationCalls ?? Owner.ActivationCalls;
        internal Host(string kind)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Visible(Owner);
            Content.Children.Add(Before); Canvas.SetTop(Before, 40); Owner.Content = Content;
            if (kind == "owner") { Content.Children.Add(Target); Content.Children.Add(Other); }
            else
            {
                Body.Children.Add(Target); Body.Children.Add(Other); Canvas.SetTop(Other, 40);
                Popup = new() { PlacementTarget = Before, Child = Body, ShouldConstrainToRootBounds = kind == "overlay", StaysOpen = true };
            }
            Owner.Measure(new Size(500, 400)); Owner.Arrange(new Rect(0, 0, 500, 400));
            if (Popup != null) Popup.IsOpen = true;
            Source = (Window?)Popup?.Host ?? Owner;
            if (kind == "overlay") { Owner.Measure(new Size(500, 400)); Owner.Arrange(new Rect(0, 0, 500, 400)); }
            Assert.True(Before.Focus()); Tree = new(Source); Handle(Source, (nint)11);
        }
        internal bool Focus(UIElement target)
        {
            Assert.True(Tree.TryGetId(target.GetAutomationPeer()!, out var id));
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Action, Index = (int)MacOSAXAction.SetFocus };
            return Tree.Handle(ref request);
        }
        internal void Accept() { Assert.True(Focus(Target)); Assert.Same(Before, Keyboard.FocusedElement); }
        internal void Process()
        { Dispatcher.CurrentDispatcher.ProcessQueue(); Dispatcher.CurrentDispatcher.ProcessQueue(); }
        internal void SetActivation(Func<bool> callback) { if (Popup?.Host is { } host) host.Activation = callback; else Owner.Activation = callback; }
        internal void Change(string change)
        {
            if (change == "focus") Other.Focus();
            else if (change == "away-back") { Other.Focus(); Before.Focus(); }
            else if (change == "hide-show") { Visible(Source, Visibility.Hidden); Visible(Source); }
            else if (change == "dispose") Tree.Dispose();
            else if (change == "modal")
            { var dialog = new ContentDialog { Visibility = Visibility.Visible }; Content.Children.Add(dialog); Owner.ActiveContentDialog = dialog; }
            else if (change == "provider") FocusService.Provider = null;
            else if (change == "handle") Handle(Source, (nint)22);
            else if (change == "disabled") Target.IsEnabled = false;
            else if (change is "deactivate" or "activation-away-back")
            {
                var method = typeof(Window).GetMethod("OnPlatformEvent", Private)!;
                method.Invoke(Source, [new PlatformEvent { Type = PlatformEventType.Deactivate }]);
                if (change == "activation-away-back") method.Invoke(Source, [new PlatformEvent { Type = PlatformEventType.Activate }]);
            }
            else if (change == "detach") ((Canvas?)Target.VisualParent)!.Children.Remove(Target);
        }
        public void Dispose()
        {
            Keyboard.Initialize(); Owner.ActiveContentDialog = null;
            Handle(Source, nint.Zero); Handle(Owner, nint.Zero); Tree.Dispose();
            if (Popup != null) Popup.IsOpen = false;
            Keyboard.ClearFocus(); Owner.Close(); Process();
        }
    }
}
