using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class InputInteropTypeShapeFinalGapParityTests
{
    [Fact]
    public void StylusDeviceIsSealedAndPointerAdapterPreservesLiveDeviceBehavior()
    {
        Type type = typeof(StylusDevice);
        Assert.True(type.IsSealed);
        Assert.False(type.IsAbstract);
        Assert.Equal(typeof(InputDevice), type.BaseType);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var target = new Border();
        var adapter = new PointerStylusDevice(517, "Parity pointer");
        adapter.UpdateState(
            new Point(12, 34),
            new StylusPointCollection([new StylusPoint(12, 34, 0.75f)]),
            inAir: false,
            inverted: true,
            inRange: true,
            barrelPressed: true,
            eraserPressed: false,
            target);

        StylusDevice device = adapter;
        Assert.Same(adapter.Device, device);
        Assert.Equal(517, device.Id);
        Assert.Equal("Parity pointer", device.Name);
        Assert.True(device.IsValid);
        Assert.True(device.Inverted);
        Assert.True(device.InRange);
        Assert.Same(target, device.DirectlyOver);
        Assert.Equal(new Point(12, 34), device.GetPosition(null));
        Assert.Equal(0.75f, Assert.Single(device.GetStylusPoints(null)).PressureFactor);
        Assert.True(device.Capture(target, CaptureMode.SubTree));
        Assert.Same(target, device.Captured);
        Assert.Equal(CaptureMode.SubTree, device.CaptureMode);

        device.Capture(null);
    }

    [Fact]
    public void StylusPointCollectionHasWpfBaseShapeAndStillRaisesCompatibilityNotifications()
    {
        Type type = typeof(StylusPointCollection);
        Assert.Equal(typeof(Collection<StylusPoint>), type.BaseType);
        Assert.False(typeof(INotifyCollectionChanged).IsAssignableFrom(type));
        MethodInfo onChanged = type.GetMethod(
            "OnChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.True(onChanged.IsVirtual);
        Assert.False(onChanged.IsFinal);

        var points = new StylusPointCollection();
        int changed = 0;
        int compatibilityChanges = 0;
        points.Changed += (_, _) => changed++;
        points.CollectionChanged += (_, _) => compatibilityChanges++;

        points.Add(new StylusPoint(1, 2));
        points[0] = new StylusPoint(3, 4);
        points.Clear();

        Assert.Equal(3, changed);
        Assert.Equal(3, compatibilityChanges);
        Assert.Equal(Rect.Empty, points.GetBounds());
    }

    [Fact]
    public void HwndSourceDeclaresTheWpfPresentationAndLifetimeTopology()
    {
        Type type = typeof(HwndSource);
        Assert.Equal(typeof(PresentationSource), type.BaseType);
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(PresentationSource)));
        Assert.False(type.IsAbstract);
        Assert.False(type.IsSealed);
        var expectedInterfaces = new HashSet<Type>
        {
            typeof(IDisposable),
            typeof(IKeyboardInputSink),
            typeof(IWin32Window),
        };
        Assert.True(expectedInterfaces.SetEquals(type.GetInterfaces()));

        MethodInfo dispose = type.GetMethod(
            nameof(IDisposable.Dispose),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        // A C# non-virtual implicit interface implementation is emitted as
        // virtual+final in CLR metadata; the important WPF contract is that
        // callers cannot override it.
        Assert.False(dispose.IsVirtual && !dispose.IsFinal);
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                new HwndSource(new HwndSourceParameters("unsupported")));
        }
    }
}
