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
        Diagnostics.ResourceDictionaryDiagnosticsStore.LinkMergedDictionary(this, dictionary);
    }

    private void OnMergedDictionaryRemoved(ResourceDictionary dictionary)
    {
        dictionary.Changed -= OnMergedDictionaryChanged;
        Diagnostics.ResourceDictionaryDiagnosticsStore.UnlinkMergedDictionary(this, dictionary);
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

        return FindResourceCoreWithSource(
            element,
            resourceKey,
            new HashSet<object>(),
            out sourceDictionary);
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

            if (current.Style?.Resources.TryGetValue(resourceKey, out var styleValue) == true)
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
            if (current.Resources.TryGetValue(resourceKey, out var value, out sourceDictionary))
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

            var styleResources = current.Style?.Resources;
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
