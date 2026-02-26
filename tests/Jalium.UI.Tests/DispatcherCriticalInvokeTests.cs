using Jalium.UI;

namespace Jalium.UI.Tests;

public class DispatcherCriticalInvokeTests
{
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
