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
        if (!CanFreeze)
            throw new InvalidOperationException("This Freezable cannot be frozen.");

        FreezeCore(false);
        _isFrozen = true;
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
    protected void OnChanged()
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
    /// Verifies that the Freezable is not frozen and is being accessed from a valid thread context.
    /// </summary>
    protected void WritePreamble()
    {
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
public sealed class FreezableCollection<T> : Freezable, IList<T>, System.Collections.IList where T : DependencyObject
{
    private readonly List<T> _items = new();

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            WritePreamble();
            var oldItem = _items[index];
            _items[index] = value;
            OnFreezablePropertyChanged(oldItem, value);
            WritePostscript();
        }
    }

    object? System.Collections.IList.this[int index]
    {
        get => _items[index];
        set
        {
            if (value is T item)
                this[index] = item;
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => IsFrozen;

    bool System.Collections.IList.IsFixedSize => IsFrozen;
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => ((System.Collections.ICollection)_items).SyncRoot;

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public void Add(T item)
    {
        WritePreamble();
        _items.Add(item);
        OnFreezablePropertyChanged(null, item);
        WritePostscript();
    }

    int System.Collections.IList.Add(object? value)
    {
        if (value is T item)
        {
            Add(item);
            return _items.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    public void Clear()
    {
        WritePreamble();
        foreach (var item in _items)
        {
            OnFreezablePropertyChanged(item, null);
        }
        _items.Clear();
        WritePostscript();
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public bool Contains(T item) => _items.Contains(item);
    bool System.Collections.IList.Contains(object? value) => value is T item && _items.Contains(item);

    /// <summary>
    /// Copies the items to an array.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void System.Collections.ICollection.CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified item.
    /// </summary>
    public int IndexOf(T item) => _items.IndexOf(item);
    int System.Collections.IList.IndexOf(object? value) => value is T item ? _items.IndexOf(item) : -1;

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, T item)
    {
        WritePreamble();
        _items.Insert(index, item);
        OnFreezablePropertyChanged(null, item);
        WritePostscript();
    }

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is T item)
            Insert(index, item);
    }

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    public bool Remove(T item)
    {
        WritePreamble();
        var removed = _items.Remove(item);
        if (removed)
        {
            OnFreezablePropertyChanged(item, null);
            WritePostscript();
        }
        return removed;
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is T item)
            Remove(item);
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        WritePreamble();
        var item = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(item, null);
        WritePostscript();
    }

    /// <summary>
    /// Creates a new instance of the collection.
    /// </summary>
    protected override Freezable CreateInstanceCore()
    {
        return new FreezableCollection<T>();
    }
}
