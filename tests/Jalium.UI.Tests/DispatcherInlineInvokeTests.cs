using Jalium.UI;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the WPF-shaped <see cref="Dispatcher.InvokeAsync(System.Action)"/> contract.
/// The public API returns a <see cref="DispatcherOperation"/> whose Task represents queued
/// execution; callbacks do not expose the internal Dispatcher's legacy inline fast path.
/// </summary>
public class DispatcherInlineInvokeTests
{
    [Fact]
    public void InvokeAsync_OnDispatcherThread_CompletesOperationWhenQueueIsPumped()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue(); // drain anything a prior test on this (reused) thread left queued

        int ran = 0;
        DispatcherOperation operation = dispatcher.InvokeAsync(() => ran++);

        // The callback ran synchronously on this thread and the returned Task is already complete —
        // no ProcessQueue / message pump was needed.
        Assert.Equal(0, ran);
        Assert.False(operation.Task.IsCompleted);

        dispatcher.ProcessQueue();

        Assert.Equal(1, ran);
        Assert.True(operation.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InvokeAsyncOfT_OnDispatcherThread_CompletesTypedOperationWhenPumped()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        DispatcherOperation<int> operation = dispatcher.InvokeAsync(() => 42);

        Assert.False(operation.Task.IsCompleted);
        dispatcher.ProcessQueue();

        Assert.Equal(42, await operation.Task);
        Assert.True(operation.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void InvokeAsync_ExplicitBackgroundPriority_DefersUntilProcessQueue()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        int ran = 0;
        DispatcherOperation operation = dispatcher.InvokeAsync(() => ran++, DispatcherPriority.Background);

        // Background < Normal: the fast path must NOT inline — the work stays pending until pumped.
        Assert.Equal(0, ran);
        Assert.False(operation.Task.IsCompleted);

        dispatcher.ProcessQueue();

        Assert.Equal(1, ran);
        Assert.True(operation.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void InvokeAsync_WhenProcessingDisabled_DefersUntilEnabled()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        int ran = 0;
        DispatcherOperation operation;
        using (dispatcher.DisableProcessing())
        {
            // Normal priority on the dispatcher thread, but the caller fenced off a
            // re-entrancy-free region: the inline fast path must hold the work back.
            operation = dispatcher.InvokeAsync(() => ran++);
            Assert.Equal(0, ran);
            Assert.False(operation.Task.IsCompleted);
        }

        // Disposing re-enables processing (which re-posts a wake); draining now runs the callback.
        dispatcher.ProcessQueue();
        Assert.Equal(1, ran);
        Assert.True(operation.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InvokeAsync_WhenCallbackThrows_FaultsOperationTaskAfterPumping()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        DispatcherOperation action = dispatcher.InvokeAsync(() => throw new InvalidOperationException("boom-action"));
        DispatcherOperation<int> func = dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("boom-func"));

        Assert.False(action.Task.IsCompleted);
        Assert.False(func.Task.IsCompleted);
        dispatcher.ProcessQueue();

        Assert.True(action.Task.IsFaulted);
        Assert.IsType<InvalidOperationException>(action.Task.Exception!.InnerException);
        await Assert.ThrowsAsync<InvalidOperationException>(() => action.Task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => func.Task);
    }

    [Fact]
    public void InvokeAsync_ReentrantCallbacks_AreQueuedWithoutRecursiveStackGrowth()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        // Re-enter through the canonical public surface. Every next callback is queued as a pending
        // operation, so the callback chain does not recursively consume the current stack.
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
                DispatcherOperation operation = dispatcher.InvokeAsync(Step);
                if (!operation.Task.IsCompleted) fellBackToQueue = true; // depth cap forced a queue instead of inlining
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
        Assert.True(fellBackToQueue);
        Assert.Equal(1, maxInlineDepth);
        Assert.Equal(target, totalSteps);
    }
}
