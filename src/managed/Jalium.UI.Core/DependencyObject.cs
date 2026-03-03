namespace Jalium.UI;

/// <summary>
/// Represents an object that participates in the dependency property system.
/// This is the base class for all objects that support dependency properties.
/// </summary>
public class DependencyObject : DispatcherObject
{
    private readonly Dictionary<DependencyProperty, object?> _localValues = new();
    private readonly Dictionary<DependencyProperty, object?> _parentTemplateValues = new();
    private readonly Dictionary<DependencyProperty, object?> _styleTriggerValues = new();
    private readonly Dictionary<DependencyProperty, object?> _templateTriggerValues = new();
    private readonly Dictionary<DependencyProperty, object?> _styleSetterValues = new();
    private readonly Dictionary<DependencyProperty, (object? value, BaseValueSource source)> _currentValues = new();
    private readonly Dictionary<DependencyProperty, BindingExpressionBase> _bindings = new();
    private readonly Dictionary<DependencyProperty, AnimatedPropertyValue> _animatedValues = new();

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

    internal enum LayerValueSource
    {
        ParentTemplate,
        StyleTrigger,
        TemplateTrigger,
        StyleSetter
    }

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
    public object ReadLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_localValues.TryGetValue(dp, out var value))
        {
            return value ?? DependencyProperty.UnsetValue;
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
        var oldValue = GetValue(dp);
        if (_currentValues.TryGetValue(dp, out var current) && current.source == BaseValueSource.Local)
            _currentValues.Remove(dp);
        _localValues[dp] = value;
        var newValue = GetValue(dp);

        if (!Equals(oldValue, newValue))
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));

            // Notify binding for TwoWay/OneWayToSource
            if (_bindings.TryGetValue(dp, out var binding))
            {
                binding.UpdateSource();
            }
        }
    }

    /// <summary>
    /// Sets the current value of a dependency property without forcing local-value precedence.
    /// </summary>
    /// <param name="dp">The dependency property to set.</param>
    /// <param name="value">The new value.</param>
    public void SetCurrentValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var source = GetValueSourceInternal(dp);
        if (source.IsAnimated && _animatedValues.TryGetValue(dp, out var animated))
        {
            var oldValue = GetValue(dp);
            _animatedValues[dp] = animated with { CurrentValue = value };
            var newValue = GetValue(dp);
            if (!Equals(oldValue, newValue))
                OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            return;
        }

        SetCurrentValueForSource(dp, value, source.BaseValueSource);
    }

    private void SetCurrentValueForSource(DependencyProperty dp, object? value, BaseValueSource baseSource)
    {
        switch (baseSource)
        {
            case BaseValueSource.Local:
                {
                    var oldValue = GetValue(dp);
                    _localValues[dp] = value;
                    var newValue = GetValue(dp);
                    if (!Equals(oldValue, newValue))
                        OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
                    return;
                }
            case BaseValueSource.ParentTemplate:
                SetLayerValue(dp, value, LayerValueSource.ParentTemplate);
                return;
            case BaseValueSource.StyleTrigger:
                SetLayerValue(dp, value, LayerValueSource.StyleTrigger);
                return;
            case BaseValueSource.TemplateTrigger:
            case BaseValueSource.ParentTemplateTrigger:
                SetLayerValue(dp, value, LayerValueSource.TemplateTrigger);
                return;
            case BaseValueSource.Style:
            case BaseValueSource.DefaultStyle:
                SetLayerValue(dp, value, LayerValueSource.StyleSetter);
                return;
            case BaseValueSource.Default:
            case BaseValueSource.Inherited:
                // Preserve no-local semantics: keep this as a lightweight current-value layer.
                {
                    var oldValue = GetValue(dp);
                    var keepSource = baseSource == BaseValueSource.Inherited
                        ? BaseValueSource.Inherited
                        : BaseValueSource.Default;
                    _currentValues[dp] = (value, keepSource);
                    var newValue = GetValue(dp);
                    if (!Equals(oldValue, newValue))
                        OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
                    return;
                }
            default:
                {
                    var oldValue = GetValue(dp);
                    _localValues[dp] = value;
                    var newValue = GetValue(dp);
                    if (!Equals(oldValue, newValue))
                        OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
                }
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
    public BindingExpressionBase? GetBindingExpression(DependencyProperty dp)
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
        var oldValue = GetValue(dp);
        if (!_localValues.Remove(dp))
            return;

        var newValue = GetValue(dp);
        if (!Equals(oldValue, newValue))
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
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
        e.Property.DefaultMetadata.PropertyChangedCallback?.Invoke(this, e);

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
    internal void SetAnimatedValue(DependencyProperty dp, object? value, bool holdEndValue)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var oldValue = GetValue(dp);

        if (!_animatedValues.ContainsKey(dp))
        {
            // Store base value for restoration when animation ends
            var (baseValue, baseSource) = GetUncoercedBaseValue(dp);
            _animatedValues[dp] = new AnimatedPropertyValue(baseValue, baseSource, value, holdEndValue);
        }
        else
        {
            var existing = _animatedValues[dp];
            _animatedValues[dp] = existing with { CurrentValue = value, HoldEndValue = holdEndValue };
        }

        if (!Equals(oldValue, value))
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, value));

            // Ensure dirty rect tracking for animated properties.
            // OnPropertyChanged fires the metadata callback which MAY call InvalidateVisual,
            // but properties without explicit callbacks (e.g., Opacity) would be missed.
            // AddDirtyElement deduplicates, so double-calls are harmless.
            if (this is UIElement uiElement)
            {
                uiElement.InvalidateVisual();
            }
        }
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
        return new ValueSource(state.BaseValueSource, state.IsExpression, state.IsAnimated, state.IsCoerced);
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
    {
        ArgumentNullException.ThrowIfNull(dp);

        var oldValue = GetValue(dp);
        var mappedSource = MapLayerValueSource(source);
        if (_currentValues.TryGetValue(dp, out var current) && current.source == mappedSource)
            _currentValues.Remove(dp);
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

        var newValue = GetValue(dp);
        if (!Equals(oldValue, newValue))
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
    }

    internal void ClearLayerValue(DependencyProperty dp, LayerValueSource source)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var oldValue = GetValue(dp);
        bool removed = source switch
        {
            LayerValueSource.ParentTemplate => _parentTemplateValues.Remove(dp),
            LayerValueSource.StyleTrigger => _styleTriggerValues.Remove(dp),
            LayerValueSource.TemplateTrigger => _templateTriggerValues.Remove(dp),
            LayerValueSource.StyleSetter => _styleSetterValues.Remove(dp),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

        if (!removed)
            return;

        var newValue = GetValue(dp);
        if (!Equals(oldValue, newValue))
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
    }

    private ValueState GetValueState(DependencyProperty dp, bool forceCoerce = false)
    {
        var (baseValue, source) = GetUncoercedBaseValue(dp);
        bool isAnimated = false;
        bool isExpression = _bindings.ContainsKey(dp);

        object? effectiveValue = baseValue;
        if (_animatedValues.TryGetValue(dp, out var animated))
        {
            effectiveValue = animated.CurrentValue;
            isAnimated = true;
        }

        bool isCoerced = false;
        if (dp.DefaultMetadata.CoerceValueCallback != null)
        {
            var coerced = dp.DefaultMetadata.CoerceValueCallback(this, effectiveValue);
            if (forceCoerce || !Equals(coerced, effectiveValue))
            {
                effectiveValue = coerced;
                isCoerced = !Equals(coerced, baseValue) || isAnimated;
            }
        }

        return new ValueState(effectiveValue, source, isAnimated, isExpression, isCoerced);
    }

    private (object? value, BaseValueSource source) GetUncoercedBaseValue(DependencyProperty dp)
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

        return (dp.DefaultMetadata.DefaultValue, BaseValueSource.Default);
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
}

/// <summary>
/// Provides a static helper method for setting bindings.
/// </summary>
public static class BindingOperations
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
