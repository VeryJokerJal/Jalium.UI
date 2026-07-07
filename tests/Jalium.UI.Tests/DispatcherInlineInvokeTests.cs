using Jalium.UI;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the same-thread inline fast path of <see cref="Dispatcher.InvokeAsync(System.Action)"/>
/// (and the generic overload): a Normal/Send callback issued from the dispatcher thread runs
/// synchronously and returns an already-completed Task, instead of being parked on the queue until
/// the message pump drains it. This is the dispatcher half of the async-hosted-work startup
/// deadlock fix; the explicit-priority, DisableProcessing, re-entrancy-depth, and exception
/// behaviours are guarded here so the fast path cannot regress into over-inlining or a stack
/// overflow.
/// </summary>
public class DispatcherInlineInvokeTests
{
    [Fact]
    public void InvokeAsync_OnDispatcherThread_RunsInlineAndCompletesSynchronously()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue(); // drain anything a prior test on this (reused) thread left queued

        int ran = 0;
        Task task = dispatcher.InvokeAsync(() => ran++);

        // The callback ran synchronously on this thread and the returned Task is already complete —
        // no ProcessQueue / message pump was needed.
        Assert.Equal(1, ran);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InvokeAsyncOfT_OnDispatcherThread_ReturnsResultInline()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        Task<int> task = dispatcher.InvokeAsync(() => 42);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(42, await task); // already complete: awaiting is non-blocking
    }

    [Fact]
    public void InvokeAsync_ExplicitBackgroundPriority_DefersUntilProcessQueue()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        int ran = 0;
        Task task = dispatcher.InvokeAsync(() => ran++, DispatcherPriority.Background);

        // Background < Normal: the fast path must NOT inline — the work stays pending until pumped.
        Assert.Equal(0, ran);
        Assert.False(task.IsCompleted);

        dispatcher.ProcessQueue();

        Assert.Equal(1, ran);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void InvokeAsync_WhenProcessingDisabled_DefersUntilEnabled()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        int ran = 0;
        Task task;
        using (dispatcher.DisableProcessing())
        {
            // Normal priority on the dispatcher thread, but the caller fenced off a
            // re-entrancy-free region: the inline fast path must hold the work back.
            task = dispatcher.InvokeAsync(() => ran++);
            Assert.Equal(0, ran);
            Assert.False(task.IsCompleted);
        }

        // Disposing re-enables processing (which re-posts a wake); draining now runs the callback.
        dispatcher.ProcessQueue();
        Assert.Equal(1, ran);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void InvokeAsync_OnDispatcherThread_WhenCallbackThrows_ReturnsFaultedTask()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        Task action = dispatcher.InvokeAsync(() => throw new InvalidOperationException("boom-action"));
        Assert.True(action.IsFaulted);
        Assert.IsType<InvalidOperationException>(action.Exception!.InnerException);

        Task<int> func = dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("boom-func"));
        Assert.True(func.IsFaulted);
        Assert.IsType<InvalidOperationException>(func.Exception!.InnerException);
    }

    [Fact]
    public void InvokeAsync_DeepReentrantInline_BoundsDepthInsteadOfOverflowing()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        // Re-enter InvokeAsync from inside its own inlined callback. Without the depth cap this is an
        // unbounded synchronous recursion (the shape of an `await Task.Yield()` storm) that would
        // overflow the stack. 'target' is past the cap (so the fall-back to the queue is exercised)
        // yet shallow enough that, were the cap removed, the test fails by assertion rather than by
        // crashing the process with an uncatchable StackOverflowException.
        const int target = 100;
        int totalSteps = 0;
        int inlineDepth = 0;
        int maxInlineDepth = 0;
        bool fellBackToQueue = false;

        void Step()
        {
            totalSteps++;
            inlineDepth++;
            if (inlineDepth > maxInlineDepth) maxInlineDepth = inlineDepth;
            if (totalSteps < target)
            {
                Task t = dispatcher.InvokeAsync(Step);
                if (!t.IsCompleted) fellBackToQueue = true; // depth cap forced a queue instead of inlining
            }
            inlineDepth--;
        }

        var ex = Record.Exception(() => Step());
        // Drive the steps that were queued once the cap engaged. ProcessQueue is a
        // bounded batch (it only processes what was queued when it was entered, so
        // continuous animation can't starve the message pump) — steps queued by a
        // step within the batch land in the NEXT batch, so pump until quiescent.
        int drained;
        do
        {
            drained = totalSteps;
            dispatcher.ProcessQueue();
        } while (totalSteps > drained);

        Assert.Null(ex);
        Assert.True(fellBackToQueue, "inline re-entrancy was not bounded; a continuation storm would overflow the stack");
        Assert.True(maxInlineDepth < target, $"inline depth {maxInlineDepth} was not capped below {target}");
        Assert.Equal(target, totalSteps);
    }
}
