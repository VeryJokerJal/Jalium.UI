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
/// allocates a <see cref="LayerValues"/> only when two value sources coexist.
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
        public DependencyProperty Property;
        public LayerMask Mask;
        public object? EffectiveValue;
        public BaseValueSource EffectiveSource;
        public LayerValues? Values;
    }

    // Linear lookup is cheaper than allocating and hashing for the small stores used by
    // Geometry/Freezable objects. Controls normally cross this threshold and get one
    // GlobalIndex lookup instead of a scan or six layer dictionaries.
    private const int IndexedThreshold = 4;
    private Entry[] _entries = new Entry[IndexedThreshold];
    private Dictionary<int, int>? _indices;
    private int _count;

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

        ref readonly var entry = ref _entries[index];
        value = entry.EffectiveValue;
        source = entry.EffectiveSource;
        return true;
    }

    internal bool ContainsLayer(DependencyProperty property, Layer layer)
    {
        var index = FindIndex(property);
        return index >= 0 && (_entries[index].Mask & ToMask(layer)) != 0;
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

        ref readonly var entry = ref _entries[index];
        var mask = ToMask(layer);
        if ((entry.Mask & mask) == 0)
        {
            value = null;
            source = BaseValueSource.Unknown;
            return false;
        }

        if (entry.Values is null)
        {
            value = entry.EffectiveValue;
            source = entry.EffectiveSource;
            return true;
        }

        value = GetLayerValue(entry.Values, layer);
        source = layer == Layer.Current
            ? entry.Values.CurrentSource
            : GetFixedSource(layer);
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
            EnsureCapacity(_count + 1);
            index = _count++;
            _entries[index] = new Entry
            {
                Property = property,
                Mask = mask,
                EffectiveValue = value,
                EffectiveSource = layer == Layer.Current ? currentSource : GetFixedSource(layer),
            };

            EnsureIndexIfNeeded();
            if (_indices is not null)
            {
                _indices[property.GlobalIndex] = index;
            }
            return;
        }

        ref var entry = ref _entries[index];
        if (entry.Values is null && entry.Mask == mask)
        {
            entry.EffectiveValue = value;
            entry.EffectiveSource = layer == Layer.Current ? currentSource : GetFixedSource(layer);
            return;
        }

        var values = entry.Values;
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
    }

    internal bool RemoveLayer(DependencyProperty property, Layer layer)
    {
        var index = FindIndex(property);
        if (index < 0)
            return false;

        ref var entry = ref _entries[index];
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
            ref readonly var entry = ref _entries[i];
            if ((entry.Mask & mask) == 0)
                continue;

            object? value = entry.Values is null
                ? entry.EffectiveValue
                : GetLayerValue(entry.Values, layer);
            result.Add(new KeyValuePair<DependencyProperty, object?>(entry.Property, value));
        }

        return result.ToArray();
    }

    internal DependencyProperty[] SnapshotEffectiveProperties()
    {
        var result = new List<DependencyProperty>(_count);
        for (var i = 0; i < _count; i++)
        {
            ref readonly var entry = ref _entries[i];
            var nonCurrentMask = entry.Mask & ~LayerMask.Current;
            if (nonCurrentMask != LayerMask.None)
            {
                result.Add(entry.Property);
                continue;
            }

            if (TryGetLayer(entry.Property, Layer.Current, out _, out var currentSource) &&
                currentSource != BaseValueSource.Inherited)
            {
                result.Add(entry.Property);
            }
        }

        return result.ToArray();
    }

    internal bool HasAnyLayer(DependencyProperty property)
        => FindIndex(property) >= 0;

    private int FindIndex(DependencyProperty property)
    {
        if (_indices is not null)
        {
            return _indices.TryGetValue(property.GlobalIndex, out var index) ? index : -1;
        }

        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries[i].Property, property))
                return i;
        }

        return -1;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _entries.Length)
            return;

        Array.Resize(ref _entries, Math.Max(required, _entries.Length * 2));
    }

    private void EnsureIndexIfNeeded()
    {
        if (_indices is not null || _count <= IndexedThreshold)
            return;

        var indices = new Dictionary<int, int>(_count);
        for (var i = 0; i < _count; i++)
        {
            indices[_entries[i].Property.GlobalIndex] = i;
        }
        _indices = indices;
    }

    private void RemoveEntry(int index)
    {
        var removedGlobalIndex = _entries[index].Property.GlobalIndex;
        var lastIndex = --_count;
        if (index != lastIndex)
        {
            _entries[index] = _entries[lastIndex];
            if (_indices is not null)
            {
                _indices[_entries[index].Property.GlobalIndex] = index;
            }
        }

        _entries[lastIndex] = default;
        _indices?.Remove(removedGlobalIndex);
        if (_count <= IndexedThreshold)
        {
            _indices = null;
        }
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

        var mask = _entries[index].Mask;
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
