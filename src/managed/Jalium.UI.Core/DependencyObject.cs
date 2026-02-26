namespace Jalium.UI;

/// <summary>
/// Represents an object that participates in the dependency property system.
/// This is the base class for all objects that support dependency properties.
/// </summary>
public class DependencyObject : DispatcherObject
{
    private readonly Dictionary<DependencyProperty, object?> _values = new();
    private readonly Dictionary<DependencyProperty, object?> _localValues = new();
    private readonly Dictionary<DependencyProperty, BindingExpressionBase> _bindings = new();
    private readonly Dictionary<DependencyProperty, AnimatedPropertyValue> _animatedValues = new();

    /// <summary>
    /// Internal record to track animated property values.
    /// </summary>
    internal record AnimatedPropertyValue(
        object? BaseValue,       // Value before animation started
        object? CurrentValue,    // Current animated value
        bool HoldEndValue);      // Whether to hold the final value after animation ends

    /// <summary>
    /// Internal event for property change notification used by triggers.
    /// </summary>
    internal event Action<DependencyProperty, object?, object?>? PropertyChangedInternal;

    /// <summary>
    /// Gets the current effective value of a dependency property.
    /// Value precedence: Animation > Local > Binding > Default
    /// </summary>
    /// <param name="dp">The dependency property to get.</param>
    /// <returns>The current effective value.</returns>
    public virtual object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        // 1. Animated values have highest precedence
        if (_animatedValues.TryGetValue(dp, out var animated))
        {
            return animated.CurrentValue;
        }

        // 2. Local values and binding values (stored in _values)
        if (_values.TryGetValue(dp, out var value))
        {
            return value;
        }

        // 3. Default value
        return dp.DefaultMetadata.DefaultValue;
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

        // Apply coercion if a CoerceValueCallback is defined
        var coercedValue = value;
        if (dp.DefaultMetadata.CoerceValueCallback != null)
        {
            coercedValue = dp.DefaultMetadata.CoerceValueCallback(this, value);
        }

        var oldValue = GetValue(dp);

        _localValues[dp] = coercedValue;
        _values[dp] = coercedValue;

        if (!Equals(oldValue, coercedValue))
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, coercedValue));

            // Notify binding for TwoWay/OneWayToSource
            if (_bindings.TryGetValue(dp, out var binding))
            {
                binding.UpdateSource();
            }
        }
    }

    /// <summary>
    /// Forces re-evaluation of a dependency property's value, including coercion.
    /// </summary>
    /// <param name="dp">The dependency property to coerce.</param>
    public void CoerceValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        // Get the current local value (or default if not set)
        var baseValue = _localValues.TryGetValue(dp, out var localValue)
            ? localValue
            : dp.DefaultMetadata.DefaultValue;

        // Re-apply SetValue which will trigger coercion
        SetValue(dp, baseValue);
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

        if (_localValues.Remove(dp))
        {
            var oldValue = _values.GetValueOrDefault(dp);
            _values.Remove(dp);

            var newValue = dp.DefaultMetadata.DefaultValue;
            if (!Equals(oldValue, newValue))
            {
                OnPropertyChanged(new DependencyPropertyChangedEventArgs(dp, oldValue, newValue));
            }
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
            var baseValue = _localValues.TryGetValue(dp, out var local)
                ? local
                : dp.DefaultMetadata.DefaultValue;
            _animatedValues[dp] = new AnimatedPropertyValue(baseValue, value, holdEndValue);
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
                // HoldEnd: The animated value becomes the new effective value
                // We store it in _values so GetValue() returns it
                _values[dp] = oldValue;
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
