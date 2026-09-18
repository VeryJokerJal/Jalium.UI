using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class RenderWorkerDispatcherLifetimeTests
{
    [Fact]
    public void ThrowingCancellationObserver_PreservesExceptionAfterReleasingDispatcher()
    {
        var owner = Dispatcher.CurrentDispatcher;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                var expected = new InvalidOperationException("Cancellation observer failed.");
                var first = dispatcher.BeginInvoke(() => throw new InvalidOperationException("Must not execute."));
                var second = dispatcher.BeginInvoke(() => throw new InvalidOperationException("Must not execute."));
                first.Aborted += (_, _) => throw expected;
                int completedShutdown = 0;
                dispatcher.ShutdownFinished += (_, _) => completedShutdown++;

                Assert.Same(expected, Assert.Throws<InvalidOperationException>(dispatcher.DisposeCore));
                Assert.Equal(DispatcherOperationStatus.Aborted, first.Status);
                Assert.Equal(DispatcherOperationStatus.Aborted, second.Status);
                Assert.Null(Dispatcher.FromThread(Thread.CurrentThread));
                Assert.True(dispatcher.HasShutdownFinished);
                Assert.Equal(1, completedShutdown);
                dispatcher.DisposeCore();
                Assert.Equal(1, completedShutdown);
            }
            catch (Exception ex) { failure = ex; }
            finally { dispatcher.DisposeCore(); }
        }) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.False(owner.HasShutdownStarted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WorkerExit_ReleasesItsDispatcherAndCancelsQueuedWork(bool waitFails, bool cancellationThrows)
    {
        var owner = Dispatcher.CurrentDispatcher;
        var window = new Window();
        using var wake = new AutoResetEvent(false);
        Field("_rtFrameAvailable").SetValue(window, wake);
        Field("_renderThreadStop").SetValue(window, !waitFails);
        if (waitFails) wake.Dispose();
        Dispatcher? remainingRegistration = null;
        DispatcherOperationStatus? pendingStatus = null;
        DispatcherOperationStatus? laterStatus = null;
        Exception? failure = null;
        int callbacks = 0;
        var worker = new Thread(() =>
        {
            Dispatcher? dispatcher = null;
            try
            {
                // Drawing resources inherit DispatcherObject, just like the
                // RenderTargetDrawingContext created by the real render worker.
                var drawing = new DrawingGroup();
                dispatcher = drawing.Dispatcher;
                var pending = dispatcher.BeginInvoke(() => callbacks++);
                var later = dispatcher.BeginInvoke(() => callbacks++);
                if (cancellationThrows)
                    pending.Aborted += (_, _) => throw new InvalidOperationException("Cancellation observer failed.");
                try
                {
                    typeof(Window).GetMethod("RenderThreadLoop", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, null);
                    if (waitFails) throw new InvalidOperationException("The disposed wake did not fail.");
                }
                catch (TargetInvocationException ex) when (waitFails && ex.InnerException is ObjectDisposedException)
                {
                }
                remainingRegistration = Dispatcher.FromThread(Thread.CurrentThread);
                pendingStatus = pending.Status;
                laterStatus = later.Status;
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // A failing pre-fix run must not retain its synthetic dispatcher.
                dispatcher?.DisposeCore();
            }
        }) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "The render worker did not terminate.");
        window.Close();
        Assert.Null(failure);
        Assert.Null(remainingRegistration);
        Assert.Equal(DispatcherOperationStatus.Aborted, pendingStatus);
        Assert.Equal(DispatcherOperationStatus.Aborted, laterStatus);
        Assert.Equal(0, callbacks);
        Assert.Same(owner, Dispatcher.FromThread(Thread.CurrentThread));
        Assert.False(owner.HasShutdownStarted);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void PresentedGpuRoundTrips_DoNotRegisterRetiredRenderThreads()
    {
        RenderContextEmptyWindowTests.DrainAllContexts();
        var owner = Dispatcher.CurrentDispatcher;
        var window = new Window { Width = 320, Height = 220, ShowActivated = false };
        var retired = new List<Thread>();
        try
        {
            window.Show();
            for (int round = 0; round < 3; round++)
            {
                window.Content = new Border { Background = Brushes.Red };
                Assert.Equal(RenderBackend.D3D12, window.CurrentRenderBackend);
                long before = window.FrameHistory.TotalFrames;
                window.ForceRenderFrame();
                var clock = Stopwatch.StartNew();
                while (window.FrameHistory.TotalFrames <= before && clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    window.Dispatcher.ProcessQueue();
                    Thread.Sleep(1);
                }
                Assert.True(window.FrameHistory.TotalFrames > before, "The GPU worker did not present a frame.");
                var worker = Assert.IsType<Thread>(Field("_renderThread").GetValue(window));
                Assert.NotNull(Dispatcher.FromThread(worker));
                retired.Add(worker);

                window.Content = null;
                Assert.Equal(RenderBackend.Software, window.CurrentRenderBackend);
                Assert.False(worker.IsAlive);
                Assert.Null(Dispatcher.FromThread(worker));
                Assert.Same(owner, Dispatcher.FromThread(Thread.CurrentThread));
                Assert.False(owner.HasShutdownStarted);
            }
            window.Close();
            Assert.All(retired, thread => Assert.Null(Dispatcher.FromThread(thread)));
        }
        finally
        {
            window.Close();
            // Pre-fix threads have already exited, so no callback can use their
            // registration while this test removes it from the static table.
            foreach (var thread in retired) Dispatcher.FromThread(thread)?.DisposeCore();
            RenderContextEmptyWindowTests.DrainAllContexts();
        }
    }

    private static FieldInfo Field(string name) =>
        typeof(Window).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
