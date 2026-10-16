using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void OrdinaryFocusAcquiresThePhysicalSurfaceBeforeChangingManagedFocus(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new FocusPlatformHost(kind); var h = fixture.Inner;
        fixture.Native.Acquire = () => { Assert.Same(h.Before, Keyboard.FocusedElement); return true; };
        Assert.True(h.Target.Focus()); Assert.Same(h.Target, Keyboard.FocusedElement);
        Assert.Equal(1, fixture.Native.Calls); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void RepeatedOrdinaryFocusReacquiresTheSurfaceWithoutRepeatingFocusEvents(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost(kind); int events = 0;
        f.Inner.Target.GotKeyboardFocus += (_, _) => events++;
        Assert.True(f.Inner.Target.Focus()); Assert.True(f.Inner.Target.Focus());
        Assert.Equal(2, f.Native.Calls); Assert.Equal(1, events); Assert.Equal(0, f.Inner.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void RefusedNativeAcquisitionLeavesManagedFocusAndEventsUntouched(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost(kind); int events = 0; f.Native.Accepted = false;
        f.Inner.Target.GotKeyboardFocus += (_, _) => events++;
        Assert.False(f.Inner.Target.Focus()); Assert.Same(f.Inner.Before, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls); Assert.Equal(0, events);
    }

    [Theory]
    [InlineData("owner", "keyboard")] [InlineData("overlay", "keyboard")] [InlineData("native", "keyboard")]
    [InlineData("owner", "service")] [InlineData("overlay", "service")] [InlineData("native", "service")]
    public void DirectKeyboardAndServiceFocusUseTheSamePhysicalAcquisition(string kind, string entry)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost(kind); var target = f.Inner.Target;
        var result = entry == "keyboard" ? Keyboard.Focus(target) : FocusService.Focus(target);
        Assert.Same(target, result); Assert.Same(target, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls); Assert.Equal(0, f.Inner.ActivationCalls);
    }

    [Theory]
    [InlineData("focus")] [InlineData("provider")] [InlineData("hide-show")] [InlineData("modal")]
    [InlineData("handle")] [InlineData("disabled")] [InlineData("detach")] [InlineData("deactivate")]
    public void NativeAcquisitionCallbacksCannotApplyAnObsoleteManagedFocus(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        f.Native.Acquire = () => { f.Inner.Change(change); return true; };
        Assert.False(f.Inner.Target.Focus()); Assert.NotSame(f.Inner.Target, Keyboard.FocusedElement);
        Assert.Equal(change == "focus" ? 2 : 1, f.Native.Calls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void AcquiringTheRequestedKeyWindowAcceptsItsOwnExpectedActivation(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost(kind);
        f.Native.Acquire = () => { f.Inner.Change("activation-away-back"); return true; };
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Target, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls); Assert.Equal(0, f.Inner.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void OrdinaryAcquisitionSuppressesDefaultRestorationOnlyForItsCall(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost(kind); using var foreign = new Host("owner");
        FocusManager.SetFocusedElement(f.Inner.Owner, f.Inner.Before);
        f.Native.Acquire = () =>
        { typeof(Window).GetMethod("RestoreMacOSKeyboardFocus", Private)!.Invoke(f.Inner.Source, null); return true; };
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Target, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls); Assert.Equal(0, AcquisitionDepth(f.Inner.Owner));
    }

    [Fact]
    public void AcquiringOrdinaryFocusRestoresItsGuardAfterAnException()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        f.Native.Acquire = () => throw new InvalidOperationException("owned focus probe");
        Assert.Throws<InvalidOperationException>(() => f.Inner.Target.Focus());
        Assert.Same(f.Inner.Before, Keyboard.FocusedElement); Assert.Equal(0, AcquisitionDepth(f.Inner.Owner));
        Assert.True(f.Inner.Target.Focus()); Assert.Equal(2, f.Native.Calls);
    }

    [Fact]
    public void FocusRaisedFromAGotFocusHandlerKeepsItsFinalTargetAndNativeSurface()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        f.Inner.Target.GotKeyboardFocus += (_, _) => f.Inner.Other.Focus();
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Other, Keyboard.FocusedElement);
        Assert.Equal(2, f.Native.Calls); Assert.Equal(0, f.Inner.ActivationCalls);
    }

    [Fact]
    public void OrdinaryFocusCannotEscapeTheCurrentModalScope()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native"); f.Inner.Change("modal");
        Assert.False(f.Inner.Target.Focus()); Assert.Same(f.Inner.Before, Keyboard.FocusedElement);
        Assert.Equal(0, f.Native.Calls);
    }

    [Fact]
    public void PointerPassThroughDoesNotPreventOrdinaryKeyboardAcquisition()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native", hitTest: false);
        Assert.True(f.Inner.Target.Focus()); Assert.Equal(1, f.Native.Calls);
        Assert.False(f.Inner.Source.IsHitTestVisible); Assert.Equal(0, f.Inner.ActivationCalls);
    }

    private static int AcquisitionDepth(Window window) =>
        (int)typeof(Window).GetField("_macOSKeyboardFocusAcquisitionDepth", Private)!.GetValue(window)!;

    [Fact]
    public void CustomFocusProvidersKeepOwnershipOfTheirNativeFocusPolicy()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        var provider = new CustomFocusProvider { Current = f.Inner.Before }; FocusService.Provider = provider;
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Target, provider.Current);
        Assert.Equal(0, f.Native.Calls);
    }

    [Fact]
    public void ARepeatedCurrentRequestReplacesAnOlderQueuedFocusRequest()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        f.Inner.Target.GotKeyboardFocus += (_, _) =>
        { Assert.True(f.Inner.Other.Focus()); Assert.True(f.Inner.Target.Focus()); };
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Target, Keyboard.FocusedElement);
        Assert.Equal(2, f.Native.Calls);
    }

    [Fact]
    public void QueuedFocusRechecksModalScopeAtThePointOfAcquisition()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        f.Inner.Target.GotKeyboardFocus += (_, _) => { f.Inner.Other.Focus(); f.Inner.Change("modal"); };
        Assert.True(f.Inner.Target.Focus()); Assert.NotSame(f.Inner.Other, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls);
    }

    [Fact]
    public void AnEnabledGetterCannotReviveAnOlderKeyboardFocusRequest()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        var target = new NativeFocusEnabledProbe { Focusable = true }; f.Inner.Body.Children.Add(target);
        target.Read = () => f.Inner.Other.Focus();
        Assert.Null(Keyboard.Focus(target)); Assert.Same(f.Inner.Other, Keyboard.FocusedElement);
        Assert.Equal(1, f.Native.Calls);
    }

    [Fact]
    public void ANewRequestDuringQueuedNativeAcquisitionSupersedesTheOldQueueItem()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native"); int otherEvents = 0;
        f.Inner.Other.GotKeyboardFocus += (_, _) => otherEvents++;
        f.Inner.Target.GotKeyboardFocus += (_, _) =>
        {
            f.Native.Acquire = () => { f.Inner.Before.Focus(); return true; };
            f.Inner.Other.Focus();
        };
        Assert.True(f.Inner.Target.Focus()); Assert.Same(f.Inner.Before, Keyboard.FocusedElement);
        // Before belongs to the renderer-neutral owner; only the popup's
        // initial and retired queued acquisitions reach this platform spy.
        Assert.Equal(0, otherEvents); Assert.Equal(2, f.Native.Calls);
    }

    [Fact]
    public void ForeignThreadFocusCannotAcquireOrChangeTheNativeSurface()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native");
        bool accepted = true; Exception? error = null; int threadId = 0;
        var thread = new Thread(() =>
        {
            threadId = Environment.CurrentManagedThreadId;
            try { accepted = f.Inner.Target.Focus(); } catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, threadId); Assert.Null(error);
        Assert.False(accepted); Assert.Same(f.Inner.Before, Keyboard.FocusedElement); Assert.Equal(0, f.Native.Calls);
    }

    private sealed class NativeFocusEnabledProbe : Control
    {
        internal Action? Read;
        protected override bool IsEnabledCore
        { get { var read = Read; Read = null; read?.Invoke(); return true; } }
    }

    [Fact]
    public void FailedNativeAcquisitionDoesNotLeaveAQueueForTheNextFocusCall()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FocusPlatformHost("native"); bool handled = false;
        f.Inner.Target.GotKeyboardFocus += (_, _) =>
        {
            if (handled) return; handled = true;
            f.Native.Acquire = () =>
            { f.Inner.Target.Focus(); throw new InvalidOperationException("queued focus probe"); };
            f.Inner.Other.Focus();
        };
        Assert.Throws<InvalidOperationException>(() => f.Inner.Target.Focus());
        Assert.Equal(0, AcquisitionDepth(f.Inner.Owner));
        Assert.True(f.Inner.Other.Focus()); Assert.Same(f.Inner.Other, Keyboard.FocusedElement);
        Assert.Equal(3, f.Native.Calls);
    }

    private sealed class FocusPlatformHost : IDisposable
    {
        internal readonly Host Inner;
        internal readonly FocusPlatform Native = new();
        internal FocusPlatformHost(string kind, bool hitTest = true)
        {
            Inner = new Host(kind, hitTest);
            typeof(Window).GetField("_platformWindow", Private)!.SetValue(Inner.Source, Native);
        }
        public void Dispose()
        {
            Native.Acquire = null;
            typeof(Window).GetField("_platformWindow", Private)!.SetValue(Inner.Source, null);
            Inner.Dispose();
        }
    }

    private sealed class FocusPlatform : IPlatformWindow, IPlatformKeyboardFocus
    {
        internal Func<bool>? Acquire;
        internal bool Accepted = true;
        internal int Calls;
        public bool TrySetKeyboardFocus()
        { Calls++; var callback = Acquire; Acquire = null; return callback?.Invoke() ?? Accepted; }
        public nint NativeHandle => (nint)11;
        public NativeSurfaceDescriptor GetSurface() => default;
        public void Show() { }
        public void Hide() { }
        public void Close() { }
        public void Dispose() { }
        public void SetTitle(string value) { }
        public void Resize(int width, int height) { }
        public void Move(int x, int y) { }
        public int GetWidth() => 500;
        public int GetHeight() => 400;
        public void GetPosition(out int x, out int y) { x = y = 0; }
        public void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight) { }
        public bool BeginMoveDrag() => false;
        public bool BeginResizeDrag(int edge) => false;
        public bool SetIcon(uint[]? pixels, int width, int height) => true;
        public bool SetTopmost(bool value) => true;
        public bool SetEnabled(bool value) => true;
        public bool SetOpacity(double value) => true;
        public bool SetShowInTaskbar(bool value) => true;
        public bool SetResizable(bool value) => true;
        public bool SetDecorated(bool value) => true;
        public bool SetOwner(nint owner) => true;
        public bool Activate() => throw new InvalidOperationException("ordinary focus must not request activation");
        public bool ShowSystemMenu(int x, int y) => false;
        public void SetState(WindowState state) { }
        public WindowState GetState() => WindowState.Normal;
        public void Invalidate() { }
        public float GetDpiScale() => 1;
        public int GetMonitorRefreshRate() => 60;
        public void SetCursor(int shape) { }
        public void UpdateImeContext(PlatformImeContext context) { }
        public void SetEventHandler(Action<PlatformEvent>? handler) { }
    }
}
