using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CompositionTargetRequestFrameTests
{
    [Fact]
    public void RequestFrame_FromIdle_PostsExactlyOneAsynchronousFirstFrame()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();

        var compositionType = typeof(CompositionTarget);
        var keepAliveField = GetStaticField(compositionType, "_keepAliveUntilTick");
        var subscriberCountField = GetStaticField(compositionType, "_subscriberCount");
        var framePostedField = GetStaticField(compositionType, "_framePosted");
        var suspendedField = GetStaticField(compositionType, "_suspended");

        var dispatcherCoreType = compositionType.Assembly.GetType("Jalium.UI.Threading.DispatcherCore")
            ?? throw new InvalidOperationException("DispatcherCore type not found.");
        var mainDispatcherField = GetStaticField(dispatcherCoreType, "_mainDispatcher");

        var previousKeepAlive = keepAliveField.GetValue(null);
        var previousSubscriberCount = subscriberCountField.GetValue(null);
        var previousFramePosted = framePostedField.GetValue(null);
        var previousSuspended = suspendedField.GetValue(null);
        var previousMainDispatcher = mainDispatcherField.GetValue(null);
        var frameStarts = 0;
        Action handler = () => frameStarts++;

        try
        {
            Dispatcher.SetAsMainThread();
            keepAliveField.SetValue(null, 0L);
            subscriberCountField.SetValue(null, 0);
            framePostedField.SetValue(null, 0);
            suspendedField.SetValue(null, false);
            CompositionTarget.FrameStarting += handler;

            CompositionTarget.RequestFrame();

            Assert.Equal(0, frameStarts);
            dispatcher.ProcessQueue();
            Assert.Equal(1, frameStarts);

            // The keep-alive deadline is still active, so another invalidation joins the
            // refresh-paced loop instead of posting a second immediate frame.
            CompositionTarget.RequestFrame();
            dispatcher.ProcessQueue();
            Assert.Equal(1, frameStarts);

            CompositionTarget.RequestImmediateFrame();

            Assert.Equal(1, frameStarts);
            dispatcher.ProcessQueue();
            Assert.Equal(2, frameStarts);
        }
        finally
        {
            CompositionTarget.FrameStarting -= handler;
            dispatcher.ProcessQueue();
            keepAliveField.SetValue(null, previousKeepAlive);
            subscriberCountField.SetValue(null, previousSubscriberCount);
            framePostedField.SetValue(null, previousFramePosted);
            suspendedField.SetValue(null, previousSuspended);
            mainDispatcherField.SetValue(null, previousMainDispatcher);
        }
    }

    private static FieldInfo GetStaticField(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"{type.FullName}.{name} field not found.");
}
