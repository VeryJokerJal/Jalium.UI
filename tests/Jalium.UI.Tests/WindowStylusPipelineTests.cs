using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

public class WindowStylusPipelineTests
{
    [Fact]
    public void CleanupPointerSession_ShouldRemoveStylusDeviceAndResetCurrentStylusDevice()
    {
        var window = new Window();
        var stylusDevice = new PointerStylusDevice(1001);
        Dictionary<uint, StylusDevice> activeStylusDevices =
            GetField<Dictionary<uint, StylusDevice>>(window, "_activeStylusDevices");
        activeStylusDevices[1001] = stylusDevice;
        Tablet.CurrentStylusDevice = stylusDevice;

        InvokeCleanupPointerSession(window, 1001);

        Assert.False(activeStylusDevices.ContainsKey(1001));
        Assert.Null(Tablet.CurrentStylusDevice);
    }

    [Fact]
    public void DispatchStylusSourcePipeline_ShouldRunPlugInBeforePreviewAndBubbleEvents()
    {
        var window = new Window();
        var order = new List<string>();
        window.GetStylusPlugIns(createIfMissing: true)!.Add(new OrderedStylusPlugIn(() => order.Add("plugin")));
        window.AddHandler(UIElement.PreviewStylusDownEvent, new RoutedEventHandler((_, _) => order.Add("preview")));
        window.AddHandler(UIElement.StylusDownEvent, new RoutedEventHandler((_, _) => order.Add("bubble")));

        object pointerData = CreatePenPointerData(pointerId: 2001, inContact: true, inRange: true, isCanceled: false);
        bool sourceHandled = false;
        bool sourceCanceled = false;

        InvokeDispatchStylusSourcePipeline(window, window, pointerData, isDown: true, isUp: false, timestamp: 33, ref sourceHandled, ref sourceCanceled);

        Assert.Equal(new[] { "plugin", "preview", "bubble" }, order);
        Assert.False(sourceCanceled);
    }

    [Fact]
    public void DispatchStylusSourcePipeline_WhenPlugInCancels_ShouldSetSourceCanceled()
    {
        var window = new Window();
        window.GetStylusPlugIns(createIfMissing: true)!.Add(new CancelingStylusPlugIn());

        object pointerData = CreatePenPointerData(pointerId: 2002, inContact: true, inRange: true, isCanceled: false);
        bool sourceHandled = false;
        bool sourceCanceled = false;

        InvokeDispatchStylusSourcePipeline(window, window, pointerData, isDown: true, isUp: false, timestamp: 44, ref sourceHandled, ref sourceCanceled);

        Assert.True(sourceCanceled);
    }

    private static void InvokeCleanupPointerSession(Window window, uint pointerId)
    {
        MethodInfo method = typeof(Window).GetMethod("CleanupPointerSession", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CleanupPointerSession not found.");
        _ = method.Invoke(window, new object[] { pointerId });
    }

    private static void InvokeDispatchStylusSourcePipeline(
        Window window,
        UIElement target,
        object pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        MethodInfo method = typeof(Window).GetMethod("DispatchStylusSourcePipeline", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DispatchStylusSourcePipeline not found.");

        object[] args =
        {
            target,
            pointerData,
            isDown,
            isUp,
            timestamp,
            sourceHandled,
            sourceCanceled
        };

        _ = method.Invoke(window, args);

        sourceHandled = (bool)args[5];
        sourceCanceled = (bool)args[6];
    }

    private static object CreatePenPointerData(uint pointerId, bool inContact, bool inRange, bool isCanceled)
    {
        Assembly controlsAssembly = typeof(Window).Assembly;
        Type dataType = controlsAssembly.GetType("Jalium.UI.Controls.PointerInputData")
            ?? throw new InvalidOperationException("PointerInputData type not found.");
        Type kindType = controlsAssembly.GetType("Jalium.UI.Controls.PointerInputKind")
            ?? throw new InvalidOperationException("PointerInputKind type not found.");

        object penKind = Enum.ToObject(kindType, 3);
        var position = new Point(50, 80);
        var properties = new PointerPointProperties
        {
            Pressure = 0.65f,
            IsBarrelButtonPressed = false,
            IsEraser = false,
            IsInverted = false,
            PointerUpdateKind = inContact ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.LeftButtonReleased
        };
        var pointerPoint = new PointerPoint(
            pointerId,
            position,
            PointerDeviceType.Pen,
            inContact,
            properties,
            timestamp: 123,
            frameId: 1);
        var stylusPoints = new StylusPointCollection(new[] { new StylusPoint(position.X, position.Y, properties.Pressure) });

        ConstructorInfo ctor = dataType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();

        return ctor.Invoke(new[]
        {
            (object)pointerId,
            penKind,
            pointerPoint,
            position,
            ModifierKeys.None,
            inRange,
            isCanceled,
            stylusPoints
        });
    }

    private static T GetField<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} not found.");
        return (T)(field.GetValue(instance) ?? throw new InvalidOperationException($"Field {fieldName} value is null."));
    }

    private sealed class OrderedStylusPlugIn(Action onStylusDown) : StylusPlugIn
    {
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            onStylusDown();
        }
    }

    private sealed class CancelingStylusPlugIn : StylusPlugIn
    {
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            rawStylusInput.Cancel();
        }
    }
}
