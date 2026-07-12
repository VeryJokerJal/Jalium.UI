using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Xunit;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class StylusInputMethodTouchWpfParityTests
{
    [Fact]
    public void StylusPoint_EnforcesWpfNumericAndPacketContracts()
    {
        StylusPoint clamped = new(double.PositiveInfinity, double.NegativeInfinity, 0.5f);
        Assert.Equal(StylusPoint.MaxXY, clamped.X);
        Assert.Equal(StylusPoint.MinXY, clamped.Y);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StylusPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StylusPoint(0, 0, -0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StylusPoint(0, 0, 1.01f));

        StylusPointDescription description = new(
        [
            new StylusPointPropertyInfo(StylusPointProperties.X),
            new StylusPointPropertyInfo(StylusPointProperties.Y),
            new StylusPointPropertyInfo(StylusPointProperties.NormalPressure),
            new StylusPointPropertyInfo(StylusPointProperties.PacketStatus),
            new StylusPointPropertyInfo(StylusPointProperties.TipButton),
            new StylusPointPropertyInfo(StylusPointProperties.BarrelButton),
        ]);
        StylusPoint point = new(96, 48, 0.75f, description, [42, 1, 0]);

        Assert.Same(description, point.Description);
        Assert.Equal(42, point.GetPropertyValue(StylusPointProperties.PacketStatus));
        Assert.Equal(1, point.GetPropertyValue(StylusPointProperties.TipButton));
        Assert.Equal(0, point.GetPropertyValue(StylusPointProperties.BarrelButton));

        StylusPoint copy = point;
        point.SetPropertyValue(StylusPointProperties.TipButton, 0);
        Assert.Equal(0, point.GetPropertyValue(StylusPointProperties.TipButton));
        Assert.Equal(1, copy.GetPropertyValue(StylusPointProperties.TipButton));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => point.SetPropertyValue(StylusPointProperties.BarrelButton, 2));
    }

    [Fact]
    public void StylusPointCollection_SharesDescription_ReformatsAndProducesHiMetricPackets()
    {
        StylusPointDescription fullDescription = new(
        [
            new StylusPointPropertyInfo(StylusPointProperties.X),
            new StylusPointPropertyInfo(StylusPointProperties.Y),
            new StylusPointPropertyInfo(StylusPointProperties.NormalPressure),
            new StylusPointPropertyInfo(StylusPointProperties.PacketStatus),
            new StylusPointPropertyInfo(StylusPointProperties.TipButton),
        ]);
        StylusPoint point = new(96, 48, 0.5f, fullDescription, [7, 1]);
        StylusPointCollection points = new(fullDescription) { point };

        int changed = 0;
        points.Changed += (_, _) => changed++;
        points.Add(new StylusPoint(12, 24, 1f, fullDescription, [8, 0]));
        Assert.Equal(1, changed);
        Assert.All(points, packet => Assert.Same(fullDescription, packet.Description));

        StylusPointDescription subset = new(
        [
            new StylusPointPropertyInfo(StylusPointProperties.X, -10, 10, StylusPointPropertyUnit.Inches, 2),
            new StylusPointPropertyInfo(StylusPointProperties.Y, -10, 10, StylusPointPropertyUnit.Inches, 2),
            new StylusPointPropertyInfo(StylusPointProperties.NormalPressure, 0, 100, StylusPointPropertyUnit.None, 1),
            new StylusPointPropertyInfo(StylusPointProperties.PacketStatus),
        ]);
        StylusPointCollection reformatted = points.Reformat(subset);

        Assert.Equal(2, reformatted.Count);
        Assert.Equal(7, reformatted[0].GetPropertyValue(StylusPointProperties.PacketStatus));
        Assert.Equal(100, reformatted.Description.GetPropertyInfo(StylusPointProperties.NormalPressure).Maximum);
        Assert.False(reformatted[0].HasProperty(StylusPointProperties.TipButton));

        int[] hiMetric = points.ToHiMetricArray();
        Assert.Equal(2540, hiMetric[0]);
        Assert.Equal(1270, hiMetric[1]);
        Assert.Equal(511, hiMetric[2]);
        Assert.Equal(7, hiMetric[3]);
        Assert.Equal(1, hiMetric[4]);
        Assert.Throws<ArgumentException>(() => new StylusPointCollection(Array.Empty<StylusPoint>()));

        StylusPointCollection guarded = new([new StylusPoint(1, 1)]);
        guarded.CountGoingToZero += (_, e) => e.Cancel = true;
        Assert.Throws<InvalidOperationException>(guarded.Clear);
        Assert.Single(guarded);
    }

    [Fact]
    public void Stylus_StaticApi_TracksCaptureAndAttachedHandlers()
    {
        Border target = new();
        PointerStylusDevice device = new(73, "Parity pen");
        device.UpdateState(
            new Point(10, 20),
            new StylusPointCollection([new StylusPoint(10, 20, 0.8f)]),
            inAir: false,
            inverted: false,
            inRange: true,
            barrelPressed: false,
            eraserPressed: false,
            target);
        Tablet.CurrentStylusDevice = device;

        try
        {
            Assert.Same(target, Stylus.DirectlyOver);
            Assert.True(Stylus.Capture(target, CaptureMode.SubTree));
            Assert.Same(target, Stylus.Captured);
            Assert.Equal(CaptureMode.SubTree, device.CaptureMode);

            int calls = 0;
            StylusEventHandler handler = (_, _) => calls++;
            Stylus.AddStylusMoveHandler(target, handler);
            target.RaiseEvent(new StylusEventArgs(device, 1) { RoutedEvent = Stylus.StylusMoveEvent });
            Stylus.RemoveStylusMoveHandler(target, handler);
            target.RaiseEvent(new StylusEventArgs(device, 2) { RoutedEvent = Stylus.StylusMoveEvent });
            Assert.Equal(1, calls);
            Assert.Equal(new Point(10, 20), new StylusEventArgs(device, 3).GetPosition(null));
        }
        finally
        {
            Stylus.Capture(null);
            Tablet.CurrentStylusDevice = null;
        }
    }

    [Fact]
    public void InputMethod_UsesAttachedPropertiesAndRaisesPreciseStateChanges()
    {
        InputMethod.IsInputMethodEnabled = true;
        InputMethod.SetTarget(null);
        Border target = new();
        InputMethod.SetPreferredImeState(target, InputMethodState.On);
        InputMethod.SetPreferredImeConversionMode(target, ImeConversionModeValues.Native | ImeConversionModeValues.FullShape);
        InputMethod.SetPreferredImeSentenceMode(target, ImeSentenceModeValues.PhrasePrediction);
        InputMethod.Current.ImeState = InputMethodState.Off;
        InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Alphanumeric;
        InputMethod.Current.ImeSentenceMode = ImeSentenceModeValues.None;

        List<InputMethodStateChangedEventArgs> changes = [];
        InputMethod.Current.StateChanged += OnStateChanged;
        try
        {
            InputMethod.SetTarget(target);
            Assert.Same(target, InputMethod.CurrentTarget);
            Assert.Equal(InputMethodState.On, InputMethod.Current.ImeState);
            Assert.Equal(ImeConversionModeValues.Native | ImeConversionModeValues.FullShape, InputMethod.Current.ImeConversionMode);
            Assert.Equal(ImeSentenceModeValues.PhrasePrediction, InputMethod.Current.ImeSentenceMode);
            Assert.Contains(changes, change => change.IsImeStateChanged);
            Assert.Contains(changes, change => change.IsImeConversionModeChanged);
            Assert.Contains(changes, change => change.IsImeSentenceModeChanged);

            InputMethod.SetIsInputMethodSuspended(target, true);
            InputMethod.StartComposition();
            Assert.False(InputMethod.IsComposing);
            InputMethod.SetIsInputMethodSuspended(target, false);
            InputMethod.StartComposition();
            InputMethod.UpdateComposition("kana", 2);
            Assert.True(InputMethod.IsComposing);
            Assert.Equal("kana", InputMethod.CompositionString);
            Assert.Equal(2, InputMethod.CompositionCursor);
            InputMethod.SetIsInputMethodEnabled(target, false);
            Assert.False(InputMethod.IsComposing);
        }
        finally
        {
            InputMethod.Current.StateChanged -= OnStateChanged;
            InputMethod.SetIsInputMethodEnabled(target, true);
            InputMethod.IsInputMethodEnabled = true;
            InputMethod.SetTarget(null);
            InputMethod.CancelComposition();
        }

        void OnStateChanged(object sender, InputMethodStateChangedEventArgs e) => changes.Add(e);
    }

    [Fact]
    public void TouchDevice_ActivationCaptureReportsAndFrameQueriesShareOneState()
    {
        foreach (TouchDevice activeDevice in Touch.ActiveDevices.ToArray())
        {
            Touch.UnregisterTouchPoint(activeDevice.Id);
        }

        Border target = new();
        TestTouchDevice device = new(91, target, new Point(3, 4));
        int activated = 0;
        int deactivated = 0;
        int updated = 0;
        int frames = 0;
        device.Activated += (_, _) => activated++;
        device.Deactivated += (_, _) => deactivated++;
        ((IManipulator)device).Updated += (_, _) => updated++;
        Touch.FrameReported += OnFrame;

        try
        {
            device.ActivateDevice();
            Assert.Equal(1, activated);
            Assert.True(device.Capture(target, CaptureMode.SubTree));
            Assert.Same(target, device.Captured);
            Assert.Equal(CaptureMode.SubTree, device.CaptureMode);

            int routed = 0;
            target.TouchDown += (_, e) =>
            {
                routed++;
                e.Handled = true;
            };
            Assert.True(device.ReportDownDevice());
            Assert.Equal(1, routed);
            Assert.True(updated > 0);
            Assert.True(frames > 0);

            TouchPointCollection activePoints = TouchDevice.GetTouchPoints(null);
            Assert.Contains(activePoints, point => ReferenceEquals(point.TouchDevice, device));
            Assert.Same(device, TouchDevice.GetPrimaryTouchPoint(null)?.TouchDevice);
            Assert.Equal(new Point(3, 4), ((IManipulator)device).GetPosition(null));

            device.DeactivateDevice();
            Assert.Equal(1, deactivated);
            Assert.False(device.IsActive);
            Assert.Null(device.Captured);
        }
        finally
        {
            Touch.FrameReported -= OnFrame;
            device.DeactivateIfActive();
        }

        void OnFrame(object sender, TouchFrameEventArgs e) => frames++;
    }

    [Fact]
    public void Surface_ContainsRefreshedVerifierContracts()
    {
        Assert.True(typeof(TouchDevice).IsAbstract);
        Assert.True(typeof(TouchDevice).IsAssignableTo(typeof(InputDevice)));
        Assert.True(typeof(TouchDevice).IsAssignableTo(typeof(IManipulator)));
        Assert.Equal(typeof(PresentationSource), typeof(InputDevice).GetProperty(nameof(InputDevice.ActiveSource))!.PropertyType);
        Assert.Equal(typeof(InputMethod), typeof(InputMethod).GetProperty(nameof(InputMethod.Current))!.PropertyType);
        Assert.Equal(typeof(Collection<StylusPoint>), typeof(StylusPointCollection).BaseType);
        Assert.Equal(typeof(Collection<TouchPoint>), typeof(TouchPointCollection).BaseType);
        Assert.NotNull(typeof(Stylus).GetMethod(nameof(Stylus.Capture), [typeof(IInputElement), typeof(CaptureMode)]));
        Assert.NotNull(typeof(StylusPointCollection).GetMethod(nameof(StylusPointCollection.ToHiMetricArray)));
        Assert.Equal(Guid.Parse("7307502D-F9F4-4E18-B3F2-2CE1B1A3610C"), StylusPointProperties.NormalPressure.Id);
        Assert.Equal(Guid.Parse("67743782-0EE5-419A-A12B-273A9EC08F3D"), StylusPointProperties.SecondaryTipButton.Id);
        Assert.Equal(int.MinValue, (int)ImeConversionModeValues.DoNotCare);
        Assert.Equal(0, (int)InputMethodState.Off);
        Assert.Equal(2, (int)InputMethodState.DoNotCare);
    }

    private sealed class TestTouchDevice : TouchDevice
    {
        private Point _position;

        public TestTouchDevice(int id, IInputElement directlyOver, Point position)
            : base(id)
        {
            _position = position;
            SetDirectlyOver(directlyOver);
        }

        public override TouchPoint GetTouchPoint(IInputElement? relativeTo)
            => new(this, _position, new Rect(_position.X - 1, _position.Y - 1, 2, 2), TouchAction.Move);

        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)
            => new() { GetTouchPoint(relativeTo) };

        public void ActivateDevice() => Activate();
        public void DeactivateDevice() => Deactivate();
        public void DeactivateIfActive()
        {
            if (IsActive)
                Deactivate();
        }
        public bool ReportDownDevice() => ReportDown();
    }
}
