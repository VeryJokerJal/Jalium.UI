using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Provides a hash table/dictionary implementation that contains resources
/// used by components and other elements of a UI application.
/// </summary>
public class ResourceDictionary : IDictionary, ISupportInitialize, Jalium.UI.Markup.INameScope, Jalium.UI.Markup.IUriContext
{
    private sealed class NotificationDeferralScope : IDisposable
    {
        private ResourceDictionary? _owner;

        public NotificationDeferralScope(ResourceDictionary owner)
        {
            _owner = owner;
            owner._notificationDeferralDepth++;
        }

        public void Dispose()
        {
            if (_owner == null)
            {
                return;
            }

            _owner.EndNotificationDeferral();
            _owner = null;
        }
    }


    private readonly Dictionary<object, object?> _innerDictionary = new();
    private readonly MergedDictionaryCollection _mergedDictionaries;
    private readonly NameScope _nameScope = new();
    private Dictionary<object, ResourceDictionary>? _themeDictionaries;
    private DeferrableContent? _deferrableContent;
    private Uri? _source;
    private bool _invalidatesImplicitDataTemplateResources;
    private bool _isInitializing;
    private int _notificationDeferralDepth;
    private bool _notificationPending;
    private HashSet<object>? _pendingChangedKeys; // null means "all keys changed"
    private bool _pendingAllChanged;

    // Cycle detection for recursive MergedDictionaries lookups
    [ThreadStatic]
    private static HashSet<ResourceDictionary>? t_lookupChain;

    /// <summary>
    /// Event args for resource dictionary changes, carrying the set of changed keys.
    /// </summary>
    public sealed class ResourcesChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The keys that were added or modified. Null means "all keys may have changed"
        /// (e.g. merged dictionary replacement).
        /// </summary>
        public IReadOnlySet<object>? ChangedKeys { get; }

        public ResourcesChangedEventArgs(IReadOnlySet<object>? changedKeys) => ChangedKeys = changedKeys;

        /// <summary>Sentinel for "everything changed".</summary>
        public static readonly ResourcesChangedEventArgs All = new(null);
    }

    /// <summary>
    /// Occurs when this dictionary or one of its merged dictionaries changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Occurs when resources change, with information about which keys changed.
    /// </summary>
    public event EventHandler<ResourcesChangedEventArgs>? ChangedWithKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDictionary"/> class.
    /// </summary>
    public ResourceDictionary()
    {
        _mergedDictionaries = new MergedDictionaryCollection(this);
    }

    /// <summary>
    /// Gets a collection of merged dictionaries.
    /// </summary>
    public Collection<ResourceDictionary> MergedDictionaries => _mergedDictionaries;

    /// <summary>
    /// Gets or sets the deferred XAML content associated with this dictionary.
    /// </summary>
    /// <remarks>
    /// The XAML loader owns the payload represented by this object. Resource values produced
    /// from deferred content are finalized through <see cref="OnGettingValue"/> when read.
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DeferrableContent? DeferrableContent
    {
        get => _deferrableContent;
        set => _deferrableContent = value;
    }

    /// <summary>
    /// Gets or sets whether implicit data-template resource changes invalidate template selection.
    /// </summary>
    [DefaultValue(false)]
    public bool InvalidatesImplicitDataTemplateResources
    {
        get => _invalidatesImplicitDataTemplateResources;
        set => _invalidatesImplicitDataTemplateResources = value;
    }

    /// <summary>
    /// Gets the collection of theme dictionaries, keyed by theme name (e.g., "Light", "Dark", "HighContrast").
    /// Theme dictionaries allow different resource sets to be applied based on the current application theme.
    /// </summary>
    public IDictionary<object, ResourceDictionary> ThemeDictionaries => _themeDictionaries ??= new Dictionary<object, ResourceDictionary>();

    private static object? s_currentThemeKey;

    /// <summary>
    /// Gets or sets the current theme key used to select resources from ThemeDictionaries.
    /// Common values include "Light", "Dark", and "HighContrast".
    /// Updating this key refreshes all active dynamic-resource bindings so themed dictionaries
    /// are re-evaluated immediately.
    /// </summary>
    public static object? CurrentThemeKey
    {
        get => s_currentThemeKey;
        set
        {
            if (Equals(s_currentThemeKey, value))
                return;

            s_currentThemeKey = value;

            // 主题键决定 TryGetValue 走哪本 ThemeDictionaries，切换即改变查找结果，
            // 所以合并子树查找缓存必须在这里失效。这与下面「不在此处 RefreshAll」的策略
            // 不冲突：失效只是丢弃缓存，不触发任何重新求值或树遍历。
            // （回归测试：MergedLookupCacheInvariantTests.ThemeKeySwitch_Invalidates_CachedThemeLookup）
            InvalidateMergedLookupCaches();

            // Note: RefreshAll() is NOT called here to avoid double-refresh.
            // Callers (e.g. ThemeManager.ForceThemeRefresh) are responsible for
            // triggering a single consolidated refresh after all dictionary
            // replacements are complete.
        }
    }

    /// <summary>
    /// Gets or sets the uniform resource identifier (URI) to load resources from.
    /// When set, the dictionary loads resources from the specified location.
    /// </summary>
    /// <remarks>
    /// The Source property is used to load resources from an external XAML file.
    /// Relative paths are resolved against the BaseUri of the parent dictionary.
    /// The actual loading is performed by the XAML parser during parsing.
    /// </remarks>
    public Uri? Source
    {
        get => _source;
        set
        {
            if (Equals(_source, value))
            {
                return;
            }

            if (_source is not null)
            {
                Diagnostics.ResourceDictionaryDiagnosticsStore.UnregisterSource(this, _source);
            }

            _source = value;

            if (_source is not null)
            {
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterSource(this, _source);
            }
        }
    }

    /// <summary>
    /// Gets or sets the base URI for resolving relative Source paths.
    /// This is typically set by the XAML parser during loading.
    /// </summary>
    internal Uri? BaseUri { get; set; }

    Uri? Jalium.UI.Markup.IUriContext.BaseUri
    {
        get => BaseUri;
        set => BaseUri = value;
    }

    /// <summary>
    /// Gets or sets the assembly used for loading embedded resources.
    /// This is typically set by the XAML parser during loading.
    /// </summary>
    internal Assembly? SourceAssembly { get; set; }

    /// <summary>
    /// Gets or sets a callback used by the XAML parser to load ResourceDictionary from Source.
    /// This allows the Core assembly to remain independent of the Xaml assembly.
    /// </summary>
    public static Func<ResourceDictionary, Uri, Assembly?, ResourceDictionary?>? SourceLoader { get; set; }

    /// <summary>
    /// Defers <see cref="Changed"/> notifications until the returned scope is disposed.
    /// Nested deferrals are supported and coalesced into a single notification.
    /// </summary>
    public IDisposable DeferNotifications()
    {
        return new NotificationDeferralScope(this);
    }

    /// <summary>
    /// Marks the beginning of an initialization transaction.
    /// </summary>
    public void BeginInit()
    {
        if (_isInitializing)
        {
            throw new InvalidOperationException("Nested BeginInit calls are not supported.");
        }

        _isInitializing = true;
        _notificationDeferralDepth++;
    }

    /// <summary>
    /// Marks the end of an initialization transaction and publishes accumulated changes.
    /// </summary>
    public void EndInit()
    {
        if (!_isInitializing)
        {
            throw new InvalidOperationException("BeginInit must be called before EndInit.");
        }

        _isInitializing = false;
        EndNotificationDeferral();
    }

    /// <summary>
    /// Registers an object in this dictionary's name scope.
    /// </summary>
    public void RegisterName(string name, object scopedElement)
    {
        _nameScope.RegisterName(name, scopedElement);
    }

    /// <summary>
    /// Removes a name from this dictionary's name scope.
    /// </summary>
    public void UnregisterName(string name)
    {
        _nameScope.UnregisterName(name);
    }

    /// <summary>
    /// Finds an object registered in this dictionary's name scope.
    /// </summary>
    public object? FindName(string name)
    {
        return _nameScope.FindName(name);
    }

    /// <summary>
    /// Copies all resources from another dictionary into this one.
    /// </summary>
    /// <param name="source">The source dictionary to copy from.</param>
    internal void CopyFrom(ResourceDictionary source)
    {
        var changed = false;

        _deferrableContent = source._deferrableContent;
        _invalidatesImplicitDataTemplateResources = source._invalidatesImplicitDataTemplateResources;

        foreach (var kvp in source._innerDictionary)
        {
            _innerDictionary[kvp.Key] = kvp.Value;
            changed = true;
        }

        // Also copy merged dictionaries
        foreach (var merged in source._mergedDictionaries)
        {
            _mergedDictionaries.Add(merged);
            changed = true;
        }

        if (source._themeDictionaries != null && source._themeDictionaries.Count > 0)
        {
            _themeDictionaries ??= new Dictionary<object, ResourceDictionary>();
            _themeDictionaries.Clear();

            foreach (var kvp in source._themeDictionaries)
            {
                _themeDictionaries[kvp.Key] = kvp.Value;
                changed = true;
            }
        }

        if (changed)
        {
            OnChanged();
        }
    }

    /// <summary>
    /// Gets the number of items in this dictionary (not including merged dictionaries).
    /// </summary>
    public int Count => _innerDictionary.Count;

    /// <summary>
    /// Gets a value indicating whether the dictionary is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether the dictionary has a fixed size.
    /// </summary>
    public bool IsFixedSize => false;

    /// <summary>
    /// Gets a collection containing the keys.
    /// </summary>
    public ICollection Keys => _innerDictionary.Keys;

    /// <summary>
    /// Gets a collection containing the values.
    /// </summary>
    public ICollection Values => new ResourceValuesCollection(this);

    /// <summary>
    /// Gets or sets the element with the specified key.
    /// </summary>
    public object? this[object key]
    {
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            throw new KeyNotFoundException($"Resource key '{key}' not found.");
        }
        set
        {
            _innerDictionary[key] = value;
            OnChangedForKey(key);
        }
    }

    /// <summary>
    /// Adds a resource with the specified key.
    /// </summary>
    public void Add(object key, object? value)
    {
        _innerDictionary.Add(key, value);
        OnChangedForKey(key);
    }

    /// <summary>
    /// Determines whether the dictionary contains a resource with the specified key.
    /// </summary>
    public bool Contains(object key)
    {
        if (_innerDictionary.ContainsKey(key))
            return true;

        var chain = t_lookupChain ??= new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
        if (!chain.Add(this))
            return false; // Cycle detected

        try
        {
            // Check theme dictionaries (higher priority than merged)
            if (_themeDictionaries != null && CurrentThemeKey != null)
            {
                if (_themeDictionaries.TryGetValue(CurrentThemeKey, out var themeDict))
                {
                    if (themeDict.Contains(key))
                        return true;
                }
            }

            // Check merged dictionaries in reverse order (later overrides earlier)
            for (int i = _mergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (_mergedDictionaries[i].Contains(key))
                    return true;
            }

            return false;
        }
        finally
        {
            chain.Remove(this);
        }
    }

    /// <summary>
    /// Determines whether the dictionary contains a resource with the specified key.
    /// </summary>
    public bool ContainsKey(object key) => Contains(key);

    /// <summary>
    /// Tries to get the value associated with the specified key.
    /// </summary>
    public bool TryGetValue(object key, out object? value)
        => TryGetValue(key, out value, out _);

    // ── 合并子树查找缓存 ──────────────────────────────────────────────────
    //
    // 缓存「本地 miss 之后穿透 theme + merged 子树」的查找结果，**含 negative 结果**。
    //
    // 动机：隐式样式查找（LookupImplicitStyle 沿类型继承链逐层 TryFindResource）绝大多数
    // 是全 miss，而 miss 恰恰是最贵的路径——要走遍整棵 merged/theme 字典树，每层一次递归
    // 调用加一次 thread-static HashSet Add/Remove。主题字典规模是几十个（Themes/
    // Generic.jalxaml 有 30 处 Source=，Themes/Controls 下 27 个字典），于是单次冷查找就要
    // 几十到上百次字典操作；整页视图替换时这条路径按 元素数 x 类型继承链长度 反复走，成为
    // 换页帧里 UI 线程的主要开销之一。Grid/StackPanel/Border 这类没有 theme style 的类型
    // 每次都是穿透式全 miss，negative 缓存正是为它们准备的。
    //
    // ★ 为什么必须用独立于 s_globalCacheGeneration 的版本号：那个版本号还会被**逻辑父变更**
    // bump（FrameworkElement.AddLogicalChild / RemoveLogicalChild），而逻辑父变化根本不改变
    // 任何字典的 key→value 映射。构树时每挂一个逻辑子就清一次全局缓存——那正是本缓存要
    // 消除的开销，复用同一版本号会让它在构树期间被反复清空、命中率归零。本版本号只在真正
    // 能改变查找结果的写操作上 bump（见 InvalidateMergedLookupCaches 的调用点）。
    //
    // 线程安全：与 ResourceLookup 的 per-element memo 同构，走 [ThreadStatic]，既不引入锁
    // 也不存在跨线程撕裂。
    private static int s_contentGeneration;

    [ThreadStatic]
    private static Dictionary<(int Dictionary, object Key), MergedLookupResult>? t_mergedLookupCache;
    [ThreadStatic]
    private static int t_mergedLookupGeneration;

    private const int MaxMergedLookupCacheEntries = 8192;

    private readonly record struct MergedLookupResult(
        bool Found,
        object? Value,
        ResourceDictionary? Source);

    /// <summary>
    /// 让所有合并子树查找缓存失效。必须在任何可能改变查找结果的写操作上调用：条目增删改、
    /// MergedDictionaries / ThemeDictionaries 结构变化、<see cref="CurrentThemeKey"/> 切换。
    /// </summary>
    private static void InvalidateMergedLookupCaches()
        => Interlocked.Increment(ref s_contentGeneration);

    /// <summary>
    /// 资源**内容**版本号：只在真正能改变查找结果的写操作上递增（条目增删改、
    /// MergedDictionaries / ThemeDictionaries 结构变化、<see cref="CurrentThemeKey"/> 切换）。
    ///
    /// <para>与 <see cref="ResourceLookup.CacheGeneration"/> 的区别很关键：那个还会被逻辑父
    /// 变更（AddLogicalChild / RemoveLogicalChild）递增，构树期间几乎每步都在变，无法用来判断
    /// 「资源内容是否还是原来那份」。需要后者语义的调用方必须用本属性。</para>
    /// </summary>
    internal static int ContentGeneration => Volatile.Read(ref s_contentGeneration);

    /// <summary>
    /// Tries to resolve a resource and also returns the dictionary that supplied the value.
    /// The source dictionary is used by the WPF-compatible static-resource diagnostics path.
    /// </summary>
    internal bool TryGetValue(
        object key,
        out object? value,
        out ResourceDictionary? sourceDictionary)
    {
        // Check local dictionary first
        if (TryGetLocalValue(key, out value))
        {
            sourceDictionary = this;
            return true;
        }

        // 只有顶层查找（当前线程的循环检测链为空）才读写缓存。
        //
        // ★ 这个限制是正确性要求，不是保守：嵌套查找的结果是「上下文相关」的——环检测会在
        // `chain.Add(this)` 失败时把结果截断成 false，而那个 false 只对「祖先链上已经有本
        // 字典」这个上下文成立。举例：A merged [B, C]、B merged A、C 里有 key。查 A 时会
        // 探到 B，B 再探 A 被环检测截断，于是 B 这一层返回 false——但 B 作为独立查询的真实
        // 结果是 true（B → A → C）。把那个 false 缓存下来就会污染后续对 B 的独立查询。
        // 顶层查找不存在截断，结果是完整的，可以安全缓存；而顶层恰好就是热路径
        // （FindResourceCore 走完祖先链后落到 ApplicationResourceLookup → 根字典），
        // 一次命中即省掉整棵字典树的穿透，收益不因这个限制而减少。
        var isTopLevelLookup = t_lookupChain is null || t_lookupChain.Count == 0;
        (int, object) cacheKey = default;
        Dictionary<(int Dictionary, object Key), MergedLookupResult>? cache = null;

        if (isTopLevelLookup)
        {
            var generation = Volatile.Read(ref s_contentGeneration);
            cache = t_mergedLookupCache;
            if (cache is null)
            {
                cache = t_mergedLookupCache = new Dictionary<(int, object), MergedLookupResult>();
                t_mergedLookupGeneration = generation;
            }
            else if (t_mergedLookupGeneration != generation)
            {
                cache.Clear();
                t_mergedLookupGeneration = generation;
            }

            cacheKey = (RuntimeHelpers.GetHashCode(this), key);
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                value = cached.Value;
                sourceDictionary = cached.Source;
                return cached.Found;
            }
        }

        var found = TryGetFromMergedSubtree(key, out value, out sourceDictionary);

        // 条目上限防无界增长（同 ResourceLookup memo 的做法）。超限后不再写入，退化成原来的
        // 穿透查找，只影响性能不影响正确性。
        if (cache is not null && cache.Count < MaxMergedLookupCacheEntries)
        {
            cache[cacheKey] = new MergedLookupResult(found, value, sourceDictionary);
        }

        return found;
    }

    /// <summary>
    /// 穿透 theme + merged 子树的原始查找（无缓存）。带 thread-static 环检测。
    /// </summary>
    private bool TryGetFromMergedSubtree(
        object key,
        out object? value,
        out ResourceDictionary? sourceDictionary)
    {
        var chain = t_lookupChain ??= new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
        if (!chain.Add(this))
        {
            value = null;
            sourceDictionary = null;
            return false; // Cycle detected
        }

        try
        {
            // Check theme dictionaries (higher priority than merged)
            if (_themeDictionaries != null && CurrentThemeKey != null)
            {
                if (_themeDictionaries.TryGetValue(CurrentThemeKey, out var themeDict))
                {
                    if (themeDict.TryGetValue(key, out value, out sourceDictionary))
                        return true;
                }
            }

            // Check merged dictionaries in reverse order (later overrides earlier)
            for (int i = _mergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (_mergedDictionaries[i].TryGetValue(key, out value, out sourceDictionary))
                    return true;
            }

            value = null;
            sourceDictionary = null;
            return false;
        }
        finally
        {
            chain.Remove(this);
        }
    }

    /// <summary>
    /// Removes the resource with the specified key.
    /// </summary>
    public void Remove(object key)
    {
        if (_innerDictionary.Remove(key))
        {
            OnChangedForKey(key);
        }
    }

    /// <summary>
    /// Removes all resources from the dictionary.
    /// </summary>
    public void Clear()
    {
        if (_innerDictionary.Count == 0)
            return;

        _innerDictionary.Clear();
        OnChanged();
    }

    /// <summary>
    /// Returns an enumerator over the local dictionary entries.
    /// </summary>
    public IDictionaryEnumerator GetEnumerator()
    {
        return new ResourceDictionaryEnumerator(CreateEntryEnumerator());
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region IDictionary

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => ((ICollection)_innerDictionary).SyncRoot;

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (array is DictionaryEntry[] dictionaryEntries)
        {
            CopyTo(dictionaryEntries, index);
            return;
        }

        if (array is KeyValuePair<object, object?>[] keyValuePairs)
        {
            CopyEntriesTo(keyValuePairs, index);
            return;
        }

        throw new ArgumentException("The destination array type is not supported.", nameof(array));
    }

    /// <summary>
    /// Copies the local dictionary entries to an array of <see cref="DictionaryEntry"/> values.
    /// </summary>
    public void CopyTo(DictionaryEntry[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ValidateCopyToArguments(array.Length, arrayIndex);

        int destinationIndex = arrayIndex;
        foreach (var entry in SnapshotEntries())
        {
            object? value = GetValueForRead(entry.Key, entry.Value);
            array[destinationIndex++] = new DictionaryEntry(entry.Key, value);
        }
    }

    private void CopyEntriesTo(KeyValuePair<object, object?>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ValidateCopyToArguments(array.Length, arrayIndex);

        int destinationIndex = arrayIndex;
        foreach (var entry in SnapshotEntries())
        {
            object? value = GetValueForRead(entry.Key, entry.Value);
            array[destinationIndex++] = new KeyValuePair<object, object?>(entry.Key, value);
        }
    }

    /// <summary>
    /// Gives derived dictionaries an opportunity to realize or replace a value when it is read.
    /// </summary>
    /// <param name="key">The resource key being read.</param>
    /// <param name="value">The resource value, which may be replaced by the override.</param>
    /// <param name="canCache">Whether a replacement value may be cached in this dictionary.</param>
    protected virtual void OnGettingValue(object key, ref object? value, out bool canCache)
    {
        canCache = true;
    }

    /// <summary>
    /// 只读取本字典自己的条目，不下探合并字典，也不按 <see cref="CurrentThemeKey"/> 走
    /// <see cref="ThemeDictionaries"/>。就地刷新调色板时必须用它：普通查找在当前主题变体
    /// 也定义了同名键时会返回变体里的那份，于是「更新既有值」会改到错误的字典上。
    /// </summary>
    internal bool TryGetOwnValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryGetLocalValue(key, out value);
    }

    private bool TryGetLocalValue(object key, out object? value)
    {
        if (!_innerDictionary.TryGetValue(key, out value))
        {
            return false;
        }

        value = GetValueForRead(key, value);
        return true;
    }

    private object? GetValueForRead(object key, object? value)
    {
        // WPF does not invoke the deferred-value hook for a stored null value.
        if (value is null)
        {
            return null;
        }

        object? resolvedValue = value;
        OnGettingValue(key, ref resolvedValue, out bool canCache);

        if (canCache &&
            _innerDictionary.TryGetValue(key, out object? currentValue) &&
            Equals(currentValue, value) &&
            !Equals(currentValue, resolvedValue))
        {
            // Realizing deferred content is a cache operation, not a resource mutation, so it
            // intentionally does not raise Changed or invalidate the resource lookup cache.
            _innerDictionary[key] = resolvedValue;

            // 但合并子树查找缓存必须失效：本字典若作为别人的 merged 子字典，刚才那个尚未
            // 实体化的值可能已经被上层缓存下来了。失效只是丢弃缓存条目，不触发任何重新求值
            // 或树遍历，因此不会造成上面那条注释要避免的 refresh 风暴。
            // （默认 OnGettingValue 不改写 value，本分支进不来；这里是为覆盖了该钩子的
            //   派生字典守住不变式。）
            InvalidateMergedLookupCaches();
        }

        return resolvedValue;
    }

    private KeyValuePair<object, object?>[] SnapshotEntries()
    {
        var entries = new KeyValuePair<object, object?>[_innerDictionary.Count];
        ((ICollection<KeyValuePair<object, object?>>)_innerDictionary).CopyTo(entries, 0);
        return entries;
    }

    private IEnumerator<KeyValuePair<object, object?>> CreateEntryEnumerator()
    {
        return new ResourceEnumerator(this, SnapshotEntries());
    }

    private void ValidateCopyToArguments(int arrayLength, int arrayIndex)
    {
        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        if (arrayIndex > arrayLength || arrayLength - arrayIndex < Count)
        {
            throw new ArgumentException("The destination array is not long enough.");
        }
    }

    private void OnChangedForKey(object key)
    {
        if (_notificationDeferralDepth > 0)
        {
            // 延迟的是**通知**，不是缓存失效——条目此刻已经改了，查找结果立即随之改变。
            // 若等到 EndNotificationDeferral 才失效，deferral 期间的查找会把旧值缓存下来
            // 并被后续命中，成为难查的 stale 读。
            InvalidateMergedLookupCaches();
            _notificationPending = true;
            if (!_pendingAllChanged)
            {
                _pendingChangedKeys ??= new HashSet<object>();
                _pendingChangedKeys.Add(key);
            }
            return;
        }

        var keys = new HashSet<object> { key };
        RaiseChanged(new ResourcesChangedEventArgs(keys));
    }

    /// <summary>
    /// 批量发布一组键的变更，语义与逐个调用 <see cref="OnChangedForKey"/> 相同，
    /// 但只产生一次通知。
    /// </summary>
    private void OnChangedForKeys(IEnumerable<object> keys)
    {
        if (_notificationDeferralDepth > 0)
        {
            InvalidateMergedLookupCaches();
            _notificationPending = true;
            if (!_pendingAllChanged)
            {
                _pendingChangedKeys ??= new HashSet<object>();
                foreach (var key in keys)
                {
                    _pendingChangedKeys.Add(key);
                }
            }

            return;
        }

        var set = keys as HashSet<object> ?? new HashSet<object>(keys);
        if (set.Count == 0)
        {
            return;
        }

        RaiseChanged(new ResourcesChangedEventArgs(set));
    }

    /// <summary>
    /// 发布一组键的变更，供在本字典之外持有的嵌套内容（例如
    /// <see cref="ThemeDictionaries"/> 中的变体字典）就地更新后补发通知使用。
    /// 变体字典不是合并字典，它的条目变更不会自行冒泡到宿主。
    /// </summary>
    internal void NotifyKeysChanged(IEnumerable<object> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        OnChangedForKeys(keys);
    }

    private void OnChanged()
    {
        if (_notificationDeferralDepth > 0)
        {
            // 同上：结构已变，缓存必须立即失效，不能跟着通知一起延迟。
            InvalidateMergedLookupCaches();
            _notificationPending = true;
            _pendingAllChanged = true; // merged dict replacement — all keys may have changed
            return;
        }

        RaiseChanged(ResourcesChangedEventArgs.All);
    }

    private void RaiseChanged(ResourcesChangedEventArgs args)
    {
        InvalidateMergedLookupCaches();
        ResourceLookup.InvalidateResourceCache();
        Changed?.Invoke(this, EventArgs.Empty);
        ChangedWithKeys?.Invoke(this, args);
    }

    private void EndNotificationDeferral()
    {
        if (_notificationDeferralDepth <= 0)
        {
            _notificationDeferralDepth = 0;
            return;
        }

        _notificationDeferralDepth--;
        if (_notificationDeferralDepth == 0 && _notificationPending)
        {
            _notificationPending = false;
            var args = _pendingAllChanged
                ? ResourcesChangedEventArgs.All
                : new ResourcesChangedEventArgs(_pendingChangedKeys);
            _pendingChangedKeys = null;
            _pendingAllChanged = false;
            RaiseChanged(args);
        }
    }

    private void OnMergedDictionaryAdded(ResourceDictionary dictionary)
    {
        // ChangedWithKeys 而不是 Changed：子字典改单个键时同样携带键集合，订阅无键的
        // Changed 会在冒泡这一步把键信息丢掉，让每一次合并子字典的条目更新都退化成
        // 「全部键都变了」。上层（Application）据此走全树隐式样式重评估，代价与主题
        // 整体替换相同——而运行时调色板更新（accent / typography）恰好全部走合并字典。
        dictionary.ChangedWithKeys += OnMergedDictionaryChangedWithKeys;
        Diagnostics.ResourceDictionaryDiagnosticsStore.LinkMergedDictionary(this, dictionary);
    }

    private void OnMergedDictionaryRemoved(ResourceDictionary dictionary)
    {
        dictionary.ChangedWithKeys -= OnMergedDictionaryChangedWithKeys;
        Diagnostics.ResourceDictionaryDiagnosticsStore.UnlinkMergedDictionary(this, dictionary);
    }

    private void OnMergedDictionaryChangedWithKeys(object? sender, ResourcesChangedEventArgs e)
    {
        if (e.ChangedKeys is { Count: > 0 } keys)
        {
            OnChangedForKeys(keys);
        }
        else
        {
            // 结构性变化（合并列表增删、Clear、整本替换）仍然是「全部键」。
            OnChanged();
        }
    }

    private sealed class MergedDictionaryCollection : Collection<ResourceDictionary>
    {
        private readonly ResourceDictionary _owner;

        public MergedDictionaryCollection(ResourceDictionary owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, ResourceDictionary item)
        {
            base.InsertItem(index, item);
            _owner.OnMergedDictionaryAdded(item);
            _owner.OnChanged();
        }

        protected override void SetItem(int index, ResourceDictionary item)
        {
            var oldItem = this[index];
            _owner.OnMergedDictionaryRemoved(oldItem);

            base.SetItem(index, item);

            _owner.OnMergedDictionaryAdded(item);
            _owner.OnChanged();
        }

        protected override void RemoveItem(int index)
        {
            var oldItem = this[index];
            _owner.OnMergedDictionaryRemoved(oldItem);

            base.RemoveItem(index);
            _owner.OnChanged();
        }

        protected override void ClearItems()
        {
            foreach (var dictionary in this)
            {
                _owner.OnMergedDictionaryRemoved(dictionary);
            }

            base.ClearItems();
            _owner.OnChanged();
        }
    }

    private sealed class ResourceEnumerator : IEnumerator<KeyValuePair<object, object?>>
    {
        private readonly ResourceDictionary _owner;
        private readonly KeyValuePair<object, object?>[] _entries;
        private int _index = -1;
        private KeyValuePair<object, object?> _current;

        public ResourceEnumerator(
            ResourceDictionary owner,
            KeyValuePair<object, object?>[] entries)
        {
            _owner = owner;
            _entries = entries;
        }

        public KeyValuePair<object, object?> Current
        {
            get
            {
                if (_index < 0 || _index >= _entries.Length)
                {
                    throw new InvalidOperationException();
                }

                return _current;
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index < _entries.Length)
            {
                _index++;
            }

            if (_index >= _entries.Length)
            {
                _current = default;
                return false;
            }

            KeyValuePair<object, object?> entry = _entries[_index];
            object? value = _owner.GetValueForRead(entry.Key, entry.Value);
            _current = new KeyValuePair<object, object?>(entry.Key, value);
            return true;
        }

        public void Reset()
        {
            _index = -1;
            _current = default;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ResourceDictionaryEnumerator : IDictionaryEnumerator, IDisposable
    {
        private readonly IEnumerator<KeyValuePair<object, object?>> _enumerator;

        public ResourceDictionaryEnumerator(IEnumerator<KeyValuePair<object, object?>> enumerator)
        {
            _enumerator = enumerator;
        }

        public DictionaryEntry Entry
        {
            get
            {
                KeyValuePair<object, object?> current = _enumerator.Current;
                return new DictionaryEntry(current.Key, current.Value);
            }
        }

        public object Key => Entry.Key;

        public object? Value => Entry.Value;

        public object Current => Entry;

        public bool MoveNext() => _enumerator.MoveNext();

        public void Reset() => _enumerator.Reset();

        public void Dispose() => _enumerator.Dispose();
    }

    private sealed class ResourceValuesCollection : ICollection<object?>, ICollection
    {
        private readonly ResourceDictionary _owner;

        public ResourceValuesCollection(ResourceDictionary owner)
        {
            _owner = owner;
        }

        public int Count => _owner.Count;

        public bool IsReadOnly => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => ((ICollection)_owner._innerDictionary).SyncRoot;

        public bool Contains(object? item)
        {
            foreach (object? value in this)
            {
                if (EqualityComparer<object?>.Default.Equals(value, item))
                {
                    return true;
                }
            }

            return false;
        }

        public void CopyTo(object?[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            _owner.ValidateCopyToArguments(array.Length, arrayIndex);

            foreach (object? value in this)
            {
                array[arrayIndex++] = value;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (array.Rank != 1 || array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException("Only single-dimensional, zero-based arrays are supported.", nameof(array));
            }

            _owner.ValidateCopyToArguments(array.Length, index);

            try
            {
                foreach (object? value in this)
                {
                    array.SetValue(value, index++);
                }
            }
            catch (Exception exception) when (exception is InvalidCastException or ArrayTypeMismatchException)
            {
                throw new ArgumentException("The destination array type is not compatible.", nameof(array), exception);
            }
        }

        public IEnumerator<object?> GetEnumerator()
        {
            return new ResourceValueEnumerator(_owner.CreateEntryEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(object? item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Remove(object? item) => throw new NotSupportedException();
    }

    private sealed class ResourceValueEnumerator : IEnumerator<object?>
    {
        private readonly IEnumerator<KeyValuePair<object, object?>> _enumerator;

        public ResourceValueEnumerator(IEnumerator<KeyValuePair<object, object?>> enumerator)
        {
            _enumerator = enumerator;
        }

        public object? Current => _enumerator.Current.Value;

        object IEnumerator.Current => Current!;

        public bool MoveNext() => _enumerator.MoveNext();

        public void Reset() => _enumerator.Reset();

        public void Dispose() => _enumerator.Dispose();
    }

    #endregion
}
/// <summary>
/// Provides static methods for finding resources in the element tree.
/// </summary>
public static class ResourceLookup
{
    /// <summary>
    /// Gets or sets a callback to retrieve application-level resources.
    /// This is set by the Application class in Jalium.UI.Controls.
    /// </summary>
    public static Func<object, object?>? ApplicationResourceLookup { get; set; }

    /// <summary>
    /// Optional application lookup that also identifies the dictionary supplying a resource.
    /// Controls installs this alongside <see cref="ApplicationResourceLookup"/> for diagnostics.
    /// </summary>
    internal static Func<object, (object? Value, ResourceDictionary? Dictionary)>?
        ApplicationResourceLookupWithSource { get; set; }

    /// <summary>
    /// Gets or sets an optional callback that can redirect resource lookup
    /// to a non-visual ancestor when the visual tree is split across hosts
    /// (for example, Popup content rendered in a separate native window).
    /// </summary>
    public static Func<FrameworkElement, FrameworkElement?>? AncestorRedirectLookup { get; set; }

    // Resource lookup cache: maps (element identity, resourceKey) to cached result.
    // Invalidated when resources change via InvalidateResourceCache().
    [ThreadStatic]
    private static Dictionary<(int, object), object?>? t_resourceCache;
    [ThreadStatic]
    private static int t_cacheGeneration;
    [ThreadStatic]
    private static Stack<HashSet<object>>? t_resourceChainPool;
    private static int s_globalCacheGeneration;
    private const int MaxPooledResourceChainDepth = 128;
    private const int MaxPooledResourceChainsPerThread = 4;

    /// <summary>
    /// Gets the current global resource-cache generation. Recycling containers use this only as
    /// a validity stamp; normal callers should continue to invalidate through
    /// <see cref="InvalidateResourceCache"/>.
    /// </summary>
    internal static int CacheGeneration => Volatile.Read(ref s_globalCacheGeneration);

    /// <summary>
    /// Invalidates the resource lookup cache. Called when resource dictionaries change.
    /// </summary>
    public static void InvalidateResourceCache()
    {
        Interlocked.Increment(ref s_globalCacheGeneration);
    }

    /// <summary>
    /// Finds a resource with the specified key, searching up the visual tree.
    /// </summary>
    /// <param name="element">The starting element for the search.</param>
    /// <param name="resourceKey">The key of the resource to find.</param>
    /// <returns>The resource value, or null if not found.</returns>
    public static object? FindResource(FrameworkElement? element, object resourceKey)
    {
        if (resourceKey == null)
            return null;

        // Check cache
        var cache = t_resourceCache ??= new Dictionary<(int, object), object?>();
        var gen = Volatile.Read(ref s_globalCacheGeneration);
        if (t_cacheGeneration != gen)
        {
            cache.Clear();
            t_cacheGeneration = gen;
        }

        if (element != null)
        {
            var cacheKey = (RuntimeHelpers.GetHashCode(element), resourceKey);
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var resourceChain = RentResourceChain();
            object? result;
            try
            {
                result = FindResourceCore(element, resourceKey, resourceChain);
            }
            finally
            {
                ReturnResourceChain(resourceChain);
            }

            // Only cache if the cache hasn't grown too large
            if (cache.Count < 4096)
            {
                cache[cacheKey] = result;
            }
            return result;
        }

        var uncachedResourceChain = RentResourceChain();
        try
        {
            return FindResourceCore(element, resourceKey, uncachedResourceChain);
        }
        finally
        {
            ReturnResourceChain(uncachedResourceChain);
        }
    }

    /// <summary>
    /// Finds a resource without using the value-only cache and reports its source dictionary.
    /// This overload is intentionally internal because the source identity is diagnostics data,
    /// not part of the regular resource lookup contract.
    /// </summary>
    internal static object? FindResource(
        FrameworkElement? element,
        object resourceKey,
        out ResourceDictionary? sourceDictionary)
    {
        if (resourceKey is null)
        {
            sourceDictionary = null;
            return null;
        }

        var resourceChain = RentResourceChain();
        try
        {
            return FindResourceCoreWithSource(
                element,
                resourceKey,
                resourceChain,
                out sourceDictionary);
        }
        finally
        {
            ReturnResourceChain(resourceChain);
        }
    }

    private static HashSet<object> RentResourceChain()
    {
        var pool = t_resourceChainPool;
        return pool is { Count: > 0 } ? pool.Pop() : new HashSet<object>();
    }

    private static void ReturnResourceChain(HashSet<object> resourceChain)
    {
        var shouldPool = resourceChain.Count <= MaxPooledResourceChainDepth;
        resourceChain.Clear();
        if (!shouldPool)
            return;

        var pool = t_resourceChainPool ??= new Stack<HashSet<object>>(2);
        if (pool.Count < MaxPooledResourceChainsPerThread)
            pool.Push(resourceChain);
    }

    private static object? FindResourceCore(
        FrameworkElement? element,
        object resourceKey,
        HashSet<object> resourceChain)
    {
        if (!resourceChain.Add(resourceKey))
            return null;

        // Walk up the visual tree looking for resources
        var current = element;
        int depthGuard = 0;
        while (current != null && depthGuard++ < 2048)
        {
            // 只读探测必须走 ResourcesOrNull / Style.ResourcesOrNull：public Resources
            // getter 带懒构造副作用，用它做 `!= null` 判断的话该判断恒为真，且判断本身
            // 就给这一级祖先分配了一个空字典（外加 Changed 订阅与诊断 owner 注册）。
            // 旧写法还额外把 getter 调了两次。Style 侧更严重——Style 实例跨元素共享，
            // 空字典会挂到 theme style 上，污染所有使用该 style 的元素。
            var localResources = current.ResourcesOrNull;
            if (localResources != null && localResources.TryGetValue(resourceKey, out var value))
            {
                return ResolveDynamicResourceValue(element, value, resourceChain);
            }

            var styleResources = current.Style?.ResourcesOrNull;
            if (styleResources != null && styleResources.TryGetValue(resourceKey, out var styleValue))
            {
                return ResolveDynamicResourceValue(element, styleValue, resourceChain);
            }

            FrameworkElement? next = null;

            // Allow controls layer to bridge non-visual ancestry (e.g., PopupRoot -> Popup owner)
            if (AncestorRedirectLookup != null)
            {
                next = AncestorRedirectLookup(current);
                if (ReferenceEquals(next, current))
                    next = null;
            }

            next ??= current.FrameworkParent;
            current = next;
        }

        // Check application resources via callback
        if (ApplicationResourceLookup != null)
        {
            return ResolveDynamicResourceValue(element, ApplicationResourceLookup.Invoke(resourceKey), resourceChain);
        }

        return null;
    }

    private static object? FindResourceCoreWithSource(
        FrameworkElement? element,
        object resourceKey,
        HashSet<object> resourceChain,
        out ResourceDictionary? sourceDictionary)
    {
        if (!resourceChain.Add(resourceKey))
        {
            sourceDictionary = null;
            return null;
        }

        var current = element;
        int depthGuard = 0;
        while (current != null && depthGuard++ < 2048)
        {
            // 同 FindResourceCore：只读探测走非分配读法。这里原本连 null 判断都没有，
            // 直接 current.Resources 触发懒构造，是祖先链上每级一个空字典。
            var localResources = current.ResourcesOrNull;
            if (localResources != null &&
                localResources.TryGetValue(resourceKey, out var value, out sourceDictionary))
            {
                if (value is IDynamicResourceReference dynamicReference)
                {
                    return FindResourceCoreWithSource(
                        element,
                        dynamicReference.ResourceKey,
                        resourceChain,
                        out sourceDictionary);
                }

                return value;
            }

            var styleResources = current.Style?.ResourcesOrNull;
            if (styleResources is not null &&
                styleResources.TryGetValue(
                    resourceKey,
                    out var styleValue,
                    out sourceDictionary))
            {
                if (styleValue is IDynamicResourceReference dynamicReference)
                {
                    return FindResourceCoreWithSource(
                        element,
                        dynamicReference.ResourceKey,
                        resourceChain,
                        out sourceDictionary);
                }

                return styleValue;
            }

            FrameworkElement? next = null;
            if (AncestorRedirectLookup != null)
            {
                next = AncestorRedirectLookup(current);
                if (ReferenceEquals(next, current))
                {
                    next = null;
                }
            }

            current = next ?? current.FrameworkParent;
        }

        if (ApplicationResourceLookupWithSource is not null)
        {
            var applicationResult = ApplicationResourceLookupWithSource(resourceKey);
            sourceDictionary = applicationResult.Dictionary;
            if (applicationResult.Value is IDynamicResourceReference dynamicReference)
            {
                return FindResourceCoreWithSource(
                    element,
                    dynamicReference.ResourceKey,
                    resourceChain,
                    out sourceDictionary);
            }

            return applicationResult.Value;
        }

        sourceDictionary = null;
        return ApplicationResourceLookup?.Invoke(resourceKey);
    }

    private static object? ResolveDynamicResourceValue(
        FrameworkElement? element,
        object? value,
        HashSet<object> resourceChain)
    {
        if (value is not IDynamicResourceReference dynamicReference)
            return value;

        return FindResourceCore(element, dynamicReference.ResourceKey, resourceChain);
    }

    /// <summary>
    /// Tries to find a resource with the specified key.
    /// </summary>
    /// <param name="element">The starting element for the search.</param>
    /// <param name="resourceKey">The key of the resource to find.</param>
    /// <param name="value">The found resource value.</param>
    /// <returns>True if the resource was found; otherwise, false.</returns>
    public static bool TryFindResource(FrameworkElement? element, object resourceKey, out object? value)
    {
        value = FindResource(element, resourceKey);
        return value != null;
    }

    /// <summary>
    /// Gets or sets a callback to find implicit DataTemplate for a data type.
    /// This is set by the Controls assembly to avoid circular dependencies.
    /// </summary>
    public static Func<FrameworkElement?, Type?, object?>? ImplicitDataTemplateLookup { get; set; }

    /// <summary>
    /// Finds an implicit DataTemplate for the specified data type.
    /// </summary>
    /// <param name="element">The starting element for the search.</param>
    /// <param name="dataType">The type of the data object.</param>
    /// <returns>The DataTemplate (as object to avoid circular dependency), or null if not found.</returns>
    public static object? FindImplicitDataTemplate(FrameworkElement? element, Type? dataType)
    {
        if (dataType == null)
            return null;

        // Use the callback if set
        if (ImplicitDataTemplateLookup != null)
        {
            return ImplicitDataTemplateLookup(element, dataType);
        }

        // Fallback: try finding by DataTemplateKey (Type as key)
        var resource = FindResource(element, new DataTemplateKey(dataType));
        if (resource != null)
            return resource;

        // Also try the type directly as key
        resource = FindResource(element, dataType);
        if (resource != null)
            return resource;

        // Try base types
        var baseType = dataType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            resource = FindResource(element, new DataTemplateKey(baseType));
            if (resource != null)
                return resource;

            resource = FindResource(element, baseType);
            if (resource != null)
                return resource;

            baseType = baseType.BaseType;
        }

        return null;
    }
}
