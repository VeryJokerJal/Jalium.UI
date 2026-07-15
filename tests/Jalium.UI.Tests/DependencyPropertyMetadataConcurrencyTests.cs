namespace Jalium.UI.Tests;

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
    public async Task GetMetadata_ConcurrentCacheMisses_AreSafeAndConsistent()
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
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var lookups = lookupTypes.Select(async lookupType =>
            {
                await start.Task.ConfigureAwait(false);
                return property.GetMetadata(lookupType);
            }).ToArray();

            start.SetResult(true);

            var results = await Task.WhenAll(lookups).WaitAsync(TestTimeout);
            Assert.All(results, result => Assert.Same(baseMetadata, result));
        }
    }

    [Fact]
    public async Task OverrideMetadata_DoesNotExposeMetadataUntilOnApplyCompletes()
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
        var lookupCompletedWhileApplyWasBlocked = false;

        try
        {
            Assert.True(overrideMetadata.ApplyStarted.Wait(TestTimeout));

            var lookupStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lookupTask = Task.Factory.StartNew(
                () =>
                {
                    lookupStarted.SetResult(true);
                    var metadata = property.GetMetadata(typeof(DerivedOwner));
                    return new MetadataObservation(metadata, overrideMetadata.PublicIsSealed);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            await lookupStarted.Task.WaitAsync(TestTimeout);
            lookupCompletedWhileApplyWasBlocked =
                await Task.WhenAny(lookupTask, Task.Delay(TimeSpan.FromMilliseconds(500))) == lookupTask;
        }
        finally
        {
            overrideMetadata.AllowApply.Set();
        }

        await overrideTask.WaitAsync(TestTimeout);
        Assert.NotNull(lookupTask);

        var observation = await lookupTask!.WaitAsync(TestTimeout);
        Assert.False(lookupCompletedWhileApplyWasBlocked);
        Assert.Same(overrideMetadata, observation.Metadata);
        Assert.True(observation.WasSealed);
    }

    [Fact]
    public async Task ConcurrentOverride_SameType_AllowsExactlyOneWinner()
    {
        var property = DependencyProperty.Register(
            $"ConcurrentMetadataOverride_{Guid.NewGuid():N}",
            typeof(string),
            typeof(BaseOwner),
            new PropertyMetadata("base"));
        using var mergeEntrants = new CountdownEvent(2);
        var firstMetadata = new CoordinatedMergeMetadata("first", mergeEntrants);
        var secondMetadata = new CoordinatedMergeMetadata("second", mergeEntrants);
        using var start = new ManualResetEventSlim(initialState: false);

        Task<Exception?> TryOverrideOnDedicatedThread(PropertyMetadata metadata)
        {
            return Task.Factory.StartNew<Exception?>(
                () =>
                {
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

        start.Set();
        var outcomes = await Task.WhenAll(attempts).WaitAsync(TestTimeout);

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

    private sealed class CoordinatedMergeMetadata(
        object? defaultValue,
        CountdownEvent mergeEntrants) : PropertyMetadata(defaultValue)
    {
        protected override void Merge(PropertyMetadata baseMetadata, DependencyProperty dp)
        {
            mergeEntrants.Signal();
            _ = mergeEntrants.Wait(TimeSpan.FromSeconds(1));
            base.Merge(baseMetadata, dp);
        }
    }

    private sealed record MetadataObservation(PropertyMetadata Metadata, bool WasSealed);

    private sealed class PayloadPair<TLeft, TRight> { }
    private class BaseOwner : DependencyObject { }
    private sealed class DerivedOwner : BaseOwner { }
    private sealed class GenericDerivedOwner<T> : BaseOwner { }
}
