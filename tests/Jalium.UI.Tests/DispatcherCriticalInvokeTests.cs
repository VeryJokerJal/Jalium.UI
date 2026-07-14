using Jalium.UI;

namespace Jalium.UI.Tests;

public class DispatcherCriticalInvokeTests
{
    [Fact]
    public void BeginInvoke_AfterDispose_IsAbortedWithoutTouchingDisposedWakeResources()
    {
        Jalium.UI.Threading.DispatcherOperation? operation = null;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            dispatcher.DisposeCore();

            exception = Record.Exception(
                () => operation = dispatcher.BeginInvoke(static () => { }));
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Dispatcher test thread did not exit.");

        Assert.Null(exception);
        Assert.NotNull(operation);
        Assert.Equal(
            Jalium.UI.Threading.DispatcherOperationStatus.Aborted,
            operation!.Status);
    }

    [Fact]
    public void BeginInvoke_WhenCallbackThrows_ProcessQueueSwallowsException()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.BeginInvoke(() => throw new InvalidOperationException("normal"));

        var exception = Record.Exception(dispatcher.ProcessQueue);

        Assert.Null(exception);
    }

    [Fact]
    public void BeginInvokeCritical_WhenCallbackThrows_ProcessQueueRethrowsException()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.BeginInvokeCritical(() => throw new InvalidOperationException("critical"));

        Assert.Throws<InvalidOperationException>(dispatcher.ProcessQueue);
    }
}
