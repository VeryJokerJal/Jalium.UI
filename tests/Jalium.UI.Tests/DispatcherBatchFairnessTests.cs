using System.Runtime.ExceptionServices;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class DispatcherBatchFairnessTests
{
    [Fact]
    public void ExistingInputRunsBeforeRepostedHigherPriorityWork()
    {
        OnDispatcher(dispatcher =>
        {
            var order = new List<string>();
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                order.Add("initial");
                dispatcher.BeginInvoke(DispatcherPriority.Send, () => order.Add("next"));
            });
            dispatcher.BeginInvoke(DispatcherPriority.Input, () => order.Add("input"));
            dispatcher.ProcessQueue();
            Assert.Equal(new[] { "initial", "input" }, order);
            dispatcher.ProcessQueue();
            Assert.Equal(new[] { "initial", "input", "next" }, order);
        });
    }

    [Fact]
    public void BatchRetainsPriorityChangesAbortsAndFifo()
    {
        OnDispatcher(dispatcher =>
        {
            var order = new List<string>();
            var promoted = dispatcher.BeginInvoke(DispatcherPriority.Background, () => order.Add("promoted"));
            var aborted = dispatcher.BeginInvoke(DispatcherPriority.Input, () => order.Add("aborted"));
            dispatcher.BeginInvoke(DispatcherPriority.Input, () => order.Add("first"));
            dispatcher.BeginInvoke(DispatcherPriority.Input, () => order.Add("second"));
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                order.Add("normal");
                promoted.Priority = DispatcherPriority.Send;
                aborted.Abort();
            });
            dispatcher.ProcessQueue();
            Assert.Equal(new[] { "normal", "promoted", "first", "second" }, order);
        });
    }

    private static void OnDispatcher(Action<Dispatcher> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try { action(dispatcher); }
            catch (Exception error) { failure = error; }
            finally { dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Dispatcher batch did not return");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
