using System.Reflection;
using System.Runtime.ExceptionServices;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DispatcherTimerLifecycleTests
{
    private static readonly TimeSpan LongInterval = TimeSpan.FromMilliseconds(int.MaxValue);

    [Fact]
    public void QueuedTickFromStoppedGenerationDoesNotRunAfterRestart()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = CreateDedicatedTimer(dispatcher);
            int ticks = 0;
            timer.Tick += (_, _) => ticks++;

            try
            {
                timer.Start();
                object? firstGeneration = CaptureActiveGeneration(timer);

                // Queue generation A's tick, but do not let the dispatcher drain it yet.
                InvokeTimerCallbackFromWorker(timer, firstGeneration);

                timer.Stop();
                timer.Start();
                dispatcher.ProcessQueue();

                Assert.Equal(0, ticks);
            }
            finally
            {
                timer.Stop();
                dispatcher.ProcessQueue();
            }
        });
    }

    [Fact]
    public void CallbackFromStoppedGenerationCannotQueueIntoRestartedTimer()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = CreateDedicatedTimer(dispatcher);
            int ticks = 0;
            timer.Tick += (_, _) => ticks++;

            try
            {
                timer.Start();
                object? firstGeneration = CaptureActiveGeneration(timer);
                timer.Stop();
                timer.Start();

                // Timer.Dispose cannot recall a callback that already started. Simulate
                // that callback reaching DispatcherTimer only after generation B exists.
                InvokeTimerCallbackFromWorker(timer, firstGeneration);
                dispatcher.ProcessQueue();

                Assert.Equal(0, ticks);
            }
            finally
            {
                timer.Stop();
                dispatcher.ProcessQueue();
            }
        });
    }

    [Fact]
    public void IntervalChangeInvalidatesAlreadyQueuedTick()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = CreateDedicatedTimer(dispatcher);
            int ticks = 0;
            timer.Tick += (_, _) => ticks++;

            try
            {
                timer.Start();
                object? firstGeneration = CaptureActiveGeneration(timer);
                InvokeTimerCallbackFromWorker(timer, firstGeneration);

                // A running Interval change is implemented as StopTimer + StartTimer.
                // The queued operation still belongs to the old schedule.
                timer.Interval = TimeSpan.FromMilliseconds(int.MaxValue - 1L);
                dispatcher.ProcessQueue();

                Assert.Equal(0, ticks);
            }
            finally
            {
                timer.Stop();
                dispatcher.ProcessQueue();
            }
        });
    }

    [Fact]
    public void RepeatedStopRemovesQueuedTicksInsteadOfAccumulatingNoOps()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = CreateDedicatedTimer(dispatcher);
            int baselineQueueDepth = GetDispatcherQueueDepth(dispatcher);

            try
            {
                for (int cycle = 0; cycle < 12; cycle++)
                {
                    timer.Start();
                    object? generation = CaptureActiveGeneration(timer);

                    // The owner thread deliberately does not pump while this synthetic
                    // worker callback posts its tick. Verify a real pending operation
                    // exists, then require Stop to Abort and unlink it immediately.
                    InvokeTimerCallbackFromWorker(timer, generation);
                    Assert.Equal(
                        baselineQueueDepth + 1,
                        GetDispatcherQueueDepth(dispatcher));

                    timer.Stop();
                    Assert.Equal(
                        baselineQueueDepth,
                        GetDispatcherQueueDepth(dispatcher));
                }
            }
            finally
            {
                timer.Stop();
            }
        });
    }

    [Fact]
    public void SynchronouslyAbortedDispatchReleasesGenerationTickGate()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = CreateDedicatedTimer(dispatcher);

            try
            {
                timer.Start();
                object? generation = CaptureActiveGeneration(timer);
                Assert.NotNull(generation);

                // EnqueueOperation returns an already-Aborted DispatcherOperation after
                // shutdown. No UI callback will run to reopen the timer's gate.
                dispatcher.DisposeCore();
                InvokeTimerCallbackFromWorker(timer, generation);

                Assert.Equal(0, GetGenerationTickPending(generation!));
                Assert.Null(GetGenerationPendingOperation(generation!));
            }
            finally
            {
                timer.Stop();
            }
        });
    }

    [Fact]
    public void CapturedRenderingHandlerFromStoppedGenerationDoesNotTickRestartedTimer()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.Zero,
            };
            int ticks = 0;
            timer.Tick += (_, _) => ticks++;

            try
            {
                timer.Start();
                EventHandler oldRenderingHandler = CaptureRenderingHandler(timer);

                timer.Stop();
                timer.Start();

                // CompositionTarget invokes a snapshot. Removing the handler cannot
                // remove this already-captured delegate from that snapshot.
                oldRenderingHandler(null, EventArgs.Empty);

                Assert.Equal(0, ticks);
            }
            finally
            {
                timer.Stop();
            }
        });
    }

    [Fact]
    public void CapturedFramePacedHandlerDoesNotTickAfterSwitchToDedicatedInterval()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.Zero,
            };
            int ticks = 0;
            timer.Tick += (_, _) => ticks++;

            try
            {
                timer.Start();
                EventHandler oldRenderingHandler = CaptureRenderingHandler(timer);

                timer.Interval = LongInterval;
                oldRenderingHandler(null, EventArgs.Empty);

                Assert.Equal(0, ticks);
            }
            finally
            {
                timer.Stop();
            }
        });
    }

    [Fact]
    public void RepeatedStartStopAndIntervalSwitchKeepOneLogicalRenderingSubscription()
    {
        RunOnFreshDispatcherThread(dispatcher =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.Zero,
            };
            int baselineSubscribers = GetCompositionSubscriberCount();

            try
            {
                for (int cycle = 0; cycle < 5; cycle++)
                {
                    timer.Start();
                    EventHandler frameHandler = CaptureRenderingHandler(timer);
                    AssertRenderingSubscription(frameHandler, expectedCount: 1);
                    Assert.Equal(baselineSubscribers + 1, GetCompositionSubscriberCount());

                    timer.Start();
                    AssertRenderingSubscription(frameHandler, expectedCount: 1);
                    Assert.Equal(baselineSubscribers + 1, GetCompositionSubscriberCount());

                    timer.Interval = LongInterval;
                    AssertRenderingSubscription(frameHandler, expectedCount: 0);
                    Assert.Equal(baselineSubscribers, GetCompositionSubscriberCount());

                    timer.Interval = TimeSpan.Zero;
                    EventHandler restartedFrameHandler = CaptureRenderingHandler(timer);
                    AssertRenderingSubscription(restartedFrameHandler, expectedCount: 1);
                    Assert.Equal(baselineSubscribers + 1, GetCompositionSubscriberCount());

                    timer.Stop();
                    timer.Stop();
                    AssertRenderingSubscription(restartedFrameHandler, expectedCount: 0);
                    Assert.Equal(baselineSubscribers, GetCompositionSubscriberCount());
                }
            }
            finally
            {
                timer.Stop();
            }
        });
    }

    private static DispatcherTimer CreateDedicatedTimer(Dispatcher dispatcher)
        => new(DispatcherPriority.Background, dispatcher)
        {
            Interval = LongInterval,
        };

    /// <summary>
    /// The pre-fix implementation has no generation field and ignores the callback
    /// state, so null deliberately exercises its old behavior. The fixed implementation
    /// returns the exact registration object carried by System.Threading.Timer.
    /// </summary>
    private static object? CaptureActiveGeneration(DispatcherTimer timer)
        => typeof(DispatcherTimer)
            .GetField("_activeGeneration", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(timer);

    private static EventHandler CaptureRenderingHandler(DispatcherTimer timer)
    {
        object? generation = CaptureActiveGeneration(timer);
        if (generation != null)
        {
            FieldInfo? handlerField = generation.GetType().GetField(
                "RenderingHandler",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handlerField?.GetValue(generation) is EventHandler generationHandler)
            {
                Assert.Single(generationHandler.GetInvocationList());
                return generationHandler;
            }
        }

        // Compatibility with the pre-fix implementation: its single instance method
        // is subscribed directly and therefore targets the DispatcherTimer itself.
        return GetRenderingInvocationList()
            .OfType<EventHandler>()
            .Single(handler =>
                ReferenceEquals(handler.Target, timer) &&
                handler.Method.Name == "OnCompositionTargetRendering");
    }

    private static void InvokeTimerCallbackFromWorker(DispatcherTimer timer, object? callbackState)
    {
        MethodInfo callback = typeof(DispatcherTimer).GetMethod(
            "OnTimerCallback",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                callback.Invoke(timer, [callbackState]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failure = exception.InnerException;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "DispatcherTimerLifecycleTests.Callback",
        };

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(5)),
            "Synthetic timer callback did not finish within the timeout.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void AssertRenderingSubscription(
        EventHandler renderingHandler,
        int expectedCount)
    {
        Delegate[] currentHandlers = GetRenderingInvocationList();
        Assert.Single(renderingHandler.GetInvocationList());
        Assert.Equal(
            expectedCount,
            currentHandlers.Count(handler => handler.Equals(renderingHandler)));
    }

    private static int GetDispatcherQueueDepth(Dispatcher dispatcher)
    {
        object coreDispatcher = dispatcher.CoreDispatcher;
        FieldInfo queueField = coreDispatcher.GetType().GetField(
            "_queue",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        object queue = queueField.GetValue(coreDispatcher)!;
        PropertyInfo countProperty = queue.GetType().GetProperty(nameof(ICollection<object>.Count))!;
        return (int)countProperty.GetValue(queue)!;
    }

    private static int GetGenerationTickPending(object generation)
    {
        FieldInfo field = generation.GetType().GetField(
            "TickPending",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (int)field.GetValue(generation)!;
    }

    private static object? GetGenerationPendingOperation(object generation)
    {
        FieldInfo field = generation.GetType().GetField(
            "PendingOperation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return field.GetValue(generation);
    }

    private static Delegate[] GetRenderingInvocationList()
    {
        FieldInfo field = typeof(CompositionTarget).GetField(
            "Rendering",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return field.GetValue(null) is Delegate handlers
            ? handlers.GetInvocationList()
            : [];
    }

    private static int GetCompositionSubscriberCount()
    {
        FieldInfo field = typeof(CompositionTarget).GetField(
            "_subscriberCount",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (int)field.GetValue(null)!;
    }

    private static void RunOnFreshDispatcherThread(Action<Dispatcher> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher? dispatcher = null;
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                action(dispatcher);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dispatcher?.DisposeCore();
            }
        })
        {
            IsBackground = true,
            Name = "DispatcherTimerLifecycleTests.Dispatcher",
        };

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(5)),
            "Fresh dispatcher test thread did not finish within the timeout.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
