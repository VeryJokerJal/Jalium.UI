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
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<DependencyProperty, DynamicResourceSubscription>> Subscriptions = new();

    internal static void SetDynamicResource(FrameworkElement target, DependencyProperty property, object resourceKey)
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
            Handler = handler
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

    internal static void RefreshAll()
    {
        // Theme switches are infrequent; a full sweep is acceptable and avoids
        // missing updates when subtree resource notifications are skipped.
        foreach (var entry in Subscriptions)
        {
            var target = entry.Key;
            if (target == null)
                continue;

            var properties = entry.Value.Keys.ToArray();
            foreach (var property in properties)
            {
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
        if (resolved != null)
        {
            target.SetValue(property, resolved);
        }
    }
}
