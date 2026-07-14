using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class HwndInteropParityTests
{
    [Fact]
    public void HwndSourceParameters_HasWpfStructDefaultsAndAssignedSizeSemantics()
    {
        bool oldDefault = HwndSource.DefaultAcquireHwndFocusInMenuMode;
        try
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = true;
            var parameters = new HwndSourceParameters("parity");

            Assert.True(typeof(HwndSourceParameters).IsValueType);
            Assert.Equal(unchecked((int)0x12CF0000), parameters.WindowStyle);
            Assert.Equal(int.MinValue, parameters.PositionX);
            Assert.Equal(int.MinValue, parameters.PositionY);
            Assert.Equal(1, parameters.Width);
            Assert.Equal(1, parameters.Height);
            Assert.False(parameters.HasAssignedSize);
            Assert.True(parameters.AcquireHwndFocusInMenuMode);
            Assert.True(parameters.TreatAsInputRoot);
            Assert.Equal(RestoreFocusMode.Auto, parameters.RestoreFocusMode);

            parameters.SetPosition(12, 34);
            parameters.SetSize(320, 180);

            Assert.Equal(12, parameters.PositionX);
            Assert.Equal(34, parameters.PositionY);
            Assert.Equal(320, parameters.Width);
            Assert.Equal(180, parameters.Height);
            Assert.True(parameters.HasAssignedSize);
        }
        finally
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = oldDefault;
        }
    }

    [Fact]
    public void HwndSourceParameters_EqualityIncludesNativeCreationStateButNotFocusPolicy()
    {
        var left = new HwndSourceParameters("parity", 100, 50);
        var right = left;

        right.AcquireHwndFocusInMenuMode = !left.AcquireHwndFocusInMenuMode;
        right.RestoreFocusMode = RestoreFocusMode.None;
        right.TreatAsInputRoot = !left.TreatAsInputRoot;
        right.TreatAncestorsAsNonClientArea = !left.TreatAncestorsAsNonClientArea;

        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        right.WindowStyle++;
        Assert.True(left != right);
        Assert.False(left.Equals((object)right));
    }

    [Fact]
    public void InteropTypes_ExposeTheWpfInheritanceAndInterfaceShape()
    {
        Assert.Equal(typeof(PresentationSource), typeof(HwndSource).BaseType);
        Assert.Contains(typeof(IKeyboardInputSink), typeof(HwndSource).GetInterfaces());
        Assert.Contains(typeof(IWin32Window), typeof(HwndSource).GetInterfaces());
        Assert.Equal(typeof(FrameworkElement), typeof(HwndHost).BaseType);
        Assert.True(typeof(HwndHost).IsAbstract);
        Assert.Contains(typeof(IKeyboardInputSink), typeof(HwndHost).GetInterfaces());
        Assert.Contains(typeof(IWin32Window), typeof(HwndHost).GetInterfaces());
        Assert.Equal(typeof(Jalium.UI.Media.CompositionTarget), typeof(HwndTarget).BaseType);

        Type[] constructorParameters =
        [
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(int), typeof(int), typeof(string), typeof(IntPtr), typeof(bool),
        ];
        Assert.NotNull(typeof(HwndSource).GetConstructor(constructorParameters));
        Assert.NotNull(typeof(HwndHost).GetField(nameof(HwndHost.DpiChangedEvent)));
        Assert.Equal(
            typeof(HwndTarget),
            typeof(HwndSource).GetProperty(
                nameof(HwndSource.CompositionTarget),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType);
    }

    [Fact]
    public void Msg_AndKeyboardSinkContracts_WorkWithoutCreatingNativeWindows()
    {
        var message = new MSG
        {
            hwnd = new IntPtr(1),
            message = 0x0100,
            wParam = new IntPtr(2),
            lParam = new IntPtr(3),
            time = 4,
            pt_x = 5,
            pt_y = 6,
        };
        var host = new TestHwndHost();
        var sink = (IKeyboardInputSink)host;

        Assert.Equal(new IntPtr(1), message.hwnd);
        Assert.Equal(0x0100, message.message);
        Assert.False(sink.HasFocusWithin());
        Assert.False(sink.TranslateAccelerator(ref message, ModifierKeys.Control));
        Assert.Equal(ModifierKeys.Control, host.LastModifiers);

        host.Dispose();
    }

    [Fact]
    public void HwndTarget_RejectsFakeOrUnsupportedHandles()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<ArgumentException>(() => new HwndTarget(IntPtr.Zero));
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => new HwndTarget(IntPtr.Zero));
        }
    }

    private sealed class TestHwndHost : HwndHost
    {
        internal ModifierKeys LastModifiers { get; private set; }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent) =>
            new(this, IntPtr.Zero);

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
        }

        protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
        {
            LastModifiers = modifiers;
            return false;
        }
    }
}
