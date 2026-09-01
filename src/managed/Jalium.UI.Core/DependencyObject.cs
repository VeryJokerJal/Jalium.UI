using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Represents an object that participates in the dependency property system.
/// This is the base class for all objects that support dependency properties.
/// </summary>
public class DependencyObject : DispatcherObject
{
    [ThreadStatic]
    private static HashSet<(DependencyObject owner, DependencyProperty property)>? t_activeCoercions;

    // All non-default base-value layers share one sparse property index. A property with a
    // single source stays inline; only genuine precedence conflicts allocate layered storage.
    // Keeping the whole store null preserves the zero-allocation path for render primitives.
    private DependencyValueStore? _valueStore;
    private Dictionary<DependencyProperty, BindingExpressionBase>? _bindings;
    private Dictionary<DependencyProperty, AnimatedPropertyValue>? _animatedValues;
    private Dictionary<DependencyProperty, Brush>? _mutableRenderBrushValues;
    private WeakReference<FrameworkElement>? _bindingMentor;
    private List<WeakReference<DependencyObject>>? _bindingMentees;

    #region Binding reactivation epoch

    // 一次模板展开（Control.ApplyTemplateCore / Page.RebuildVisualTree）会对同一批元素反复调用
    // ReactivateBindings：
    //   1. SetTemplatedParentRecursive 里每设一次 TemplatedParent 就调一次（首次激活，真活）；
    //   2. AddVisualChild -> FrameworkElement.OnVisualParentChanged 对整棵可视子树再来一趟
    //      （让 RelativeSource FindAncestor / 继承 DataContext 的绑定得以激活，也是真活）；
    //   3. 紧接着 Control.ReactivateBindingsRecursive 又来一趟——这一趟对**已激活**的绑定只是
    //      再跑一遍 UpdateTarget()，取到同样的源值写回同样的目标，纯属重复。
    //
    // 实测（Release，Button 主题模板 2 个元素 7 个 TemplateBinding，PageSwapCostBenchmarkTests
    // .TemplateExpansion_PhaseBreakdown_IsMeasured）：第 3 趟单独就要 7.3-8.2 us / 次展开，而
    // 三趟递归合计 21.4 us。整页替换里带模板控件是绝对主力，这笔重复不小。
    //
    // 这里用「展开轮次戳」把重复剪掉：一次展开开始时取一个全局唯一的 epoch，元素每次真正跑完
    // ReactivateBindings 就把 epoch 记在自己身上；第 3 趟遇到本轮已经跑过的元素直接跳过它的
    // 重算（**仍然继续遍历**，因为第 3 趟的价值正是去够到第 2 趟够不到的非可视子节点：
    // Popup.Child、尚未进可视树的 Border.Child / ContentControl.Content / Panel.Children）。
    //
    // ★为什么这不是「延迟」：没有任何工作被推迟到 attach 之后——三趟依然全部在
    // ApplyTemplateCore 返回之前完成，只是把其中可证明重复的那部分不再做第二遍。
    // 框架契约「Children.Add() 返回时子树已按新 scope 重算」原样成立。
    //
    // epoch 用 long + Interlocked 递增，保证跨线程（多 UI 线程 / 多窗口）永不重号，
    // 因此元素上的戳不会被另一线程的轮次误判为「本轮已算过」。
    private static long s_bindingReactivationEpochCounter;

    [ThreadStatic]
    private static long t_currentBindingReactivationEpoch;

    private long _bindingReactivationEpoch;

    /// <summary>
    /// 模板展开去重总开关，仅供基准测试把「开/关」两条路径在**同一个进程里交替测量**——
    /// 跨进程的漂移（主题字典缓存冷热、GC 堆形态、JIT 分层）能达到 30%，远大于本优化本身的
    /// 量级，只有同进程交替才能给出可信的对照。生产路径恒为 true。
    /// <para>它同时管两件事：(1) 本文件的绑定重算轮次剪枝；(2) Control 三趟模板递归的环检测
    /// 集合（没有它，Promote / ReactivateBindings 两趟会因为 Border/Panel 特例与可视子节点
    /// 循环重叠而变成 O(2^depth)）。</para>
    /// </summary>
    internal static bool TemplateExpansionDedupEnabled { get; set; } = ReadTemplateExpansionDedupDefault();

    /// <summary>
    /// 默认开启；设 <c>JALIUM_TEMPLATE_EXPANSION_DEDUP=0</c> 可整进程关掉，用来在**不重新构建**
    /// 的前提下把任意一条测试或整个测试套在「优化前/优化后」两种行为下各跑一遍做对照。
    /// 排查「某个失败是不是本优化引入的」时这是最省事也最可靠的判据。
    /// </summary>
    private static bool ReadTemplateExpansionDedupDefault()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable("JALIUM_TEMPLATE_EXPANSION_DEDUP");
            return !string.Equals(value, "0", StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 开启一次模板展开的绑定重算轮次，返回调用前的轮次值供调用方在 finally 中还原。
    /// 嵌套展开（模板里的控件在本次展开过程中展开自己的模板）靠保存/还原来隔离：内层用自己的
    /// 新 epoch，退出后外层的 epoch 原样恢复，外层元素身上的戳仍然有效。
    /// </summary>
    internal static long BeginBindingReactivationEpoch()
    {
        var previous = t_currentBindingReactivationEpoch;

        // 关掉去重时把轮次置 0：WasBindingReactivationDoneInCurrentEpoch 恒 false，
        // 于是第三趟照旧对每个元素重算，行为与优化前逐字一致。
        t_currentBindingReactivationEpoch = TemplateExpansionDedupEnabled
            ? Interlocked.Increment(ref s_bindingReactivationEpochCounter)
            : 0L;
        return previous;
    }

    /// <summary>还原 <see cref="BeginBindingReactivationEpoch"/> 之前的轮次。</summary>
    internal static void EndBindingReactivationEpoch(long previous) =>
        t_currentBindingReactivationEpoch = previous;

    /// <summary>
    /// 本元素是否已在当前展开轮次里跑过 <see cref="ReactivateBindings"/>。
    /// 不在任何展开轮次中（epoch 为 0）时恒为 false——这样非模板路径的行为完全不变。
    /// </summary>
    internal bool WasBindingReactivationDoneInCurrentEpoch()
    {
        var epoch = t_currentBindingReactivationEpoch;
        return epoch != 0 && _bindingReactivationEpoch == epoch;
    }

    #endregion

    // Source-compatibility shims for Jalium's historical public Visual tree surface. They are
    // deliberately fields (and a callable delegate field), so metadata verification sees only
    // Visual's exact protected WPF properties/method while existing C# member syntax continues
    // to work through the inherited members.
    public Visual? VisualParent;

    public int VisualChildrenCount;

    public readonly Func<int, Visual?> GetVisualChild;

    public DependencyObject()
    {
        GetVisualChild = GetVisualChildCompatibility;
    }

    private Visual? GetVisualChildCompatibility(int index)
    {
        if (this is not Visual visual)
        {
            throw new InvalidOperationException("The object is not a Visual.");
        }

        return visual.InternalGetVisualChild(index);
    }

    /// <summary>
    /// Internal record to track animated property values.
    /// </summary>
    internal record AnimatedPropertyValue(
        object? BaseValue,       // Value before animation started
        BaseValueSource BaseSource, // Source before animation started
        object? CurrentValue,    // Current animated value
        bool HoldEndValue);      // Whether to hold the final value after animation ends

    /// <summary>
    /// Internal event for property change notification used by triggers.
    /// </summary>
    internal event Action<DependencyProperty, object?, object?>? PropertyChangedInternal;

    private readonly struct ValueState
    {
        public ValueState(object? value, BaseValueSource baseValueSource, bool isAnimated, bool isExpression, bool isCoerced)
        {
            Value = value;
            BaseValueSource = baseValueSource;
            IsAnimated = isAnimated;
            IsExpression = isExpression;
            IsCoerced = isCoerced;
        }

        public object? Value { get; }
        public BaseValueSource BaseValueSource { get; }
        public bool IsAnimated { get; }
        public bool IsExpression { get; }
        public bool IsCoerced { get; }
    }

    private sealed class CoercionKeyComparer : IEqualityComparer<(DependencyObject owner, DependencyProperty property)>
    {
        public static readonly CoercionKeyComparer Instance = new();

        public bool Equals((DependencyObject owner, DependencyProperty property) x, (DependencyObject owner, DependencyProperty property) y)
        {
            return ReferenceEquals(x.owner, y.owner) && ReferenceEquals(x.property, y.property);
        }

        public int GetHashCode((DependencyObject owner, DependencyProperty property) obj)
        {
            return HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.owner),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.property));
        }
    }

    /// <summary>
    /// 非 local 值层。优先级由 <see cref="DependencyValueStore"/> 定义，
    /// 高 → 低为：ParentTemplateTrigger &gt; ParentTemplate &gt; StyleTrigger
    /// &gt; TemplateTrigger &gt; StyleSetter。
    /// </summary>
    internal enum LayerValueSource
    {
        ParentTemplate,
        StyleTrigger,
        /// <summary>模板对**被模板化控件自身**下的 trigger（无 TargetName）。低于 StyleTrigger。</summary>
        TemplateTrigger,
        StyleSetter,
        /// <summary>模板对**自己生成的具名部件**下的 trigger（TargetName）。仅次于 local。</summary>
        ParentTemplateTrigger,
        /// <summary>CSS 引擎:胜者选择器含动态伪类(:hover 等)。高于 StyleTrigger、低于 ParentTemplate。</summary>
        CssState,
        /// <summary>CSS 引擎:普通命中值与内联 Css.Style。高于 StyleSetter、低于 TemplateTrigger。</summary>
        CssBase,
    }

    private enum ValueMutationKind : byte
    {
        SetLocalCore,
        SetLocalDirect,
        SetCurrent,
        SetLayer,
        ClearLocal,
        ClearLayer,
    }

    private readonly struct ValueMutation
    {
        private ValueMutation(
            ValueMutationKind kind,
            object? value = null,
            BaseValueSource baseSource = BaseValueSource.Unknown,
            LayerValueSource layerSource = default)
        {
            Kind = kind;
            Value = value;
            BaseSource = baseSource;
            LayerSource = layerSource;
        }

        private ValueMutationKind Kind { get; }
        private object? Value { get; }
        private BaseValueSource BaseSource { get; }
        private LayerValueSource LayerSource { get; }

        public static ValueMutation ForSetLocalCore(object? value) =>
            new(ValueMutationKind.SetLocalCore, value);

        public static ValueMutation ForSetLocalDirect(object? value) =>
            new(ValueMutationKind.SetLocalDirect, value);

        public static ValueMutation ForSetCurrent(object? value, BaseValueSource source) =>
            new(ValueMutationKind.SetCurrent, value, source);

        public static ValueMutation ForSetLayer(object? value, LayerValueSource source) =>
            new(ValueMutationKind.SetLayer, value, layerSource: source);

        public static ValueMutation ForClearLocal() => new(ValueMutationKind.ClearLocal);

        public static ValueMutation ForClearLayer(LayerValueSource source) =>
            new(ValueMutationKind.ClearLayer, layerSource: source);

        public bool Apply(DependencyObject owner, DependencyProperty dp)
        {
            switch (Kind)
            {
                case ValueMutationKind.SetLocalCore:
                    owner.SetLocalValueCore(dp, Value);
                    return true;
                case ValueMutationKind.SetLocalDirect:
                    owner.SetStoredValue(dp, DependencyValueStore.Layer.Local, Value);
                    return true;
                case ValueMutationKind.SetCurrent:
                    owner.SetStoredValue(dp, DependencyValueStore.Layer.Current, Value, BaseSource);
                    return true;
                case ValueMutationKind.SetLayer:
                    owner.SetLayerValueCore(dp, Value, LayerSource);
                    return true;
                case ValueMutationKind.ClearLocal:
                    return owner.ClearLocalValueCore(dp);
                case ValueMutationKind.ClearLayer:
                    return owner.ClearLayerValueCore(dp, LayerSource);
                default:
                    throw new InvalidOperationException("Unknown dependency-property mutation.");
            }
        }
    }

    /// <summary>
    /// Gets the cached dependency-object type descriptor for this instance.
    /// </summary>
    public DependencyObjectType DependencyObjectType
        => Jalium.UI.DependencyObjectType.FromSystemType(GetType());

    /// <summary>
    /// Gets whether this dependency object can no longer be modified.
    /// </summary>
    public bool IsSealed => IsSealedCore;

    private protected virtual bool IsSealedCore => false;

    /// <summary>
    /// Gets the current effective value of a dependency property.
    /// Value precedence: Animation > Local > Binding > Default
    /// </summary>
    /// <param name="dp">The dependency property to get.</param>
    /// <returns>The current effective value.</returns>
    public virtual object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        object? value = GetValueState(dp).Value;

        // Keep the ubiquitous non-brush GetValue path allocation-free and free of reflection-
        // based type checks. We only enter owner bookkeeping for a mutable brush, or when a
        // previously registered brush must be detached because this property's value changed.
        if (value is Brush { IsFrozen: false }
            || (_mutableRenderBrushValues?.ContainsKey(dp) ?? false))
        {
            return TrackMutableRenderBrushValue(dp, value);
        }

        return value;
    }

    /// <summary>
    /// Tracks mutable brushes consumed by a visual dependency property so an in-place brush
    /// mutation can invalidate only the visuals that actually use that brush. The association
    /// is weak on the brush side; this per-owner map supplies the per-property reference count
    /// needed when one brush is used by several properties on the same element.
    /// </summary>
    /// <remarks>
    /// <see cref="FrameworkElement.GetValue(DependencyProperty)"/> also calls this helper for
    /// inherited values, whose lookup path does not otherwise pass through this implementation.
    /// </remarks>
    internal object? TrackMutableRenderBrushValue(DependencyProperty dp, object? value)
    {
        Brush? newBrush = value as Brush;
        if (newBrush?.IsFrozen == true)
        {
            // Frozen brushes cannot raise a future Changed notification, so retaining an owner
            // registration for them only adds memory and lookup work.
            newBrush = null;
        }

        Brush? oldBrush = null;
        bool hadOldBrush = _mutableRenderBrushValues?.TryGetValue(dp, out oldBrush) == true;
        if (newBrush is null && !hadOldBrush)
        {
            return value;
        }

        if (this is not UIElement renderOwner
            || !typeof(Brush).IsAssignableFrom(dp.PropertyType))
        {
            return value;
        }

        if (ReferenceEquals(oldBrush, newBrush))
        {
            return value;
        }

        if (oldBrush is not null)
        {
            oldBrush.RemoveRenderOwner(renderOwner);
            _mutableRenderBrushValues!.Remove(dp);
        }

        if (newBrush is not null)
        {
            (_mutableRenderBrushValues ??= new Dictionary<DependencyProperty, Brush>())[dp] = newBrush;
            newBrush.AddRenderOwner(renderOwner);
        }
        else if (_mutableRenderBrushValues?.Count == 0)
        {
            _mutableRenderBrushValues = null;
        }

        return value;
    }

    /// <summary>
    /// Gets a value indicating whether this object has a local value set for the specified property.
    /// </summary>
    /// <param name="dp">The dependency property to check.</param>
    /// <returns>True if a local value is set; otherwise, false.</returns>
    /// <summary>
    /// True when the property carries a local or animated value — the precedence tiers a
    /// CSS layout-state override must never displace (used by the %-margin guard).
    /// </summary>
    internal bool HasLocalOrAnimatedValue(DependencyProperty dp)
        => HasLocalValue(dp) || _animatedValues?.ContainsKey(dp) == true;

    public bool HasLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _valueStore?.ContainsLayer(dp, DependencyValueStore.Layer.Local) == true;
    }

    /// <summary>
    /// Returns the local value of a dependency property, if a local value is set.
    /// </summary>
    /// <param name="dp">The dependency property to read.</param>
    /// <returns>The local value, or DependencyProperty.UnsetValue if no local value is set.</returns>
    public object? ReadLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_valueStore?.TryGetLayer(
                dp,
                DependencyValueStore.Layer.Local,
                out var value,
                out _) == true)
        {
            return value;
        }
        return DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// Sets the local value of a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to set.</param>
    /// <param name="value">The new value.</param>
    public void SetValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfReadOnly(dp);
        SetValueCore(dp, value);
    }

    private void SetValueCore(DependencyProperty dp, object? value)
    {
        CheckSealedAccess();
        ValidateValueOrThrow(dp, value);
        MutateValue(
            dp,
            ValueMutation.ForSetLocalCore(value),
            notifyBinding: true,
            allowAutoTransition: true);
    }

    /// <summary>
    /// Sets a read-only dependency property through its registration key.
    /// </summary>
    public void SetValue(DependencyPropertyKey key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        SetValueCore(key.DependencyProperty, value);
    }

    /// <summary>
    /// Sets the current value of a dependency property without forcing local-value precedence.
    /// </summary>
    /// <param name="dp">The dependency property to set.</param>
    /// <param name="value">The new value.</param>
    public void SetCurrentValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfReadOnly(dp);
        CheckSealedAccess();
        ValidateValueOrThrow(dp, value);

        // A null can never be the effective value of a non-nullable value-type property — the generated
        // CLR getter unboxes it (e.g. (Thickness)GetValue(...)) and would throw at layout. SetCurrentValue
        // is a "soft" set; degrade an illegal null to a no-op so the property keeps its current valid
        // value / synthesized default rather than pinning a null. This covers the Default/Inherited and
        // Local re-dispatch branches of SetCurrentValueForSource that write _currentValues/_localValues
        // directly, bypassing the SetLayerValueCore backstop.
        if (IsNullForNonNullableValueType(dp, value))
            return;

        // When the effective value comes from a CSS layer, BaseValueSource reports the closest
        // WPF analogue (Style/StyleTrigger); dispatching on it would misroute the write into the
        // StyleSetter/StyleTrigger layer where it stays shadowed by the CSS layer. Write back to
        // the owning CSS layer instead — the next CSS re-evaluation overwrites it, matching the
        // "style reapplication overwrites SetCurrentValue" semantics.
        if (_valueStore is { } cssProbe &&
            cssProbe.TryGetEffectiveLayer(dp, out var effectiveLayer))
        {
            if (effectiveLayer == DependencyValueStore.Layer.CssBase)
            {
                SetLayerValue(dp, value, LayerValueSource.CssBase, allowAutoTransition: true);
                return;
            }

            if (effectiveLayer == DependencyValueStore.Layer.CssState)
            {
                SetLayerValue(dp, value, LayerValueSource.CssState, allowAutoTransition: true);
                return;
            }
        }

        var source = GetValueSourceInternal(dp);
        SetCurrentValueForSource(dp, value, source.BaseValueSource, allowAutoTransition: true);
    }

    private static void ValidateValueOrThrow(DependencyProperty dp, object? value)
    {
        // Keep the established null-to-non-nullable-value-type degradation semantics, but reject every
        // non-null type mismatch at the public API boundary. Otherwise an untyped SetValue can publish,
        // for example, object into a Thickness property and defer the InvalidCastException until layout.
        var isLegacyNullValueTypeWrite = IsNullForNonNullableValueType(dp, value);
        if ((!isLegacyNullValueTypeWrite && !dp.IsValidType(value)) || !dp.IsValidValue(value))
        {
            throw new ArgumentException(
                $"Value '{value ?? "<null>"}' is not valid for dependency property '{dp.OwnerType.Name}.{dp.Name}'.",
                nameof(value));
        }
    }

    private static void ThrowIfReadOnly(DependencyProperty dp)
    {
        if (dp.ReadOnly)
        {
            throw new InvalidOperationException(
                $"'{dp.Name}' is read-only and can only be changed with its DependencyPropertyKey.");
        }
    }

    private void SetCurrentValueForSource(DependencyProperty dp, object? value, BaseValueSource baseSource)
    {
        SetCurrentValueForSource(dp, value, baseSource, allowAutoTransition: false);
    }

    private void SetCurrentValueForSource(DependencyProperty dp, object? value, BaseValueSource baseSource, bool allowAutoTransition)
    {
        switch (baseSource)
        {
            case BaseValueSource.Local:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLocalDirect(value),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.ParentTemplate:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLayer(value, LayerValueSource.ParentTemplate),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.StyleTrigger:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLayer(value, LayerValueSource.StyleTrigger),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.TemplateTrigger:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLayer(value, LayerValueSource.TemplateTrigger),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.ParentTemplateTrigger:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLayer(value, LayerValueSource.ParentTemplateTrigger),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.Style:
            case BaseValueSource.DefaultStyle:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLayer(value, LayerValueSource.StyleSetter),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.Default:
            case BaseValueSource.Inherited:
                var keepSource = baseSource == BaseValueSource.Inherited
                    ? BaseValueSource.Inherited
                    : BaseValueSource.Default;
                MutateValue(
                    dp,
                    ValueMutation.ForSetCurrent(value, keepSource),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            default:
                MutateValue(
                    dp,
                    ValueMutation.ForSetLocalDirect(value),
                    notifyBinding: false,
                    allowAutoTransition);
                return;
        }
    }

    /// <summary>
    /// Forces re-evaluation of a dependency property's value, including coercion.
    /// </summary>
    /// <param name="dp">The dependency property to coerce.</param>
    public void CoerceValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        var oldValue = GetValue(dp);
        var newValue = GetValueState(dp, forceCoerce: true).Value;
        if (!Equals(oldValue, newValue))
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
    }

    /// <summary>
    /// Sets a binding on a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to bind.</param>
    /// <param name="binding">The binding to set.</param>
    /// <returns>The binding expression for the binding.</returns>
    public BindingExpressionBase SetBinding(DependencyProperty dp, BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(binding);
        CheckSealedAccess();

        // Remove existing binding
        ClearBinding(dp);

        // Create and activate the binding expression
        var expression = binding.CreateBindingExpression(this, dp);
        (_bindings ??= new())[dp] = expression;
        expression.Activate();

        return expression;
    }

    /// <summary>
    /// Gets the binding expression for a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property.</param>
    /// <returns>The binding expression, or null if the property is not bound.</returns>
    internal BindingExpressionBase? GetBindingExpression(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _bindings?.GetValueOrDefault(dp);
    }

    /// <summary>
    /// Gets the framework element that supplies an inherited binding context for this
    /// non-visual dependency object.
    /// </summary>
    /// <remarks>
    /// Some dependency objects are owned by a visual control without participating in
    /// either its visual or logical tree (for example, chart series). A weak mentor lets
    /// bindings on those objects resolve the owner's <see cref="FrameworkElement.DataContext"/>
    /// without turning configuration objects into visuals or keeping the owner alive.
    /// </remarks>
    internal FrameworkElement? BindingMentor
    {
        get
        {
            if (_bindingMentor?.TryGetTarget(out var mentor) == true)
            {
                return mentor;
            }

            return null;
        }
    }

    /// <summary>
    /// Sets the framework element that supplies this object's inherited binding context.
    /// </summary>
    /// <remarks>
    /// Bindings are detached before the mentor changes so subscriptions to the previous
    /// mentor are removed, then activated again against the new context.
    /// </remarks>
    internal void SetBindingMentor(FrameworkElement? mentor)
    {
        var previousMentor = BindingMentor;
        if (ReferenceEquals(previousMentor, mentor) &&
            (mentor != null || _bindingMentor == null))
        {
            return;
        }

        var bindings = _bindings?.Values.ToArray();
        if (bindings != null)
        {
            foreach (var expression in bindings)
            {
                expression.Deactivate();
            }
        }

        previousMentor?.RemoveBindingMentee(this);
        _bindingMentor = mentor == null
            ? null
            : new WeakReference<FrameworkElement>(mentor);
        mentor?.AddBindingMentee(this);

        if (bindings != null)
        {
            foreach (var expression in bindings)
            {
                expression.Activate();
            }
        }
    }

    private void AddBindingMentee(DependencyObject mentee)
    {
        _bindingMentees ??= new List<WeakReference<DependencyObject>>();

        for (int i = _bindingMentees.Count - 1; i >= 0; i--)
        {
            if (!_bindingMentees[i].TryGetTarget(out var existing))
            {
                _bindingMentees.RemoveAt(i);
            }
            else if (ReferenceEquals(existing, mentee))
            {
                return;
            }
        }

        _bindingMentees.Add(new WeakReference<DependencyObject>(mentee));
    }

    private void RemoveBindingMentee(DependencyObject mentee)
    {
        if (_bindingMentees == null)
        {
            return;
        }

        for (int i = _bindingMentees.Count - 1; i >= 0; i--)
        {
            if (!_bindingMentees[i].TryGetTarget(out var existing) ||
                ReferenceEquals(existing, mentee))
            {
                _bindingMentees.RemoveAt(i);
            }
        }

        if (_bindingMentees.Count == 0)
        {
            _bindingMentees = null;
        }
    }

    /// <summary>
    /// Removes the binding from a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to unbind.</param>
    public void ClearBinding(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_bindings?.TryGetValue(dp, out var expression) == true)
        {
            expression.Deactivate();
            RemoveStoredValue(ref _bindings, dp);
        }
    }

    /// <summary>
    /// Removes all bindings from this object.
    /// </summary>
    public void ClearAllBindings()
    {
        var bindings = _bindings;
        if (bindings is null)
            return;

        foreach (var expression in bindings.Values)
        {
            expression.Deactivate();
        }
        bindings.Clear();
        if (ReferenceEquals(_bindings, bindings))
            _bindings = null;
    }

    /// <summary>
    /// Reactivates all bindings on this object.
    /// This is called when the TemplatedParent is set to allow deferred template bindings to resolve.
    /// </summary>
    internal void ReactivateBindings()
    {
        // 记下本轮已重算，供模板展开的第三趟剪枝（见 Binding reactivation epoch 一节）。
        // 无条件写入：非展开期 epoch 为 0，写 0 等于把戳清掉，下次展开不会误判为已算过。
        _bindingReactivationEpoch = t_currentBindingReactivationEpoch;

        if (_bindings is { } bindings)
        {
            foreach (var expression in bindings.Values)
            {
                // Only reactivate if not already active (deferred bindings that couldn't activate earlier)
                if (!expression.IsActive)
                {
                    expression.Activate();
                }
                else
                {
                    // For already active bindings, update the target to get latest value
                    expression.UpdateTarget();
                }
            }
        }

        ReactivateBindingMentees();
    }

    private void ReactivateBindingMentees()
    {
        if (_bindingMentees == null)
        {
            return;
        }

        // Work from a snapshot because reactivation may replace a series collection and
        // consequently add or remove mentor relationships.
        var mentees = _bindingMentees.ToArray();
        foreach (var weakMentee in mentees)
        {
            if (weakMentee.TryGetTarget(out var mentee) &&
                ReferenceEquals(mentee.BindingMentor, this))
            {
                mentee.ReactivateBindings();
            }
        }

        for (int i = _bindingMentees.Count - 1; i >= 0; i--)
        {
            if (!_bindingMentees[i].TryGetTarget(out _))
            {
                _bindingMentees.RemoveAt(i);
            }
        }

        if (_bindingMentees.Count == 0)
        {
            _bindingMentees = null;
        }
    }

    /// <summary>
    /// Clears the local value of a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to clear.</param>
    public void ClearValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfReadOnly(dp);
        ClearValueCore(dp);
    }

    private void ClearValueCore(DependencyProperty dp)
    {
        CheckSealedAccess();
        MutateValue(
            dp,
            ValueMutation.ForClearLocal(),
            notifyBinding: false,
            allowAutoTransition: true);
    }

    /// <summary>
    /// Clears a read-only dependency property through its registration key.
    /// </summary>
    public void ClearValue(DependencyPropertyKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ClearValueCore(key.DependencyProperty);
    }

    /// <summary>
    /// Returns a snapshot enumerator over locally set dependency properties.
    /// </summary>
    public LocalValueEnumerator GetLocalValueEnumerator()
    {
        var entries = _valueStore?
            .SnapshotLayer(DependencyValueStore.Layer.Local)
            .Select(static pair => new LocalValueEntry(pair.Key, pair.Value))
            .ToArray() ?? Array.Empty<LocalValueEntry>();
        return new LocalValueEnumerator(entries);
    }

    /// <summary>
    /// Re-evaluates a dependency property's binding and coercion.
    /// </summary>
    public void InvalidateProperty(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_bindings?.TryGetValue(dp, out BindingExpressionBase? binding) == true)
        {
            binding.UpdateTarget();
        }

        CoerceValue(dp);
    }

    /// <summary>
    /// Determines whether a property currently has a locally serializable value.
    /// </summary>
    protected internal virtual bool ShouldSerializeProperty(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _valueStore?.ContainsLayer(dp, DependencyValueStore.Layer.Local) == true;
    }

    /// <summary>
    /// Moves local values to the specified non-local layer.
    /// Used when applying template-generated trees so template defaults do not block template triggers.
    /// </summary>
    internal void PromoteLocalValuesToLayer(LayerValueSource source)
    {
        if (_valueStore is not { Count: > 0 } valueStore)
            return;

        var mappedSource = MapLayerValueSource(source);
        var entries = valueStore.SnapshotLayer(DependencyValueStore.Layer.Local);
        if (entries.Length == 0)
            return;

        foreach (var (dp, localValue) in entries)
        {
            var oldValue = GetValue(dp);

            RemoveStoredValue(dp, DependencyValueStore.Layer.Local);
            if (_valueStore?.TryGetLayer(
                    dp,
                    DependencyValueStore.Layer.Current,
                    out _,
                    out var currentSource) == true && currentSource == mappedSource)
            {
                RemoveStoredValue(dp, DependencyValueStore.Layer.Current);
            }

            // Never promote a null local onto a non-nullable value-type layer (mirrors the
            // SetLayerValueCore backstop, which this direct-write loop bypasses): drop it so the
            // property falls through to its synthesized/registered default instead of unbox-crashing at
            // layout. The local was already removed above, so skipping the write degrades to default; the
            // change notification below still fires for the null -> default transition.
            if (!IsNullForNonNullableValueType(dp, localValue) &&
                dp.IsValidType(localValue) &&
                dp.IsValidValue(localValue))
            {
                SetStoredValue(dp, MapStoreLayer(source), localValue);
            }

            var newValue = GetValue(dp);
            if (!Equals(oldValue, newValue))
                OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
        }

    }

    /// <summary>
    /// Called when a dependency property value changes.
    /// </summary>
    /// <param name="e">Event arguments containing the changed property information.</param>
    protected virtual void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        // Use per-type metadata so that shared properties (e.g. TextElement.ForegroundProperty
        // used by both Control and TextBlock via AddOwner) invoke the correct callback.
        var metadata = e.Property.GetMetadata(GetType());
        metadata.PropertyChangedCallback?.Invoke(this, e);

        // WPF 语义：FrameworkPropertyMetadata 上的 Affects* flag 必须自动触发对应失效。
        // 在此之前框架只读 AffectsCompositionOnly（仅在 SetAnimatedValue 路径），
        // AffectsMeasure / AffectsRender / AffectsArrange / AffectsParentMeasure /
        // AffectsParentArrange 全部被忽略 —— 比如 ConnectionLine 用
        // AffectsMeasure | AffectsRender 注册 SourceX/Y/TargetX/Y 后改坐标，元素
        // RenderSize 不更新、OnRender 也不重跑，连线根本画不出来。
        // 与显式 PropertyChangedCallback 共存：LayoutManager / dirty queue 自带 dedup。
        if (this is UIElement element && metadata is FrameworkPropertyMetadata fpm)
        {
            if (fpm.AffectsMeasure)
                element.InvalidateMeasure();
            if (fpm.AffectsArrange)
                element.InvalidateArrange();
            if (fpm.AffectsRender)
            {
                if (fpm.AffectsCompositionOnly)
                    element.InvalidateComposition();
                else
                    element.InvalidateVisual();
            }
            if (fpm.AffectsParentMeasure && element.VisualParent is UIElement parentForMeasure)
                parentForMeasure.InvalidateMeasure();
            if (fpm.AffectsParentArrange && element.VisualParent is UIElement parentForArrange)
                parentForArrange.InvalidateArrange();

            if ((fpm.AffectsParentMeasure || fpm.AffectsParentArrange)
                && element.VisualParent is FrameworkElement frameworkParent)
            {
                frameworkParent.ParentLayoutInvalidated(element);
            }
        }

        // Notify internal listeners (triggers, etc.)
        PropertyChangedInternal?.Invoke(e.Property, e.OldValue, e.NewValue);
    }

    #region Animation Value Support

    /// <summary>
    /// Sets an animated value for a dependency property. Called by the animation system.
    /// </summary>
    /// <param name="dp">The dependency property to animate.</param>
    /// <param name="value">The current animated value.</param>
    /// <param name="holdEndValue">Whether to hold the final value after animation ends (FillBehavior.HoldEnd).</param>
    /// <returns>
    /// <c>true</c> if the animated value actually changed this call (and therefore a
    /// present was scheduled); <c>false</c> if the new value equals the currently
    /// displayed one. A running clock that produces an unchanged value (settled spring
    /// tail, held end value, paused timeline) returns <c>false</c> so the render loop is
    /// NOT forced to submit a frame for a pixel-identical result.
    /// </returns>
    internal bool SetAnimatedValue(DependencyProperty dp, object? value, bool holdEndValue)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var oldValue = GetValue(dp);

        var animatedValues = _animatedValues ??= new();
        if (!animatedValues.TryGetValue(dp, out var existing))
        {
            // Store base value for restoration when animation ends
            var (baseValue, baseSource) = GetUncoercedBaseValueInternal(dp);
            animatedValues[dp] = new AnimatedPropertyValue(baseValue, baseSource, value, holdEndValue);
        }
        else
        {
            animatedValues[dp] = existing with { CurrentValue = value, HoldEndValue = holdEndValue };
        }

        if (Equals(oldValue, value))
        {
            // No visible change: do not fire OnPropertyChanged and do not schedule a
            // present. This is the single source of truth that lets a frame on which
            // the animated value did not move skip rendering entirely.
            return false;
        }

        OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, value));

        // The metadata-callback path is the primary invalidation hook (e.g.
        // OnRenderPropertyChanged → InvalidateVisual, OnCompositionPropertyChanged
        // → InvalidateComposition). Without an explicit callback nothing would
        // schedule a present, so animated DPs without a callback need a fallback.
        // AddDirtyElement deduplicates so double-calls are harmless when both
        // paths fire.
        if (this is UIElement uiElement && !DpHasInvalidationCallback(dp))
        {
            if (DpAffectsCompositionOnly(dp))
                uiElement.InvalidateComposition();
            else
                uiElement.InvalidateVisual();
        }

        return true;
    }

    private bool DpHasInvalidationCallback(DependencyProperty dp)
    {
        var metadata = dp.GetMetadata(GetType());
        return metadata.PropertyChangedCallback != null;
    }

    private bool DpAffectsCompositionOnly(DependencyProperty dp)
    {
        return dp.GetMetadata(GetType()) is FrameworkPropertyMetadata fpm && fpm.AffectsCompositionOnly;
    }

    /// <summary>
    /// Clears the animated value for a dependency property, restoring the base value if not holding.
    /// </summary>
    /// <param name="dp">The dependency property to clear animation from.</param>
    internal void ClearAnimatedValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_animatedValues?.TryGetValue(dp, out var animated) == true)
        {
            var oldValue = animated.CurrentValue;
            RemoveStoredValue(ref _animatedValues, dp);

            if (animated.HoldEndValue)
            {
                SetCurrentValueForSource(dp, oldValue, animated.BaseSource);
                return;
            }

            // Get the new effective value after removing animation
            var newValue = GetValue(dp);

            if (!Equals(oldValue, newValue))
            {
                OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            }

            InvalidateAfterAnimationCleared(dp);
        }
    }

    /// <summary>
    /// 动画层清除后的兜底重绘。当 oldValue 与 newValue 看似 Equals（例如两个返回
    /// 相同 Color 的 SolidColorBrush，或同 Reference 的资源 Brush），
    /// OnPropertyChanged 不会触发，但 visual 系统可能仍持有 animated value
    /// 时期产生的临时 Brush（每帧 GetCurrentValueCore 创建新实例）引用，导致
    /// 渲染未刷新到 base value。显式 schedule 一次 present 兜住这种竞态。
    /// 合成型 DP 不需要让 cached drawing 失效，仅 schedule present 即可。
    /// </summary>
    private void InvalidateAfterAnimationCleared(DependencyProperty dp)
    {
        if (this is UIElement uiElement)
        {
            if (DpAffectsCompositionOnly(dp))
                uiElement.InvalidateComposition();
            else
                uiElement.InvalidateVisual();
        }
    }

    /// <summary>
    /// Removes the animated value for a dependency property WITHOUT the HoldEnd
    /// promotion performed by <see cref="ClearAnimatedValue"/>: the end value is
    /// never written back as a current value. Used for container-recycling
    /// hygiene, where a pooled element must not carry an animation's final value
    /// as a ghost. Fires OnPropertyChanged when the effective value changes and
    /// always schedules the same invalidation fallback as ClearAnimatedValue.
    /// </summary>
    /// <param name="dp">The dependency property whose animated value to discard.</param>
    internal void DiscardAnimatedValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_animatedValues?.TryGetValue(dp, out var animated) == true)
        {
            var oldValue = animated.CurrentValue;
            RemoveStoredValue(ref _animatedValues, dp);

            var newValue = GetValue(dp);

            if (!Equals(oldValue, newValue))
            {
                OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            }

            InvalidateAfterAnimationCleared(dp);
        }
    }

    /// <summary>
    /// Discards every animated value on this object (no HoldEnd promotion).
    /// Keys are snapshotted first because OnPropertyChanged handlers may
    /// re-enter and mutate the animated layer. Not a per-frame path, so the
    /// snapshot allocation is acceptable.
    /// </summary>
    internal void DiscardAllAnimatedValues()
    {
        if (_animatedValues is not { Count: > 0 } animatedValues)
            return;

        var keys = new DependencyProperty[animatedValues.Count];
        animatedValues.Keys.CopyTo(keys, 0);

        foreach (var dp in keys)
        {
            DiscardAnimatedValue(dp);
        }
    }

    /// <summary>
    /// Checks if a dependency property currently has an active animated value.
    /// </summary>
    /// <param name="dp">The dependency property to check.</param>
    /// <returns>True if the property has an animated value; otherwise, false.</returns>
    internal bool HasAnimatedValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _animatedValues?.ContainsKey(dp) == true;
    }

    /// <summary>
    /// Gets the base value (before animation) for a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property.</param>
    /// <returns>The base value, or the current effective value if not animated.</returns>
    internal object? GetAnimationBaseValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_animatedValues?.TryGetValue(dp, out var animated) == true)
        {
            return animated.BaseValue;
        }

        return GetValue(dp);
    }

    internal virtual ValueSource GetValueSourceInternal(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        var state = GetValueState(dp);
        return new ValueSource(
            state.BaseValueSource,
            state.IsExpression,
            state.IsAnimated,
            state.IsCoerced,
            _valueStore?.ContainsLayer(dp, DependencyValueStore.Layer.Current) == true);
    }

    internal bool HasValueAboveInherited(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_animatedValues?.ContainsKey(dp) == true || _valueStore?.HasAnyLayer(dp) == true)
            return true;

        return false;
    }

    internal bool TryGetLayerValue(
        DependencyProperty dp,
        LayerValueSource source,
        out object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_valueStore is { } store)
            return store.TryGetLayer(dp, MapStoreLayer(source), out value, out _);

        value = null;
        return false;
    }

    internal void SetLayerValue(DependencyProperty dp, object? value, LayerValueSource source)
        => SetLayerValue(dp, value, source, allowAutoTransition: true);

    internal void SetLayerValue(DependencyProperty dp, object? value, LayerValueSource source, bool allowAutoTransition)
    {
        ArgumentNullException.ThrowIfNull(dp);
        MutateValue(
            dp,
            ValueMutation.ForSetLayer(value, source),
            notifyBinding: false,
            allowAutoTransition: allowAutoTransition);
    }

    internal void ClearLayerValue(DependencyProperty dp, LayerValueSource source)
        => ClearLayerValue(dp, source, allowAutoTransition: true);

    internal void ClearLayerValue(DependencyProperty dp, LayerValueSource source, bool allowAutoTransition)
    {
        ArgumentNullException.ThrowIfNull(dp);
        MutateValue(
            dp,
            ValueMutation.ForClearLayer(source),
            notifyBinding: false,
            allowAutoTransition: allowAutoTransition);
    }

    private ValueState GetValueState(DependencyProperty dp, bool forceCoerce = false)
    {
        var (baseValue, source) = GetUncoercedBaseValueInternal(dp);
        bool isAnimated = false;
        bool isExpression = _bindings?.ContainsKey(dp) == true;

        object? effectiveValue = baseValue;
        if (_animatedValues?.TryGetValue(dp, out var animated) == true)
        {
            effectiveValue = animated.CurrentValue;
            isAnimated = true;
        }

        bool isCoerced = false;
        var metadata = dp.GetMetadata(GetType());
        if (metadata.CoerceValueCallback != null)
        {
            var activeCoercions = t_activeCoercions ??= new HashSet<(DependencyObject owner, DependencyProperty property)>(CoercionKeyComparer.Instance);
            var coercionKey = (this, dp);
            var shouldInvokeCoerce = activeCoercions.Add(coercionKey);

            try
            {
                if (shouldInvokeCoerce)
                {
                    var coerced = metadata.CoerceValueCallback(this, effectiveValue);
                    if (forceCoerce || !Equals(coerced, effectiveValue))
                    {
                        effectiveValue = coerced;
                        isCoerced = !Equals(coerced, baseValue) || isAnimated;
                    }
                }
            }
            finally
            {
                if (shouldInvokeCoerce)
                {
                    activeCoercions.Remove(coercionKey);
                }
            }
        }

        return new ValueState(effectiveValue, source, isAnimated, isExpression, isCoerced);
    }

    internal object? GetEffectiveBaseValue(DependencyProperty dp, bool forceCoerce = false)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return GetBaseValueState(dp, forceCoerce).Value;
    }

    internal virtual (object? value, BaseValueSource source) GetUncoercedBaseValueInternal(DependencyProperty dp)
    {
        // The store maintains the winning precedence contribution as layers mutate,
        // reducing this render/layout hot path to one property-index lookup.
        if (_valueStore?.TryGetEffective(dp, out var value, out var source) == true)
            return (value, source);

        // GetEffectiveDefaultValue (not the raw metadata DefaultValue) guarantees a non-nullable
        // value-type property never resolves to null here — a DP mis-registered with a null/absent
        // default still yields a synthesized default(T) instead of crashing the getter on unbox.
        return (dp.GetEffectiveDefaultValue(GetType()), BaseValueSource.Default);
    }

    private ValueState GetBaseValueState(DependencyProperty dp, bool forceCoerce = false)
    {
        var (baseValue, source) = GetUncoercedBaseValueInternal(dp);
        bool isExpression = _bindings?.ContainsKey(dp) == true;
        object? effectiveValue = baseValue;
        bool isCoerced = false;
        PropertyMetadata metadata = dp.GetMetadata(GetType());

        if (metadata.CoerceValueCallback != null)
        {
            var activeCoercions = t_activeCoercions ??= new HashSet<(DependencyObject owner, DependencyProperty property)>(CoercionKeyComparer.Instance);
            var coercionKey = (this, dp);
            var shouldInvokeCoerce = activeCoercions.Add(coercionKey);

            try
            {
                if (shouldInvokeCoerce)
                {
                    var coerced = metadata.CoerceValueCallback(this, effectiveValue);
                    if (forceCoerce || !Equals(coerced, effectiveValue))
                    {
                        effectiveValue = coerced;
                        isCoerced = !Equals(coerced, baseValue);
                    }
                }
            }
            finally
            {
                if (shouldInvokeCoerce)
                {
                    activeCoercions.Remove(coercionKey);
                }
            }
        }

        return new ValueState(effectiveValue, source, false, isExpression, isCoerced);
    }

    private void MutateValue(DependencyProperty dp, ValueMutation mutateCore, bool notifyBinding, bool allowAutoTransition)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (allowAutoTransition && TryMutateValueWithAutomaticTransition(dp, mutateCore, notifyBinding))
            return;

        var oldValue = GetValue(dp);
        if (!mutateCore.Apply(this, dp))
            return;

        var newValue = GetValue(dp);
        if (!Equals(oldValue, newValue))
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            if (notifyBinding && _bindings?.TryGetValue(dp, out var binding) == true)
            {
                binding.UpdateSource();
            }
        }
    }

    private bool TryMutateValueWithAutomaticTransition(DependencyProperty dp, ValueMutation mutateCore, bool notifyBinding)
    {
        if (this is not UIElement uiElement ||
            !uiElement.ShouldAutomaticallyTransition(dp) ||
            uiElement.HasExplicitAnimation(dp))
        {
            return false;
        }

        var hadAutomaticTransition = uiElement.HasAutomaticTransition(dp);
        var oldDisplayedValue = GetValue(dp);
        var oldBaseValue = GetEffectiveBaseValue(dp);

        SetAnimatedValue(dp, oldDisplayedValue, holdEndValue: false);

        if (!mutateCore.Apply(this, dp))
        {
            if (!hadAutomaticTransition)
            {
                ClearAnimatedValue(dp);
            }

            return true;
        }

        var newBaseValue = GetEffectiveBaseValue(dp, forceCoerce: true);
        if (Equals(oldBaseValue, newBaseValue))
        {
            if (!hadAutomaticTransition)
            {
                ClearAnimatedValue(dp);
            }

            return true;
        }

        if (Equals(oldDisplayedValue, newBaseValue))
        {
            uiElement.StopAutomaticTransition(dp, clearAnimatedValue: false);
            ClearAnimatedValue(dp);
        }
        else if (uiElement.TryStartAutomaticTransition(dp, oldDisplayedValue, newBaseValue))
        {
            if (notifyBinding && _bindings?.TryGetValue(dp, out var binding) == true)
            {
                binding.UpdateSource();
            }

            return true;
        }
        else
        {
            uiElement.StopAutomaticTransition(dp, clearAnimatedValue: false);
            ClearAnimatedValue(dp);
        }

        if (notifyBinding && _bindings?.TryGetValue(dp, out var fallbackBinding) == true)
        {
            fallbackBinding.UpdateSource();
        }

        return true;
    }

    private void SetLocalValueCore(DependencyProperty dp, object? value)
    {
        // Local-value backstop (WPF parity): a null can never be the effective value of a non-nullable
        // value-type property — the generated CLR getter unboxes it and crashes at layout. This is the
        // canonical write path: plain SetValue AND the data-binding pipeline's coerced target write (a
        // {Binding} to a null source whose target type is absent from BindingValueCoercion's default
        // table — Color/GridLength/Duration/… — lands a boxed null here). Drop the local instead of
        // pinning the null, so resolution falls through to the registered/synthesized default. This
        // matches the layer / SetCurrentValue / promotion guards and keeps reflection out of the binding
        // hot path (the read-side GetEffectiveDefaultValue supplies the typed default).
        if (IsNullForNonNullableValueType(dp, value))
        {
            ClearLocalValueCore(dp);
            return;
        }

        if (_valueStore?.TryGetLayer(
                dp,
                DependencyValueStore.Layer.Current,
                out _,
                out var currentSource) == true && currentSource == BaseValueSource.Local)
        {
            RemoveStoredValue(dp, DependencyValueStore.Layer.Current);
        }

        SetStoredValue(dp, DependencyValueStore.Layer.Local, value);
    }

    private bool ClearLocalValueCore(DependencyProperty dp) =>
        RemoveStoredValue(dp, DependencyValueStore.Layer.Local);

    // A null can never be the effective value of a non-nullable value-type dependency property:
    // the generated CLR accessor unboxes it (e.g. (Thickness)GetValue(BorderThicknessProperty)) and
    // throws NullReferenceException during layout. Reference types and Nullable<T> accept null.
    private static bool IsNullForNonNullableValueType(DependencyProperty dp, object? value)
        => value is null && dp.PropertyType.IsValueType && Nullable.GetUnderlyingType(dp.PropertyType) is null;

    private void SetLayerValueCore(DependencyProperty dp, object? value, LayerValueSource source)
    {
        // Central backstop (WPF parity, mirroring StyleHelper's "if (!IsValidValue) value = UnsetValue"):
        // never pin a null or any other invalid value into a layer. Drop the contribution and let
        // resolution fall through to a valid lower-precedence value / the default. This guards every
        // caller that funnels a LAYER write through here — template-binding transfers, Style
        // setters/triggers, {DynamicResource}, and SetCurrentValue re-dispatch for layer base-sources —
        // and runs before the auto-transition path snapshots the value.
        // (SetCurrentValue's Default/Inherited/Local branches write _currentValues/_localValues directly,
        // bypassing this method; that null is caught at the SetCurrentValue entry instead. Local promotion
        // is guarded in PromoteLocalValuesToLayer.)
        if (IsNullForNonNullableValueType(dp, value) ||
            !dp.IsValidType(value) ||
            !dp.IsValidValue(value))
        {
            ClearLayerValueCore(dp, source);
            return;
        }

        var mappedSource = MapLayerValueSource(source);
        if (_valueStore?.TryGetLayer(
                dp,
                DependencyValueStore.Layer.Current,
                out _,
                out var currentSource) == true && currentSource == mappedSource)
        {
            RemoveStoredValue(dp, DependencyValueStore.Layer.Current);
        }

        SetStoredValue(dp, MapStoreLayer(source), value);
    }

    private bool ClearLayerValueCore(DependencyProperty dp, LayerValueSource source)
    {
        return RemoveStoredValue(dp, MapStoreLayer(source));
    }

    private void SetStoredValue(
        DependencyProperty dp,
        DependencyValueStore.Layer layer,
        object? value,
        BaseValueSource currentSource = BaseValueSource.Unknown)
    {
        (_valueStore ??= new DependencyValueStore()).SetLayer(dp, layer, value, currentSource);
    }

    private bool RemoveStoredValue(DependencyProperty dp, DependencyValueStore.Layer layer)
    {
        var store = _valueStore;
        if (store is null || !store.RemoveLayer(dp, layer))
            return false;

        if (store.Count == 0 && ReferenceEquals(_valueStore, store))
            _valueStore = null;
        return true;
    }

    private static bool RemoveStoredValue<TValue>(
        ref Dictionary<DependencyProperty, TValue>? values,
        DependencyProperty dp)
    {
        var existing = values;
        if (existing is null || !existing.Remove(dp))
            return false;

        if (existing.Count == 0 && ReferenceEquals(values, existing))
            values = null;

        return true;
    }

    private static DependencyValueStore.Layer MapStoreLayer(LayerValueSource source) => source switch
    {
        LayerValueSource.ParentTemplate => DependencyValueStore.Layer.ParentTemplate,
        LayerValueSource.StyleTrigger => DependencyValueStore.Layer.StyleTrigger,
        LayerValueSource.TemplateTrigger => DependencyValueStore.Layer.TemplateTrigger,
        LayerValueSource.StyleSetter => DependencyValueStore.Layer.StyleSetter,
        LayerValueSource.ParentTemplateTrigger => DependencyValueStore.Layer.ParentTemplateTrigger,
        LayerValueSource.CssState => DependencyValueStore.Layer.CssState,
        LayerValueSource.CssBase => DependencyValueStore.Layer.CssBase,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private static BaseValueSource MapLayerValueSource(LayerValueSource source) =>
        source switch
        {
            LayerValueSource.ParentTemplate => BaseValueSource.ParentTemplate,
            LayerValueSource.StyleTrigger => BaseValueSource.StyleTrigger,
            LayerValueSource.TemplateTrigger => BaseValueSource.TemplateTrigger,
            LayerValueSource.StyleSetter => BaseValueSource.Style,
            LayerValueSource.ParentTemplateTrigger => BaseValueSource.ParentTemplateTrigger,
            LayerValueSource.CssState => BaseValueSource.StyleTrigger,
            LayerValueSource.CssBase => BaseValueSource.Style,
            _ => BaseValueSource.Unknown
        };

    #endregion

    #region Freezable clone / freeze support

    // WPF 的 Freezable.CloneCore 只复制"本地设置"(local) 的基值（ReadLocalValue 对
    // style/trigger/inherited 返回 UnsetValue 而被跳过）。Freezable 子类(Brush/Geometry/
    // Transform…) 的属性几乎都经 CLR 包装器 SetValue 落到 _localValues，因此 base 克隆
    // 枚举 _localValues 即与 WPF 行为一致。返回 (dp, 原始 base 值) 包含显式 null。
    internal KeyValuePair<DependencyProperty, object?>[] GetLocalValueEntriesInternal()
        => _valueStore?.SnapshotLayer(DependencyValueStore.Layer.Local)
           ?? Array.Empty<KeyValuePair<DependencyProperty, object?>>();

    // CloneCurrentValue / FreezeCore 需要"所有高于默认值的有效属性"集合，对应 WPF 的
    // EffectiveValues 数组（不含纯默认值）。排除纯 Inherited 值，避免把继承值烤进克隆体
    // （Freezable 极少参与属性继承），但保留 SetCurrentValue 写出的 modified-default。
    internal DependencyProperty[] GetEffectiveSetPropertiesInternal()
    {
        var set = new HashSet<DependencyProperty>(
            _valueStore?.SnapshotEffectiveProperties() ?? Array.Empty<DependencyProperty>());
        if (_animatedValues is not null)
            foreach (var k in _animatedValues.Keys) set.Add(k);
        var result = new DependencyProperty[set.Count];
        set.CopyTo(result);
        return result;
    }

    // True 当该属性绑定了表达式（WPF 中表达式不可冻结、且 base 克隆需特殊复制）。
    internal bool HasBindingInternal(DependencyProperty dp) => _bindings?.ContainsKey(dp) == true;

    internal IReadOnlyCollection<BindingExpressionBase> GetBindingExpressionsInternal() =>
        _bindings?.Values.ToArray() ?? Array.Empty<BindingExpressionBase>();

    /// <summary>
    /// 值写入守卫钩子。基类不封闭；Freezable 冻结后重写为抛异常，使属性系统层面（而非
    /// 仅靠个别派生 setter 调用 WritePreamble）即可拒绝对冻结对象的任何写入 —— 对齐 WPF
    /// 中 SetValue/ClearValue 对冻结 Freezable 直接抛 InvalidOperationException 的语义。
    /// </summary>
    private protected virtual void CheckSealedAccess()
    {
    }

    #endregion
}

/// <summary>
/// Provides a static helper method for setting bindings.
/// </summary>
internal static class LegacyBindingOperations
{
    /// <summary>
    /// Sets a binding on a dependency property.
    /// </summary>
    /// <param name="target">The target object.</param>
    /// <param name="dp">The dependency property to bind.</param>
    /// <param name="binding">The binding to set.</param>
    /// <returns>The binding expression for the binding.</returns>
    public static BindingExpressionBase SetBinding(DependencyObject target, DependencyProperty dp, BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.SetBinding(dp, binding);
    }

    /// <summary>
    /// Gets the binding expression for a dependency property.
    /// </summary>
    /// <param name="target">The target object.</param>
    /// <param name="dp">The dependency property.</param>
    /// <returns>The binding expression, or null if the property is not bound.</returns>
    public static BindingExpressionBase? GetBindingExpression(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetBindingExpression(dp);
    }

    /// <summary>
    /// Removes the binding from a dependency property.
    /// </summary>
    /// <param name="target">The target object.</param>
    /// <param name="dp">The dependency property to unbind.</param>
    public static void ClearBinding(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ClearBinding(dp);
    }

    /// <summary>
    /// Removes all bindings from an object.
    /// </summary>
    /// <param name="target">The target object.</param>
    public static void ClearAllBindings(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ClearAllBindings();
    }
}
