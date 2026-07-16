namespace Jalium.UI.Tests;

#pragma warning disable xUnit1031 // These tests intentionally wait only on dedicated LongRunning workers.

public sealed class DependencyPropertyMetadataConcurrencyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Type[] PayloadTypes =
    [
        typeof(byte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(DateTime),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(object),
    ];

    [Fact]
    public void GetMetadata_ConcurrentCacheMisses_AreSafeAndConsistent()
    {
        var lookupTypes =
            (from left in PayloadTypes
             from right in PayloadTypes
             let payloadType = typeof(PayloadPair<,>).MakeGenericType(left, right)
             select typeof(GenericDerivedOwner<>).MakeGenericType(payloadType))
            .ToArray();

        for (var round = 0; round < 12; round++)
        {
            var baseMetadata = new PropertyMetadata($"base-{round}");
            var property = DependencyProperty.Register(
                $"ConcurrentMetadataCache_{Guid.NewGuid():N}",
                typeof(string),
                typeof(BaseOwner),
                baseMetadata);
            const int workerCount = 12;
            using var workersReady = new CountdownEvent(workerCount);
            using var start = new ManualResetEventSlim(initialState: false);
            var results = new PropertyMetadata[lookupTypes.Length];
            var workers = Enumerable.Range(0, workerCount)
                .Select(workerIndex => Task.Factory.StartNew(
                    () =>
                    {
                        workersReady.Signal();
                        if (!start.Wait(TestTimeout))
                            throw new TimeoutException("Timed out waiting to start metadata cache lookups.");

                        for (var index = workerIndex; index < lookupTypes.Length; index += workerCount)
                            results[index] = property.GetMetadata(lookupTypes[index]);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            var workersReadyInTime = workersReady.Wait(TestTimeout);
            start.Set();
            var workersCompletedInTime = WaitForCompletion(workers);

            Assert.True(workersCompletedInTime);
            foreach (var worker in workers)
                worker.GetAwaiter().GetResult();
            Assert.True(workersReadyInTime);
            Assert.All(results, result => Assert.Same(baseMetadata, result));
        }
    }

    [Fact]
    public void OverrideMetadata_DoesNotExposeMetadataUntilOnApplyCompletes()
    {
        var baseMetadata = new PropertyMetadata("base");
        var property = DependencyProperty.Register(
            $"AtomicMetadataOverride_{Guid.NewGuid():N}",
            typeof(string),
            typeof(BaseOwner),
            baseMetadata);

        // Prime the derived-type cache with inherited metadata so the override must invalidate it.
        Assert.Same(baseMetadata, property.GetMetadata(typeof(DerivedOwner)));

        var overrideMetadata = new BlockingApplyMetadata("derived");
        // Use dedicated workers for blocking concurrency probes. Under the full Linux suite the
        // shared ThreadPool can be temporarily saturated by other blocking tests, which can leave
        // a Task.Run worker unscheduled for the entire assertion timeout and turn this test into a
        // scheduler-load check instead of a metadata-publication check.
        var overrideTask = Task.Factory.StartNew(
            () => property.OverrideMetadata(typeof(DerivedOwner), overrideMetadata),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task<MetadataObservation>? lookupTask = null;
        var applyStartedInTime = false;
        var lookupStartedInTime = false;
        var lookupCompletedWhileApplyWasBlocked = false;

        try
        {
            applyStartedInTime = overrideMetadata.ApplyStarted.Wait(TestTimeout);

            if (applyStartedInTime)
            {
                using var lookupStarted = new ManualResetEventSlim(initialState: false);
                lookupTask = Task.Factory.StartNew(
                    () =>
                    {
                        lookupStarted.Set();
                        var metadata = property.GetMetadata(typeof(DerivedOwner));
                        return new MetadataObservation(metadata, overrideMetadata.PublicIsSealed);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                lookupStartedInTime = lookupStarted.Wait(TestTimeout);
                if (lookupStartedInTime)
                {
                    lookupCompletedWhileApplyWasBlocked = SpinWait.SpinUntil(
                        () => lookupTask.IsCompleted,
                        TimeSpan.FromMilliseconds(500));
                }
            }
        }
        finally
        {
            overrideMetadata.AllowApply.Set();
        }

        var overrideCompletedInTime = WaitForCompletion(overrideTask);
        var lookupCompletedInTime = lookupTask is not null && WaitForCompletion(lookupTask);

        Assert.True(overrideCompletedInTime);
        overrideTask.GetAwaiter().GetResult();
        Assert.True(applyStartedInTime);
        Assert.NotNull(lookupTask);
        Assert.True(lookupStartedInTime);
        Assert.True(lookupCompletedInTime);
        var observation = lookupTask.GetAwaiter().GetResult();
        Assert.False(lookupCompletedWhileApplyWasBlocked);
        Assert.Same(overrideMetadata, observation.Metadata);
        Assert.True(observation.WasSealed);
    }

    [Fact]
    public void ConcurrentOverride_SameType_AllowsExactlyOneWinner()
    {
        var property = DependencyProperty.Register(
            $"ConcurrentMetadataOverride_{Guid.NewGuid():N}",
            typeof(string),
            typeof(BaseOwner),
            new PropertyMetadata("base"));
        var firstMetadata = new PropertyMetadata("first");
        var secondMetadata = new PropertyMetadata("second");
        using var workersReady = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(initialState: false);

        Task<Exception?> TryOverrideOnDedicatedThread(PropertyMetadata metadata)
        {
            return Task.Factory.StartNew<Exception?>(
                () =>
                {
                    workersReady.Signal();
                    if (!start.Wait(TestTimeout))
                        return new TimeoutException("Timed out waiting to start the metadata override.");

                    try
                    {
                        property.OverrideMetadata(typeof(DerivedOwner), metadata);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        var attempts = new[]
        {
            TryOverrideOnDedicatedThread(firstMetadata),
            TryOverrideOnDedicatedThread(secondMetadata),
        };

        var workersReadyInTime = workersReady.Wait(TestTimeout);
        start.Set();
        var workersCompletedInTime = WaitForCompletion(attempts);

        Assert.True(workersCompletedInTime);
        var outcomes = attempts.Select(static attempt => attempt.GetAwaiter().GetResult()).ToArray();
        Assert.True(workersReadyInTime);

        Assert.Equal(1, outcomes.Count(static outcome => outcome is null));
        var failure = Assert.Single(outcomes, static outcome => outcome is not null);
        Assert.IsType<ArgumentException>(failure);

        var appliedMetadata = property.GetMetadata(typeof(DerivedOwner));
        Assert.True(ReferenceEquals(firstMetadata, appliedMetadata) || ReferenceEquals(secondMetadata, appliedMetadata));
    }

    private sealed class BlockingApplyMetadata(object? defaultValue) : PropertyMetadata(defaultValue)
    {
        internal ManualResetEventSlim ApplyStarted { get; } = new(initialState: false);
        internal ManualResetEventSlim AllowApply { get; } = new(initialState: false);
        internal bool PublicIsSealed => IsSealed;

        protected override void OnApply(DependencyProperty dp, Type targetType)
        {
            ApplyStarted.Set();
            if (!AllowApply.Wait(TestTimeout))
                throw new TimeoutException("Timed out waiting for the concurrent metadata lookup.");

            base.OnApply(dp, targetType);
        }
    }

    private static bool WaitForCompletion(Task task) =>
        task.IsCompleted || SpinWait.SpinUntil(() => task.IsCompleted, TestTimeout);

    private static bool WaitForCompletion(IEnumerable<Task> tasks)
    {
        var taskArray = tasks as Task[] ?? tasks.ToArray();
        return taskArray.All(static task => task.IsCompleted) ||
            SpinWait.SpinUntil(() => taskArray.All(static task => task.IsCompleted), TestTimeout);
    }

    private sealed record MetadataObservation(PropertyMetadata Metadata, bool WasSealed);

    private sealed class PayloadPair<TLeft, TRight> { }
    private class BaseOwner : DependencyObject { }
    private sealed class DerivedOwner : BaseOwner { }
    private sealed class GenericDerivedOwner<T> : BaseOwner { }
}

#pragma warning restore xUnit1031
