using System.Runtime.InteropServices;
using Jalium.UI.Core.Platform;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed partial class MacOSDispatcherEventPumpThreadTests
{
    [Fact]
    public async Task SecondaryDispatcherProcessesWorkWithoutEnteringAppKitEventQueue()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var result = await Task.Factory.StartNew(() =>
        {
            Assert.Equal(0, PThreadMain());
            NativePlatformEventPump.PollEvents();
            var dispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;
            var frame = new Jalium.UI.Threading.DispatcherFrame();
            var thread = Environment.CurrentManagedThreadId;
            var ranOn = 0;
            dispatcher.BeginInvoke(() => { ranOn = Environment.CurrentManagedThreadId; frame.Continue = false; });
            Jalium.UI.Threading.Dispatcher.PushFrame(frame);
            return (thread, ranOn);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(result.thread, result.ranOn);
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_main_np")]
    private static partial int PThreadMain();
}
