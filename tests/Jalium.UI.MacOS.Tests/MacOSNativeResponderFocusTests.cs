using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void AcceptedNativeResponderLossClearsKeyboardFocusAndKeepsLogicalHistory(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); var target = h.Inner.Target;
        Assert.True(target.Focus()); var scope = FocusManager.GetFocusScope(target);
        int lost = 0; target.LostKeyboardFocus += (_, _) => lost++;
        bool active = h.Inner.Source.IsActive;
        h.Send(false);
        Assert.Null(Keyboard.FocusedElement); Assert.Null(Keyboard.PrimaryDevice.FocusedElement);
        Assert.False(target.IsKeyboardFocused); Assert.False(target.IsKeyboardFocusWithin);
        Assert.Same(target, FocusManager.GetFocusedElement(scope)); Assert.Equal(1, lost);
        Assert.Equal(active, h.Inner.Source.IsActive); Assert.Equal(0, h.Inner.ActivationCalls);
        h.Send(true);
        Assert.Same(target, Keyboard.FocusedElement); Assert.True(target.IsKeyboardFocused);
        Assert.Equal(2, h.Native.Calls); Assert.Equal(0, h.Inner.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void NativeResponderReturnRestoresTheNearestNestedLogicalScope(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind);
        var scope = kind == "owner" ? h.Inner.Content : h.Inner.Body;
        FocusManager.SetIsFocusScope(scope, true);
        Assert.True(h.Inner.Target.Focus()); Assert.Same(scope, FocusManager.GetFocusScope(h.Inner.Target));
        h.Send(false); Assert.Null(Keyboard.FocusedElement); h.Send(true);
        Assert.Same(h.Inner.Target, Keyboard.FocusedElement);
        Assert.Same(h.Inner.Target, FocusManager.GetFocusedElement(scope));
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void NativeResponderReturnUsesTheCurrentLogicalScopeHistory(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); Assert.True(h.Inner.Target.Focus());
        var scope = FocusManager.GetFocusScope(h.Inner.Target);
        h.Send(false); FocusManager.SetFocusedElement(scope, h.Inner.Other); h.Send(true);
        Assert.Same(h.Inner.Other, Keyboard.FocusedElement);
        Assert.False(h.Inner.Target.IsKeyboardFocused);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void DuplicateResponderNotificationsDoNotRepeatKeyboardFocusEvents(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); Assert.True(h.Inner.Target.Focus());
        int lost = 0, got = 0;
        h.Inner.Target.LostKeyboardFocus += (_, _) => lost++;
        h.Inner.Target.GotKeyboardFocus += (_, _) => got++;
        h.Send(false); h.Send(false); h.Send(true); h.Send(true);
        Assert.Equal((1, 1), (lost, got)); Assert.Equal(2, h.Native.Calls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void AnExplicitFocusAfterNativeLossWinsOverTheOldScopeHistory(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); Assert.True(h.Inner.Target.Focus());
        h.Send(false); Assert.True(h.Inner.Other.Focus()); h.Send(true);
        Assert.Same(h.Inner.Other, Keyboard.FocusedElement); Assert.Equal(2, h.Native.Calls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void ExplicitKeyboardClearAfterNativeLossPreventsAutomaticRestoration(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); Assert.True(h.Inner.Target.Focus());
        h.Send(false); Keyboard.ClearFocus(); h.Send(true);
        Assert.Null(Keyboard.FocusedElement); Assert.Equal(1, h.Native.Calls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void AForeignPhysicalWindowKeepsItsKeyboardFocusDuringResponderNotifications(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); using var other = new ResponderHost("owner");
        Assert.True(other.Inner.Target.Focus()); h.Send(false); h.Send(true);
        Assert.Same(other.Inner.Target, Keyboard.FocusedElement); Assert.Equal(0, h.Native.Calls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")] [InlineData("pass-through")]
    public void LostKeyboardFocusHandlersCanChooseANewerTarget(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost(kind); Assert.True(h.Inner.Target.Focus());
        h.Inner.Target.LostKeyboardFocus += (_, _) => h.Inner.Other.Focus();
        h.Send(false); h.Send(true);
        Assert.Same(h.Inner.Other, Keyboard.FocusedElement); Assert.Equal(2, h.Native.Calls);
    }

    [Theory]
    [InlineData("provider")] [InlineData("handle")] [InlineData("disabled")]
    [InlineData("detach")] [InlineData("modal")] [InlineData("hide-show")]
    public void NativeResponderReturnRejectsAnInvalidatedSourceOrTarget(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus());
        h.Send(false); h.Inner.Change(change); h.Send(true);
        Assert.NotSame(h.Inner.Target, FocusService.FocusedElement);
    }

    [Fact]
    public void OrdinaryAcquisitionSupersedesSavedResponderHistoryWithoutRestoringIt()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("native"); Assert.True(h.Inner.Target.Focus()); h.Send(false);
        h.Native.Acquire = () => { h.Send(true); return true; };
        Assert.True(h.Inner.Other.Focus()); Assert.Same(h.Inner.Other, Keyboard.FocusedElement);
        Assert.Equal(2, h.Native.Calls); Assert.Equal(0, h.Inner.ActivationCalls);
    }

    [Fact]
    public void AReentrantResponderReturnDuringLostFocusRestoresThroughTheFocusQueue()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus());
        bool returned = false;
        h.Inner.Target.LostKeyboardFocus += (_, _) => { if (!returned) { returned = true; h.Send(true); } };
        h.Send(false);
        Assert.True(returned); Assert.Same(h.Inner.Target, Keyboard.FocusedElement);
        Assert.True(h.Inner.Target.IsKeyboardFocused); Assert.Equal(2, h.Native.Calls);
    }

    [Fact]
    public void ResponderLossReleasesTheKeyboardPressedVisual()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner");
        var button = new Button(); h.Inner.Content.Children.Add(button); Assert.True(button.Focus());
        var input = (WindowInputDispatcher)typeof(Window).GetField("_inputDispatcher", Private)!.GetValue(h.Inner.Source)!;
        input.ActivateKeyboardPressedChain(button); Assert.True(button.IsPressed);
        h.Send(false);
        Assert.False(button.IsPressed); Assert.Null(Keyboard.FocusedElement);
    }

    [Fact]
    public void ATargetMovedToAnotherLogicalScopeIsNotRestoredThroughOldHistory()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus()); h.Send(false);
        var nextScope = new Canvas(); FocusManager.SetIsFocusScope(nextScope, true);
        h.Inner.Content.Children.Remove(h.Inner.Target); nextScope.Children.Add(h.Inner.Target);
        h.Inner.Content.Children.Add(nextScope); h.Send(true);
        Assert.Null(Keyboard.FocusedElement);
    }

    [Fact]
    public void RestorationPropertyCallbacksCannotOverrideANewerExplicitFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); var target = new ResponderReadTextBox();
        h.Inner.Content.Children.Add(target); Assert.True(target.Focus()); h.Send(false);
        target.OnEnabledRead = () => h.Inner.Other.Focus(); h.Send(true);
        Assert.Same(h.Inner.Other, Keyboard.FocusedElement); Assert.False(target.IsKeyboardFocused);
    }

    [Fact]
    public void WindowTeardownReleasesSavedNativeResponderRestoration()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus()); h.Send(false);
        h.Inner.Owner.Close(); h.Send(true);
        Assert.Null(Keyboard.FocusedElement);
        Assert.Null(typeof(Window).GetField("_macOSResponderFocusRestore", Private)?.GetValue(h.Inner.Source));
    }

    [Fact]
    public void ResponderRoundTripDuringCanExecuteRetiresTheOldNativeKeyPacket()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus());
        int queries = 0, executions = 0; bool handoff = true;
        h.Inner.Target.InputBindings.Add(new KeyBinding(new ResponderCounter(() =>
        { queries++; if (handoff) { handoff = false; h.Send(false); h.Send(true); } return true; }, () => executions++), new KeyGesture(Key.F8, ModifierKeys.Control)));
        h.SendKey();
        Assert.True(queries > 0); Assert.Same(h.Inner.Target, Keyboard.FocusedElement); Assert.Equal(0, executions);
        h.SendKey();
        Assert.Equal(1, executions);
    }

    [Fact]
    public void ResponderRoundTripDuringTextPreviewRetiresTheOldNativeTextPacket()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new ResponderHost("owner"); Assert.True(h.Inner.Target.Focus());
        string before = h.Inner.Target.Text; int previews = 0;
        h.Inner.Target.PreviewTextInput += (_, _) => { if (++previews == 1) { h.Send(false); h.Send(true); } };
        var input = (WindowInputDispatcher)typeof(Window).GetField("_inputDispatcher", Private)!.GetValue(h.Inner.Source)!;
        input.HandleCharInput("x", 42);
        Assert.Equal(1, previews); Assert.Same(h.Inner.Target, Keyboard.FocusedElement); Assert.Equal(before, h.Inner.Target.Text);
        input.HandleCharInput("y", 43); Assert.Contains("y", h.Inner.Target.Text);
    }

    private sealed class ResponderCounter(Func<bool> query, Action execute) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => query();
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class ResponderReadTextBox : TextBox
    {
        internal Action? OnEnabledRead;
        protected override bool IsEnabledCore
        {
            get { var read = OnEnabledRead; OnEnabledRead = null; read?.Invoke(); return base.IsEnabledCore; }
        }
    }

    private sealed class ResponderHost : IDisposable
    {
        internal readonly FocusPlatformHost Platform;
        internal Host Inner => Platform.Inner;
        internal FocusPlatform Native => Platform.Native;
        internal ResponderHost(string kind)
        { Platform = new(kind == "pass-through" ? "native" : kind, hitTest: kind != "pass-through"); }
        internal void Send(bool gained) => typeof(Window).GetMethod("OnPlatformEvent", Private)!.Invoke(Inner.Source,
            [new PlatformEvent { Type = gained ? PlatformEventType.ResponderFocusGained : PlatformEventType.ResponderFocusLost }]);
        internal void SendKey() => typeof(Window).GetMethod("OnPlatformEvent", Private)!.Invoke(Inner.Source,
            [new PlatformEvent { Type = PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(Key.F8), Modifiers = 8 }]);
        public void Dispose() => Platform.Dispose();
    }
}
