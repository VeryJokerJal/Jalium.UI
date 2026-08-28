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
        public required DependencyProperty Property { get; init; }
        public required object ResourceKey { get; init; }
        public required EventHandler Handler { get; init; }
        public DependencyObject.LayerValueSource? LayerSource { get; set; }

        /// <summary>本订阅在键反向索引里的槽位，替换订阅时用来把旧槽位就地作废。</summary>
        public KeyIndexEntry? IndexEntry { get; set; }
    }

    /// <summary>
    /// 键反向索引的一个槽位：「资源键 X 被某个目标的某个订阅使用」。
    /// </summary>
    /// <remarks>
    /// 槽位刻意做成**只增不删 + 使用时验证**：订阅可以从六七条不同的路径被移除
    /// （属性清除、样式换层、模板重建、目标回收……），要求每一条都记得维护索引，
    /// 是一定会漏的设计。这里改为在使用时按「注册仍活跃 / 目标仍在 / 该订阅仍存在且键未变」
    /// 三重校验剔除失效槽位，并在扫描时顺手压缩列表。
    /// </remarks>
    private sealed class KeyIndexEntry
    {
        public required DynamicResourceTargetRegistration Registration { get; init; }
        public required SubscriptionKey SubscriptionKey { get; init; }
        public required object ResourceKey { get; init; }
        public bool IsActive = true;
    }

    private readonly record struct SubscriptionKey(
        DependencyProperty Property,
        DependencyObject.LayerValueSource? LayerSource);

    // A property may simultaneously have a live resource expression at several
    // dependency-property precedence layers. For example, a StyleSetter supplies
    // the normal brush while a StyleTrigger temporarily supplies the hover brush.
    // Keeping only one subscription per property destroys the lower expression
    // when the trigger activates, so it can no longer react to later theme changes.
    private static readonly ConditionalWeakTable<
        FrameworkElement,
        Dictionary<SubscriptionKey, DynamicResourceSubscription>> Subscriptions = new();
    private static readonly ConditionalWeakTable<FrameworkElement, DynamicResourceTargetRegistration> TargetRegistrations = new();
    private static readonly List<DynamicResourceTargetRegistration> RegisteredTargets = [];
    private static readonly object RegistryGate = new();
    private static int _inactiveTargetCount;

    /// <summary>
    /// 资源键 → 使用它的订阅槽位。定向刷新据此只碰真正引用了变更键的订阅。
    /// </summary>
    /// <remarks>
    /// 没有它时，一次带键的刷新仍要走遍全部注册目标：每个目标一次 ConditionalWeakTable
    /// 查找加一遍订阅扫描。8400 元素的页面上这条「零命中」的空扫描要 2.3 ms——而运行时改
    /// 调色板每帧都要走一次。索引把代价从「订阅总数」变成「命中订阅数」。
    /// 读写都在 <see cref="RegistryGate"/> 下。
    /// </remarks>
    private static readonly Dictionary<object, List<KeyIndexEntry>> KeyIndex = new();

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
        var subscriptionKey = new SubscriptionKey(property, layerSource);
        if (subscriptions.TryGetValue(subscriptionKey, out var existingSubscription))
        {
            if (Equals(existingSubscription.ResourceKey, resourceKey))
            {
                RefreshDynamicResource(target, subscriptionKey);
                return;
            }

            target.ResourcesChanged -= existingSubscription.Handler;
            DeactivateIndexEntry(existingSubscription);
            subscriptions.Remove(subscriptionKey);
        }

        // The event source is the target itself, so use the sender instead of closing over
        // the target. This keeps the subscription value free of an unnecessary strong edge
        // back to the ConditionalWeakTable key.
        DynamicResourceSubscription? subscription = null;
        EventHandler handler = (sender, _) =>
        {
            if (sender is FrameworkElement element && subscription != null)
            {
                RefreshDynamicResource(
                    element,
                    new SubscriptionKey(subscription.Property, subscription.LayerSource));
            }
        };
        subscription = new DynamicResourceSubscription
        {
            Property = property,
            ResourceKey = resourceKey,
            Handler = handler,
            LayerSource = layerSource
        };
        subscriptions[subscriptionKey] = subscription;

        target.ResourcesChanged += handler;
        var registration = EnsureTargetRegistered(target);
        AddToKeyIndex(registration, subscriptionKey, resourceKey, subscription);
        RefreshDynamicResource(target, subscriptionKey);
    }

    internal static bool TryGetDynamicResourceKey(FrameworkElement target, DependencyProperty property, out object? resourceKey)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        resourceKey = null;

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return false;

        var source = target.GetValueSourceInternal(property).BaseValueSource;
        var effectiveLayer = source switch
        {
            BaseValueSource.ParentTemplate => DependencyObject.LayerValueSource.ParentTemplate,
            BaseValueSource.StyleTrigger => DependencyObject.LayerValueSource.StyleTrigger,
            BaseValueSource.TemplateTrigger => DependencyObject.LayerValueSource.TemplateTrigger,
            BaseValueSource.ParentTemplateTrigger
                => DependencyObject.LayerValueSource.ParentTemplateTrigger,
            BaseValueSource.Style or BaseValueSource.DefaultStyle
                => DependencyObject.LayerValueSource.StyleSetter,
            _ => (DependencyObject.LayerValueSource?)null
        };

        if (!subscriptions.TryGetValue(new SubscriptionKey(property, effectiveLayer), out var subscription))
        {
            // A caller can ask while a higher non-resource value is effective. Prefer
            // the local expression and then the highest framework layer as a stable
            // fallback, matching dependency-property precedence.
            var bestPrecedence = int.MaxValue;
            foreach (var pair in subscriptions)
            {
                if (!ReferenceEquals(pair.Key.Property, property))
                    continue;

                var precedence = GetLayerPrecedence(pair.Key.LayerSource);
                if (precedence < bestPrecedence)
                {
                    bestPrecedence = precedence;
                    subscription = pair.Value;
                }
            }

            if (subscription == null)
                return false;
        }

        resourceKey = subscription.ResourceKey;
        return true;
    }

    internal static void ClearDynamicResource(FrameworkElement target, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        var keys = subscriptions.Keys
            .Where(key => ReferenceEquals(key.Property, property))
            .ToArray();
        foreach (var key in keys)
        {
            if (!subscriptions.Remove(key, out var subscription))
                continue;
            target.ResourcesChanged -= subscription.Handler;
            DeactivateIndexEntry(subscription);
        }

        RemoveEmptyTargetRegistration(target, subscriptions);
    }

    /// <summary>
    /// Local-expression-only variant of <see cref="TryGetDynamicResourceKey"/>: reports a key only
    /// when the markup itself declared <c>{DynamicResource}</c> on the property (layer == null),
    /// never falling back to style/template-layer subscriptions. Hot reload uses this to decide
    /// whether a patched attribute IS a dynamic resource — the permissive overload also matches the
    /// implicit theme style's subscription, so under a theme every styled property (e.g.
    /// TextBlock.Foreground) reported a key and the literal value in the patch was silently
    /// discarded in favor of re-subscribing the theme resource.
    /// </summary>
    internal static bool TryGetLocalDynamicResourceKey(
        FrameworkElement target, DependencyProperty property, out object? resourceKey)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        resourceKey = null;
        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return false;

        if (subscriptions.TryGetValue(new SubscriptionKey(property, null), out var subscription))
        {
            resourceKey = subscription.ResourceKey;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes only the local-expression (layer == null) subscription on the property, leaving
    /// style/template-layer subscriptions (e.g. the implicit theme style's) alive. Hot reload uses
    /// this before writing a literal patch value: the layer-agnostic
    /// <see cref="ClearDynamicResource(FrameworkElement, DependencyProperty)"/> would also tear
    /// down the theme's subscription, so the property would stop tracking theme changes after the
    /// literal is later removed again.
    /// </summary>
    internal static void ClearLocalDynamicResource(FrameworkElement target, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        var key = new SubscriptionKey(property, null);
        if (!subscriptions.Remove(key, out var subscription))
            return;

        target.ResourcesChanged -= subscription.Handler;
        DeactivateIndexEntry(subscription);
        RemoveEmptyTargetRegistration(target, subscriptions);
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

        var key = new SubscriptionKey(property, layerSource);
        if (!subscriptions.TryGetValue(key, out var subscription))
            return;

        target.ResourcesChanged -= subscription.Handler;
        subscriptions.Remove(key);
        DeactivateIndexEntry(subscription);
        RemoveEmptyTargetRegistration(target, subscriptions);
    }

    internal static void PromoteDynamicResourcesToLayer(
        FrameworkElement target,
        DependencyObject.LayerValueSource layerSource)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            return;

        foreach (var entry in subscriptions
                     .Where(static pair => pair.Key.LayerSource == null)
                     .ToArray())
        {
            var oldKey = entry.Key;
            var subscription = entry.Value;
            var newKey = new SubscriptionKey(oldKey.Property, layerSource);
            if (subscriptions.Remove(newKey, out var replaced))
            {
                target.ResourcesChanged -= replaced.Handler;
                DeactivateIndexEntry(replaced);
            }

            subscriptions.Remove(oldKey);
            subscription.LayerSource = layerSource;
            subscriptions[newKey] = subscription;

            // 换层改的是订阅在字典里的键，而索引条目记的正是那个键。不搬家的话，旧条目
            // 会在下次校验时被判为失效剔除，而新键从未登记——这条订阅就此**永久掉出**
            // 定向刷新，主题调色板变化再也到不了它。样式 / 模板应用时会走这条路径，
            // 掉出的将是整棵树上所有由样式提供的 ThemeResource。
            DeactivateIndexEntry(subscription);
            AddToKeyIndex(
                EnsureTargetRegistered(target), newKey, subscription.ResourceKey, subscription);

            RefreshDynamicResource(target, newKey);
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

        foreach (var key in subscriptions.Keys.ToArray())
        {
            RefreshDynamicResource(target, key);
        }
    }

    internal static void RefreshAll()
    {
        // Theme switches are infrequent; a full sweep is acceptable and avoids
        // missing updates when subtree resource notifications are skipped.
        RefreshForKeys(changedKeys: null);
    }

    /// <summary>
    /// Refreshes registrations that are not part of a loaded live-root broadcast. Loaded
    /// elements receive the lightweight theme ResourcesChanged walk; detached and unshown
    /// trees are completed through this global registry pass.
    /// </summary>
    internal static void RefreshUnloaded()
    {
        RefreshForKeysCore(changedKeys: null, unloadedOnly: true);
    }

    /// <summary>
    /// Refreshes only subscriptions whose resource key is in <paramref name="changedKeys"/>.
    /// Pass null to refresh ALL subscriptions (theme switch).
    /// </summary>
    internal static void RefreshForKeys(IReadOnlySet<object>? changedKeys)
    {
        RefreshForKeysCore(changedKeys, unloadedOnly: false);
    }

    private static void RefreshForKeysCore(
        IReadOnlySet<object>? changedKeys,
        bool unloadedOnly)
    {
        if (changedKeys != null)
        {
            // 定向刷新走键索引，代价与命中数成正比。它扫的是索引而不是注册表，
            // 天然覆盖 detached / 未显示的目标，所以 unloadedOnly 在这条路径上无意义。
            RefreshByKeyIndex(changedKeys);
            return;
        }

        // 以下是全量路径（主题键切换等，没有可用的键集合）：只能走一遍注册表。
        //
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

            if (unloadedOnly && target.IsLoaded)
                continue;

            if (!Subscriptions.TryGetValue(target, out var subscriptions) || subscriptions.Count == 0)
            {
                UnregisterTarget(target);
                continue;
            }

            // 快照后再刷新：刷新可能展开模板并注册新的动态资源，直接在字典上迭代会抛。
            foreach (var key in subscriptions.Keys.ToArray())
            {
                RefreshDynamicResource(target, key);
            }
        }

        // ThemeResource can also target Freezable-like dependency objects (for example a
        // brush or transform) through a FrameworkElement host. A whole-tree resource
        // broadcast used to refresh these indirectly via the host's ResourcesChanged event.
        // The optimized theme path deliberately skips that expensive broadcast, so include
        // non-visual subscriptions in the same finite global sweep.
        foreach (var entry in NonVisualSubscriptions.ToArray())
        {
            foreach (var subscription in entry.Value.Values.ToArray())
            {
                if (unloadedOnly && subscription.Host.IsLoaded)
                    continue;

                RefreshNonVisualDynamicResource(entry.Key, subscription.Property);
            }
        }
    }

    /// <summary>
    /// 只刷新真正引用了变更键的订阅，代价与命中数成正比，与页面规模无关。
    /// </summary>
    private static void RefreshByKeyIndex(IReadOnlySet<object> changedKeys)
    {
        foreach (var resourceKey in changedKeys)
        {
            var entries = SnapshotKeyIndexEntries(resourceKey);
            foreach (var entry in entries)
            {
                if (!entry.IsActive || !entry.Registration.Target.TryGetTarget(out var target))
                {
                    continue;
                }

                // 索引条目只增不删，所以这里必须校验它描述的订阅仍然存在且键没被改过，
                // 否则会去刷一个早已被换掉的槽位。
                if (!Subscriptions.TryGetValue(target, out var subscriptions) ||
                    !subscriptions.TryGetValue(entry.SubscriptionKey, out var subscription) ||
                    !Equals(subscription.ResourceKey, resourceKey))
                {
                    entry.IsActive = false;
                    continue;
                }

                RefreshDynamicResource(target, entry.SubscriptionKey);
            }
        }

        // 非视觉订阅（画刷 / 变换等 Freezable 上的 ThemeResource）数量很少，不进索引：
        // 一次弱表快照加一次键过滤就够，不值得再维护一份平行索引。
        foreach (var entry in NonVisualSubscriptions.ToArray())
        {
            var hostSubscriptions = entry.Value;
            if (hostSubscriptions.Count == 0 ||
                !HasMatchingNonVisualSubscription(hostSubscriptions, changedKeys))
            {
                continue;
            }

            foreach (var subscription in hostSubscriptions.Values.ToArray())
            {
                if (!changedKeys.Contains(subscription.ResourceKey))
                {
                    continue;
                }

                RefreshNonVisualDynamicResource(entry.Key, subscription.Property);
            }
        }
    }

    private static bool HasMatchingNonVisualSubscription(
        Dictionary<DependencyProperty, NonVisualDynamicResourceSubscription> subscriptions,
        IReadOnlySet<object> changedKeys)
    {
        foreach (var subscription in subscriptions.Values)
        {
            if (changedKeys.Contains(subscription.ResourceKey))
            {
                return true;
            }
        }

        return false;
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
            KeyIndex.Clear();
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

    private static DynamicResourceTargetRegistration EnsureTargetRegistered(FrameworkElement target)
    {
        lock (RegistryGate)
        {
            if (TargetRegistrations.TryGetValue(target, out var existingRegistration))
            {
                if (existingRegistration.IsActive)
                    return existingRegistration;

                TargetRegistrations.Remove(target);
            }

            var registration = new DynamicResourceTargetRegistration(target);
            TargetRegistrations.Add(target, registration);
            RegisteredTargets.Add(registration);

            if (_inactiveTargetCount >= 64 && _inactiveTargetCount * 2 >= RegisteredTargets.Count)
            {
                CompactRegistryNoLock();
            }

            return registration;
        }
    }

    /// <summary>
    /// 把一个订阅的索引槽位就地作废。订阅被移除或换键时调用。
    /// </summary>
    /// <remarks>
    /// 漏掉某条路径不会导致错刷——槽位在使用时还会做三重校验——但会让失效槽位堆在列表里，
    /// 直到下一次扫描压缩为止。<see cref="PromoteDynamicResourcesToLayer"/> 是例外：那里
    /// 必须同时重新登记，否则订阅会掉出索引。
    /// </remarks>
    private static void DeactivateIndexEntry(DynamicResourceSubscription? subscription)
    {
        if (subscription?.IndexEntry is { } entry)
        {
            entry.IsActive = false;
            subscription.IndexEntry = null;
        }
    }

    private static void AddToKeyIndex(
        DynamicResourceTargetRegistration registration,
        SubscriptionKey subscriptionKey,
        object resourceKey,
        DynamicResourceSubscription subscription)
    {
        var entry = new KeyIndexEntry
        {
            Registration = registration,
            SubscriptionKey = subscriptionKey,
            ResourceKey = resourceKey,
        };

        subscription.IndexEntry = entry;

        lock (RegistryGate)
        {
            if (!KeyIndex.TryGetValue(resourceKey, out var entries))
            {
                entries = [];
                KeyIndex[resourceKey] = entries;
            }

            entries.Add(entry);
        }
    }

    /// <summary>
    /// 取出某个资源键下仍然有效的订阅槽位快照，并顺手压缩已失效的条目。
    /// </summary>
    /// <remarks>
    /// 必须返回快照而不是就地迭代：刷新一个动态资源可能展开模板，模板构造会注册新的动态
    /// 资源，从而在同一次调用里改到这张列表。
    /// </remarks>
    private static KeyIndexEntry[] SnapshotKeyIndexEntries(object resourceKey)
    {
        lock (RegistryGate)
        {
            if (!KeyIndex.TryGetValue(resourceKey, out var entries) || entries.Count == 0)
            {
                return [];
            }

            var writeIndex = 0;
            for (var readIndex = 0; readIndex < entries.Count; readIndex++)
            {
                var entry = entries[readIndex];
                if (!entry.IsActive || !entry.Registration.IsActive)
                {
                    continue;
                }

                entries[writeIndex++] = entry;
            }

            if (writeIndex < entries.Count)
            {
                entries.RemoveRange(writeIndex, entries.Count - writeIndex);
            }

            if (entries.Count == 0)
            {
                KeyIndex.Remove(resourceKey);
                return [];
            }

            return entries.ToArray();
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

    private static void RefreshDynamicResource(FrameworkElement target, SubscriptionKey key)
    {
        if (!Subscriptions.TryGetValue(target, out var subscriptions))
            return;

        if (!subscriptions.TryGetValue(key, out var subscription))
            return;

        var resolved = ResourceLookup.FindResource(target, subscription.ResourceKey);
        if (subscription.LayerSource.HasValue)
        {
            var layerSource = subscription.LayerSource.Value;
            if (resolved != null)
            {
                if (target.TryGetLayerValue(key.Property, layerSource, out var current) &&
                    (ReferenceEquals(current, resolved) || Equals(current, resolved)))
                {
                    return;
                }

                target.SetLayerValue(key.Property, resolved, layerSource);
            }
            else if (target.TryGetLayerValue(key.Property, layerSource, out _))
            {
                target.ClearLayerValue(key.Property, layerSource);
            }
            return;
        }

        if (resolved != null && IsValidResourceValue(key.Property, resolved))
        {
            target.SetValue(key.Property, resolved);
        }
        else if (target.HasLocalValue(key.Property))
        {
            target.ClearValue(key.Property);
        }
    }

    private static int GetLayerPrecedence(DependencyObject.LayerValueSource? source)
    {
        // 数值越小优先级越高；顺序必须与 DependencyValueStore.RecomputeEffective 一致。
        return source switch
        {
            null => 0,
            DependencyObject.LayerValueSource.ParentTemplateTrigger => 1,
            DependencyObject.LayerValueSource.ParentTemplate => 2,
            DependencyObject.LayerValueSource.StyleTrigger => 3,
            DependencyObject.LayerValueSource.TemplateTrigger => 4,
            DependencyObject.LayerValueSource.StyleSetter => 5,
            _ => 6
        };
    }

    private static void RemoveEmptyTargetRegistration(
        FrameworkElement target,
        Dictionary<SubscriptionKey, DynamicResourceSubscription> subscriptions)
    {
        if (subscriptions.Count != 0)
            return;

        Subscriptions.Remove(target);
        UnregisterTarget(target);
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
        if (resolved != null && IsValidResourceValue(property, resolved))
        {
            target.SetValue(property, resolved);
        }
        else if (target.HasLocalValue(property))
        {
            target.ClearValue(property);
        }
    }

    private static bool IsValidResourceValue(DependencyProperty property, object value)
    {
        return property.IsValidType(value) && property.IsValidValue(value);
    }
}
