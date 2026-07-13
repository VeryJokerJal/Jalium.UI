using System.Linq;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Represents a deferred dynamic resource reference that should resolve at runtime.
/// </summary>
public interface IDynamicResourceReference
{
    /// <summary>
    /// Gets the key used to look up the resource.
    /// </summary>
    object ResourceKey { get; }
}

/// <summary>
/// Tracks dynamic resource subscriptions for dependency properties.
/// </summary>
internal static class DynamicResourceBindingOperations
{
    private sealed class DynamicResourceTargetRegistration
    {
        public DynamicResourceTargetRegistration(FrameworkElement target)
        {
            Target = new WeakReference<FrameworkElement>(target);
        }

        public WeakReference<FrameworkElement> Target { get; }

        public volatile bool IsActive = true;
    }

    private sealed class DynamicResourceSubscription
    {
        public required object ResourceKey { get; init; }
        public required EventHandler Handler { get; init; }
        public DependencyObject.LayerValueSource? LayerSource { get; set; }
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<DependencyProperty, DynamicResourceSubscription>> Subscriptions = new();
    private static readonly ConditionalWeakTable<FrameworkElement, DynamicResourceTargetRegistration> TargetRegistrations = new();
    private static readonly List<DynamicResourceTargetRegistration> RegisteredTargets = [];
    private static readonly object RegistryGate = new();
    private static int _inactiveTargetCount;

    // Binary compatibility overload for callers compiled against the historical
    // 3-parameter signature (e.g. older Jalium.UI.Xaml binaries).
    internal static void SetDynamicResource(
        FrameworkElement target,
        DependencyProperty property,
        object resourceKey)
    {
        SetDynamicResource(target, property, resourceKey, layerSource: null);
    }

    internal static void SetDynamicResource(
        FrameworkElement target,
        DependencyProperty property,
        object resourceKey,
        DependencyObject.LayerValueSource? layerSource = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(resourceKey);

        var subscriptions = Subscriptions.GetOrCreateValue(target);
        if (subscriptions.TryGetValue(property, out var existingSubscription))
        {
            if (Equals(existingSubscription.ResourceKey, resourceKey) &&
                existingSubscription.LayerSource == layerSource)
            {
                RefreshDynamicResource(target, property);
                return;
            }

            target.ResourcesChanged -= existingSubscription.Handler;
            subscriptions.Remove(property);
        }

        // The event source is the target itself, so use the sender instead of closing over
        // the target. This keeps the subscription value free of an unnecessary strong edge
        // back to the ConditionalWeakTable key.
        EventHandler handler = (sender, _) =>
        {
            if (sender is FrameworkElement element)
            {
                RefreshDynamicResource(element, property);
            }
        };
        subscriptions[property] = new DynamicResourceSubscription
        {
            ResourceKey = resourceKey,
            Handler = handler,
            LayerSource = layerSource
        };

        target.ResourcesChanged += handler;
        EnsureTargetRegistered(target);
        RefreshDynamicResource(target, property);
    }

    internal static bool TryGetDynamicResourceKey(FrameworkElement target, DependencyProperty property, out object? resourceKey)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        resourceKey = null;

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return false;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return false;

        resourceKey = subscription.ResourceKey;
        return true;
    }

    internal static void ClearDynamicResource(FrameworkElement target, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return;

        target.ResourcesChanged -= subscription.Handler;
        subscriptions.Remove(property);

        if (subscriptions.Count == 0)
        {
            Subscriptions.Remove(target);
            UnregisterTarget(target);
        }
    }

    /// <summary>
    /// Clears the dynamic-resource subscription on the property only when it belongs to the
    /// given layer. Used when a higher-priority style setter writes a plain value: a lower
    /// style layer (theme default style) may hold a live subscription on the same DP whose
    /// next refresh would overwrite that value. Subscriptions from other layers (e.g. a
    /// local SetDynamicResource) are left untouched.
    /// </summary>
    internal static void ClearDynamicResource(
        FrameworkElement target,
        DependencyProperty property,
        DependencyObject.LayerValueSource layerSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return;

        if (subscription.LayerSource != layerSource)
            return;

        target.ResourcesChanged -= subscription.Handler;
        subscriptions.Remove(property);
    }

    internal static void PromoteDynamicResourcesToLayer(
        FrameworkElement target,
        DependencyObject.LayerValueSource layerSource)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            return;

        foreach (var property in subscriptions.Keys.ToArray())
        {
            if (!subscriptions.TryGetValue(property, out var subscription))
                continue;

            if (subscription.LayerSource.HasValue)
                continue;

            subscription.LayerSource = layerSource;
            RefreshDynamicResource(target, property);
        }
    }

    /// <summary>
    /// Re-resolves every dynamic-resource subscription registered on <paramref name="target"/>.
    /// Called when the element's resource-lookup scope may have widened — most importantly
    /// when it is attached to a visual parent (see <c>FrameworkElement.OnVisualParentChanged</c>).
    /// A subscription created during XAML construction, before the element could reach
    /// ancestor / application resources, resolves to null at that point; without this retry
    /// it would stay null permanently. No-op (a single dictionary probe) for elements that
    /// have no subscriptions, and idempotent for properties already resolved.
    /// </summary>
    internal static void RefreshElement(FrameworkElement target)
    {
        if (target == null)
            return;

        if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            return;

        foreach (var property in subscriptions.Keys.ToArray())
        {
            RefreshDynamicResource(target, property);
        }
    }

    internal static void RefreshAll()
    {
        // Theme switches are infrequent; a full sweep is acceptable and avoids
        // missing updates when subtree resource notifications are skipped.
        RefreshForKeys(changedKeys: null);
    }

    /// <summary>
    /// Refreshes only subscriptions whose resource key is in <paramref name="changedKeys"/>.
    /// Pass null to refresh ALL subscriptions (theme switch).
    /// </summary>
    internal static void RefreshForKeys(IReadOnlySet<object>? changedKeys)
    {
        // Never enumerate ConditionalWeakTable directly here. Refreshing a resource can
        // instantiate a template, and template construction can register more dynamic
        // resources. A live table enumeration can therefore keep discovering work created
        // by the same sweep. A weak snapshot makes each sweep finite and does not root all
        // registered element graphs for the duration of the operation.
        var registrations = SnapshotLiveRegistrations();
        foreach (var registration in registrations)
        {
            if (!registration.IsActive || !registration.Target.TryGetTarget(out var target))
                continue;

            if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            {
                UnregisterTarget(target);
                continue;
            }

            var properties = subscriptions.Keys.ToArray();
            foreach (var property in properties)
            {
                if (changedKeys != null)
                {
                    // Only refresh if this subscription's key was actually changed
                    if (!subscriptions.TryGetValue(property, out var sub) ||
                        !changedKeys.Contains(sub.ResourceKey))
                        continue;
                }

                RefreshDynamicResource(target, property);
            }
        }
    }

    /// <summary>
    /// Returns registry counts for diagnostics and regression tests. Taking the snapshot
    /// also removes inactive and collected weak entries from the global index.
    /// </summary>
    internal static (int LiveTargets, int LiveSubscriptions, int RegistrySlots) GetRegistryDiagnostics()
    {
        var registrations = SnapshotLiveRegistrations();
        var liveTargets = 0;
        var liveSubscriptions = 0;

        foreach (var registration in registrations)
        {
            if (!registration.IsActive || !registration.Target.TryGetTarget(out var target))
                continue;

            if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            {
                UnregisterTarget(target);
                continue;
            }

            liveTargets++;
            liveSubscriptions += subscriptions.Count;
        }

        lock (RegistryGate)
        {
            CompactRegistryNoLock();
            return (liveTargets, liveSubscriptions, RegisteredTargets.Count);
        }
    }

    /// <summary>
    /// Clears all process-wide dynamic-resource subscriptions between isolated test scopes.
    /// Normal theme changes must use <see cref="RefreshAll"/> and never call this method.
    /// </summary>
    internal static void ResetRegistryForTesting()
    {
        DynamicResourceTargetRegistration[] registrations;
        lock (RegistryGate)
        {
            registrations = RegisteredTargets.ToArray();
            foreach (var registration in registrations)
            {
                registration.IsActive = false;
            }

            RegisteredTargets.Clear();
            TargetRegistrations.Clear();
            _inactiveTargetCount = 0;
        }

        foreach (var registration in registrations)
        {
            if (!registration.Target.TryGetTarget(out var target) ||
                !Subscriptions.TryGetValue(target, out var subscriptions))
            {
                continue;
            }

            foreach (var subscription in subscriptions.Values.ToArray())
            {
                target.ResourcesChanged -= subscription.Handler;
            }

            subscriptions.Clear();
        }

        Subscriptions.Clear();

        // Non-visual subscriptions hang their handler on a FrameworkElement host, so they
        // must be explicitly detached as well; clearing only their CWT would leave those
        // host event lists carrying stale delegates into the next test scope.
        foreach (var entry in NonVisualSubscriptions.ToArray())
        {
            foreach (var subscription in entry.Value.Values.ToArray())
            {
                subscription.Host.ResourcesChanged -= subscription.Handler;
            }

            entry.Value.Clear();
        }

        NonVisualSubscriptions.Clear();
    }

    private static void EnsureTargetRegistered(FrameworkElement target)
    {
        lock (RegistryGate)
        {
            if (TargetRegistrations.TryGetValue(target, out var existingRegistration))
            {
                if (existingRegistration.IsActive)
                    return;

                TargetRegistrations.Remove(target);
            }

            var registration = new DynamicResourceTargetRegistration(target);
            TargetRegistrations.Add(target, registration);
            RegisteredTargets.Add(registration);

            if (_inactiveTargetCount >= 64 && _inactiveTargetCount * 2 >= RegisteredTargets.Count)
            {
                CompactRegistryNoLock();
            }
        }
    }

    private static void UnregisterTarget(FrameworkElement target)
    {
        lock (RegistryGate)
        {
            if (!TargetRegistrations.TryGetValue(target, out var registration))
                return;

            TargetRegistrations.Remove(target);
            if (registration.IsActive)
            {
                registration.IsActive = false;
                _inactiveTargetCount++;
            }

            if (_inactiveTargetCount >= 64 && _inactiveTargetCount * 2 >= RegisteredTargets.Count)
            {
                CompactRegistryNoLock();
            }
        }
    }

    private static DynamicResourceTargetRegistration[] SnapshotLiveRegistrations()
    {
        lock (RegistryGate)
        {
            CompactRegistryNoLock();
            return RegisteredTargets.ToArray();
        }
    }

    private static void CompactRegistryNoLock()
    {
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < RegisteredTargets.Count; readIndex++)
        {
            var registration = RegisteredTargets[readIndex];
            if (!registration.IsActive || !registration.Target.TryGetTarget(out _))
            {
                registration.IsActive = false;
                continue;
            }

            RegisteredTargets[writeIndex++] = registration;
        }

        if (writeIndex < RegisteredTargets.Count)
        {
            RegisteredTargets.RemoveRange(writeIndex, RegisteredTargets.Count - writeIndex);
        }

        _inactiveTargetCount = 0;
    }

    private static void RefreshDynamicResource(FrameworkElement target, DependencyProperty property)
    {
        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return;

        var resolved = ResourceLookup.FindResource(target, subscription.ResourceKey);
        var currentValue = target.GetValue(property);
        if (subscription.LayerSource.HasValue)
        {
            if (resolved != null)
            {
                if (ReferenceEquals(currentValue, resolved) || Equals(currentValue, resolved))
                {
                    return;
                }

                target.SetLayerValue(property, resolved, subscription.LayerSource.Value);
            }
            else
            {
                if (ReferenceEquals(currentValue, DependencyProperty.UnsetValue))
                {
                    return;
                }

                target.ClearLayerValue(property, subscription.LayerSource.Value);
            }
            return;
        }

        if (resolved != null)
        {
            if (ReferenceEquals(currentValue, resolved) || Equals(currentValue, resolved))
            {
                return;
            }

            target.SetValue(property, resolved);
        }
        else if (target.HasLocalValue(property))
        {
            target.ClearValue(property);
        }
    }

    // ---- Non-FrameworkElement DependencyObject support (Freezable-like) ----

    private sealed class NonVisualDynamicResourceSubscription
    {
        public required FrameworkElement Host { get; init; }
        public required DependencyObject Target { get; init; }
        public required DependencyProperty Property { get; init; }
        public required object ResourceKey { get; init; }
        public required EventHandler Handler { get; init; }
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, NonVisualDynamicResourceSubscription>> NonVisualSubscriptions = new();

    /// <summary>
    /// Sets a dynamic resource on a non-FrameworkElement DependencyObject by using
    /// a host FrameworkElement for resource lookup, similar to WPF's Freezable
    /// inheritance context.
    /// </summary>
    internal static void SetDynamicResourceForNonVisual(
        FrameworkElement host,
        DependencyObject target,
        DependencyProperty property,
        object resourceKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(resourceKey);

        ClearDynamicResourceForNonVisual(target, property);

        var subscriptions = NonVisualSubscriptions.GetOrCreateValue(target);
        EventHandler handler = (_, _) => RefreshNonVisualDynamicResource(target, property);
        subscriptions[property] = new NonVisualDynamicResourceSubscription
        {
            Host = host,
            Target = target,
            Property = property,
            ResourceKey = resourceKey,
            Handler = handler,
        };

        host.ResourcesChanged += handler;
        RefreshNonVisualDynamicResource(target, property);
    }

    internal static void ClearDynamicResourceForNonVisual(DependencyObject target, DependencyProperty property)
    {
        if (!NonVisualSubscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return;

        subscription.Host.ResourcesChanged -= subscription.Handler;
        subscriptions.Remove(property);
    }

    private static void RefreshNonVisualDynamicResource(DependencyObject target, DependencyProperty property)
    {
        if (!NonVisualSubscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(property, out var subscription))
            return;

        var resolved = ResourceLookup.FindResource(subscription.Host, subscription.ResourceKey);
        if (resolved != null)
        {
            target.SetValue(property, resolved);
        }
        else if (target.HasLocalValue(property))
        {
            target.ClearValue(property);
        }
    }
}
