using System.Reflection;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class DispatcherHooksOperationWpfParityTests
{
    [Fact]
    public void HooksExposeTypedOperationLifecycleAndInactiveNotifications()
    {
        var emptyArgs = new DispatcherHookEventArgs(null!);
        Assert.Null(emptyArgs.Operation);
        Assert.Null(emptyArgs.Dispatcher);

        var dispatcher = Dispatcher.GetForCurrentThread();
        var canonicalDispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.ProcessQueue();
        var hooks = dispatcher.Hooks;
        var order = new List<string>();
        var notifications = new List<(object Sender, DispatcherHookEventArgs Args)>();

        DispatcherHookEventHandler posted = (sender, args) =>
        {
            order.Add("posted");
            notifications.Add((sender, args));
        };
        DispatcherHookEventHandler started = (sender, args) =>
        {
            order.Add("started");
            notifications.Add((sender, args));
        };
        DispatcherHookEventHandler completed = (sender, args) =>
        {
            order.Add("completed");
            notifications.Add((sender, args));
        };
        EventHandler inactive = (_, _) => order.Add("inactive");

        hooks.OperationPosted += posted;
        hooks.OperationStarted += started;
        hooks.OperationCompleted += completed;
        hooks.DispatcherInactive += inactive;
        try
        {
            DispatcherOperation operation = dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                () => order.Add("callback"));

            Assert.Same(operation.Task, operation.Task);
            Assert.False(operation.Task.IsCompleted);

            dispatcher.ProcessQueue();

            Assert.True(operation.Task.IsCompletedSuccessfully);
            Assert.Equal(
                new[] { "posted", "started", "callback", "completed", "inactive" },
                order);
            Assert.Equal(3, notifications.Count);
            Assert.All(notifications, item => Assert.Same(canonicalDispatcher, item.Sender));
            Assert.All(notifications, item => Assert.Same(canonicalDispatcher, item.Args.Dispatcher));
            Assert.All(notifications, item => Assert.Same(operation, item.Args.Operation));
        }
        finally
        {
            hooks.OperationPosted -= posted;
            hooks.OperationStarted -= started;
            hooks.OperationCompleted -= completed;
            hooks.DispatcherInactive -= inactive;
        }
    }

    [Fact]
    public void PriorityAndAbortHooksCarryTheOperationAndCancelItsTask()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        var hooks = dispatcher.Hooks;
        DispatcherOperation? priorityOperation = null;
        DispatcherOperation? abortedOperation = null;

        DispatcherHookEventHandler priorityChanged = (_, args) =>
            priorityOperation = args.Operation;
        DispatcherHookEventHandler aborted = (_, args) =>
            abortedOperation = args.Operation;

        hooks.OperationPriorityChanged += priorityChanged;
        hooks.OperationAborted += aborted;
        try
        {
            DispatcherOperation operation = dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => throw new InvalidOperationException("must not run"));

            operation.Priority = Jalium.UI.Threading.DispatcherPriority.Input;
            Assert.Same(operation, priorityOperation);
            Assert.True(operation.Abort());
            Assert.Same(operation, abortedOperation);
            Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status);
            Assert.True(operation.Task.IsCanceled);
            Assert.False(operation.Abort());
        }
        finally
        {
            hooks.OperationPriorityChanged -= priorityChanged;
            hooks.OperationAborted -= aborted;
        }
    }

    [Fact]
    public void InvokeDelegateCoreIsVirtualAndSuppliesResultAndTaskCompletion()
    {
        MethodInfo method = typeof(DispatcherOperation).GetMethod(
            "InvokeDelegateCore",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);

        var operation = new ResultDispatcherOperation(Dispatcher.GetForCurrentThread());
        operation.InvokeInternal();

        Assert.Equal(42, operation.Result);
        Assert.True(operation.Task.IsCompletedSuccessfully);
        Assert.Equal(DispatcherOperationStatus.Completed, operation.Status);
    }

    private sealed class ResultDispatcherOperation : DispatcherOperation
    {
        public ResultDispatcherOperation(Dispatcher dispatcher)
            : base(
                dispatcher,
                DispatcherPriority.Normal,
                () => throw new InvalidOperationException("base action must not run"),
                critical: false,
                observed: true)
        {
        }

        protected override object InvokeDelegateCore() => 42;
    }
}
