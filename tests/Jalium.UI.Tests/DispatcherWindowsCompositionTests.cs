using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class DispatcherWindowsCompositionTests
{
    [RequiresWindowsFact]
    public void CompositionQueue_CanBeShutDownAndRecreatedOnItsOwnerThread()
    {
        RunOnThread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher.CoreDispatcher;
            for (int round = 0; round < 3; round++)
            {
                dispatcher.EnsureWindowsCompositionQueue();
                nint controller = GetController(dispatcher);
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
                    Assert.NotEqual(0, controller);
                else
                    Assert.Equal(0, controller);
                dispatcher.ShutdownWindowsCompositionQueue();
                Assert.Equal(0, GetController(dispatcher));
                dispatcher.ShutdownWindowsCompositionQueue();
            }
        });
    }

    [RequiresWindowsFact]
    public void CompositionQueueShutdown_PreservesTheHostQuitMessage()
    {
        RunOnThread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher.CoreDispatcher;
            dispatcher.EnsureWindowsCompositionQueue();
            PostQuitMessage(37);
            dispatcher.ShutdownWindowsCompositionQueue();
            Assert.True(PeekMessage(out var message, 0, 0x0012, 0x0012, 1));
            Assert.Equal(0x0012u, message.Message);
            Assert.Equal((nint)37, message.WParam);
            Assert.False(PeekMessage(out _, 0, 0x0012, 0x0012, 1));
        });
    }

    internal static void RunOnThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { DispatcherCore.CurrentDispatcher?.Dispose(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Composition owner thread did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static nint GetController(DispatcherCore dispatcher) =>
        (nint)typeof(DispatcherCore).GetField("_windowsCompositionQueueController", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dispatcher)!;

    [StructLayout(LayoutKind.Sequential)]
    private struct MessageData
    {
        public nint Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MessageData message, nint window, uint first, uint last, uint remove);
}
