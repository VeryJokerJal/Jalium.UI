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
    private sealed class DynamicResourceSubscription
    {
        public required object ResourceKey { get; init; }
        public required EventHandler Handler { get; init; }
        public DependencyObject.LayerValueSource? LayerSource { get; set; }
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<DependencyProperty, DynamicResourceSubscription>> Subscriptions = new();

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
        ClearDynamicResource(target, property);

        var subscriptions = Subscriptions.GetOrCreateValue(target);
        EventHandler handler = (_, _) => RefreshDynamicResource(target, property);
        subscriptions[property] = new DynamicResourceSubscription
        {
            ResourceKey = resourceKey,
            Handler = handler,
            LayerSource = layerSource
        };

        target.ResourcesChanged += handler;
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
        foreach (var entry in Subscriptions)
        {
            var target = entry.Key;
            if (target == null)
                continue;

            var properties = entry.Value.Keys.ToArray();
            foreach (var property in properties)
            {
                if (changedKeys != null)
                {
                    // Only refresh if this subscription's key was actually changed
                    if (!entry.Value.TryGetValue(property, out var sub) ||
                        !changedKeys.Contains(sub.ResourceKey))
                        continue;
                }

                RefreshDynamicResource(target, property);
            }
        }
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
}
