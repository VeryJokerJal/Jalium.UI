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

    private readonly Dictionary<DependencyProperty, object?> _localValues = new();
    private readonly Dictionary<DependencyProperty, object?> _parentTemplateValues = new();
    private readonly Dictionary<DependencyProperty, object?> _styleTriggerValues = new();
    private readonly Dictionary<DependencyProperty, object?> _templateTriggerValues = new();
    private readonly Dictionary<DependencyProperty, object?> _styleSetterValues = new();
    private readonly Dictionary<DependencyProperty, (object? value, BaseValueSource source)> _currentValues = new();
    private readonly Dictionary<DependencyProperty, BindingExpressionBase> _bindings = new();
    private readonly Dictionary<DependencyProperty, AnimatedPropertyValue> _animatedValues = new();

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

    internal enum LayerValueSource
    {
        ParentTemplate,
        StyleTrigger,
        TemplateTrigger,
        StyleSetter
    }

    private delegate bool ValueMutation();

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
        return GetValueState(dp).Value;
    }

    /// <summary>
    /// Gets a value indicating whether this object has a local value set for the specified property.
    /// </summary>
    /// <param name="dp">The dependency property to check.</param>
    /// <returns>True if a local value is set; otherwise, false.</returns>
    public bool HasLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _localValues.ContainsKey(dp);
    }

    /// <summary>
    /// Returns the local value of a dependency property, if a local value is set.
    /// </summary>
    /// <param name="dp">The dependency property to read.</param>
    /// <returns>The local value, or DependencyProperty.UnsetValue if no local value is set.</returns>
    public object? ReadLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_localValues.TryGetValue(dp, out var value))
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
            () =>
            {
                SetLocalValueCore(dp, value);
                return true;
            },
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

        var source = GetValueSourceInternal(dp);
        SetCurrentValueForSource(dp, value, source.BaseValueSource, allowAutoTransition: true);
    }

    private static void ValidateValueOrThrow(DependencyProperty dp, object? value)
    {
        // Mirrors WPF: the per-property ValidateValueCallback (registered via the
        // Register/RegisterAttached overloads that take a validator) gates every
        // public write of the value. Enum-typed attached properties such as
        // TextOptions.TextRenderingMode use it to reject out-of-range members so an
        // illegal value never reaches the render pipeline.
        if (!dp.IsValidValue(value))
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
                    () =>
                    {
                        _localValues[dp] = value;
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.ParentTemplate:
                MutateValue(
                    dp,
                    () =>
                    {
                        SetLayerValueCore(dp, value, LayerValueSource.ParentTemplate);
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.StyleTrigger:
                MutateValue(
                    dp,
                    () =>
                    {
                        SetLayerValueCore(dp, value, LayerValueSource.StyleTrigger);
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.TemplateTrigger:
            case BaseValueSource.ParentTemplateTrigger:
                MutateValue(
                    dp,
                    () =>
                    {
                        SetLayerValueCore(dp, value, LayerValueSource.TemplateTrigger);
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.Style:
            case BaseValueSource.DefaultStyle:
                MutateValue(
                    dp,
                    () =>
                    {
                        SetLayerValueCore(dp, value, LayerValueSource.StyleSetter);
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            case BaseValueSource.Default:
            case BaseValueSource.Inherited:
                MutateValue(
                    dp,
                    () =>
                    {
                        var keepSource = baseSource == BaseValueSource.Inherited
                            ? BaseValueSource.Inherited
                            : BaseValueSource.Default;
                        _currentValues[dp] = (value, keepSource);
                        return true;
                    },
                    notifyBinding: false,
                    allowAutoTransition);
                return;
            default:
                MutateValue(
                    dp,
                    () =>
                    {
                        _localValues[dp] = value;
                        return true;
                    },
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
        _bindings[dp] = expression;
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
        return _bindings.GetValueOrDefault(dp);
    }

    /// <summary>
    /// Removes the binding from a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to unbind.</param>
    public void ClearBinding(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_bindings.TryGetValue(dp, out var expression))
        {
            expression.Deactivate();
            _bindings.Remove(dp);
        }
    }

    /// <summary>
    /// Removes all bindings from this object.
    /// </summary>
    public void ClearAllBindings()
    {
        foreach (var expression in _bindings.Values)
        {
            expression.Deactivate();
        }
        _bindings.Clear();
    }

    /// <summary>
    /// Reactivates all bindings on this object.
    /// This is called when the TemplatedParent is set to allow deferred template bindings to resolve.
    /// </summary>
    internal void ReactivateBindings()
    {
        foreach (var expression in _bindings.Values)
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
            () => ClearLocalValueCore(dp),
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
        var entries = _localValues
            .Select(static pair => new LocalValueEntry(pair.Key, pair.Value))
            .ToArray();
        return new LocalValueEnumerator(entries);
    }

    /// <summary>
    /// Re-evaluates a dependency property's binding and coercion.
    /// </summary>
    public void InvalidateProperty(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_bindings.TryGetValue(dp, out BindingExpressionBase? binding))
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
        return _localValues.ContainsKey(dp);
    }

    /// <summary>
    /// Moves local values to the specified non-local layer.
    /// Used when applying template-generated trees so template defaults do not block template triggers.
    /// </summary>
    internal void PromoteLocalValuesToLayer(LayerValueSource source)
    {
        if (_localValues.Count == 0)
            return;

        var mappedSource = MapLayerValueSource(source);
        var entries = _localValues.ToArray();

        foreach (var (dp, localValue) in entries)
        {
            var oldValue = GetValue(dp);

            _localValues.Remove(dp);
            if (_currentValues.TryGetValue(dp, out var current) && current.source == mappedSource)
                _currentValues.Remove(dp);

            // Never promote a null local onto a non-nullable value-type layer (mirrors the
            // SetLayerValueCore backstop, which this direct-write loop bypasses): drop it so the
            // property falls through to its synthesized/registered default instead of unbox-crashing at
            // layout. The local was already removed above, so skipping the write degrades to default; the
            // change notification below still fires for the null -> default transition.
            if (!IsNullForNonNullableValueType(dp, localValue))
            {
                switch (source)
                {
                    case LayerValueSource.ParentTemplate:
                        _parentTemplateValues[dp] = localValue;
                        break;
                    case LayerValueSource.StyleTrigger:
                        _styleTriggerValues[dp] = localValue;
                        break;
                    case LayerValueSource.TemplateTrigger:
                        _templateTriggerValues[dp] = localValue;
                        break;
                    case LayerValueSource.StyleSetter:
                        _styleSetterValues[dp] = localValue;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(source), source, null);
                }
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

        if (!_animatedValues.ContainsKey(dp))
        {
            // Store base value for restoration when animation ends
            var (baseValue, baseSource) = GetUncoercedBaseValueInternal(dp);
            _animatedValues[dp] = new AnimatedPropertyValue(baseValue, baseSource, value, holdEndValue);
        }
        else
        {
            var existing = _animatedValues[dp];
            _animatedValues[dp] = existing with { CurrentValue = value, HoldEndValue = holdEndValue };
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

        if (_animatedValues.TryGetValue(dp, out var animated))
        {
            var oldValue = animated.CurrentValue;
            _animatedValues.Remove(dp);

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

        if (_animatedValues.TryGetValue(dp, out var animated))
        {
            var oldValue = animated.CurrentValue;
            _animatedValues.Remove(dp);

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
        if (_animatedValues.Count == 0)
            return;

        var keys = new DependencyProperty[_animatedValues.Count];
        _animatedValues.Keys.CopyTo(keys, 0);

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
        return _animatedValues.ContainsKey(dp);
    }

    /// <summary>
    /// Gets the base value (before animation) for a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property.</param>
    /// <returns>The base value, or the current effective value if not animated.</returns>
    internal object? GetAnimationBaseValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_animatedValues.TryGetValue(dp, out var animated))
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
            _currentValues.ContainsKey(dp));
    }

    internal bool HasValueAboveInherited(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_animatedValues.ContainsKey(dp) || _localValues.ContainsKey(dp))
            return true;

        return _parentTemplateValues.ContainsKey(dp)
               || _styleTriggerValues.ContainsKey(dp)
               || _templateTriggerValues.ContainsKey(dp)
               || _styleSetterValues.ContainsKey(dp)
               || _currentValues.ContainsKey(dp);
    }

    internal void SetLayerValue(DependencyProperty dp, object? value, LayerValueSource source)
        => SetLayerValue(dp, value, source, allowAutoTransition: true);

    internal void SetLayerValue(DependencyProperty dp, object? value, LayerValueSource source, bool allowAutoTransition)
    {
        ArgumentNullException.ThrowIfNull(dp);
        MutateValue(
            dp,
            () =>
            {
                SetLayerValueCore(dp, value, source);
                return true;
            },
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
            () => ClearLayerValueCore(dp, source),
            notifyBinding: false,
            allowAutoTransition: allowAutoTransition);
    }

    private ValueState GetValueState(DependencyProperty dp, bool forceCoerce = false)
    {
        var (baseValue, source) = GetUncoercedBaseValueInternal(dp);
        bool isAnimated = false;
        bool isExpression = _bindings.ContainsKey(dp);

        object? effectiveValue = baseValue;
        if (_animatedValues.TryGetValue(dp, out var animated))
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
        if (_localValues.TryGetValue(dp, out var local))
            return (local, BaseValueSource.Local);

        if (_templateTriggerValues.TryGetValue(dp, out var templateTrigger))
            return (templateTrigger, BaseValueSource.TemplateTrigger);

        if (_styleTriggerValues.TryGetValue(dp, out var styleTrigger))
            return (styleTrigger, BaseValueSource.StyleTrigger);

        if (_parentTemplateValues.TryGetValue(dp, out var parentTemplate))
            return (parentTemplate, BaseValueSource.ParentTemplate);

        if (_styleSetterValues.TryGetValue(dp, out var styleSetter))
            return (styleSetter, BaseValueSource.Style);

        if (_currentValues.TryGetValue(dp, out var current))
            return current;

        // GetEffectiveDefaultValue (not the raw metadata DefaultValue) guarantees a non-nullable
        // value-type property never resolves to null here — a DP mis-registered with a null/absent
        // default still yields a synthesized default(T) instead of crashing the getter on unbox.
        return (dp.GetEffectiveDefaultValue(GetType()), BaseValueSource.Default);
    }

    private ValueState GetBaseValueState(DependencyProperty dp, bool forceCoerce = false)
    {
        var (baseValue, source) = GetUncoercedBaseValueInternal(dp);
        bool isExpression = _bindings.ContainsKey(dp);
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
        ArgumentNullException.ThrowIfNull(mutateCore);

        if (allowAutoTransition && TryMutateValueWithAutomaticTransition(dp, mutateCore, notifyBinding))
            return;

        var oldValue = GetValue(dp);
        if (!mutateCore())
            return;

        var newValue = GetValue(dp);
        if (!Equals(oldValue, newValue))
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            if (notifyBinding && _bindings.TryGetValue(dp, out var binding))
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

        if (!mutateCore())
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
            if (notifyBinding && _bindings.TryGetValue(dp, out var binding))
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

        if (notifyBinding && _bindings.TryGetValue(dp, out var fallbackBinding))
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

        if (_currentValues.TryGetValue(dp, out var current) && current.source == BaseValueSource.Local)
        {
            _currentValues.Remove(dp);
        }

        _localValues[dp] = value;
    }

    private bool ClearLocalValueCore(DependencyProperty dp) => _localValues.Remove(dp);

    // A null can never be the effective value of a non-nullable value-type dependency property:
    // the generated CLR accessor unboxes it (e.g. (Thickness)GetValue(BorderThicknessProperty)) and
    // throws NullReferenceException during layout. Reference types and Nullable<T> accept null.
    private static bool IsNullForNonNullableValueType(DependencyProperty dp, object? value)
        => value is null && dp.PropertyType.IsValueType && Nullable.GetUnderlyingType(dp.PropertyType) is null;

    private void SetLayerValueCore(DependencyProperty dp, object? value, LayerValueSource source)
    {
        // Central backstop (WPF parity, mirroring StyleHelper's "if (!IsValidValue) value = UnsetValue"):
        // never pin a null into a non-nullable value-type layer. No legitimate writer needs to — doing
        // so shadows the registered default and crashes on unbox — so degrade it to "no contribution":
        // drop any existing value at this layer and let resolution fall through to a valid
        // lower-precedence value / the default. This guards every caller that funnels a LAYER write
        // through here — template-binding transfers, Style setters/triggers, {DynamicResource}, and the
        // SetCurrentValue re-dispatch for layer base-sources — and runs before the auto-transition path
        // ever snapshots a value-type base value, so the animator never interpolates to/from null.
        // (SetCurrentValue's Default/Inherited/Local branches write _currentValues/_localValues directly,
        // bypassing this method; that null is caught at the SetCurrentValue entry instead. Local promotion
        // is guarded in PromoteLocalValuesToLayer.)
        if (IsNullForNonNullableValueType(dp, value))
        {
            ClearLayerValueCore(dp, source);
            return;
        }

        var mappedSource = MapLayerValueSource(source);
        if (_currentValues.TryGetValue(dp, out var current) && current.source == mappedSource)
        {
            _currentValues.Remove(dp);
        }

        switch (source)
        {
            case LayerValueSource.ParentTemplate:
                _parentTemplateValues[dp] = value;
                break;
            case LayerValueSource.StyleTrigger:
                _styleTriggerValues[dp] = value;
                break;
            case LayerValueSource.TemplateTrigger:
                _templateTriggerValues[dp] = value;
                break;
            case LayerValueSource.StyleSetter:
                _styleSetterValues[dp] = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }
    }

    private bool ClearLayerValueCore(DependencyProperty dp, LayerValueSource source)
    {
        return source switch
        {
            LayerValueSource.ParentTemplate => _parentTemplateValues.Remove(dp),
            LayerValueSource.StyleTrigger => _styleTriggerValues.Remove(dp),
            LayerValueSource.TemplateTrigger => _templateTriggerValues.Remove(dp),
            LayerValueSource.StyleSetter => _styleSetterValues.Remove(dp),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static BaseValueSource MapLayerValueSource(LayerValueSource source) =>
        source switch
        {
            LayerValueSource.ParentTemplate => BaseValueSource.ParentTemplate,
            LayerValueSource.StyleTrigger => BaseValueSource.StyleTrigger,
            LayerValueSource.TemplateTrigger => BaseValueSource.TemplateTrigger,
            LayerValueSource.StyleSetter => BaseValueSource.Style,
            _ => BaseValueSource.Unknown
        };

    #endregion

    #region Freezable clone / freeze support

    // WPF 的 Freezable.CloneCore 只复制"本地设置"(local) 的基值（ReadLocalValue 对
    // style/trigger/inherited 返回 UnsetValue 而被跳过）。Freezable 子类(Brush/Geometry/
    // Transform…) 的属性几乎都经 CLR 包装器 SetValue 落到 _localValues，因此 base 克隆
    // 枚举 _localValues 即与 WPF 行为一致。返回 (dp, 原始 base 值) 包含显式 null。
    internal KeyValuePair<DependencyProperty, object?>[] GetLocalValueEntriesInternal()
        => _localValues.ToArray();

    // CloneCurrentValue / FreezeCore 需要"所有高于默认值的有效属性"集合，对应 WPF 的
    // EffectiveValues 数组（不含纯默认值）。排除纯 Inherited 值，避免把继承值烤进克隆体
    // （Freezable 极少参与属性继承），但保留 SetCurrentValue 写出的 modified-default。
    internal DependencyProperty[] GetEffectiveSetPropertiesInternal()
    {
        var set = new HashSet<DependencyProperty>();
        foreach (var k in _localValues.Keys) set.Add(k);
        foreach (var k in _parentTemplateValues.Keys) set.Add(k);
        foreach (var k in _styleTriggerValues.Keys) set.Add(k);
        foreach (var k in _templateTriggerValues.Keys) set.Add(k);
        foreach (var k in _styleSetterValues.Keys) set.Add(k);
        foreach (var kv in _currentValues)
        {
            if (kv.Value.source != BaseValueSource.Inherited)
                set.Add(kv.Key);
        }
        foreach (var k in _animatedValues.Keys) set.Add(k);
        var result = new DependencyProperty[set.Count];
        set.CopyTo(result);
        return result;
    }

    // True 当该属性绑定了表达式（WPF 中表达式不可冻结、且 base 克隆需特殊复制）。
    internal bool HasBindingInternal(DependencyProperty dp) => _bindings.ContainsKey(dp);

    internal IReadOnlyCollection<BindingExpressionBase> GetBindingExpressionsInternal() =>
        _bindings.Values.ToArray();

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
