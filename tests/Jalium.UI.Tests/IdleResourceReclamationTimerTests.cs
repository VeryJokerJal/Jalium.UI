using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Jalium.UI.Hosting;
using Jalium.UI.Media;
using Jalium.UI.Tests.TestHelpers;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class IdleResourceReclamationTimerTests
{
    private sealed class ReclaimableVisual : UIElement, IReclaimableResource
    {
        public int Calls;
        public int ReclaimThreadId;
        public void ReclaimIdleResources()
        {
            Calls++;
            ReclaimThreadId = Environment.CurrentManagedThreadId;
        }
    }

    [Fact]
    public void IdleWindowIsReclaimedOnItsDispatcherWithoutRenderingFrames()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher? dispatcher = null;
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                using var reclaimer = new ResourceReclaimer(new ResourceReclamationOptions
                {
                    IdleTimeoutMs = 250,
                    ScanFrameInterval = int.MaxValue,
                    ReclaimBackendCaches = false,
                });
                reclaimer.Start();
                reclaimer.Start(); // idempotent: one observer and one timer
                var visual = new ReclaimableVisual();
                visual.Render(new StubDrawingContext());
                var clock = Stopwatch.StartNew();
                while (visual.Calls == 0 && clock.ElapsedMilliseconds < 4000)
                {
                    dispatcher.ProcessQueue();
                    Thread.Sleep(10);
                }
                Assert.Equal(1, visual.Calls);
                Assert.Equal(Environment.CurrentManagedThreadId, visual.ReclaimThreadId);
                reclaimer.Stop();
                Assert.False(visual.IsTrackedByIdleReclaimer);
                Thread.Sleep(1100);
                dispatcher.ProcessQueue();
                Assert.Equal(1, visual.Calls);
            }
            catch (Exception exception) { failure = exception; }
            finally { dispatcher?.DisposeCore(); }
        }) { IsBackground = true, Name = "IdleReclamationTest" };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Idle reclaimer test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
