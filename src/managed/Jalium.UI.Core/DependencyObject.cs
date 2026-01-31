namespace Jalium.UI;

/// <summary>
/// Represents an object that participates in the dependency property system.
/// This is the base class for all objects that support dependency properties.
/// </summary>
public class DependencyObject
{
    private readonly Dictionary<DependencyProperty, object?> _values = new();
    private readonly Dictionary<DependencyProperty, object?> _localValues = new();
    private readonly Dictionary<DependencyProperty, BindingExpressionBase> _bindings = new();

    /// <summary>
    /// Internal event for property change notification used by triggers.
    /// </summary>
    internal event Action<DependencyProperty, object?, object?>? PropertyChangedInternal;

    /// <summary>
    /// Gets the current effective value of a dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to get.</param>
    /// <returns>The current effective value.</returns>
    public virtual object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (_values.TryGetValue(dp, out var value))
        {
            return value;
        }

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
