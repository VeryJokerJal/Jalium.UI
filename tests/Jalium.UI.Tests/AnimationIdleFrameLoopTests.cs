using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI.Animation;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationIdleFrameLoopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleMessagePump_AdvancesAndInvalidatesAnimationWithoutInput(bool useStoryboard)
    {
        if (!OperatingSystem.IsWindows()) return;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { RunIdleMessagePump(useStoryboard); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "The animation dispatcher did not exit within its timeout.");
        failure?.Throw();
    }

    private static void RunIdleMessagePump(bool useStoryboard)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.ProcessQueue();
        var mainField = typeof(Dispatcher).Assembly.GetType("Jalium.UI.Threading.DispatcherCore")!
            .GetField("_mainDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousMain = mainField.GetValue(null);
        bool wasSuspended = CompositionTarget.IsRenderingSuspended;
        var host = new Host();
        var transform = new RotateTransform();
        var element = new Element { RenderTransform = transform };
        host.Add(element);
        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(.25)) { RepeatBehavior = RepeatBehavior.Forever };
        var storyboard = new Storyboard();
        storyboard.AccelerationRatio = 1;
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, new PropertyPath(RotateTransform.AngleProperty));
        var frame = new DispatcherFrame();
        bool timedOut = false;
        int changes = 0;
        transform.Changed += (_, _) =>
        {
            if (++changes >= 5) frame.Continue = false;
        };

        try
        {
            Dispatcher.SetAsMainThread();
            CompositionTarget.ResumeRendering();
            CompositionTarget.NotifyRenderableWindowAdded();
            using var timeout = new Timer(_ => dispatcher.BeginInvoke(() =>
            {
                timedOut = true;
                frame.Continue = false;
            }), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
            host.Invalidations = 0;
            if (useStoryboard) storyboard.Begin();
            else transform.BeginAnimation(RotateTransform.AngleProperty, animation);

            // Run the real Windows dispatcher and frame timer. No synthetic
            // animation frames, mouse events, button clicks, or paint messages.
            Dispatcher.PushFrame(frame);

            Assert.False(timedOut, "The idle frame source did not advance the animation.");
            Assert.True(changes >= 5);
            Assert.True(host.Invalidations >= 5);
            Assert.NotEqual(0d, transform.Angle);
        }
        finally
        {
            storyboard.Stop();
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            CompositionTarget.NotifyRenderableWindowRemoved();
            dispatcher.ProcessQueue();
            AnimationManager.ProcessFrame(System.Diagnostics.Stopwatch.GetTimestamp());
            if (wasSuspended) CompositionTarget.SuspendRendering();
            mainField.SetValue(null, previousMain);
            dispatcher.CoreDispatcher.Dispose();
        }
    }

    private sealed class Element : FrameworkElement;

    private sealed class Host : FrameworkElement, IWindowHost
    {
        internal int Invalidations;
        internal void Add(UIElement child) => AddVisualChild(child);
        public void InvalidateWindow() => Invalidations++;
        public void AddDirtyElement(UIElement element) { }
        public void RequestFullInvalidation() { }
        public void SetNativeCapture() { }
        public void ReleaseNativeCapture() { }
    }
}
