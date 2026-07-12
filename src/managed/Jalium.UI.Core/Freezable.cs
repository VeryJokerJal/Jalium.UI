using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Jalium.UI;

/// <summary>
/// Defines an object that has a modifiable state and a read-only (frozen) state.
/// Classes that derive from Freezable provide detailed change notification, can be made immutable,
/// and can clone themselves.
/// </summary>
public abstract class Freezable : DependencyObject
{
    private bool _isFrozen;

    /// <summary>
    /// Occurs when the Freezable or an object it contains is modified.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value that indicates whether the object can be made unmodifiable.
    /// </summary>
    public bool CanFreeze => FreezeCore(true);

    /// <summary>
    /// Gets a value that indicates whether the object is currently modifiable.
    /// </summary>
    public bool IsFrozen => _isFrozen;

    private protected override bool IsSealedCore => _isFrozen;

    /// <summary>
    /// Creates a modifiable clone of the Freezable, making deep copies of the object's values.
    /// </summary>
    /// <returns>A modifiable clone of the current object.</returns>
    public Freezable Clone()
    {
        // WPF: clone.CloneCore(this) —— 拷贝动作发生在新实例上，源对象只读。
        // 旧实现写成 CloneCore(this)（在 this 上调用），克隆体永远是空对象。
        var clone = CreateInstance();
        clone.CloneCore(this);
        return clone;
    }

    /// <summary>
    /// Creates a modifiable clone of the Freezable using its current values.
    /// </summary>
    /// <returns>A modifiable clone of the current object.</returns>
    public Freezable CloneCurrentValue()
    {
        var clone = CreateInstance();
        clone.CloneCurrentValueCore(this);
        return clone;
    }

    /// <summary>
    /// Makes the current object unmodifiable and sets its IsFrozen property to true.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
            return;

        // WPF 语义：先用 CanFreeze(FreezeCore isChecking=true) 整图校验能否冻结，
        // 通过后再 FreezeCore(false) 真正递归冻结子 Freezable —— 避免半路失败时
        // 已经把一部分子对象冻住造成不一致状态。
        if (!Freeze(this, isChecking: true))
            throw new InvalidOperationException("This Freezable cannot be frozen.");

        Freeze(this, isChecking: false);
    }

    /// <summary>
    /// Tests or freezes an arbitrary Freezable through its virtual freeze hook.
    /// </summary>
    protected internal static bool Freeze(Freezable freezable, bool isChecking)
    {
        ArgumentNullException.ThrowIfNull(freezable);
        if (freezable._isFrozen)
        {
            return true;
        }

        bool canFreeze = freezable.FreezeCore(isChecking);
        if (!isChecking && canFreeze)
        {
            freezable._isFrozen = true;
        }

        return canFreeze;
    }

    /// <summary>
    /// Creates a frozen copy of the Freezable.
    /// </summary>
    /// <returns>A frozen copy of the Freezable.</returns>
    public Freezable GetAsFrozen()
    {
        // 已冻结则直接共享自身（WPF：避免拷贝图中已冻结的部分）。
        if (_isFrozen)
            return this;

        var clone = CreateInstance();
        clone.GetAsFrozenCore(this);
        clone.Freeze();
        return clone;
    }

    /// <summary>
    /// Creates a frozen copy of the Freezable using current property values.
    /// </summary>
    /// <returns>A frozen copy of the Freezable.</returns>
    public Freezable GetCurrentValueAsFrozen()
    {
        if (_isFrozen)
            return this;

        var clone = CreateInstance();
        clone.GetCurrentValueAsFrozenCore(this);
        clone.Freeze();
        return clone;
    }

    /// <summary>
    /// When implemented in a derived class, creates a new instance of the Freezable derived class.
    /// </summary>
    /// <returns>The new instance.</returns>
    protected abstract Freezable CreateInstanceCore();

    /// <summary>
    /// Makes the Freezable object unmodifiable or tests whether it can be made unmodifiable.
    /// </summary>
    /// <param name="isChecking">true to return an indication of whether the object can be frozen; false to actually freeze the object.</param>
    /// <returns>If isChecking is true, this method returns true if the Freezable can be made unmodifiable, or false if it cannot. If isChecking is false, this method returns true if the specified Freezable is now unmodifiable, or throws an exception if it cannot be made unmodifiable.</returns>
    protected virtual bool FreezeCore(bool isChecking)
    {
        // WPF 默认实现：遍历所有有效值，任一属性带表达式(绑定)或带活动动画则不可冻结；
        // 子 Freezable 必须可冻结(isChecking) / 被递归冻结(!isChecking)。
        foreach (var dp in GetEffectiveSetPropertiesInternal())
        {
            if (HasBindingInternal(dp))
                return false;       // 表达式不可冻结

            if (HasAnimatedValue(dp))
                return false;       // 活动动画不可冻结

            var (baseValue, _) = GetUncoercedBaseValueInternal(dp);
            if (baseValue is Freezable child)
            {
                if (isChecking)
                {
                    if (!child.CanFreeze)
                        return false;
                }
                else
                {
                    child.Freeze();
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Makes the instance a clone (deep copy) of the specified Freezable using base (non-animated) property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to clone.</param>
    protected virtual void CloneCore(Freezable sourceFreezable)
    {
        CloneCoreCommon(sourceFreezable, useCurrentValue: false, cloneFrozenValues: true);
    }

    /// <summary>
    /// Makes the instance a modifiable clone (deep copy) of the specified Freezable using current property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        CloneCoreCommon(sourceFreezable, useCurrentValue: true, cloneFrozenValues: true);
    }

    /// <summary>
    /// Makes the instance a frozen clone of the specified Freezable using base (non-animated) property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void GetAsFrozenCore(Freezable sourceFreezable)
    {
        CloneCoreCommon(sourceFreezable, useCurrentValue: false, cloneFrozenValues: false);
    }

    /// <summary>
    /// Makes the current instance a frozen clone of the specified Freezable using current property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        CloneCoreCommon(sourceFreezable, useCurrentValue: true, cloneFrozenValues: false);
    }

    /// <summary>
    /// Called when the current Freezable object is modified.
    /// </summary>
    protected virtual void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ensures that appropriate actions are taken when a DependencyObject member of a Freezable changes.
    /// </summary>
    /// <param name="oldValue">The previous value of the property.</param>
    /// <param name="newValue">The current value of the property.</param>
    protected virtual void OnFreezablePropertyChanged(DependencyObject? oldValue, DependencyObject? newValue)
    {
        // Unsubscribe from old value's change notifications
        if (oldValue is Freezable oldFreezable)
        {
            oldFreezable.Changed -= OnSubPropertyChanged;
        }

        // Subscribe to new value's change notifications
        if (newValue is Freezable newFreezable && !newFreezable.IsFrozen)
        {
            newFreezable.Changed += OnSubPropertyChanged;
        }
    }

    /// <summary>
    /// Updates a child Freezable relationship associated with a dependency property.
    /// </summary>
    protected void OnFreezablePropertyChanged(
        DependencyObject? oldValue,
        DependencyObject? newValue,
        DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        OnFreezablePropertyChanged(oldValue, newValue);
    }

    /// <summary>
    /// Verifies that the current thread may read this instance unless it is frozen.
    /// </summary>
    protected void ReadPreamble()
    {
        if (!_isFrozen)
        {
            VerifyAccess();
        }
    }

    /// <summary>
    /// Verifies that the Freezable is not frozen and is being accessed from a valid thread context.
    /// </summary>
    protected void WritePreamble()
    {
        VerifyAccess();
        if (_isFrozen)
            throw new InvalidOperationException("Cannot modify a frozen Freezable.");
    }

    /// <summary>
    /// 属性系统写入守卫：冻结后任何 SetValue/SetCurrentValue/ClearValue/SetBinding 都抛异常。
    /// 这使 CLR 属性包装器（<c>set =&gt; SetValue(...)</c>）即便不显式调用 <see cref="WritePreamble"/>
    /// 也能在冻结后正确拒绝写入，对齐 WPF 行为。
    /// </summary>
    private protected override void CheckSealedAccess()
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot modify a frozen Freezable.");
    }

    /// <summary>
    /// Raises the Changed event for the Freezable and invokes its OnChanged method.
    /// </summary>
    protected void WritePostscript()
    {
        OnChanged();
    }

    /// <summary>
    /// Creates a new instance of the Freezable class.
    /// </summary>
    protected Freezable CreateInstance()
    {
        return CreateInstanceCore();
    }

    /// <summary>
    /// 对齐 WPF Freezable.CloneCoreCommon：把 source 的有效属性深拷贝到当前(克隆)实例。
    /// </summary>
    /// <param name="source">被克隆的源 Freezable。</param>
    /// <param name="useCurrentValue">true 拷贝当前(已解析/动画后)值；false 拷贝 base(本地)值。</param>
    /// <param name="cloneFrozenValues">true 始终深拷贝子 Freezable(Clone/CloneCurrentValue)；
    /// false 仅拷贝未冻结的子 Freezable、已冻结的直接共享(GetAsFrozen/GetCurrentValueAsFrozen)。</param>
    private void CloneCoreCommon(Freezable source, bool useCurrentValue, bool cloneFrozenValues)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (useCurrentValue)
        {
            // 遍历所有高于默认值的有效属性，拷贝其当前解析值（含动画快照与 modified-default）。
            foreach (var dp in source.GetEffectiveSetPropertiesInternal())
            {
                if (dp.ReadOnly)
                    continue; // 只读 DP：SetValue 会抛，跳过（WPF 同样跳过）

                var value = CloneValueIfFreezable(source.GetValue(dp), useCurrentValue: true, cloneFrozenValues);
                SetValue(dp, value);
            }
        }
        else
        {
            // base 克隆：只拷贝本地设置(local)的基值，对齐 WPF ReadLocalValue 语义。
            foreach (var entry in source.GetLocalValueEntriesInternal())
            {
                var dp = entry.Key;
                if (dp.ReadOnly)
                    continue;

                // 纯绑定属性的表达式复制需要 Expression.Copy 机制，Jalium 绑定模型与 WPF
                // 不同（绑定单独存放、非 local 值），且 Freezable 上挂绑定极罕见 —— base
                // 克隆此处跳过，避免共享同一个表达式状态造成串扰；其当前解析值仍会被
                // CloneCurrentValue 路径捕获。
                if (source.HasBindingInternal(dp))
                    continue;

                var value = CloneValueIfFreezable(entry.Value, useCurrentValue: false, cloneFrozenValues);
                SetValue(dp, value);
            }
        }
    }

    /// <summary>
    /// 若值是 Freezable 则按四种克隆语义递归深拷贝，否则原样返回（值类型/字符串/普通引用）。
    /// </summary>
    private static object? CloneValueIfFreezable(object? value, bool useCurrentValue, bool cloneFrozenValues)
    {
        if (value is not Freezable freezable)
            return value;

        if (cloneFrozenValues)
        {
            // Clone / CloneCurrentValue：即使子值已冻结也产生可变深拷贝。
            var clone = freezable.CreateInstanceCore();
            if (useCurrentValue)
                clone.CloneCurrentValueCore(freezable);
            else
                clone.CloneCore(freezable);
            return clone;
        }

        // GetAsFrozen / GetCurrentValueAsFrozen：已冻结的子值直接共享，未冻结才拷贝。
        if (!freezable.IsFrozen)
        {
            var clone = freezable.CreateInstanceCore();
            if (useCurrentValue)
                clone.GetCurrentValueAsFrozenCore(freezable);
            else
                clone.GetAsFrozenCore(freezable);
            return clone;
        }

        return freezable;
    }

    private void OnSubPropertyChanged(object? sender, EventArgs e)
    {
        OnChanged();
    }
}

/// <summary>
/// Represents a collection of Freezable objects.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class FreezableCollection<T> : Media.Animation.Animatable, IList, IList<T>, INotifyCollectionChanged, INotifyPropertyChanged where T : DependencyObject
{
    private List<T> _items;
    private readonly SimpleMonitor _monitor = new();
    private uint _version;
    private event NotifyCollectionChangedEventHandler? CollectionChanged;
    private event PropertyChangedEventHandler? PrivatePropertyChanged;

    /// <summary>
    /// Initializes an empty collection.
    /// </summary>
    public FreezableCollection()
    {
        _items = new List<T>();
    }

    /// <summary>
    /// Initializes an empty collection with the specified capacity.
    /// </summary>
    public FreezableCollection(int capacity)
    {
        _items = new List<T>(capacity);
    }

    /// <summary>
    /// Initializes a collection with the elements from the specified sequence.
    /// </summary>
    public FreezableCollection(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        _items = collection is ICollection<T> sourceCollection
            ? new List<T>(sourceCollection.Count)
            : new List<T>();

        foreach (var item in collection)
        {
            if (item is null)
            {
                throw new ArgumentException("The collection cannot contain null items.");
            }

            OnFreezablePropertyChanged(null, item);
            _items.Add(item);
        }
    }

    /// <summary>
    /// Creates a modifiable deep clone of this collection.
    /// </summary>
    public new FreezableCollection<T> Clone() => (FreezableCollection<T>)base.Clone();

    /// <summary>
    /// Creates a modifiable deep clone of this collection using current values.
    /// </summary>
    public new FreezableCollection<T> CloneCurrentValue() => (FreezableCollection<T>)base.CloneCurrentValue();

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            EnsureValidItem(value);
            CheckReentrancy();
            WritePreamble();

            var oldItem = _items[index];
            bool isChanging = !ReferenceEquals(oldItem, value);
            if (isChanging)
            {
                OnFreezablePropertyChanged(oldItem, value);
                _items[index] = value;
            }

            ++_version;
            WritePostscript();

            if (isChanging)
            {
                RaiseCollectionChanged(NotifyCollectionChangedAction.Replace, index, oldItem, index, value);
            }
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen || Dispatcher is not null;
    object ICollection.SyncRoot => this;

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public void Add(T item)
    {
        CheckReentrancy();
        int index = AddWithoutFiringPublicEvents(item);
        WritePostscript();
        RaiseCollectionChanged(NotifyCollectionChangedAction.Add, 0, null, index - 1, item);
    }

    int IList.Add(object? value)
    {
        T item = Cast(value);
        CheckReentrancy();
        int index = AddWithoutFiringPublicEvents(item);
        WritePostscript();
        RaiseCollectionChanged(NotifyCollectionChangedAction.Add, 0, null, index - 1, item);
        return index;
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    public void Clear()
    {
        CheckReentrancy();
        WritePreamble();

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            OnFreezablePropertyChanged(_items[i], null);
        }

        _items.Clear();
        ++_version;
        WritePostscript();
        RaiseCollectionChanged(NotifyCollectionChangedAction.Reset, 0, null, 0, null);
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public bool Contains(T item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is T item && Contains(item);

    /// <summary>
    /// Copies the items to an array.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length - _items.Count);
        _items.CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length - _items.Count);

        if (array.Rank != 1)
        {
            throw new ArgumentException("The destination array must be one-dimensional.", nameof(array));
        }

        try
        {
            for (int i = 0; i < _items.Count; i++)
            {
                array.SetValue(_items[i], index + i);
            }
        }
        catch (InvalidCastException exception)
        {
            throw new ArgumentException("The destination array type is not compatible with the collection item type.", nameof(array), exception);
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the index of the specified item.
    /// </summary>
    public int IndexOf(T item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is T item ? IndexOf(item) : -1;

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, T item)
    {
        EnsureValidItem(item);
        CheckReentrancy();
        WritePreamble();

        OnFreezablePropertyChanged(null, item);
        _items.Insert(index, item);
        ++_version;
        WritePostscript();
        RaiseCollectionChanged(NotifyCollectionChangedAction.Add, 0, null, index, item);
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    public bool Remove(T item)
    {
        WritePreamble();

        int index = IndexOf(item);
        if (index >= 0)
        {
            CheckReentrancy();
            T oldItem = _items[index];
            OnFreezablePropertyChanged(oldItem, null);
            _items.RemoveAt(index);
            ++_version;
            WritePostscript();
            RaiseCollectionChanged(NotifyCollectionChangedAction.Remove, index, oldItem, 0, null);
            return true;
        }

        return false;
    }

    void IList.Remove(object? value) => Remove(value is T item ? item : null!);

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        T item = _items[index];
        CheckReentrancy();
        WritePreamble();

        OnFreezablePropertyChanged(item, null);
        _items.RemoveAt(index);
        ++_version;
        WritePostscript();
        RaiseCollectionChanged(NotifyCollectionChangedAction.Remove, index, item, 0, null);
    }

    event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
    {
        add => CollectionChanged += value;
        remove => CollectionChanged -= value;
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PrivatePropertyChanged += value;
        remove => PrivatePropertyChanged -= value;
    }

    /// <summary>
    /// Creates a new instance of the collection.
    /// </summary>
    protected override Freezable CreateInstanceCore()
    {
        return new FreezableCollection<T>();
    }

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CloneCommon((FreezableCollection<T>)sourceFreezable, CloneCommonType.Clone);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CloneCommon((FreezableCollection<T>)sourceFreezable, CloneCommonType.CloneCurrentValue);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CloneCommon((FreezableCollection<T>)sourceFreezable, CloneCommonType.GetAsFrozen);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CloneCommon((FreezableCollection<T>)sourceFreezable, CloneCommonType.GetCurrentValueAsFrozen);
    }

    /// <inheritdoc />
    protected override bool FreezeCore(bool isChecking)
    {
        bool canFreeze = base.FreezeCore(isChecking);

        for (int i = 0; i < _items.Count && canFreeze; i++)
        {
            T item = _items[i];
            if (item is Freezable freezable)
            {
                if (isChecking)
                {
                    canFreeze = freezable.CanFreeze;
                }
                else
                {
                    freezable.Freeze();
                }
            }
            else
            {
                canFreeze = item.Dispatcher is null;
            }
        }

        return canFreeze;
    }

    private int AddWithoutFiringPublicEvents(T item)
    {
        EnsureValidItem(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Add(item);
        ++_version;
        return _items.Count;
    }

    private void CloneCommon(FreezableCollection<T> source, CloneCommonType cloneType)
    {
        int count = source._items.Count;
        _items = new List<T>(count);

        for (int i = 0; i < count; i++)
        {
            T item = source._items[i];
            T newItem = item;
            if (item is Freezable freezable)
            {
                Freezable clone = cloneType switch
                {
                    CloneCommonType.Clone => freezable.Clone(),
                    CloneCommonType.CloneCurrentValue => freezable.CloneCurrentValue(),
                    CloneCommonType.GetAsFrozen => freezable.GetAsFrozen(),
                    CloneCommonType.GetCurrentValueAsFrozen => freezable.GetCurrentValueAsFrozen(),
                    _ => throw new InvalidOperationException("Unexpected clone operation."),
                };

                if (clone is not T typedClone)
                {
                    throw new InvalidOperationException($"The cloned item is not assignable to {typeof(T).Name}.");
                }

                newItem = typedClone;
            }

            OnFreezablePropertyChanged(null, newItem);
            _items.Add(newItem);
        }
    }

    private static void EnsureValidItem(T item)
    {
        if (item is null)
        {
            throw new ArgumentException("The collection cannot contain null items.");
        }
    }

    private static T Cast(object? value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not T item)
        {
            throw new ArgumentException(
                $"A value of type {value.GetType().Name} cannot be added to {typeof(FreezableCollection<T>).Name}.",
                nameof(value));
        }

        return item;
    }

    private void RaiseCollectionChanged(
        NotifyCollectionChangedAction action,
        int oldIndex,
        T? oldItem,
        int newIndex,
        T? newItem)
    {
        if (PrivatePropertyChanged is null && CollectionChanged is null)
        {
            return;
        }

        using (BlockReentrancy())
        {
            if (PrivatePropertyChanged is not null)
            {
                if (action is not NotifyCollectionChangedAction.Replace and not NotifyCollectionChangedAction.Move)
                {
                    PrivatePropertyChanged(this, new PropertyChangedEventArgs("Count"));
                }

                PrivatePropertyChanged(this, new PropertyChangedEventArgs("Item[]"));
            }

            if (CollectionChanged is not null)
            {
                NotifyCollectionChangedEventArgs args = action switch
                {
                    NotifyCollectionChangedAction.Reset => new NotifyCollectionChangedEventArgs(action),
                    NotifyCollectionChangedAction.Add => new NotifyCollectionChangedEventArgs(action, newItem, newIndex),
                    NotifyCollectionChangedAction.Remove => new NotifyCollectionChangedEventArgs(action, oldItem, oldIndex),
                    NotifyCollectionChangedAction.Replace => new NotifyCollectionChangedEventArgs(action, newItem, oldItem, newIndex),
                    _ => throw new InvalidOperationException("Unexpected collection change action."),
                };

                CollectionChanged(this, args);
            }
        }
    }

    private IDisposable BlockReentrancy()
    {
        _monitor.Enter();
        return _monitor;
    }

    private void CheckReentrancy()
    {
        if (_monitor.Busy)
        {
            throw new InvalidOperationException("Cannot modify the collection while a change notification is being raised.");
        }
    }

    private enum CloneCommonType
    {
        Clone,
        CloneCurrentValue,
        GetAsFrozen,
        GetCurrentValueAsFrozen,
    }

    /// <summary>
    /// Enumerates the elements of a <see cref="FreezableCollection{T}"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly FreezableCollection<T> _list;
        private readonly uint _version;
        private int _index;
        private T? _current;

        internal Enumerator(FreezableCollection<T> list)
        {
            _list = list;
            _version = list._version;
            _index = -1;
            _current = default;
        }

        /// <inheritdoc />
        public T Current
        {
            get
            {
                if (_index >= 0)
                {
                    return _current!;
                }

                throw new InvalidOperationException(
                    _index == -1
                        ? "Enumeration has not started. Call MoveNext."
                        : "Enumeration has already finished.");
            }
        }

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            VerifyVersion();

            if (_index > -2 && _index < _list._items.Count - 1)
            {
                _current = _list._items[++_index];
                return true;
            }

            _index = -2;
            _current = default;
            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = default;
        }

        void IDisposable.Dispose()
        {
        }

        private readonly void VerifyVersion()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("The collection was modified after the enumerator was created.");
            }
        }
    }

    private sealed class SimpleMonitor : IDisposable
    {
        private int _busyCount;

        public bool Busy => _busyCount > 0;

        public void Enter() => ++_busyCount;

        public void Dispose()
        {
            --_busyCount;
            GC.SuppressFinalize(this);
        }
    }
}
