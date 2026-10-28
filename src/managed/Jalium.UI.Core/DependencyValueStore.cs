namespace Jalium.UI;

/// <summary>
/// Sparse dependency-property value storage optimized for the overwhelmingly common
/// case where a property has exactly one non-default value source.
/// </summary>
/// <remarks>
/// The previous representation kept one dictionary per precedence layer. Reading an
/// effective value consequently performed as many as six independent hash lookups,
/// while an element using styles and templates could allocate several dictionaries.
/// This store indexes a property once, keeps its effective value beside the key, and
/// allocates a <see cref="LayerValues"/> when two value sources coexist or a
/// current value has a source outside the compact representation.
/// </remarks>
internal sealed class DependencyValueStore
{
    [Flags]
    internal enum LayerMask : ushort
    {
        None = 0,
        Local = 1 << 0,
        ParentTemplate = 1 << 1,
        StyleTrigger = 1 << 2,
        TemplateTrigger = 1 << 3,
        StyleSetter = 1 << 4,
        Current = 1 << 5,
        ParentTemplateTrigger = 1 << 6,
        CssState = 1 << 7,
        CssBase = 1 << 8,
    }

    internal enum Layer : byte
    {
        Local,
        ParentTemplate,
        StyleTrigger,
        TemplateTrigger,
        StyleSetter,
        Current,
        ParentTemplateTrigger,
        CssState,
        CssBase,
    }

    private sealed class LayerValues
    {
        public object? EffectiveValue;
        public object? Local;
        public object? ParentTemplateTrigger;
        public object? ParentTemplate;
        public object? CssState;
        public object? StyleTrigger;
        public object? TemplateTrigger;
        public object? CssBase;
        public object? StyleSetter;
        public object? Current;
        public BaseValueSource CurrentSource;
    }

    private struct Entry
    {
        public int PropertyIndex;
        public LayerMask Mask;
        private const byte ExtendedSource = byte.MaxValue;
        private byte _effectiveSource;
        public readonly DependencyProperty Property => DependencyProperty.FromGlobalIndex(PropertyIndex);

        public BaseValueSource EffectiveSource
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            readonly get => _effectiveSource == ExtendedSource
                ? LayerStorage.CurrentSource : (BaseValueSource)_effectiveSource;
            set
            {
                if ((uint)value < ExtendedSource)
                {
                    _effectiveSource = (byte)value;
                    return;
                }

                // Keep the full enum domain, including negative/unknown casts.
                // Framework sources fit in a byte; only an exceptional Current
                // source needs the sidecar that already stores layered values.
                if (!_hasValues)
                {
                    System.Diagnostics.Debug.Assert(Mask == LayerMask.Current);
                    _payload = new LayerValues { EffectiveValue = _payload, Current = _payload };
                    _hasValues = true;
                }
                LayerStorage.CurrentSource = value;
                _effectiveSource = ExtendedSource;
            }
        }
        // A single source normally stores its value directly. LayerValues also
        // holds the cached effective value and exceptional Current sources. Keep
        // the discriminator separate so reading a scalar does not inspect the
        // value's runtime type, and every object remains a valid user payload.
        private object? _payload;
        private bool _hasValues;

        // In an expanded store the second inline slot holds the backing array
        // instead of a value. The store's discriminator owns that switch;
        // an arbitrary user payload can never select the storage representation.
        public object? StoragePayload
        {
            readonly get => _payload;
            set => _payload = value;
        }

        private readonly LayerValues LayerStorage
        {
            get
            {
                System.Diagnostics.Debug.Assert(_hasValues && _payload is LayerValues);
                return System.Runtime.CompilerServices.Unsafe.As<LayerValues>(_payload)!;
            }
        }

        public object? EffectiveValue
        {
            readonly get => _hasValues ? LayerStorage.EffectiveValue : _payload;
            set
            {
                if (_hasValues) LayerStorage.EffectiveValue = value;
                else _payload = value;
            }
        }

        public LayerValues? Values
        {
            readonly get => _hasValues ? LayerStorage : null;
            set
            {
                if (value is null && _effectiveSource == ExtendedSource)
                    return;
                _payload = value ?? EffectiveValue;
                _hasValues = value is not null;
            }
        }
    }

    // Keep short scans for render primitives and binary-search larger stores.
    // Sorted overflow entries need no separately allocated property index.
    private const int BinarySearchThreshold = 4;
    // Two values fit in the same space previously occupied by one entry and
    // the array/index references. On the third property, move the second value
    // to the overflow array and reuse its payload reference for that array.
    // The first property stays inline after growth as well.
    // The store remains 56 bytes on x64, including for one-property primitives.
    private Entry _firstEntry;
    private Entry _secondEntry;
    private int _count;
    private bool _expanded;

    private Entry[] ExpandedEntries
    {
        get => System.Runtime.CompilerServices.Unsafe.As<Entry[]>(_secondEntry.StoragePayload)!;
        set => _secondEntry.StoragePayload = value;
    }

    internal int Count => _count;

    internal bool TryGetEffective(
        DependencyProperty property,
        out object? value,
        out BaseValueSource source)
    {
        var index = FindIndex(property);
        if (index < 0)
        {
            value = null;
            source = BaseValueSource.Unknown;
            return false;
        }

        ref readonly var entry = ref GetEntry(index);
        value = entry.EffectiveValue;
        source = entry.EffectiveSource;
        return true;
    }

    internal bool ContainsLayer(DependencyProperty property, Layer layer)
    {
        var index = FindIndex(property);
        return index >= 0 && (GetEntry(index).Mask & ToMask(layer)) != 0;
    }

    internal bool TryGetLayer(
        DependencyProperty property,
        Layer layer,
        out object? value,
        out BaseValueSource source)
    {
        var index = FindIndex(property);
        if (index < 0)
        {
            value = null;
            source = BaseValueSource.Unknown;
            return false;
        }

        ref readonly var entry = ref GetEntry(index);
        var mask = ToMask(layer);
        if ((entry.Mask & mask) == 0)
        {
            value = null;
            source = BaseValueSource.Unknown;
            return false;
        }

        var values = entry.Values;
        if (values is null)
        {
            value = entry.EffectiveValue;
            source = entry.EffectiveSource;
            return true;
        }

        value = GetLayerValue(values, layer);
        source = layer == Layer.Current
            ? values.CurrentSource
            : GetFixedSource(layer);
        return true;
    }

    /// <summary>Reads the highest native value layer, excluding both CSS contributions.</summary>
    internal bool TryGetWithoutCss(DependencyProperty property, out object? value)
    {
        var index = FindIndex(property);
        if (index < 0)
        {
            value = null;
            return false;
        }

        ref readonly var entry = ref GetEntry(index);
        var values = entry.Values;
        if (values is null)
        {
            value = entry.Mask is LayerMask.CssState or LayerMask.CssBase ? null : entry.EffectiveValue;
            return entry.Mask is not (LayerMask.CssState or LayerMask.CssBase);
        }

        var mask = entry.Mask;
        var layer = (mask & LayerMask.Local) != 0 ? Layer.Local
            : (mask & LayerMask.ParentTemplateTrigger) != 0 ? Layer.ParentTemplateTrigger
            : (mask & LayerMask.ParentTemplate) != 0 ? Layer.ParentTemplate
            : (mask & LayerMask.StyleTrigger) != 0 ? Layer.StyleTrigger
            : (mask & LayerMask.TemplateTrigger) != 0 ? Layer.TemplateTrigger
            : (mask & LayerMask.StyleSetter) != 0 ? Layer.StyleSetter
            : (mask & LayerMask.Current) != 0 ? Layer.Current
            : (Layer?)null;
        if (layer is null)
        {
            value = null;
            return false;
        }
        value = GetLayerValue(values, layer.Value);
        return true;
    }

    internal void SetLayer(
        DependencyProperty property,
        Layer layer,
        object? value,
        BaseValueSource currentSource = BaseValueSource.Unknown)
    {
        var mask = ToMask(layer);
        var index = FindIndex(property);
        if (index < 0)
        {
            ref var inserted = ref AddEntry(property.GlobalIndex);
            inserted = new Entry
            {
                PropertyIndex = property.GlobalIndex,
                Mask = mask,
                EffectiveValue = value,
                EffectiveSource = layer == Layer.Current ? currentSource : GetFixedSource(layer),
            };
            return;
        }

        ref var entry = ref GetEntry(index);
        var values = entry.Values;
        if (values is null && entry.Mask == mask)
        {
            entry.EffectiveValue = value;
            entry.EffectiveSource = layer == Layer.Current ? currentSource : GetFixedSource(layer);
            return;
        }

        if (values is null)
        {
            values = new LayerValues();
            var existingLayer = GetSingleLayer(entry.Mask);
            SetLayerValue(values, existingLayer, entry.EffectiveValue, entry.EffectiveSource);
            entry.Values = values;
        }

        SetLayerValue(values, layer, value, currentSource);
        entry.Mask |= mask;
        RecomputeEffective(ref entry);
        if (entry.Mask == mask)
            entry.Values = null;
    }

    internal bool RemoveLayer(DependencyProperty property, Layer layer)
    {
        var index = FindIndex(property);
        if (index < 0)
            return false;

        ref var entry = ref GetEntry(index);
        var mask = ToMask(layer);
        if ((entry.Mask & mask) == 0)
            return false;

        if (entry.Mask == mask)
        {
            RemoveEntry(index);
            return true;
        }

        entry.Mask &= ~mask;
        ClearLayerValue(entry.Values!, layer);
        RecomputeEffective(ref entry);

        if (IsSingleLayer(entry.Mask))
        {
            entry.Values = null;
        }

        return true;
    }

    internal KeyValuePair<DependencyProperty, object?>[] SnapshotLayer(Layer layer)
    {
        var mask = ToMask(layer);
        var result = new List<KeyValuePair<DependencyProperty, object?>>();
        for (var i = 0; i < _count; i++)
        {
            ref readonly var entry = ref GetEntry(i);
            if ((entry.Mask & mask) == 0)
                continue;

            var values = entry.Values;
            object? value = values is null
                ? entry.EffectiveValue
                : GetLayerValue(values, layer);
            result.Add(new KeyValuePair<DependencyProperty, object?>(entry.Property, value));
        }

        return result.ToArray();
    }

    internal EffectivePropertySnapshot SnapshotEffectiveProperties()
    {
        if (_count == 0)
            return default;

        DependencyProperty? first = null, second = null, third = null, fourth = null;
        var result = _count > 4 ? new DependencyProperty[_count] : null;
        var resultCount = 0;
        for (var i = 0; i < _count; i++)
        {
            ref readonly var entry = ref GetEntry(i);
            var nonCurrentMask = entry.Mask & ~LayerMask.Current;
            bool included = nonCurrentMask != LayerMask.None ||
                entry.EffectiveSource != BaseValueSource.Inherited;
            if (included)
            {
                var property = entry.Property;
                if (result is not null) result[resultCount] = property;
                else
                {
                    switch (resultCount)
                    {
                        case 0: first = property; break;
                        case 1: second = property; break;
                        case 2: third = property; break;
                        case 3: fourth = property; break;
                    }
                }
                resultCount++;
            }
        }

        if (resultCount == 0)
            return default;
        return result is not null
            ? new EffectivePropertySnapshot(result, resultCount)
            : new EffectivePropertySnapshot(first, second, third, fourth, resultCount);
    }

    internal bool HasAnyLayer(DependencyProperty property)
        => FindIndex(property) >= 0;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int FindIndex(DependencyProperty property)
    {
        if (_count == 0)
            return -1;
        var propertyIndex = property.GlobalIndex;
        if (_firstEntry.PropertyIndex == propertyIndex)
            return 0;
        if (!_expanded)
        {
            return _count == 2 && _secondEntry.PropertyIndex == propertyIndex ? 1 : -1;
        }

        var entries = ExpandedEntries;
        int overflowCount = _count - 1;
        if (_count <= BinarySearchThreshold)
        {
            for (var i = 0; i < overflowCount; i++)
                if (entries[i].PropertyIndex == propertyIndex)
                    return i + 1;
            return -1;
        }

        int low = 0, high = overflowCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int candidate = entries[middle].PropertyIndex;
            if (candidate == propertyIndex)
                return middle + 1;
            if (candidate < propertyIndex) low = middle + 1;
            else high = middle - 1;
        }

        return -1;
    }

    private void EnsureCapacity(int required)
    {
        if (!_expanded)
        {
            if (required <= 2)
                return;

            var promoted = new Entry[Math.Max(required, BinarySearchThreshold) - 1];
            promoted[0] = _secondEntry;
            _secondEntry = default;
            ExpandedEntries = promoted;
            _expanded = true;
            return;
        }

        var entries = ExpandedEntries;
        if (required <= entries.Length + 1)
            return;

        Array.Resize(ref entries, Math.Max(required, (entries.Length + 1) * 2) - 1);
        ExpandedEntries = entries;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private ref Entry GetEntry(int index)
    {
        if (index == 0)
            return ref _firstEntry;

        if (_expanded)
            return ref ExpandedEntries[index - 1];

        return ref _secondEntry;
    }

    private ref Entry AddEntry(int propertyIndex)
    {
        EnsureCapacity(_count + 1);
        if (!_expanded)
            return ref GetEntry(_count++);

        var entries = ExpandedEntries;
        int insertion = _count - 1;
        while (insertion > 0 && entries[insertion - 1].PropertyIndex > propertyIndex)
        {
            entries[insertion] = entries[insertion - 1];
            insertion--;
        }
        _count++;
        return ref entries[insertion];
    }

    private void RemoveEntry(int index)
    {
        if (_count == 1)
        {
            _firstEntry = default;
            _secondEntry = default;
            _expanded = false;
            _count = 0;
            return;
        }

        int lastIndex = _count - 1;
        if (index == 0)
        {
            _firstEntry = GetEntry(lastIndex);
            index = lastIndex;
        }
        if (_expanded)
        {
            var entries = ExpandedEntries;
            int removed = index - 1;
            int last = lastIndex - 1;
            if (removed < last)
                Array.Copy(entries, removed + 1, entries, removed, last - removed);
            entries[last] = default;
        }
        else
        {
            _secondEntry = default;
        }
        _count--;
    }

    /// <summary>
    /// 依赖属性优先级（高 → 低），与 WPF 的 <see cref="BaseValueSource"/> 数值序一致：
    /// Local(11) &gt; ParentTemplateTrigger(10) &gt; ParentTemplate(9) &gt; StyleTrigger(7)
    /// &gt; TemplateTrigger(6) &gt; Style(5)。
    ///
    /// <para>两个 trigger 层的区别（这正是 WPF 把它们分开的原因）：
    /// <c>ParentTemplateTrigger</c> 是**模板对自己生成的具名部件**下的 trigger（
    /// <c>&lt;Setter TargetName="PART_Bd" …/&gt;</c>），它代表宿主控件对模板内部件的支配，
    /// 优先级仅次于 local；<c>TemplateTrigger</c> 是模板对**被模板化的控件自身**下的
    /// trigger（无 TargetName），它必须低于该控件自己 Style 上的 trigger，否则用户写的
    /// <c>&lt;Style.Triggers&gt;</c> 永远盖不过主题模板的 hover/pressed 反馈。</para>
    ///
    /// <para>CSS 引擎占两层：<c>CssState</c>（胜者选择器含动态伪类，如 :hover）压过
    /// StyleTrigger——作者显式写的 :hover 必须盖过主题的 hover 反馈——但永不压 local；
    /// <c>CssBase</c>（普通命中值与内联 Css.Style）压过 StyleSetter（CSS 能覆盖主题/隐式/
    /// 显式样式的 setter）但低于任何 trigger，保住「setter 类基线值输给 trigger」的不变式。</para>
    /// </summary>
    private static void RecomputeEffective(ref Entry entry)
    {
        var values = entry.Values!;
        if ((entry.Mask & LayerMask.Local) != 0)
        {
            entry.EffectiveValue = values.Local;
            entry.EffectiveSource = BaseValueSource.Local;
        }
        else if ((entry.Mask & LayerMask.ParentTemplateTrigger) != 0)
        {
            entry.EffectiveValue = values.ParentTemplateTrigger;
            entry.EffectiveSource = BaseValueSource.ParentTemplateTrigger;
        }
        else if ((entry.Mask & LayerMask.ParentTemplate) != 0)
        {
            entry.EffectiveValue = values.ParentTemplate;
            entry.EffectiveSource = BaseValueSource.ParentTemplate;
        }
        else if ((entry.Mask & LayerMask.CssState) != 0)
        {
            entry.EffectiveValue = values.CssState;
            entry.EffectiveSource = BaseValueSource.StyleTrigger;
        }
        else if ((entry.Mask & LayerMask.StyleTrigger) != 0)
        {
            entry.EffectiveValue = values.StyleTrigger;
            entry.EffectiveSource = BaseValueSource.StyleTrigger;
        }
        else if ((entry.Mask & LayerMask.TemplateTrigger) != 0)
        {
            entry.EffectiveValue = values.TemplateTrigger;
            entry.EffectiveSource = BaseValueSource.TemplateTrigger;
        }
        else if ((entry.Mask & LayerMask.CssBase) != 0)
        {
            entry.EffectiveValue = values.CssBase;
            entry.EffectiveSource = BaseValueSource.Style;
        }
        else if ((entry.Mask & LayerMask.StyleSetter) != 0)
        {
            entry.EffectiveValue = values.StyleSetter;
            entry.EffectiveSource = BaseValueSource.Style;
        }
        else
        {
            entry.EffectiveValue = values.Current;
            entry.EffectiveSource = values.CurrentSource;
        }
    }

    /// <summary>Reports which layer currently supplies the effective value (mirrors <see cref="RecomputeEffective"/>).</summary>
    internal bool TryGetEffectiveLayer(DependencyProperty property, out Layer layer)
    {
        var index = FindIndex(property);
        if (index < 0)
        {
            layer = default;
            return false;
        }

        var mask = GetEntry(index).Mask;
        if ((mask & LayerMask.Local) != 0) layer = Layer.Local;
        else if ((mask & LayerMask.ParentTemplateTrigger) != 0) layer = Layer.ParentTemplateTrigger;
        else if ((mask & LayerMask.ParentTemplate) != 0) layer = Layer.ParentTemplate;
        else if ((mask & LayerMask.CssState) != 0) layer = Layer.CssState;
        else if ((mask & LayerMask.StyleTrigger) != 0) layer = Layer.StyleTrigger;
        else if ((mask & LayerMask.TemplateTrigger) != 0) layer = Layer.TemplateTrigger;
        else if ((mask & LayerMask.CssBase) != 0) layer = Layer.CssBase;
        else if ((mask & LayerMask.StyleSetter) != 0) layer = Layer.StyleSetter;
        else if ((mask & LayerMask.Current) != 0) layer = Layer.Current;
        else
        {
            layer = default;
            return false;
        }

        return true;
    }

    private static void SetLayerValue(
        LayerValues values,
        Layer layer,
        object? value,
        BaseValueSource source = BaseValueSource.Unknown)
    {
        switch (layer)
        {
            case Layer.Local: values.Local = value; break;
            case Layer.ParentTemplateTrigger: values.ParentTemplateTrigger = value; break;
            case Layer.ParentTemplate: values.ParentTemplate = value; break;
            case Layer.CssState: values.CssState = value; break;
            case Layer.StyleTrigger: values.StyleTrigger = value; break;
            case Layer.TemplateTrigger: values.TemplateTrigger = value; break;
            case Layer.CssBase: values.CssBase = value; break;
            case Layer.StyleSetter: values.StyleSetter = value; break;
            case Layer.Current:
                values.Current = value;
                values.CurrentSource = source;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(layer), layer, null);
        }
    }

    private static object? GetLayerValue(LayerValues values, Layer layer) => layer switch
    {
        Layer.Local => values.Local,
        Layer.ParentTemplateTrigger => values.ParentTemplateTrigger,
        Layer.ParentTemplate => values.ParentTemplate,
        Layer.CssState => values.CssState,
        Layer.StyleTrigger => values.StyleTrigger,
        Layer.TemplateTrigger => values.TemplateTrigger,
        Layer.CssBase => values.CssBase,
        Layer.StyleSetter => values.StyleSetter,
        Layer.Current => values.Current,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    private static void ClearLayerValue(LayerValues values, Layer layer) =>
        SetLayerValue(values, layer, null);

    private static LayerMask ToMask(Layer layer) => layer switch
    {
        Layer.Local => LayerMask.Local,
        Layer.ParentTemplateTrigger => LayerMask.ParentTemplateTrigger,
        Layer.ParentTemplate => LayerMask.ParentTemplate,
        Layer.CssState => LayerMask.CssState,
        Layer.StyleTrigger => LayerMask.StyleTrigger,
        Layer.TemplateTrigger => LayerMask.TemplateTrigger,
        Layer.CssBase => LayerMask.CssBase,
        Layer.StyleSetter => LayerMask.StyleSetter,
        Layer.Current => LayerMask.Current,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    private static BaseValueSource GetFixedSource(Layer layer) => layer switch
    {
        Layer.Local => BaseValueSource.Local,
        Layer.ParentTemplateTrigger => BaseValueSource.ParentTemplateTrigger,
        Layer.ParentTemplate => BaseValueSource.ParentTemplate,
        // CSS layers surface as their closest WPF analogue: BaseValueSource is a WPF-parity
        // enum (pinned by parity tests) and must not grow new members. Precise attribution
        // is available through CssDiagnostics instead.
        Layer.CssState => BaseValueSource.StyleTrigger,
        Layer.StyleTrigger => BaseValueSource.StyleTrigger,
        Layer.TemplateTrigger => BaseValueSource.TemplateTrigger,
        Layer.CssBase => BaseValueSource.Style,
        Layer.StyleSetter => BaseValueSource.Style,
        _ => BaseValueSource.Unknown,
    };

    private static bool IsSingleLayer(LayerMask mask)
    {
        var bits = (ushort)mask;
        return bits != 0 && (bits & (bits - 1)) == 0;
    }

    private static Layer GetSingleLayer(LayerMask mask)
    {
        if (!IsSingleLayer(mask))
            throw new InvalidOperationException("Expected exactly one dependency-property value layer.");

        return mask switch
        {
            LayerMask.Local => Layer.Local,
            LayerMask.ParentTemplateTrigger => Layer.ParentTemplateTrigger,
            LayerMask.ParentTemplate => Layer.ParentTemplate,
            LayerMask.CssState => Layer.CssState,
            LayerMask.StyleTrigger => Layer.StyleTrigger,
            LayerMask.TemplateTrigger => Layer.TemplateTrigger,
            LayerMask.CssBase => Layer.CssBase,
            LayerMask.StyleSetter => Layer.StyleSetter,
            LayerMask.Current => Layer.Current,
            _ => throw new InvalidOperationException("Unknown dependency-property value layer."),
        };
    }
}
