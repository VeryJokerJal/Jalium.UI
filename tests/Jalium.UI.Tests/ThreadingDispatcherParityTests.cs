using System.ComponentModel;
using System.Reflection;
using WpfDispatcher = Jalium.UI.Threading.Dispatcher;
using WpfDispatcherObject = Jalium.UI.Threading.DispatcherObject;
using WpfPriority = Jalium.UI.Threading.DispatcherPriority;

namespace Jalium.UI.Tests;

[Collection(nameof(ParityFoundationBehaviorCollection))]
public sealed class ThreadingDispatcherParityTests
{
    [Fact]
    public void CanonicalDispatcherOwnsTheEstablishedPerThreadQueue()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;

        Assert.Same(dispatcher, WpfDispatcher.CurrentDispatcher);
        Assert.Same(dispatcher, WpfDispatcher.FromThread(Thread.CurrentThread));
        Assert.Same(Thread.CurrentThread, dispatcher.Thread);
        Assert.True(dispatcher.CheckAccess());

        dispatcher.ProcessQueue();
        var order = new List<string>();
        Jalium.UI.Threading.DispatcherOperation background = dispatcher.InvokeAsync(
            () => order.Add("background"),
            WpfPriority.Background);
        Jalium.UI.Threading.DispatcherOperation normal = dispatcher.InvokeAsync(
            () => order.Add("normal"),
            WpfPriority.Normal);

        Assert.Equal(Jalium.UI.Threading.DispatcherOperationStatus.Pending, background.Status);
        Assert.Equal(Jalium.UI.Threading.DispatcherOperationStatus.Pending, normal.Status);

        dispatcher.ProcessQueue();

        Assert.Equal(new[] { "normal", "background" }, order);
        Assert.True(background.Task.IsCompletedSuccessfully);
        Assert.True(normal.Task.IsCompletedSuccessfully);
        Assert.Same(dispatcher, background.Dispatcher);
    }

    [Fact]
    public async Task GenericInvokeAsyncCarriesTypedResultTaskAndAwaiter()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        dispatcher.ProcessQueue();

        Jalium.UI.Threading.DispatcherOperation<int> operation = dispatcher.InvokeAsync(
            () => 42,
            WpfPriority.Background);

        _ = Assert.IsAssignableFrom<Task<int>>(operation.Task);
        Assert.False(operation.Task.IsCompleted);

        dispatcher.ProcessQueue();

        Assert.Equal(42, operation.Result);
        Assert.Equal(42, await operation);
        Assert.True(operation.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void InvokeAndLegacyDelegateOverloadsExecuteWithWpfSemantics()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        int actionCount = 0;

        dispatcher.Invoke(
            () => actionCount++,
            WpfPriority.Send,
            CancellationToken.None);
        object? value = dispatcher.Invoke(
            (Func<int, int>)(input => input + 1),
            WpfPriority.Send,
            41);

        Assert.Equal(1, actionCount);
        Assert.Equal(42, value);
        Assert.Equal(7, dispatcher.Invoke(() => 7, WpfPriority.Send, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => dispatcher.Invoke(
            () => actionCount++,
            WpfPriority.Send,
            new CancellationToken(canceled: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => dispatcher.Invoke(
            () => actionCount++,
            WpfPriority.Send,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(-2)));
    }

    [Fact]
    public void PreCanceledInvokeAsyncReturnsAnAbortedCanceledOperation()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        bool invoked = false;
        Jalium.UI.Threading.DispatcherOperation operation = dispatcher.InvokeAsync(
            () => invoked = true,
            WpfPriority.Normal,
            new CancellationToken(canceled: true));

        Assert.False(invoked);
        Assert.Equal(Jalium.UI.Threading.DispatcherOperationStatus.Aborted, operation.Status);
        Assert.True(operation.Task.IsCanceled);
    }

    [Fact]
    public void HooksUseCanonicalDispatcherAndStronglyTypedEventArgs()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        dispatcher.ProcessQueue();
        object? sender = null;
        Jalium.UI.Threading.DispatcherHookEventArgs? observed = null;

        Jalium.UI.Threading.DispatcherHookEventHandler handler = (value, args) =>
        {
            sender = value;
            observed = args;
        };
        dispatcher.Hooks.OperationPosted += handler;
        try
        {
            Jalium.UI.Threading.DispatcherOperation operation = dispatcher.BeginInvoke(
                WpfPriority.Background,
                () => { });

            Assert.Same(dispatcher, sender);
            Assert.Same(dispatcher, observed!.Dispatcher);
            Assert.Same(operation, observed.Operation);
            operation.Abort();
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= handler;
        }
    }

    [Fact]
    public void ProcessingDisabledSupportsValueEqualityAndCanonicalDispatcherObject()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        var first = dispatcher.DisableProcessing();
        var copy = first;
        try
        {
            Assert.True(first.Equals(copy));
            Assert.True(first == copy);
            Assert.False(first != copy);
            Assert.Equal(first.GetHashCode(), copy.GetHashCode());

            var dispatcherObject = new ProbeDispatcherObject();
            Assert.Same(dispatcher, dispatcherObject.Dispatcher);
            Assert.True(dispatcherObject.CheckAccess());
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public void SynchronizationContextAndTimerAcceptCanonicalDispatcherTypes()
    {
        WpfDispatcher dispatcher = WpfDispatcher.CurrentDispatcher;
        dispatcher.ProcessQueue();
        var context = new Jalium.UI.Threading.DispatcherSynchronizationContext(
            dispatcher,
            WpfPriority.Background);
        bool posted = false;

        context.Post(_ => posted = true, null);
        Assert.False(posted);
        dispatcher.ProcessQueue();
        Assert.True(posted);

        var timer = new Jalium.UI.Threading.DispatcherTimer(WpfPriority.Input, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(25),
        };
        Assert.Same(dispatcher, timer.Dispatcher);
        Assert.Equal(WpfPriority.Input, timer.Priority);
        Assert.Throws<InvalidEnumArgumentException>(() =>
            WpfDispatcher.ValidatePriority((WpfPriority)int.MaxValue, "priority"));
    }

    [Fact]
    public void PublicReflectionSurfaceContainsEveryReportedDispatcherOverload()
    {
        Type dispatcher = typeof(WpfDispatcher);
        Assert.Equal("Jalium.UI.Threading", dispatcher.Namespace);
        Assert.Equal("Jalium.UI.Threading", typeof(WpfDispatcherObject).Namespace);
        Assert.Equal("Jalium.UI.Threading", typeof(WpfPriority).Namespace);
        Assert.True(typeof(WpfPriority).IsEnum);
        Assert.NotNull(typeof(Jalium.UI.Threading.DispatcherEventArgs));
        Assert.NotNull(typeof(Jalium.UI.Threading.DispatcherOperationCallback));
        Assert.NotNull(typeof(Jalium.UI.Threading.DispatcherOperation<>));
        Assert.Null(dispatcher.Assembly.GetType("Jalium.UI.Dispatcher", throwOnError: false));
        Assert.Null(dispatcher.Assembly.GetType("Jalium.UI.DispatcherObject", throwOnError: false));
        Assert.Null(dispatcher.Assembly.GetType("Jalium.UI.DispatcherPriority", throwOnError: false));

        AssertStaticVoid(dispatcher, nameof(WpfDispatcher.Run));
        AssertStaticVoid(dispatcher, nameof(WpfDispatcher.ExitAllFrames));
        AssertStaticVoid(dispatcher, nameof(WpfDispatcher.PushFrame), typeof(Jalium.UI.Threading.DispatcherFrame));
        AssertStaticVoid(dispatcher, nameof(WpfDispatcher.ValidatePriority), typeof(WpfPriority), typeof(string));
        Assert.NotNull(dispatcher.GetMethod(
            nameof(WpfDispatcher.BeginInvokeShutdown),
            [typeof(WpfPriority)]));
        Assert.NotNull(dispatcher.GetMethod(nameof(WpfDispatcher.InvokeShutdown), Type.EmptyTypes));
        Assert.NotNull(dispatcher.GetMethod(
            nameof(WpfDispatcher.BeginInvoke),
            [typeof(Delegate), typeof(WpfPriority), typeof(object[])]));
        Assert.NotNull(dispatcher.GetMethod(
            nameof(WpfDispatcher.Invoke),
            [typeof(Action), typeof(WpfPriority), typeof(CancellationToken), typeof(TimeSpan)]));
        Assert.NotNull(dispatcher.GetMethod(
            nameof(WpfDispatcher.InvokeAsync),
            [typeof(Action), typeof(WpfPriority), typeof(CancellationToken)]));

        MethodInfo genericInvoke = dispatcher.GetMethods()
            .Single(method =>
                method.Name == nameof(WpfDispatcher.Invoke) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 4);
        Assert.True(genericInvoke.ReturnType.IsGenericParameter);

        MethodInfo genericAsync = dispatcher.GetMethods()
            .Single(method =>
                method.Name == nameof(WpfDispatcher.InvokeAsync) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 3);
        Assert.Equal(typeof(Jalium.UI.Threading.DispatcherOperation<>), genericAsync.ReturnType.GetGenericTypeDefinition());
    }

    private static void AssertStaticVoid(Type type, string name, params Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            null,
            parameters,
            null)!;
        Assert.Equal(typeof(void), method.ReturnType);
    }

    private sealed class ProbeDispatcherObject : WpfDispatcherObject
    {
    }
}
