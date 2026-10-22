using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupOwnershipTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NewerOwnerFromTheNativeCallbackKeepsItsParentAndAllTextBindings(bool outerSucceeded)
    {
        using var h = new Host();
        bool nested = false;
        h.Native.Assign = _ =>
        {
            if (nested) return true;
            nested = true;
            Assert.True(h.Target.ReparentMacOSPopupHost(h.Second));
            return outerSucceeded;
        };
        Assert.False(h.Target.ReparentMacOSPopupHost(h.First));
        h.AssertOwner(h.Second);
        h.Second.FontSize = 37;
        Assert.Equal(37, h.Target.FontSize);
        Assert.Equal("Miii中文🙂e\u0301", h.Editor.Text);
        Assert.Equal((4, 4), (h.Editor.SelectionStart, h.Editor.SelectionLength));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlacementTargetReentryKeepsTheNewerPopupOpenAndObserved(bool outerSucceeded)
    {
        using var h = new Host();
        bool nested = false;
        h.Native.Assign = _ =>
        {
            if (nested) return true;
            nested = true;
            h.Popup.PlacementTarget = h.SecondPopup.Child;
            return outerSucceeded;
        };
        h.Popup.PlacementTarget = h.FirstPopup.Child;
        h.AssertPlacementOwner();
    }

    [Theory]
    [InlineData("FontFamily")] [InlineData("FontSize")] [InlineData("FontWeight")]
    [InlineData("FontStyle")] [InlineData("FontStretch")] [InlineData("Foreground")]
    [InlineData("FlowDirection")] [InlineData("Language")]
    public void PlacementTargetChangesDuringBindingKeepTheLatestLogicalOwner(string property)
    {
        using var h = new Host();
        bool changed = false;
        h.Target.PropertyChangedInternal += (changedProperty, _, _) =>
        {
            if (changed || changedProperty.Name != property) return;
            changed = true;
            h.Popup.PlacementTarget = h.SecondPopup.Child;
        };
        h.Popup.PlacementTarget = h.FirstPopup.Child;
        Assert.True(changed);
        h.AssertPlacementOwner();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlacementTargetCallbackCannotCloseAReopenedPopup(bool nativeResult)
    {
        using var h = new Host();
        h.Native.Assign = _ =>
        {
            h.Popup.IsOpen = false;
            h.Popup.PlacementTarget = (UIElement)h.Owner.Content!;
            h.Popup.IsOpen = true;
            return nativeResult;
        };
        h.Popup.PlacementTarget = h.FirstPopup.Child;
        Assert.True(h.Popup.IsOpen);
        Assert.NotSame(h.Target, h.Popup.Host);
        Assert.Same(h.Editor, Assert.IsType<PopupRoot>(h.Popup.Host!.Content).Child);
        Assert.Same(h.Owner, h.Popup.Host.MacOSInputOwner);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeFailureWithoutANewerRequestRollsBackOnlyTheParent(bool succeeds)
    {
        using var h = new Host();
        h.Native.Assign = _ => succeeds;
        Assert.Equal(succeeds, h.Target.ReparentMacOSPopupHost(h.First));
        h.AssertOwner(succeeds ? h.First : h.Owner);
        Assert.True(h.Popup.IsOpen);
    }

    [Theory]
    [InlineData("FontFamily")] [InlineData("FontSize")] [InlineData("FontWeight")]
    [InlineData("FontStyle")] [InlineData("FontStretch")] [InlineData("Foreground")]
    [InlineData("FlowDirection")] [InlineData("Language")]
    public void ATextBindingCallbackCannotResumeBindingsFromTheRetiredOwner(string property)
    {
        using var h = new Host();
        bool changed = false;
        h.Target.PropertyChangedInternal += (changedProperty, _, _) =>
        {
            if (changed || changedProperty.Name != property) return;
            changed = true;
            Assert.True(h.Target.ReparentMacOSPopupHost(h.Second));
        };
        Assert.False(h.Target.ReparentMacOSPopupHost(h.First));
        Assert.True(changed);
        h.AssertOwner(h.Second);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosingOrReopeningInsideTheNativeCallbackCannotBindTheRetiredHost(bool reopen)
    {
        using var h = new Host();
        var previousFamily = h.Target.FontFamily;
        h.Native.Assign = _ =>
        {
            h.Popup.IsOpen = false;
            if (reopen) h.Popup.IsOpen = true;
            return true;
        };
        Assert.False(h.Target.ReparentMacOSPopupHost(h.First));
        Assert.True(h.Target.IsClosedForPlatformTermination);
        Assert.Null(h.Target.Content);
        Assert.Same(previousFamily, h.Target.FontFamily);
        Assert.Equal(reopen, h.Popup.IsOpen);
        if (reopen)
        {
            Assert.NotSame(h.Target, h.Popup.Host);
            Assert.Same(h.Owner, h.Popup.Host!.MacOSInputOwner);
        }
    }

    [Fact]
    public void AReplacementPlatformInvalidatesTheOuterOwnershipCommit()
    {
        using var h = new Host();
        h.Native.Assign = _ => { SetPlatform(h.Target, new OwnerPlatform()); return true; };
        Assert.False(h.Target.ReparentMacOSPopupHost(h.First));
        Assert.Same(h.Owner, Source(h.Target, Control.FontFamilyProperty));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectedCyclesAndUnrelatedInputOwnersDoNotReachTheNativeBridge(bool unrelated)
    {
        using var h = new Host();
        var other = new Window();
        int calls = 0;
        h.Native.Assign = _ => { calls++; return true; };
        Assert.False(h.Target.ReparentMacOSPopupHost(unrelated ? other : h.Target));
        Assert.Equal(0, calls);
        h.AssertOwner(h.Owner);
        other.Close();
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void SetPlatform(Window window, IPlatformWindow? value) =>
        typeof(Window).GetField("_platformWindow", Private)!.SetValue(window, value);
    private static object? Source(DependencyObject value, DependencyProperty property) =>
        BindingOperations.GetBinding(value, property)?.Source;

    private sealed class Host : IDisposable
    {
        internal readonly Window Owner;
        internal readonly ExternalPopup FirstPopup, SecondPopup, Popup;
        internal Window First => FirstPopup.Host!;
        internal Window Second => SecondPopup.Host!;
        internal readonly HeadlessHost Target;
        internal readonly OwnerPlatform Native = new();
        internal readonly TextBox Editor = new() { Text = "Miii中文🙂e\u0301", Width = 220, Height = 40 };
        internal Host()
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Owner = new Window { Width = 600, Height = 400, Content = new Control { Width = 100, Height = 30 } };
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!.Invoke(Owner, [Visibility.Visible]);
            Owner.Measure(new Size(600, 400)); Owner.Arrange(new Rect(0, 0, 600, 400));
            FirstPopup = Open(new Control { Width = 80, Height = 30 });
            SecondPopup = Open(new Control { Width = 80, Height = 30 });
            SetFonts(First, false); SetFonts(Second, true);
            Popup = Open(Editor); Target = Popup.Host!;
            Editor.Select(4, 4);
            Assert.True(Editor.Focus());
            SetPlatform(Target, Native);
        }
        private ExternalPopup Open(UIElement child)
        {
            var popup = new ExternalPopup { PlacementTarget = (UIElement)Owner.Content!, Child = child, StaysOpen = true };
            popup.IsOpen = true;
            return popup;
        }
        internal void AssertOwner(Window expected)
        {
            Assert.Same(expected, typeof(Window).GetField("_macOSPopupParent", Private)!.GetValue(Target));
            foreach (var property in TextProperties)
            {
                Assert.Same(expected, Source(Target, property));
                Assert.Equal(expected.GetValue(property), Target.GetValue(property));
            }
            Assert.Same(Owner, Target.MacOSInputOwner);
        }
        internal void AssertPlacementOwner()
        {
            Assert.True(Popup.IsOpen);
            Assert.Same(Target, Popup.Host);
            AssertOwner(Second);
            Assert.Same(Second, typeof(Popup).GetField("_parentWindow", Private)!.GetValue(Popup));
            Assert.Same(Second, typeof(Popup).GetField("_macOSObservedOwner", Private)!.GetValue(Popup));
            Assert.Contains(Popup, Second.ActiveExternalPopups);
            Assert.DoesNotContain(Popup, First.ActiveExternalPopups);
            Assert.DoesNotContain(Popup, Owner.ActiveExternalPopups);
            Assert.Same(Editor, Keyboard.FocusedElement);
            Assert.Equal("Miii中文🙂e\u0301", Editor.Text);
            Assert.Equal((4, 4), (Editor.SelectionStart, Editor.SelectionLength));
        }
        public void Dispose()
        {
            SetPlatform(Target, null);
            if (Popup.Host != null) SetPlatform(Popup.Host, null);
            Popup.IsOpen = FirstPopup.IsOpen = SecondPopup.IsOpen = false;
            Owner.Close(); Keyboard.ClearFocus();
        }
        private static void SetFonts(Window window, bool second)
        {
            window.FontFamily = new FontFamily(second ? "Monaco" : "Times");
            window.FontSize = second ? 24 : 20;
            window.FontWeight = second ? FontWeights.Light : FontWeights.Bold;
            window.FontStyle = second ? FontStyles.Oblique : FontStyles.Italic;
            window.FontStretch = second ? FontStretches.Expanded : FontStretches.Condensed;
            window.Foreground = second ? Brushes.Blue : Brushes.Red;
            window.FlowDirection = second ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            window.Language = XmlLanguage.GetLanguage(second ? "fr-FR" : "ja-JP");
        }
    }

    private static readonly DependencyProperty[] TextProperties =
    [Control.FontFamilyProperty, Control.FontSizeProperty, Control.FontWeightProperty, Control.FontStyleProperty,
        Control.FontStretchProperty, Control.ForegroundProperty, FrameworkElement.FlowDirectionProperty, FrameworkElement.LanguageProperty];

    private sealed class ExternalPopup : Popup
    {
        internal HeadlessHost? Host;
        internal ExternalPopup() { PreferExternalWindow = true; PopupAnimation = PopupAnimation.None; }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) =>
            Host = new HeadlessHost(owner, this, root);
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
    private sealed class OwnerPlatform : IPlatformWindow
    {
        internal Func<nint, bool>? Assign;
        public nint NativeHandle => 11;
        public NativeSurfaceDescriptor GetSurface() => default;
        public void Show() { } public void Hide() { } public void Close() { } public void Dispose() { }
        public void SetTitle(string value) { } public void Resize(int width, int height) { } public void Move(int x, int y) { }
        public int GetWidth() => 500; public int GetHeight() => 400;
        public void GetPosition(out int x, out int y) { x = y = 0; }
        public void SetMinMaxSize(int a, int b, int c, int d) { }
        public bool BeginMoveDrag() => false; public bool BeginResizeDrag(int edge) => false;
        public bool SetIcon(uint[]? p, int w, int h) => true; public bool SetTopmost(bool v) => true;
        public bool SetEnabled(bool v) => true; public bool SetOpacity(double v) => true;
        public bool SetShowInTaskbar(bool v) => true; public bool SetResizable(bool v) => true;
        public bool SetDecorated(bool v) => true; public bool SetOwner(nint v) => Assign?.Invoke(v) ?? true;
        public bool Activate() => true; public bool ShowSystemMenu(int x, int y) => false;
        public void SetState(WindowState s) { } public WindowState GetState() => WindowState.Normal;
        public void Invalidate() { } public float GetDpiScale() => 1; public int GetMonitorRefreshRate() => 60;
        public void SetCursor(int s) { } public void UpdateImeContext(PlatformImeContext c) { }
        public void SetEventHandler(Action<PlatformEvent>? h) { }
    }
}
