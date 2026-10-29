using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DynamicResourceRegistryTests
{
    private sealed class RegistryProbe : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(object),
            typeof(RegistryProbe),
            new PropertyMetadata(null, OnValueChanged));

        public object? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static bool ExpansionEnabled { get; set; }

        public static object? ExpansionValue { get; set; }

        public static object? ResetValue { get; set; }

        public static object? ExpansionKey { get; set; }

        public static List<RegistryProbe> Spawned { get; } = [];

        private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (!ExpansionEnabled || !ReferenceEquals(args.NewValue, ExpansionValue))
                return;

            // Leave a newly registered target pending for the current RefreshAll call. A live
            // registry enumeration would discover it, refresh it, and repeat this expansion.
            ExpansionEnabled = false;
            try
            {
                var child = new RegistryProbe();
                DynamicResourceBindingOperations.SetDynamicResource(child, ValueProperty, ExpansionKey!);
                child.Value = ResetValue;
                Spawned.Add(child);
            }
            finally
            {
                ExpansionEnabled = true;
            }
        }
    }

    [Fact]
    public void Registry_DeduplicatesTargetAndUnregistersLastProperty()
    {
        var key = new object();
        var before = DynamicResourceBindingOperations.GetRegistryDiagnostics();
        var target = new RegistryProbe();

        for (var i = 0; i < 10_000; i++)
        {
            DynamicResourceBindingOperations.SetDynamicResource(target, RegistryProbe.ValueProperty, key);
        }

        var registered = DynamicResourceBindingOperations.GetRegistryDiagnostics();
        Assert.Equal(before.LiveTargets + 1, registered.LiveTargets);
        Assert.Equal(before.LiveSubscriptions + 1, registered.LiveSubscriptions);
        Assert.Equal(registered.LiveTargets, registered.RegistrySlots);

        DynamicResourceBindingOperations.ClearDynamicResource(target, RegistryProbe.ValueProperty);

        var cleared = DynamicResourceBindingOperations.GetRegistryDiagnostics();
        Assert.Equal(before.LiveTargets, cleared.LiveTargets);
        Assert.Equal(before.LiveSubscriptions, cleared.LiveSubscriptions);
        Assert.Equal(cleared.LiveTargets, cleared.RegistrySlots);
    }

    [Fact]
    public void Registry_CompactsThousandsOfCollectedTargets_AndRefreshStaysBounded()
    {
        var before = DynamicResourceBindingOperations.GetRegistryDiagnostics();
        var weakTargets = CreateTransientTargets(5_000, new object());

        ForceFullCollection();
        var survivors = weakTargets.Count(static weak => weak.TryGetTarget(out _));
        Assert.True(survivors <= 5, $"Expected transient targets to be collectible, but {survivors} survived.");

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 25; i++)
        {
            DynamicResourceBindingOperations.RefreshAll();
        }
        stopwatch.Stop();

        var after = DynamicResourceBindingOperations.GetRegistryDiagnostics();
        Assert.True(after.LiveTargets <= before.LiveTargets + survivors);
        Assert.True(after.LiveSubscriptions <= before.LiveSubscriptions + survivors);
        Assert.Equal(after.LiveTargets, after.RegistrySlots);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Twenty-five compacted refreshes took {stopwatch.Elapsed}.");

        GC.KeepAlive(weakTargets);
    }

    [Fact]
    public void Registry_NormalRegistrationAfterCollection_CompactsDeadWeakMetadata()
    {
        ThemeManager.Reset();
        var deadKey = new object();
        var triggerKey = new object();
        var deadTarget = CreateTransientTarget(deadKey);

        try
        {
            Assert.Equal(1, GetRawRegisteredTargetCount());
            Assert.Equal(1, GetRawKeyIndexEntryCount(deadKey));

            ForceFullCollection();
            Assert.False(deadTarget.TryGetTarget(out _));

            // Merely collecting the target does not mutate either static metadata table.
            // The next normal registration batch observes GC progress and performs the
            // amortized cleanup without RefreshAll or GetRegistryDiagnostics doing it for us.
            Assert.Equal(1, GetRawRegisteredTargetCount());
            Assert.Equal(1, GetRawKeyIndexEntryCount(deadKey));

            var liveTargets = new RegistryProbe[128];
            for (var index = 0; index < liveTargets.Length; index++)
            {
                var target = new RegistryProbe();
                DynamicResourceBindingOperations.SetDynamicResource(
                    target,
                    RegistryProbe.ValueProperty,
                    triggerKey);
                liveTargets[index] = target;
            }

            Assert.Equal(liveTargets.Length, GetRawRegisteredTargetCount());
            Assert.Equal(0, GetRawKeyIndexEntryCount(deadKey));
            Assert.Equal(liveTargets.Length, GetRawKeyIndexEntryCount(triggerKey));
            GC.KeepAlive(liveTargets);
        }
        finally
        {
            ThemeManager.Reset();
            GC.KeepAlive(deadTarget);
        }
    }

    [Fact]
    public void RefreshAll_UsesEntrySnapshot_WhenRefreshRegistersMoreTargets()
    {
        var key = new object();
        var originalLookup = ResourceLookup.ApplicationResourceLookup;
        var initialValue = new object();
        var refreshedValue = new object();
        object? currentValue = initialValue;
        var root = new RegistryProbe();

        ResourceLookup.ApplicationResourceLookup = resourceKey =>
            ReferenceEquals(resourceKey, key) ? currentValue : originalLookup?.Invoke(resourceKey);

        RegistryProbe.ExpansionKey = key;
        RegistryProbe.ExpansionValue = refreshedValue;
        RegistryProbe.ResetValue = initialValue;
        RegistryProbe.Spawned.Clear();

        try
        {
            ResourceLookup.InvalidateResourceCache();
            DynamicResourceBindingOperations.SetDynamicResource(root, RegistryProbe.ValueProperty, key);
            Assert.Same(initialValue, root.Value);

            currentValue = refreshedValue;
            ResourceLookup.InvalidateResourceCache();
            RegistryProbe.ExpansionEnabled = true;

            var stopwatch = Stopwatch.StartNew();
            DynamicResourceBindingOperations.RefreshAll();
            stopwatch.Stop();

            Assert.Same(refreshedValue, root.Value);
            Assert.Single(RegistryProbe.Spawned);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Snapshot refresh took {stopwatch.Elapsed}.");
        }
        finally
        {
            RegistryProbe.ExpansionEnabled = false;
            DynamicResourceBindingOperations.ClearDynamicResource(root, RegistryProbe.ValueProperty);
            foreach (var spawned in RegistryProbe.Spawned)
            {
                DynamicResourceBindingOperations.ClearDynamicResource(spawned, RegistryProbe.ValueProperty);
            }

            RegistryProbe.Spawned.Clear();
            RegistryProbe.ExpansionKey = null;
            RegistryProbe.ExpansionValue = null;
            RegistryProbe.ResetValue = null;
            ResourceLookup.ApplicationResourceLookup = originalLookup;
            ResourceLookup.InvalidateResourceCache();
        }
    }

    [Fact]
    public void ThemeReset_UnregistersOldTargets_AndAllowsFreshRegistrations()
    {
        ThemeManager.Reset();
        var key = new object();
        var initialValue = new object();
        var updatedValue = new object();
        var oldTarget = new RegistryProbe();
        oldTarget.Resources[key] = initialValue;
        DynamicResourceBindingOperations.SetDynamicResource(oldTarget, RegistryProbe.ValueProperty, key);
        Assert.Same(initialValue, oldTarget.Value);

        ThemeManager.Reset();

        Assert.Equal((0, 0, 0), DynamicResourceBindingOperations.GetRegistryDiagnostics());
        Assert.False(DynamicResourceBindingOperations.TryGetDynamicResourceKey(
            oldTarget,
            RegistryProbe.ValueProperty,
            out _));

        oldTarget.Resources[key] = updatedValue;
        Assert.Same(initialValue, oldTarget.Value);

        var newTarget = new RegistryProbe();
        newTarget.Resources[key] = initialValue;
        DynamicResourceBindingOperations.SetDynamicResource(newTarget, RegistryProbe.ValueProperty, key);
        Assert.Same(initialValue, newTarget.Value);

        newTarget.Resources[key] = updatedValue;
        Assert.Same(updatedValue, newTarget.Value);
        Assert.Equal((1, 1, 1), DynamicResourceBindingOperations.GetRegistryDiagnostics());

        ThemeManager.Reset();
    }

    [Fact]
    public void ThemeReset_RepeatedBulkRegistryCleanup_StaysBounded()
    {
        ThemeManager.Reset();
        var stopwatch = Stopwatch.StartNew();

        for (var cycle = 0; cycle < 40; cycle++)
        {
            var targets = new RegistryProbe[250];
            for (var index = 0; index < targets.Length; index++)
            {
                var target = new RegistryProbe();
                DynamicResourceBindingOperations.SetDynamicResource(
                    target,
                    RegistryProbe.ValueProperty,
                    $"ResetProbe.{cycle}.{index}");
                targets[index] = target;
            }

            Assert.Equal((250, 250, 250), DynamicResourceBindingOperations.GetRegistryDiagnostics());
            ThemeManager.Reset();
            Assert.Equal((0, 0, 0), DynamicResourceBindingOperations.GetRegistryDiagnostics());
            GC.KeepAlive(targets);
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Forty bulk registry resets took {stopwatch.Elapsed}.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FrameworkElement>[] CreateTransientTargets(int count, object key)
    {
        var weakTargets = new WeakReference<FrameworkElement>[count];
        for (var i = 0; i < count; i++)
        {
            var target = new RegistryProbe();
            DynamicResourceBindingOperations.SetDynamicResource(target, RegistryProbe.ValueProperty, key);
            weakTargets[i] = new WeakReference<FrameworkElement>(target);
        }

        return weakTargets;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FrameworkElement> CreateTransientTarget(object key)
    {
        var target = new RegistryProbe();
        DynamicResourceBindingOperations.SetDynamicResource(target, RegistryProbe.ValueProperty, key);
        return new WeakReference<FrameworkElement>(target);
    }

    private static int GetRawRegisteredTargetCount()
    {
        var field = typeof(DependencyObject).Assembly
            .GetType("Jalium.UI.DynamicResourceBindingOperations")!
            .GetField("RegisteredTargets", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return ((ICollection)field!.GetValue(null)!).Count;
    }

    private static int GetRawKeyIndexEntryCount(object key)
    {
        var field = typeof(DependencyObject).Assembly
            .GetType("Jalium.UI.DynamicResourceBindingOperations")!
            .GetField("KeyIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var index = (IDictionary)field!.GetValue(null)!;
        return index[key] is ICollection entries ? entries.Count : 0;
    }

    private static void ForceFullCollection()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
