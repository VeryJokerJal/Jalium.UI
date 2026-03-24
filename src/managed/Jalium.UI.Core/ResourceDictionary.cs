using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Provides a hash table/dictionary implementation that contains resources
/// used by components and other elements of a UI application.
/// </summary>
public class ResourceDictionary : IDictionary<object, object?>, IDictionary
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
    private Dictionary<object, ResourceDictionary>? _themeDictionaries;
    private Uri? _source;
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
    public IList<ResourceDictionary> MergedDictionaries => _mergedDictionaries;

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
        set => _source = value;
    }

    /// <summary>
    /// Gets or sets the base URI for resolving relative Source paths.
    /// This is typically set by the XAML parser during loading.
    /// </summary>
    internal Uri? BaseUri { get; set; }

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
    /// Copies all resources from another dictionary into this one.
    /// </summary>
    /// <param name="source">The source dictionary to copy from.</param>
    internal void CopyFrom(ResourceDictionary source)
    {
        var changed = false;

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
    /// Gets a collection containing the keys.
    /// </summary>
    public ICollection<object> Keys => _innerDictionary.Keys;

    /// <summary>
    /// Gets a collection containing the values.
    /// </summary>
    public ICollection<object?> Values => _innerDictionary.Values;

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
    {
        // Check local dictionary first
        if (_innerDictionary.TryGetValue(key, out value))
            return true;

        var chain = t_lookupChain ??= new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
        if (!chain.Add(this))
        {
            value = null;
            return false; // Cycle detected
        }

        try
        {
            // Check theme dictionaries (higher priority than merged)
            if (_themeDictionaries != null && CurrentThemeKey != null)
            {
                if (_themeDictionaries.TryGetValue(CurrentThemeKey, out var themeDict))
                {
                    if (themeDict.TryGetValue(key, out value))
                        return true;
                }
            }

            // Check merged dictionaries in reverse order (later overrides earlier)
            for (int i = _mergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (_mergedDictionaries[i].TryGetValue(key, out value))
                    return true;
            }

            value = null;
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
    public bool Remove(object key)
    {
        if (_innerDictionary.Remove(key))
        {
            OnChangedForKey(key);
            return true;
        }

        return false;
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

    #region ICollection<KeyValuePair<object, object?>>

    public void Add(KeyValuePair<object, object?> item)
    {
        _innerDictionary.Add(item.Key, item.Value);
        OnChanged();
    }

    public bool Contains(KeyValuePair<object, object?> item)
    {
        return _innerDictionary.ContainsKey(item.Key) &&
               EqualityComparer<object?>.Default.Equals(_innerDictionary[item.Key], item.Value);
    }

    public void CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<object, object?>>)_innerDictionary).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<object, object?> item)
    {
        if (Contains(item))
        {
            var removed = _innerDictionary.Remove(item.Key);
            if (removed)
                OnChanged();
            return removed;
        }

        return false;
    }

    public IEnumerator<KeyValuePair<object, object?>> GetEnumerator()
    {
        return _innerDictionary.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion

    #region IDictionary

    bool IDictionary.IsFixedSize => false;

    ICollection IDictionary.Keys => _innerDictionary.Keys;

    ICollection IDictionary.Values => _innerDictionary.Values;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => ((ICollection)_innerDictionary).SyncRoot;

    object? IDictionary.this[object key]
    {
        get => this[key];
        set => this[key] = value;
    }

    void IDictionary.Add(object key, object? value)
    {
        Add(key, value);
    }

    void IDictionary.Remove(object key)
    {
        Remove(key);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator()
    {
        return ((IDictionary)_innerDictionary).GetEnumerator();
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ((ICollection)_innerDictionary).CopyTo(array, index);
    }

    private void OnChangedForKey(object key)
    {
        if (_notificationDeferralDepth > 0)
        {
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

    private void OnChanged()
    {
        if (_notificationDeferralDepth > 0)
        {
            _notificationPending = true;
            _pendingAllChanged = true; // merged dict replacement — all keys may have changed
            return;
        }

        RaiseChanged(ResourcesChangedEventArgs.All);
    }

    private void RaiseChanged(ResourcesChangedEventArgs args)
    {
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
        dictionary.Changed += OnMergedDictionaryChanged;
    }

    private void OnMergedDictionaryRemoved(ResourceDictionary dictionary)
    {
        dictionary.Changed -= OnMergedDictionaryChanged;
    }

    private void OnMergedDictionaryChanged(object? sender, EventArgs e)
    {
        OnChanged();
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
    private static volatile int s_globalCacheGeneration;

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
        var gen = s_globalCacheGeneration;
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

            var result = FindResourceCore(element, resourceKey, new HashSet<object>());
            // Only cache if the cache hasn't grown too large
            if (cache.Count < 4096)
            {
                cache[cacheKey] = result;
            }
            return result;
        }

        return FindResourceCore(element, resourceKey, new HashSet<object>());
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
            if (current.Resources != null && current.Resources.TryGetValue(resourceKey, out var value))
            {
                return ResolveDynamicResourceValue(element, value, resourceChain);
            }

            FrameworkElement? next = null;

            // Allow controls layer to bridge non-visual ancestry (e.g., PopupRoot -> Popup owner)
            if (AncestorRedirectLookup != null)
            {
                next = AncestorRedirectLookup(current);
                if (ReferenceEquals(next, current))
                    next = null;
            }

            next ??= current.VisualParent as FrameworkElement;
            current = next;
        }

        // Check application resources via callback
        if (ApplicationResourceLookup != null)
        {
            return ResolveDynamicResourceValue(element, ApplicationResourceLookup.Invoke(resourceKey), resourceChain);
        }

        return null;
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

/// <summary>
/// Represents a key for an implicit DataTemplate resource.
/// </summary>
public sealed class DataTemplateKey : IEquatable<DataTemplateKey>
{
    /// <summary>
    /// Gets the data type for which this key is used.
    /// </summary>
    public Type DataType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplateKey"/> class.
    /// </summary>
    /// <param name="dataType">The data type for which this key is used.</param>
    public DataTemplateKey(Type dataType)
    {
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DataTemplateKey);

    /// <inheritdoc />
    public bool Equals(DataTemplateKey? other) => other != null && DataType == other.DataType;

    /// <inheritdoc />
    public override int GetHashCode() => DataType.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => $"DataTemplateKey({DataType.Name})";
}
