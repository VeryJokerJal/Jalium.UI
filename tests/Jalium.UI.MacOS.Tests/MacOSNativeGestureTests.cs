using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class MacOSNativeGestureTests
{
    [Theory]
    [InlineData(true, true)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(false, false)]
    public void ExcludedFirstKindWaitsForAnEnabledKind(bool rotateOnly, bool startingMode)
    {
        using var f = new Fixture();
        var mode = rotateOnly ? ManipulationModes.Rotate : ManipulationModes.Scale;
        if (startingMode) f.Target.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, e) =>
            ((ManipulationStartingEventArgs)e).Mode = mode));
        else Manipulation.SetManipulationMode(f.Target, mode);
        int excluded = rotateOnly ? 1 : 2, enabled = rotateOnly ? 2 : 1;
        Assert.True(f.Send(1, 1.1, 5, kind: excluded, flags: 1).Handled);
        Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.Equal(["starting-preview", "starting"], f.Order);
        Assert.Empty(f.Deltas); Assert.Empty(f.Completed);
        Assert.True(f.Send(1, 1.2, 12, kind: enabled, flags: 1).Handled);
        Assert.True(Manipulation.IsManipulationActive(f.Target));
        Assert.Equal(1, f.Order.Count(x => x == "started"));
        var delta = Assert.Single(f.Deltas);
        Assert.Equal(rotateOnly ? 1 : 1.2, delta.CumulativeManipulation.Scale.X, 10);
        Assert.Equal(rotateOnly ? 12 : 0, delta.CumulativeManipulation.Rotation, 10);
        f.Send(3, kind: excluded, flags: 1); f.Send(3, kind: enabled, flags: 1); f.Send(3, kind: 0);
        Assert.Single(f.Completed); Assert.False(Manipulation.IsManipulationActive(f.Target));
    }

    [Theory] [InlineData(3)] [InlineData(4)]
    public void PendingGestureEndsWithoutInventingStartedOrCompletedEvents(int terminal)
    {
        using var f = new Fixture(); Manipulation.SetManipulationMode(f.Target, ManipulationModes.Rotate);
        Assert.True(f.Send(1, 1.2, flags: 1).Handled);
        Assert.True(f.Send(2, 1.1, flags: 1).Handled);
        Assert.Throws<InvalidOperationException>(() => Manipulation.StartInertia(f.Target));
        Assert.True(f.Send(terminal, kind: 0).Handled);
        Assert.Equal(["starting-preview", "starting"], f.Order);
        Assert.Null(typeof(Window).GetField("_macOSManipulation", Private)!.GetValue(f.Window));
        Assert.True(f.Send(1, rotation: 5, kind: 2, sequence: 2).Handled);
        f.Send(3, kind: 0, sequence: 2);
        Assert.Single(f.Completed);
    }

    [Theory] [InlineData("disable")] [InlineData("hide")] [InlineData("complete")]
    public void PendingOwnershipRetiresBeforeALaterEnabledPacket(string change)
    {
        using var f = new Fixture(); Manipulation.SetManipulationMode(f.Target, ManipulationModes.Rotate);
        Assert.True(f.Send(1, 1.1, flags: 1).Handled);
        if (change == "disable") { f.Target.IsEnabled = false; f.Target.IsEnabled = true; }
        if (change == "hide") { f.Target.Visibility = Visibility.Hidden; f.Target.Visibility = Visibility.Visible; }
        if (change == "complete") Manipulation.CompleteManipulation(f.Target);
        Assert.False(f.Send(1, rotation: 5, kind: 2, flags: 1).Handled);
        Assert.Empty(f.Deltas); Assert.Empty(f.Completed);
        Assert.Null(typeof(Window).GetField("_macOSManipulation", Private)!.GetValue(f.Window));
        Assert.True(f.Send(1, rotation: 7, kind: 2, sequence: 2).Handled);
        f.Send(3, kind: 0, sequence: 2); Assert.Single(f.Completed);
    }

    [Theory] [InlineData(ManipulationModes.None)] [InlineData(ManipulationModes.Translate)]
    public void NoNativeModeStillRejectsTheSequence(ManipulationModes mode)
    {
        using var f = new Fixture(); Manipulation.SetManipulationMode(f.Target, mode);
        Assert.False(f.Send(1, 1.2).Handled);
        Assert.Empty(f.Deltas); Assert.Empty(f.Completed);
        Assert.Null(typeof(Window).GetField("_macOSManipulation", Private)!.GetValue(f.Window));
    }

    [Fact]
    public void MagnificationRetainsTheAngularVelocityPassedToInertia()
    {
        using var f = new Fixture();
        f.Send(1, rotation: 2, kind: 2, flags: 1, timestamp: 1);
        f.Send(2, rotation: 6, kind: 2, flags: 1, timestamp: 1.02);
        var earlier = f.Deltas.Last().Velocities;
        Assert.Equal(.3, earlier.AngularVelocity, 10);
        f.Send(1, 1.1, kind: 1, flags: 1, timestamp: 1.03);
        Assert.Equal(.3, f.Deltas.Last().Velocities.AngularVelocity, 10);
        Assert.Equal(.3, earlier.AngularVelocity, 10);
        double initial = double.NaN;
        f.Target.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
            initial = ((ManipulationInertiaStartingEventArgs)e).InitialVelocities.AngularVelocity));
        Manipulation.StartInertia(f.Target); Assert.Equal(.3, initial, 10);
        f.Inertia().TickForTesting(16);
        Assert.Contains(f.Deltas, e => e.IsInertial && e.DeltaManipulation.Rotation > 0);
        Manipulation.CompleteManipulation(f.Target); Assert.Single(f.Completed);
    }

    [Fact]
    public void RotationTimingExcludesInterleavedMagnificationPackets()
    {
        using var f = new Fixture();
        f.Send(1, rotation: 2, kind: 2, flags: 1, timestamp: 1);
        f.Send(1, 1.1, kind: 1, flags: 1, timestamp: 1.01);
        f.Send(2, rotation: 6, kind: 2, flags: 1, timestamp: 1.02);
        var earlier = f.Deltas.Last().Velocities;
        Assert.Equal(.3, earlier.AngularVelocity, 10);
        f.Send(2, 1.05, kind: 1, flags: 1, timestamp: 1.03);
        f.Send(2, rotation: 8, kind: 2, flags: 1, timestamp: 1.06);
        Assert.Equal(.2, f.Deltas.Last().Velocities.AngularVelocity, 10);
        Assert.Equal(.3, earlier.AngularVelocity, 10);
        f.Send(3, kind: 0);
    }

    [Fact]
    public void NeutralRotationBeginEstablishesItsOwnSampleClock()
    {
        using var f = new Fixture();
        f.Send(1, kind: 2, flags: 1, timestamp: 1);
        Assert.Empty(f.Deltas);
        f.Send(2, rotation: 6, kind: 2, flags: 1, timestamp: 1.02);
        Assert.Equal(.3, Assert.Single(f.Deltas).Velocities.AngularVelocity, 10);
        f.Send(3, kind: 0);
    }

    [Fact]
    public void ADeferredStartedCallbackKeepsANewerSequenceIntact()
    {
        using var f = new Fixture(); Manipulation.SetManipulationMode(f.Target, ManipulationModes.Rotate);
        bool once = false;
        f.Target.AddHandler(UIElement.PreviewManipulationStartedEvent, new RoutedEventHandler((_, _) =>
        { if (!once) { once = true; f.Send(1, rotation: 7, kind: 2, sequence: 2); } }));
        Assert.True(f.Send(1, 1.1, flags: 1).Handled);
        f.Send(1, rotation: 5, kind: 2, flags: 1);
        Assert.True(Manipulation.IsManipulationActive(f.Target));
        f.Send(2, rotation: 3, kind: 2, sequence: 2); f.Send(3, kind: 0, sequence: 2);
        Assert.Equal(2, f.Completed.Count); Assert.Equal(10, f.Completed.Last().TotalManipulation.Rotation, 10);
    }

    [Fact]
    public void ChangingThePendingModeCanStartOnTheNextMatchingUpdate()
    {
        using var f = new Fixture(); Manipulation.SetManipulationMode(f.Target, ManipulationModes.Rotate);
        Assert.True(f.Send(1, 1.1, flags: 1).Handled);
        Manipulation.SetManipulationMode(f.Target, ManipulationModes.Scale);
        Assert.True(f.Send(2, 1.2, flags: 1).Handled);
        Assert.True(Manipulation.IsManipulationActive(f.Target));
        Assert.Equal(1.2, Assert.Single(f.Deltas).CumulativeManipulation.Scale.X, 10);
        f.Send(3, kind: 0); Assert.Single(f.Completed);
    }


    [Fact]
    public void NativeFactorsAccumulateAndImperativeStateEndsBeforeCompletion()
    {
        using var f = new Fixture();
        var begin = f.Send(1, 1.25);
        Assert.True(begin.Handled); Assert.True(Manipulation.IsManipulationActive(f.Target));
        f.Send(2, .8, 12); f.Send(3);
        Assert.Equal(["starting-preview", "starting", "started-preview", "started"], f.Order.Take(4));
        Assert.Single(f.Completed); Assert.Equal(1, f.Completed[0].TotalManipulation.Scale.X, 10);
        Assert.Equal(12, f.Completed[0].TotalManipulation.Rotation);
        Assert.False(f.CompletedActive); Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.Empty(f.Completed[0].Manipulators); Assert.Equal(0, f.Native.Activations);
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public void OriginUsesPhysicalDpiAndTheRequestedContainer(int dpi)
    {
        using var f = new Fixture(dpi);
        f.Target.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, e) =>
            ((ManipulationStartingEventArgs)e).ManipulationContainer = f.Canvas));
        f.Send(1, 1.1);
        var delta = Assert.Single(f.Deltas);
        Assert.Same(f.Canvas, delta.ManipulationContainer);
        Assert.Equal(f.Target.TranslatePoint(new(10, 10), f.Canvas), delta.ManipulationOrigin);
        Assert.Equal(1.1, delta.DeltaManipulation.Scale.X, 10);
    }

    [Theory] [InlineData("disabled")] [InlineData("hidden")] [InlineData("hit-test")]
    [InlineData("parent-disabled")] [InlineData("not-enabled")]
    public void IneligibleTargetsDoNotClaimNativeInput(string state)
    {
        using var f = new Fixture();
        if (state == "disabled") f.Target.IsEnabled = false;
        if (state == "hidden") f.Target.Visibility = Visibility.Hidden;
        if (state == "hit-test") f.Target.IsHitTestVisible = false;
        if (state == "parent-disabled") f.Canvas.IsEnabled = false;
        if (state == "not-enabled") f.Target.IsManipulationEnabled = false;
        Assert.False(f.Send(1, 1.2).Handled); Assert.Empty(f.Order);
    }

    [Fact]
    public void PreviewHandlingSuppressesBubbleWithoutLosingCumulativeValues()
    {
        using var f = new Fixture();
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, e) => e.Handled = true));
        f.Send(1, 1.2); f.Send(2, 1.5); f.Send(3);
        Assert.Empty(f.Deltas); Assert.Equal(1.8, Assert.Single(f.Completed).TotalManipulation.Scale.X, 10);
    }

    [Theory] [InlineData("starting")] [InlineData("started")] [InlineData("delta")]
    public void CancellationAndCompletionStopThePhysicalSequenceUntilANewBegin(string stage)
    {
        using var f = new Fixture();
        if (stage == "starting") f.Target.AddHandler(UIElement.PreviewManipulationStartingEvent,
            new RoutedEventHandler((_, e) => ((ManipulationStartingEventArgs)e).Cancel()));
        if (stage == "started") f.Target.AddHandler(UIElement.PreviewManipulationStartedEvent,
            new RoutedEventHandler((_, e) => ((ManipulationStartedEventArgs)e).Complete()));
        if (stage == "delta") f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent,
            new RoutedEventHandler((_, e) => ((ManipulationDeltaEventArgs)e).Cancel()));
        f.Send(1, 1.1); int count = f.Order.Count;
        Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.False(f.Send(2, 1.4).Handled); Assert.Equal(count, f.Order.Count);
        Assert.Equal(stage == "starting" ? 0 : 1, f.Completed.Count);
    }

    [Fact]
    public void CombinedNativeKindsShareOneManipulationAndFinishAtTheGenericEnd()
    {
        using var f = new Fixture();f.Send(1, 1.2, flags: 1);
        f.Send(1, rotation: 20, kind: 2, flags: 1);
        f.Send(3, kind: 1, flags: 1);f.Send(3, kind: 2, flags: 1);
        Assert.True(Manipulation.IsManipulationActive(f.Target)); Assert.Empty(f.Completed);
        f.Send(3, kind: 0);Assert.Single(f.Completed);
        Assert.Equal(1, f.Order.Count(value => value == "started"));
    }

    [Fact]
    public void SelectedModesFilterUnsupportedComponents()
    {
        using var f = new Fixture();
        f.Target.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, e) =>
            ((ManipulationStartingEventArgs)e).Mode = ManipulationModes.Scale));
        f.Send(1, 1.2);f.Send(2, 1.1, 40);f.Send(3);
        Assert.Equal(0, Assert.Single(f.Completed).TotalManipulation.Rotation);
        Assert.Equal(1.32, f.Completed[0].TotalManipulation.Scale.X, 10);
    }

    [Theory] [InlineData("target")] [InlineData("parent")] [InlineData("visibility")]
    [InlineData("reparent")]
    public void EligibilityAndParentRoundtripsRetireTheOldOperation(string change)
    {
        using var f = new Fixture();bool once = false;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, _) =>
        {
            if (once) return;once = true;
            if (change == "target") { f.Target.IsEnabled = false;f.Target.IsEnabled = true; }
            if (change == "parent") { f.Canvas.IsEnabled = false;f.Canvas.IsEnabled = true; }
            if (change == "visibility") { f.Target.Visibility = Visibility.Hidden;f.Target.Visibility = Visibility.Visible; }
            if (change == "reparent") { f.Canvas.Children.Remove(f.Target);f.Canvas.Children.Add(f.Target); }
        }));
        f.Send(1, 1.1);Assert.Empty(f.Deltas);Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.False(f.Send(2, 1.2).Handled);
        Assert.True(f.Send(1, 1.3, sequence: 2).Handled);Assert.True(Manipulation.IsManipulationActive(f.Target));
    }

    [Fact]
    public void NestedDeltaWinsWithoutAnOuterBubbleOrLostScale()
    {
        using var f = new Fixture();bool once = false;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, _) =>
        { if (!once) { once = true;f.Send(2, 1.1); } }));
        f.Send(1, 1.25);f.Send(3);
        Assert.Single(f.Deltas);Assert.Equal(1.375, f.Completed[0].TotalManipulation.Scale.X, 10);
    }

    [Fact]
    public void ACompletionCallbackCanCreateANewSessionWithoutOldCleanupRemovingIt()
    {
        using var f = new Fixture();bool once = false;
        f.Target.AddHandler(UIElement.PreviewManipulationCompletedEvent, new RoutedEventHandler((_, _) =>
        { if (!once) { once = true;f.Send(1, 1.3, sequence: 2); } }));
        f.Send(1, 1.1);f.Send(3);
        Assert.True(Manipulation.IsManipulationActive(f.Target));
        f.Send(2, 1.2, sequence: 2);f.Send(3, sequence: 2);
        Assert.Equal(1.56, f.Completed.Last().TotalManipulation.Scale.X, 10);
    }

    [Fact]
    public void ImperativeCompleteUsesTheRealCumulativeValues()
    {
        using var f = new Fixture();f.Send(1, 1.25);f.Send(2, 1.2, 4);
        Manipulation.CompleteManipulation(f.Target);
        Assert.Equal(1.5, Assert.Single(f.Completed).TotalManipulation.Scale.X, 10);
        Assert.Equal(4, f.Completed[0].TotalManipulation.Rotation);
    }

    [Fact]
    public void ExplicitInertiaUsesTheSharedIntegratorAndCanCompleteFromPreview()
    {
        using var f = new Fixture();f.Send(1, 1.2);
        f.Target.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
        { var args = (ManipulationInertiaStartingEventArgs)e;args.RotationBehavior.InitialVelocity = .5; }));
        Manipulation.StartInertia(f.Target);
        var processor = f.Inertia();Assert.True(processor.IsRunning);
        bool completed = false;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
        { if (((ManipulationDeltaEventArgs)e).IsInertial) { completed = true;Manipulation.CompleteManipulation(f.Target); } }));
        processor.TickForTesting(16);
        Assert.True(completed);Assert.False(processor.IsRunning);Assert.Single(f.Completed);
        Assert.True(f.Completed[0].IsInertial);Assert.True(f.Completed[0].TotalManipulation.Rotation > 0);
        Assert.DoesNotContain(f.Deltas, args => args.IsInertial);
    }

    [Fact]
    public void ExplicitInertiaFinishesAndDetachesItsImperativeSession()
    {
        using var f = new Fixture();f.Send(1, 1.2);
        f.Target.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
        { var args=(ManipulationInertiaStartingEventArgs)e;args.RotationBehavior.InitialVelocity=.5;args.RotationBehavior.DesiredDeceleration=.02; }));
        Manipulation.StartInertia(f.Target);var processor=f.Inertia();
        for(int i=0;i<100&&processor.IsRunning;i++)processor.TickForTesting(16);
        Assert.False(processor.IsRunning);Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.Single(f.Completed);Assert.True(f.Completed[0].IsInertial);
    }

    [Theory] [InlineData(double.NaN)] [InlineData(0)] [InlineData(-1)]
    public void InvalidFactorsNeverReachControls(double scale)
    {
        using var f = new Fixture();f.Send(1,1.2);f.Send(2,scale);
        Assert.Single(f.Deltas);Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.Equal(1.2, Assert.Single(f.Completed).TotalManipulation.Scale.X, 10);
    }

    [Fact]
    public void HandlerExceptionsDoNotLeaveAnAttachedSession()
    {
        using var f = new Fixture();f.Target.AddHandler(UIElement.ManipulationDeltaEvent,
            new RoutedEventHandler((_, _) => throw new InvalidOperationException("gesture handler")));
        Assert.Throws<InvalidOperationException>(() => f.Send(1, 1.2));
        Assert.False(Manipulation.IsManipulationActive(f.Target));Assert.Empty(f.Completed);
    }

    [Fact]
    public void ModalRoundtripCannotResumeAnOldPreview()
    {
        using var f=new Fixture();bool once=false;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent,new RoutedEventHandler((_,_)=>
        {if(once)return;once=true;f.Window.ActiveContentDialog=new ContentDialog();f.Window.ActiveContentDialog=null;}));
        f.Send(1,1.2);Assert.Empty(f.Deltas);Assert.False(Manipulation.IsManipulationActive(f.Target));
        Assert.True(f.Send(1,1.1,sequence:2).Handled);
    }

    [Fact]
    public void PreviewBoundaryFeedbackSurvivesTheBubble()
    {
        using var f=new Fixture();int feedback=0;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent,new RoutedEventHandler((_,e)=>
            ((ManipulationDeltaEventArgs)e).ReportBoundaryFeedback(new ManipulationDelta(Vector.Zero,5,new Vector(1,1),Vector.Zero))));
        f.Target.AddHandler(UIElement.ManipulationBoundaryFeedbackEvent,new RoutedEventHandler((_,e)=>
        {Assert.Equal(5,((ManipulationBoundaryFeedbackEventArgs)e).BoundaryFeedback.Rotation);feedback++;}));
        f.Send(1,1.2);Assert.Equal(1,feedback);
    }

    [Fact]
    public void MirroredContainerUsesItsLocalRotationDirection()
    {
        using var f=new Fixture();f.Canvas.RenderTransformOrigin=new Point(.5,.5);f.Canvas.RenderTransform=new ScaleTransform(-1,1);
        f.Target.AddHandler(UIElement.ManipulationStartingEvent,new RoutedEventHandler((_,e)=>
            ((ManipulationStartingEventArgs)e).ManipulationContainer=f.Canvas));
        f.Send(1,rotation:12,kind:2);Assert.Equal(-12,Assert.Single(f.Deltas).DeltaManipulation.Rotation);
    }

    [Fact]
    public void EligibilityGetterReentryCannotDeliverAnotherOldHandler()
    {
        using var f=new Fixture();bool once=false;int stale=0;
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent,new RoutedEventHandler((_,e)=>
        {
            if(!once&&Math.Abs(((ManipulationDeltaEventArgs)e).DeltaManipulation.Scale.X-1.2)<1e-8)
            {once=true;f.Target.OnEnabled=()=>f.Send(1,1.1,sequence:2);}
        }));
        f.Target.AddHandler(UIElement.PreviewManipulationDeltaEvent,new RoutedEventHandler((_,e)=>
        {if(Math.Abs(((ManipulationDeltaEventArgs)e).DeltaManipulation.Scale.X-1.2)<1e-8)stale++;}));
        f.Send(1,1.2);Assert.Equal(0,stale);Assert.True(Manipulation.IsManipulationActive(f.Target));
        f.Send(3,sequence:2);Assert.Equal(1.1,f.Completed.Last().TotalManipulation.Scale.X,10);
    }

    [Fact]
    public void AcquisitionGetterReentryToAnotherTargetKeepsTheNewSession()
    {
        int exercised=0;
        for(int readNumber=1;readNumber<=24;readNumber++)
        {
            using var f=new Fixture();var next=new Border{Width=140,Height=90,IsManipulationEnabled=true,Background=Brushes.Blue};
            f.Canvas.Children.Add(next);Canvas.SetLeft(next,280);Canvas.SetTop(next,40);
            f.Window.Measure(new(500,400));f.Window.Arrange(new(0,0,500,400));
            int reads=0;bool nextAccepted=false;var nextDeltas=new List<ManipulationDeltaEventArgs>();
            next.AddHandler(UIElement.ManipulationDeltaEvent,new RoutedEventHandler((_,e)=>nextDeltas.Add((ManipulationDeltaEventArgs)e)));
            void SendNext(int phase)
            {
                var point=next.TranslatePoint(new(10,10),f.Window);
                var request=new MacOSGestureRequest(new(2,1,phase,point.X,point.Y,phase==1?1.1:1));
                typeof(Window).GetMethod("HandleMacOSGesture",Private)!.Invoke(f.Window,[request]);
                if(phase==1)nextAccepted=request.Handled&&Manipulation.IsManipulationActive(next);
            }
            void Query(){if(++reads<readNumber)f.Target.OnEnabled=Query;else SendNext(1);}
            f.Target.OnEnabled=Query;
            try
            {
                f.Send(1,1.2);f.Target.OnEnabled=null;
                if(reads<readNumber||!nextAccepted)continue;
                exercised++;
                Assert.True(Manipulation.IsManipulationActive(next),$"newer target lost after eligibility read {readNumber}");
                Assert.False(Manipulation.IsManipulationActive(f.Target));
                SendNext(3);Assert.Single(nextDeltas);Assert.False(Manipulation.IsManipulationActive(next));
            }
            finally {f.Target.OnEnabled=null;Manipulation.CompleteManipulation(next);}
        }
        Assert.True(exercised>=8,$"Only {exercised} eligibility stages exercised");
    }

    [Fact]
    public void InertiaHandlerExceptionsStopTheTimerAndDetachTheSession()
    {
        using var f=new Fixture();f.Send(1,1.2);
        f.Target.AddHandler(UIElement.ManipulationInertiaStartingEvent,new RoutedEventHandler((_,e)=>
            ((ManipulationInertiaStartingEventArgs)e).RotationBehavior.InitialVelocity=.5));
        Manipulation.StartInertia(f.Target);var processor=f.Inertia();
        f.Target.AddHandler(UIElement.ManipulationDeltaEvent,new RoutedEventHandler((_,e)=>
        {if(((ManipulationDeltaEventArgs)e).IsInertial)throw new InvalidOperationException("inertia handler");}));
        Assert.Throws<InvalidOperationException>(()=>processor.TickForTesting(16));
        Assert.False(processor.IsRunning);Assert.False(Manipulation.IsManipulationActive(f.Target));Assert.Empty(f.Completed);
    }

    [Fact]
    public void RemovingAnExplicitManipulatorPreservesTheNativeSession()
    {
        using var f=new Fixture();f.Send(1,1.2);var manipulator=new TestManipulator();
        Manipulation.AddManipulator(f.Target,manipulator);Manipulation.RemoveManipulator(f.Target,manipulator);
        Assert.True(Manipulation.IsManipulationActive(f.Target));f.Send(3);Assert.Single(f.Completed);
    }

    [Fact]
    public void RemovingAManipulatorDoesNotRemoveItsReentrantReplacement()
    {
        var target=new Border{IsManipulationEnabled=true};var next=new TestManipulator();
        var previous=new TestManipulator{Ended=()=>Manipulation.AddManipulator(target,next)};
        Manipulation.AddManipulator(target,previous);Manipulation.RemoveManipulator(target,previous);
        Assert.True(Manipulation.IsManipulationActive(target));Manipulation.RemoveManipulator(target,next);
        Assert.False(Manipulation.IsManipulationActive(target));
    }

    private sealed class TestManipulator : IManipulator
    {
        internal Action? Ended;
        public int Id=>1;
        public Point GetPosition(IInputElement? relativeTo)=>new();
        public event EventHandler Updated { add { } remove { } }
        public void ManipulationEnded(bool cancel)=>Ended?.Invoke();
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Fixture : IDisposable
    {
        internal readonly Window Window = new() { Width = 500, Height = 400, TitleBarStyle = WindowTitleBarStyle.Native };
        internal readonly Canvas Canvas = new() { Width = 500, Height = 400 };
        internal readonly EligibilityBorder Target = new() { Width = 180, Height = 90, IsManipulationEnabled = true,
            Background = new SolidColorBrush(Color.FromRgb(40, 110, 160)) };
        internal readonly FakePlatform Native = new();
        internal readonly List<string> Order = [];
        internal readonly List<ManipulationDeltaEventArgs> Deltas = [];
        internal readonly List<ManipulationCompletedEventArgs> Completed = [];
        internal bool CompletedActive;
        private readonly double _dpi;
        internal Fixture(double dpi = 1)
        {
            _dpi = dpi;Window.Content = Canvas;Canvas.Children.Add(Target);Jalium.UI.Controls.Canvas.SetLeft(Target,35);Jalium.UI.Controls.Canvas.SetTop(Target,40);
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay",Private)!.Invoke(Window,[Visibility.Visible]);
            Window.Measure(new(500,400));Window.Arrange(new(0,0,500,400));
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Window,(nint)11);
            typeof(Window).GetField("_platformWindow",Private)!.SetValue(Window,Native);
            typeof(Window).GetField("_dpiScale",Private)!.SetValue(Window,(float)dpi);
            var events = new (RoutedEvent Event, string Name)[] {
                (UIElement.PreviewManipulationStartingEvent,"starting-preview"),(UIElement.ManipulationStartingEvent,"starting"),
                (UIElement.PreviewManipulationStartedEvent,"started-preview"),(UIElement.ManipulationStartedEvent,"started"),
                (UIElement.PreviewManipulationDeltaEvent,"delta-preview"),(UIElement.ManipulationDeltaEvent,"delta"),
                (UIElement.PreviewManipulationCompletedEvent,"completed-preview"),(UIElement.ManipulationCompletedEvent,"completed") };
            foreach(var item in events)Target.AddHandler(item.Event,new RoutedEventHandler((_,_)=>Order.Add(item.Name)));
            Target.AddHandler(UIElement.ManipulationDeltaEvent,new RoutedEventHandler((_,e)=>Deltas.Add((ManipulationDeltaEventArgs)e)));
            Target.AddHandler(UIElement.ManipulationCompletedEvent,new RoutedEventHandler((_,e)=>
            { CompletedActive=Manipulation.IsManipulationActive(Target);Completed.Add((ManipulationCompletedEventArgs)e); }));
        }
        internal MacOSGestureRequest Send(int phase,double scale=1,double rotation=0,int kind=1,ulong sequence=1,uint flags=0,double? timestamp=null)
        {
            var point=Target.TranslatePoint(new(10,10),Window);
            var request=new MacOSGestureRequest(new(sequence,kind,phase,point.X*_dpi,point.Y*_dpi,scale,rotation,timestamp ?? 1+Order.Count*.02,flags));
            try { typeof(Window).GetMethod("HandleMacOSGesture",Private)!.Invoke(Window,[request]); }
            catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            return request;
        }
        internal ManipulationInertiaProcessor Inertia()
        { var session=typeof(Window).GetField("_macOSManipulation",Private)!.GetValue(Window)!;
          return (ManipulationInertiaProcessor)session.GetType().GetField("Inertia",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!; }
        public void Dispose()
        {
            if(Manipulation.IsManipulationActive(Target))Manipulation.CompleteManipulation(Target);
            typeof(Window).GetField("_platformWindow",Private)!.SetValue(Window,null);
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Window,nint.Zero);
            Window.Content=null;Window.Close();
        }
    }
    private sealed class EligibilityBorder : Border
    {
        internal Action? OnEnabled;
        protected override bool IsEnabledCore
        {get{var action=OnEnabled;OnEnabled=null;action?.Invoke();return base.IsEnabledCore;}}
    }
    internal sealed class FakePlatform : IPlatformWindow, IPlatformGestureInput
    {
        internal int Activations;
        public bool SupportsIndirectGestures { get; set; } = true;
        public nint NativeHandle => 11;
        public NativeSurfaceDescriptor GetSurface()=>default;
        public void Show(){} public void Hide(){} public void Close(){} public void Dispose(){}
        public void SetTitle(string value){} public void Resize(int width,int height){} public void Move(int x,int y){}
        public int GetWidth()=>500; public int GetHeight()=>400;public void GetPosition(out int x,out int y){x=y=0;}
        public void SetMinMaxSize(int a,int b,int c,int d){}public bool BeginMoveDrag()=>false;public bool BeginResizeDrag(int edge)=>false;
        public bool SetIcon(uint[]? p,int w,int h)=>true;public bool SetTopmost(bool v)=>true;public bool SetEnabled(bool v)=>true;
        public bool SetOpacity(double v)=>true;public bool SetShowInTaskbar(bool v)=>true;public bool SetResizable(bool v)=>true;
        public bool SetDecorated(bool v)=>true;public bool SetOwner(nint v)=>true;public bool Activate(){Activations++;return true;}
        public bool ShowSystemMenu(int x,int y)=>false;public void SetState(WindowState s){}public WindowState GetState()=>WindowState.Normal;
        public void Invalidate(){}public float GetDpiScale()=>1;public int GetMonitorRefreshRate()=>60;public void SetCursor(int s){}
        public void UpdateImeContext(PlatformImeContext c){}public void SetEventHandler(Action<PlatformEvent>? h){}
    }
}
