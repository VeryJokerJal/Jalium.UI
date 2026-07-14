using System.Collections;
using System.Reflection;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.Manipulations;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class InputStylusPlugInsSecondPassParityTests
{
    [Fact]
    public void ManipulationStartingEventArgs_UsesWpfContainerCancelAndParameterContracts()
    {
        PropertyInfo container = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(ManipulationStartingEventArgs).GetProperty(nameof(ManipulationStartingEventArgs.ManipulationContainer)));
        Assert.Equal(typeof(IInputElement), container.PropertyType);

        Assert.Null(typeof(ManipulationStartingEventArgs).GetProperty("Cancel"));
        MethodInfo cancel = Assert.IsAssignableFrom<MethodInfo>(
            typeof(ManipulationStartingEventArgs).GetMethod(nameof(ManipulationStartingEventArgs.Cancel), Type.EmptyTypes));
        Assert.Equal(typeof(bool), cancel.ReturnType);

        var args = new ManipulationStartingEventArgs();
        Assert.True(args.Cancel());
        Assert.True(args.Cancel());

        MethodInfo setParameter = Assert.IsAssignableFrom<MethodInfo>(
            typeof(ManipulationStartingEventArgs).GetMethod(
                nameof(ManipulationStartingEventArgs.SetManipulationParameter),
                [typeof(ManipulationParameters2D)]));
        Assert.Equal(typeof(void), setParameter.ReturnType);

        var parameter = new TestManipulationParameter();
        args.SetManipulationParameter(parameter);
        Assert.Same(parameter, Assert.Single(args.Parameters));
    }

    [Fact]
    public void StylusPlugIn_ExposesExactHooksAndTracksActiveState()
    {
        PropertyInfo active = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(StylusPlugIn).GetProperty(nameof(StylusPlugIn.IsActiveForInput)));
        Assert.Equal(typeof(bool), active.PropertyType);
        Assert.NotNull(active.GetMethod);
        Assert.Null(active.SetMethod);

        AssertProtectedVirtual(typeof(StylusPlugIn), "OnEnabledChanged");
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnIsActiveForInputChanged");
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusEnter", typeof(RawStylusInput), typeof(bool));
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusLeave", typeof(RawStylusInput), typeof(bool));
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusDownProcessed", typeof(object), typeof(bool));
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusMoveProcessed", typeof(object), typeof(bool));
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusUpProcessed", typeof(object), typeof(bool));
        AssertProtectedVirtual(typeof(StylusPlugIn), "OnStylusInAirMoveProcessed", typeof(object), typeof(bool));

        var element = new TestElement();
        var plugIn = new LifecyclePlugIn();
        Assert.False(plugIn.IsActiveForInput);

        element.GetStylusPlugIns(createIfMissing: true)!.Add(plugIn);
        Assert.True(plugIn.IsActiveForInput);
        Assert.Equal(1, plugIn.ActiveChangedCount);

        element.IsEnabled = false;
        Assert.False(plugIn.IsActiveForInput);
        Assert.Equal(2, plugIn.ActiveChangedCount);

        plugIn.Enabled = false;
        Assert.Equal(1, plugIn.EnabledChangedCount);
        element.IsEnabled = true;
        Assert.False(plugIn.IsActiveForInput);
        Assert.Equal(2, plugIn.ActiveChangedCount);

        plugIn.Enabled = true;
        Assert.True(plugIn.IsActiveForInput);
        Assert.Equal(2, plugIn.EnabledChangedCount);
        Assert.Equal(3, plugIn.ActiveChangedCount);

        element.GetStylusPlugIns(createIfMissing: true)!.Clear();
        Assert.False(plugIn.IsActiveForInput);
        Assert.Equal(4, plugIn.ActiveChangedCount);
    }

    [Fact]
    public void RawStylusInput_UsesCallbackDataAndWpfDeviceIdentifiers()
    {
        MethodInfo notify = Assert.IsAssignableFrom<MethodInfo>(
            typeof(RawStylusInput).GetMethod(nameof(RawStylusInput.NotifyWhenProcessed), [typeof(object)]));
        Assert.Equal(typeof(void), notify.ReturnType);
        Assert.Equal(typeof(int), typeof(RawStylusInput).GetProperty(nameof(RawStylusInput.StylusDeviceId))!.PropertyType);
        Assert.Equal(typeof(int), typeof(RawStylusInput).GetProperty(nameof(RawStylusInput.TabletDeviceId))!.PropertyType);

        var raw = new RawStylusInput(
            uint.MaxValue,
            new TestElement(),
            StylusInputAction.Move,
            new StylusPointCollection([new StylusPoint(1, 2)]),
            timestamp: 1,
            inAir: false,
            inRange: true,
            inverted: false,
            barrelButtonPressed: false,
            eraserPressed: false);

        Assert.Equal(-1, raw.StylusDeviceId);
        Assert.Throws<InvalidOperationException>(() => raw.NotifyWhenProcessed(new object()));
    }

    [Fact]
    public void DynamicRenderer_ExposesAndExecutesWpfDrawingHooks()
    {
        Assert.Equal(typeof(Visual), typeof(DynamicRenderer).GetProperty(nameof(DynamicRenderer.RootVisual))!.PropertyType);

        MethodInfo getDispatcher = Assert.IsAssignableFrom<MethodInfo>(
            typeof(DynamicRenderer).GetMethod("GetDispatcher", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(getDispatcher.IsFamily);
        Assert.Equal(typeof(Jalium.UI.Threading.Dispatcher), getDispatcher.ReturnType);

        AssertProtectedVirtual(
            typeof(DynamicRenderer),
            "OnDraw",
            typeof(DrawingContext),
            typeof(StylusPointCollection),
            typeof(Geometry),
            typeof(Brush));
        AssertProtectedVirtual(typeof(DynamicRenderer), "OnDrawingAttributesReplaced");

        MethodInfo reset = Assert.IsAssignableFrom<MethodInfo>(
            typeof(DynamicRenderer).GetMethod(
                nameof(DynamicRenderer.Reset),
                [typeof(StylusDevice), typeof(StylusPointCollection)]));
        Assert.True(reset.IsPublic && reset.IsVirtual && !reset.IsFinal);

        var renderer = new TrackingDynamicRenderer();
        Assert.Same(renderer.RootVisual, renderer.RootVisual);
        Assert.Same(Jalium.UI.Threading.Dispatcher.CurrentDispatcher, renderer.DispatcherForTest);

        renderer.DrawingAttributes = new DrawingAttributes { Width = 4, Height = 4 };
        Assert.Equal(1, renderer.DrawingAttributesReplacedCount);

        renderer.Reset(
            new PointerStylusDevice(14),
            new StylusPointCollection([new StylusPoint(1, 2), new StylusPoint(4, 5)]));
        Assert.Equal(1, renderer.DrawCount);
    }

    [Fact]
    public void TabletAndStylusShapes_MatchWpfCollectionAndSynchronizationContracts()
    {
        Assert.True(typeof(TabletDevice).IsSealed);
        Assert.True(typeof(ICollection).IsAssignableFrom(typeof(TabletDeviceCollection)));
        Assert.True(typeof(IEnumerable).IsAssignableFrom(typeof(TabletDeviceCollection)));

        var tablet = new TabletDevice(
            id: 7,
            name: "Pen tablet",
            productId: "VID_1234&PID_5678",
            type: TabletDeviceType.Stylus,
            capabilities: TabletHardwareCapabilities.HardProximity);
        Assert.Equal(7, tablet.Id);
        Assert.Equal("Pen tablet", tablet.Name);
        Assert.Equal("VID_1234&PID_5678", tablet.ProductId);
        Assert.Equal(TabletDeviceType.Stylus, tablet.Type);
        Assert.Equal(TabletHardwareCapabilities.HardProximity, tablet.TabletHardwareCapabilities);

        var tablets = new TabletDeviceCollection();
        tablets.Add(tablet);
        Assert.Same(tablet, tablets[0]);
        var copied = new TabletDevice[1];
        tablets.CopyTo(copied, 0);
        Assert.Same(tablet, copied[0]);

        MethodInfo synchronize = Assert.IsAssignableFrom<MethodInfo>(
            typeof(StylusDevice).GetMethod(nameof(StylusDevice.Synchronize), Type.EmptyTypes));
        Assert.True(synchronize.IsPublic);
        Assert.False(synchronize.IsVirtual);
    }

    private static MethodInfo AssertProtectedVirtual(Type type, string name, params Type[] parameterTypes)
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null));
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
        return method;
    }

    private sealed class TestElement : FrameworkElement
    {
    }

    private sealed class TestManipulationParameter : ManipulationParameters2D
    {
    }

    private sealed class LifecyclePlugIn : StylusPlugIn
    {
        public int EnabledChangedCount { get; private set; }
        public int ActiveChangedCount { get; private set; }

        protected override void OnEnabledChanged() => EnabledChangedCount++;
        protected override void OnIsActiveForInputChanged() => ActiveChangedCount++;
    }

    private sealed class TrackingDynamicRenderer : DynamicRenderer
    {
        public int DrawCount { get; private set; }
        public int DrawingAttributesReplacedCount { get; private set; }
        public Jalium.UI.Threading.Dispatcher DispatcherForTest => GetDispatcher();

        protected override void OnDraw(
            DrawingContext drawingContext,
            StylusPointCollection stylusPoints,
            Geometry geometry,
            Brush fillBrush)
        {
            DrawCount++;
            base.OnDraw(drawingContext, stylusPoints, geometry, fillBrush);
        }

        protected override void OnDrawingAttributesReplaced()
        {
            DrawingAttributesReplacedCount++;
            base.OnDrawingAttributesReplaced();
        }
    }
}
