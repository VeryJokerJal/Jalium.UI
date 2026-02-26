using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI;

/// <summary>
/// Contains property setters that can be shared between instances of a type.
/// </summary>
[ContentProperty("Setters")]
public sealed class Style
{
    private readonly List<Setter> _setters = new();
    private readonly List<EventSetter> _eventSetters = new();
    private readonly List<Trigger> _triggers = new();
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type for which this style is intended.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <summary>
    /// Gets or sets a style that is the basis of the current style.
    /// </summary>
    public Style? BasedOn { get; set; }

    /// <summary>
    /// Gets the collection of property setters.
    /// </summary>
    public IList<Setter> Setters => _setters;

    /// <summary>
    /// Gets the collection of event setters.
    /// </summary>
    public IList<EventSetter> EventSetters => _eventSetters;

    /// <summary>
    /// Gets the collection of triggers.
    /// </summary>
    public IList<Trigger> Triggers => _triggers;

    /// <summary>
    /// Gets a value that indicates whether the style is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class.
    /// </summary>
    public Style()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    public Style(Type targetType)
    {
        TargetType = targetType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type and base style.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    /// <param name="basedOn">A style that is the basis of the current style.</param>
    public Style(Type targetType, Style? basedOn)
    {
        TargetType = targetType;
        BasedOn = basedOn;
    }

    /// <summary>
    /// Seals the style so that it can no longer be modified.
    /// </summary>
    public void Seal()
    {
        _isSealed = true;
    }

    /// <summary>
    /// Applies this style to the specified framework element.
    /// </summary>
    /// <param name="element">The element to apply the style to.</param>
    internal void Apply(FrameworkElement element)
    {
        Apply(element, null);
    }

    private void Apply(FrameworkElement element, HashSet<Style>? visited)
    {
        // Apply base style first (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Apply(element, visited);
        }

        // Apply setters
        foreach (var setter in _setters)
        {
            setter.Apply(element);
        }

        // Apply event setters
        foreach (var eventSetter in _eventSetters)
        {
            eventSetter.Apply(element);
        }

        // Apply triggers
        foreach (var trigger in _triggers)
        {
            trigger.ParentStyle = this;
            trigger.Attach(element);
        }
    }

    /// <summary>
    /// Removes this style from the specified framework element.
    /// </summary>
    /// <param name="element">The element to remove the style from.</param>
    internal void Remove(FrameworkElement element)
    {
        Remove(element, null);
    }

    private void Remove(FrameworkElement element, HashSet<Style>? visited)
    {
        // Remove triggers
        foreach (var trigger in _triggers)
        {
            trigger.Detach(element);
        }

        // Remove event setters
        foreach (var eventSetter in _eventSetters)
        {
            eventSetter.Remove(element);
        }

        // Remove setters (in reverse order)
        for (int i = _setters.Count - 1; i >= 0; i--)
        {
            _setters[i].Remove(element);
        }

        // Remove base style (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Remove(element, visited);
        }
    }
}

/// <summary>
/// Represents a setter that sets a property value.
/// </summary>
[ContentProperty("Value")]
public sealed class Setter
{
    /// <summary>
    /// Gets or sets the property to set.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value to set.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to apply the setter to.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// Gets or sets the unresolved property name for deferred resolution.
    /// When a Setter has a TargetName pointing to a different element type than the Style's TargetType,
    /// the DependencyProperty cannot be resolved at parse time. The property name is stored here
    /// and resolved at runtime against the actual target element type.
    /// </summary>
    public string? PropertyName { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class.
    /// </summary>
    public Setter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class with the specified property and value.
    /// </summary>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value to set.</param>
    public Setter(DependencyProperty property, object? value)
    {
        Property = property;
        Value = value;
    }

    /// <summary>
    /// Applies this setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        var target = GetTarget(element);
        if (target == null)
            return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null)
            return;

        // Resolve the actual property on the target type
        // This handles the case where the property was resolved against the Style's TargetType
        // but the setter targets a different element type via TargetName
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null)
            return;

        // Don't override local values - WPF style behavior
        // Local values have higher precedence than style values
        if (target.HasLocalValue(actualProperty))
            return;

        // Store original value for restoration
        if (!target._styleOriginalValues.ContainsKey(actualProperty))
        {
            target._styleOriginalValues[actualProperty] = target.GetValue(actualProperty);
        }

        if (Value is IDynamicResourceReference dynamicReference)
        {
            DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey);
            return;
        }

        // Convert value to the correct type if needed
        var valueToSet = ConvertValueIfNeeded(Value, actualProperty.PropertyType);
        target.SetValue(actualProperty, valueToSet);
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    private static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // String to target type conversion
        if (value is string stringValue)
        {
            if (targetType == typeof(double))
                return double.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return float.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (targetType == typeof(int))
                return int.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (targetType == typeof(bool))
                return bool.Parse(stringValue);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, stringValue, ignoreCase: true);
            if (targetType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (targetType == typeof(Thickness))
                return ParseThickness(stringValue);
        }

        return value;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes this setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        var target = GetTarget(element);
        if (target == null) return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null) return;

        // Resolve the actual property on the target type
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null) return;

        DynamicResourceBindingOperations.ClearDynamicResource(target, actualProperty);

        if (target._styleOriginalValues.Remove(actualProperty))
        {
            // Use ClearValue instead of SetValue to restore the property.
            // The original value was always from a non-local source (default or inherited),
            // because Setter.Apply skips properties that already have a local value.
            // Using SetValue here would create a local value, which would then cause
            // a subsequent Setter.Apply to skip the property (HasLocalValue check).
            target.ClearValue(actualProperty);
        }
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "DependencyProperty static fields are public static readonly and preserved by static initialization")]
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, FrameworkElement target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Try to find the property by name on the target type
        var propertyName = originalProperty.Name;
        var fieldName = $"{propertyName}Property";

        var currentType = targetType;
        while (currentType != null && currentType != typeof(object))
        {
            var dpField = currentType.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            if (dpField != null && dpField.FieldType == typeof(DependencyProperty))
            {
                return dpField.GetValue(null) as DependencyProperty;
            }
            currentType = currentType.BaseType;
        }

        // Fallback to the original property
        return originalProperty;
    }

    /// <summary>
    /// Resolves a DependencyProperty by name on the target type.
    /// Used for deferred resolution when the property couldn't be resolved at parse time.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "DependencyProperty static fields are public static readonly and preserved by static initialization")]
    internal static DependencyProperty? ResolveDependencyPropertyByName(string propertyName, Type targetType)
    {
        var fieldName = $"{propertyName}Property";
        var currentType = targetType;
        while (currentType != null && currentType != typeof(object))
        {
            var dpField = currentType.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            if (dpField != null && dpField.FieldType == typeof(DependencyProperty))
            {
                return dpField.GetValue(null) as DependencyProperty;
            }
            currentType = currentType.BaseType;
        }
        return null;
    }

    private FrameworkElement? GetTarget(FrameworkElement element)
    {
        if (string.IsNullOrEmpty(TargetName))
            return element;

        // Look up named element in the template scope
        // First, try using the element's FindName method
        if (element.FindName(TargetName) is FrameworkElement found)
        {
            return found;
        }

        // If that fails, search the visual tree starting from the element
        return SearchVisualTreeForName(element, TargetName);
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name)
    {
        if (visual == null) return null;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
        {
            return fe;
        }

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>
/// Base class for triggers that conditionally apply property values.
/// </summary>
[ContentProperty("Setters")]
public abstract class Trigger
{
    /// <summary>
    /// Gets the collection of setters to apply when the trigger is active.
    /// </summary>
    public IList<Setter> Setters { get; } = new List<Setter>();

    /// <summary>
    /// Tracks which properties this trigger has set for each element.
    /// Used to properly decrement the active count in shared storage.
    /// </summary>
    private readonly HashSet<(FrameworkElement, DependencyProperty)> _activeSetters = new();

    /// <summary>
    /// Gets or sets the parent style that contains this trigger.
    /// </summary>
    internal Style? ParentStyle { get; set; }

    /// <summary>
    /// Gets or sets the parent template triggers collection.
    /// This is set when the trigger is attached as part of a ControlTemplate.
    /// </summary>
    internal IList<Trigger>? ParentTemplateTriggers { get; set; }

    /// <summary>
    /// Attaches this trigger to the specified element.
    /// </summary>
    internal abstract void Attach(FrameworkElement element);

    /// <summary>
    /// Detaches this trigger from the specified element.
    /// </summary>
    internal abstract void Detach(FrameworkElement element);

    /// <summary>
    /// Gets whether this trigger is currently active for the specified element.
    /// </summary>
    internal abstract bool IsActiveForElement(FrameworkElement element);

    /// <summary>
    /// Applies the trigger's setters, storing pre-trigger values for later restoration.
    /// Uses element-level shared storage to ensure original values are preserved correctly
    /// even when multiple triggers affect the same property.
    /// </summary>
    protected void ApplyTriggerSetters(FrameworkElement element)
    {
        foreach (var setter in Setters)
        {
            // Get the target element (may be different from element if TargetName is set)
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null)
                continue;

            // Resolve the property - may need deferred resolution for TargetName setters
            var resolvedProperty = setter.Property;
            if (resolvedProperty == null && setter.PropertyName != null)
            {
                resolvedProperty = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
            }
            if (resolvedProperty == null)
                continue;

            // Resolve the actual property on the target type
            // This is important when TargetName is set and the target is a different type
            // (e.g., Setter targets a Border but was parsed with Style TargetType=Button)
            var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
            if (actualProperty == null)
                continue;

            var key = (target, actualProperty);

            // Use the styled element's shared storage for original values
            // This ensures we store the value BEFORE any trigger modified it
            if (element._triggerOriginalValues.TryGetValue(key, out var stored))
            {
                // Another trigger already stored the original value, just increment count
                element._triggerOriginalValues[key] = (stored.OriginalValue, stored.ActiveCount + 1, stored.SuspendedDynamicResourceKey);
            }
            else
            {
                // This is the first trigger affecting this property, store the original value
                var originalValue = target.GetValue(actualProperty);
                object? suspendedDynamicResourceKey = null;
                if (DynamicResourceBindingOperations.TryGetDynamicResourceKey(target, actualProperty, out var existingKey))
                {
                    suspendedDynamicResourceKey = existingKey;
                    DynamicResourceBindingOperations.ClearDynamicResource(target, actualProperty);
                }

                element._triggerOriginalValues[key] = (originalValue, 1, suspendedDynamicResourceKey);
            }

            // Track that this trigger has set this property
            _activeSetters.Add(key);

            if (setter.Value is IDynamicResourceReference dynamicReference)
            {
                DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey);
                continue;
            }

            // Convert value to the correct type if needed and apply
            var valueToSet = ConvertValueIfNeeded(setter.Value, actualProperty.PropertyType);
            target.SetValue(actualProperty, valueToSet);
        }

        // Invalidate visual to ensure re-render
        element.InvalidateVisual();
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "DependencyProperty static fields are public static readonly and preserved by static initialization")]
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, FrameworkElement target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Try to find the property by name on the target type
        var propertyName = originalProperty.Name;
        var fieldName = $"{propertyName}Property";

        var currentType = targetType;
        while (currentType != null && currentType != typeof(object))
        {
            var dpField = currentType.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            if (dpField != null && dpField.FieldType == typeof(DependencyProperty))
            {
                return dpField.GetValue(null) as DependencyProperty;
            }
            currentType = currentType.BaseType;
        }

        // Fallback to the original property
        return originalProperty;
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    protected static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // String to target type conversion
        if (value is string stringValue)
        {
            if (actualType == typeof(double))
                return double.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (actualType == typeof(float))
                return float.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (actualType == typeof(int))
                return int.Parse(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            if (actualType == typeof(bool))
                return bool.Parse(stringValue);
            if (actualType.IsEnum)
                return Enum.Parse(actualType, stringValue, ignoreCase: true);
            if (actualType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (actualType == typeof(Thickness))
                return ParseThickness(stringValue);
        }

        return value;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes the trigger's setters, restoring pre-trigger values.
    /// Uses element-level shared storage to ensure correct restoration
    /// when multiple triggers affect the same property.
    /// </summary>
    protected void RemoveTriggerSetters(FrameworkElement element)
    {
        // Collect the properties that need other triggers re-applied
        var needsReapply = new HashSet<(FrameworkElement, DependencyProperty)>();

        foreach (var setter in Setters)
        {
            // Get the target element (may be different from element if TargetName is set)
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null)
                continue;

            // Resolve the property - may need deferred resolution for TargetName setters
            var resolvedProperty = setter.Property;
            if (resolvedProperty == null && setter.PropertyName != null)
            {
                resolvedProperty = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
            }
            if (resolvedProperty == null)
                continue;

            // Resolve the actual property on the target type
            var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
            if (actualProperty == null)
                continue;

            var key = (target, actualProperty);

            // Check if this trigger actually set this property
            if (!_activeSetters.Contains(key))
                continue;

            if (setter.Value is IDynamicResourceReference)
            {
                DynamicResourceBindingOperations.ClearDynamicResource(target, actualProperty);
            }

            _activeSetters.Remove(key);

            // Decrement the active count in shared storage
            if (element._triggerOriginalValues.TryGetValue(key, out var stored))
            {
                var newCount = stored.ActiveCount - 1;
                if (newCount <= 0)
                {
                    // No more triggers affecting this property, restore original value
                    target.SetValue(actualProperty, stored.OriginalValue);
                    if (stored.SuspendedDynamicResourceKey != null)
                    {
                        DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, stored.SuspendedDynamicResourceKey);
                    }
                    element._triggerOriginalValues.Remove(key);
                }
                else
                {
                    // Other triggers still affect this property
                    element._triggerOriginalValues[key] = (stored.OriginalValue, newCount, stored.SuspendedDynamicResourceKey);
                    needsReapply.Add(key);
                }
            }

        }

        // Re-apply any other still-active triggers that affect the same properties
        // This ensures that if trigger A deactivates but trigger B is still active,
        // trigger B's values are re-applied
        if (needsReapply.Count > 0)
        {
            // Collect triggers to check - from ParentStyle or from ParentTemplateTriggers
            IEnumerable<Trigger>? triggersToCheck = ParentStyle?.Triggers ?? ParentTemplateTriggers;

            if (triggersToCheck != null)
            {
                foreach (var otherTrigger in triggersToCheck)
                {
                    if (otherTrigger == this) continue;
                    if (!otherTrigger.IsActiveForElement(element)) continue;

                    foreach (var setter in otherTrigger.Setters)
                    {
                        var target = GetSetterTarget(element, setter.TargetName);
                        if (target == null) continue;

                        // Resolve the property - may need deferred resolution
                        var resolvedProp = setter.Property;
                        if (resolvedProp == null && setter.PropertyName != null)
                        {
                            resolvedProp = Setter.ResolveDependencyPropertyByName(setter.PropertyName, target.GetType());
                        }
                        if (resolvedProp == null) continue;

                        // Resolve the actual property on the target type
                        var actualProperty = ResolvePropertyForTarget(resolvedProp, target);
                        if (actualProperty == null) continue;

                        var key = (target, actualProperty);
                        if (needsReapply.Contains(key))
                        {
                            if (setter.Value is IDynamicResourceReference dynamicReference)
                            {
                                DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey);
                            }
                            else
                            {
                                // This property needs another trigger's value re-applied
                                var valueToSet = ConvertValueIfNeeded(setter.Value, actualProperty.PropertyType);
                                target.SetValue(actualProperty, valueToSet);
                            }
                        }
                    }
                }
            }
        }

        // Invalidate visual to ensure re-render
        element.InvalidateVisual();
    }

    /// <summary>
    /// Gets the target element for a setter, handling TargetName lookup.
    /// </summary>
    private static FrameworkElement? GetSetterTarget(FrameworkElement element, string? targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return element;

        // Look up named element in the template scope
        if (element.FindName(targetName) is FrameworkElement found)
            return found;

        // If that fails, search the visual tree starting from the element
        return SearchVisualTreeForName(element, targetName);
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name)
    {
        if (visual == null) return null;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
            return fe;

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Clears all stored pre-trigger values for an element.
    /// </summary>
    protected void ClearPreTriggerValues(FrameworkElement element)
    {
        // Clear this trigger's tracking
        var keysToRemove = _activeSetters.Where(k => k.Item1 == element).ToList();
        foreach (var key in keysToRemove)
        {
            _activeSetters.Remove(key);

            // Decrement count in shared storage
            if (element._triggerOriginalValues.TryGetValue(key, out var stored))
            {
                var newCount = stored.ActiveCount - 1;
                if (newCount <= 0)
                {
                    var target = key.Item1;
                    if (stored.SuspendedDynamicResourceKey != null)
                    {
                        DynamicResourceBindingOperations.SetDynamicResource(target, key.Item2, stored.SuspendedDynamicResourceKey);
                    }
                    element._triggerOriginalValues.Remove(key);
                }
                else
                {
                    element._triggerOriginalValues[key] = (stored.OriginalValue, newCount, stored.SuspendedDynamicResourceKey);
                }
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when a property value equals a specified value.
/// </summary>
public sealed class PropertyTrigger : Trigger
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    /// <summary>
    /// Gets or sets the property that activates the trigger.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value { get; set; }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        if (Property == null)
            return;

        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == Property)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes
        element.PropertyChangedInternal += state.Handler;

        // Check initial state
        var currentValue = element.GetValue(Property);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (Property == null) return;
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // Convert Value to the property's type for proper comparison
        var triggerValue = ConvertValueIfNeeded(Value, Property.PropertyType);
        var shouldBeActive = Equals(currentValue, triggerValue);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a condition for a MultiTrigger.
/// </summary>
public sealed class Condition
{
    /// <summary>
    /// Gets or sets the property to evaluate.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the value that the property must equal for the condition to be true.
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Represents a condition based on a data binding for a MultiDataTrigger.
/// </summary>
public sealed class BindingCondition
{
    /// <summary>
    /// Gets or sets the binding to evaluate.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <summary>
    /// Gets or sets the value that the binding must equal for the condition to be true.
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Represents a trigger that applies property values when multiple conditions are all true.
/// </summary>
[ContentProperty("Setters")]
public sealed class MultiTrigger : Trigger
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    /// <summary>
    /// Gets the collection of conditions that must all be true for the trigger to activate.
    /// </summary>
    public IList<Condition> Conditions { get; } = new List<Condition>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our conditions
            bool shouldEvaluate = false;
            foreach (var condition in Conditions)
            {
                // Compare by property reference or by name as fallback
                if (dp == condition.Property ||
                    (condition.Property != null && dp.Name == condition.Property.Name))
                {
                    shouldEvaluate = true;
                    break;
                }
            }

            if (shouldEvaluate)
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe to property changes
        element.PropertyChangedInternal += state.Handler;

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        foreach (var condition in Conditions)
        {
            if (condition.Property == null)
            {
                shouldBeActive = false;
                break;
            }

            var currentValue = element.GetValue(condition.Property);
            var conditionValue = ConvertValueIfNeeded(condition.Value, condition.Property.PropertyType);

            if (!Equals(currentValue, conditionValue))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when data equals a specified value.
/// </summary>
public sealed class DataTrigger : Trigger
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public BindingExpressionBase? BindingExpression;
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    // Shadow property for receiving binding updates
    private static readonly DependencyProperty TriggerValueProperty =
        DependencyProperty.RegisterAttached("_DataTriggerValue", typeof(object),
            typeof(DataTrigger), new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the binding that produces the value to compare with Value.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value { get; set; }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == TriggerValueProperty)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes for the shadow property
        element.PropertyChangedInternal += state.Handler;

        // Set up binding to the shadow property
        if (Binding != null)
        {
            state.BindingExpression = BindingOperations.SetBinding(element, TriggerValueProperty, Binding);
        }

        // Check initial state
        var currentValue = element.GetValue(TriggerValueProperty);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear binding
            if (state.BindingExpression != null)
            {
                BindingOperations.ClearBinding(element, TriggerValueProperty);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        var shouldBeActive = Equals(currentValue, Value);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when an event occurs.
/// </summary>
public sealed class EventTrigger : Trigger
{
    private FrameworkElement? _attachedElement;

    /// <summary>
    /// Gets or sets the name of the event that activates the trigger.
    /// </summary>
    public RoutedEvent? RoutedEvent { get; set; }

    /// <summary>
    /// Gets the collection of actions to perform when the event occurs.
    /// </summary>
    public IList<TriggerAction> Actions { get; } = new List<TriggerAction>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        _attachedElement = element;

        if (RoutedEvent != null)
        {
            element.AddHandler(RoutedEvent, new RoutedEventHandler(OnEventRaised));
        }
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (RoutedEvent != null)
        {
            element.RemoveHandler(RoutedEvent, new RoutedEventHandler(OnEventRaised));
        }

        _attachedElement = null;
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        // EventTriggers don't have a persistent active state - they fire on events
        return false;
    }

    private void OnEventRaised(object sender, RoutedEventArgs e)
    {
        foreach (var action in Actions)
        {
            action.Invoke(_attachedElement);
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when multiple data binding conditions are all true.
/// </summary>
[ContentProperty("Setters")]
public sealed class MultiDataTrigger : Trigger
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public List<BindingExpressionBase> BindingExpressions = new();
        public List<DependencyProperty> ShadowProperties = new();
    }

    private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();

    // Counter for generating unique shadow property names
    private static int _propertyCounter;

    /// <summary>
    /// Gets the collection of binding conditions that must all be true for the trigger to activate.
    /// </summary>
    public IList<BindingCondition> Conditions { get; } = new List<BindingCondition>();

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // Create per-element state
        var state = new ElementState();
        _elementStates[element] = state;

        // Create shadow properties for each condition
        foreach (var condition in Conditions)
        {
            var propId = Interlocked.Increment(ref _propertyCounter);
            var shadowProp = DependencyProperty.RegisterAttached(
                $"_MultiDataTriggerValue{propId}",
                typeof(object),
                typeof(MultiDataTrigger),
                new PropertyMetadata(null));
            state.ShadowProperties.Add(shadowProp);
        }

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our shadow properties
            if (state.ShadowProperties.Contains(dp))
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe to property changes
        element.PropertyChangedInternal += state.Handler;

        // Set up bindings to shadow properties
        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (condition.Binding != null)
            {
                var bindingExpr = BindingOperations.SetBinding(element, state.ShadowProperties[i], condition.Binding);
                state.BindingExpressions.Add(bindingExpr);
            }
        }

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear all bindings
            foreach (var shadowProp in state.ShadowProperties)
            {
                BindingOperations.ClearBinding(element, shadowProp);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (i >= state.ShadowProperties.Count)
            {
                shouldBeActive = false;
                break;
            }

            var currentValue = element.GetValue(state.ShadowProperties[i]);
            if (!Equals(currentValue, condition.Value))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                ApplyTriggerSetters(element);
            }
            else
            {
                RemoveTriggerSetters(element);
            }
        }
    }
}

/// <summary>
/// Base class for actions that can be invoked by event triggers.
/// </summary>
public abstract class TriggerAction
{
    /// <summary>
    /// Invokes the action on the specified element.
    /// </summary>
    internal abstract void Invoke(FrameworkElement? element);
}

/// <summary>
/// Represents a setter that applies an event handler in a Style.
/// </summary>
public sealed class EventSetter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class.
    /// </summary>
    public EventSetter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class with the specified event and handler.
    /// </summary>
    /// <param name="routedEvent">The routed event that this EventSetter responds to.</param>
    /// <param name="handler">The handler to assign.</param>
    public EventSetter(RoutedEvent routedEvent, Delegate handler)
    {
        Event = routedEvent;
        Handler = handler;
    }

    /// <summary>
    /// Gets or sets the particular routed event that this EventSetter responds to.
    /// </summary>
    public RoutedEvent? Event { get; set; }

    /// <summary>
    /// Gets or sets the handler to assign in this setter.
    /// </summary>
    public Delegate? Handler { get; set; }

    /// <summary>
    /// Gets or sets whether the handler should be invoked even if the event is marked as handled.
    /// </summary>
    public bool HandledEventsToo { get; set; }

    /// <summary>
    /// Applies this event setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.AddHandler(Event, Handler, HandledEventsToo);
    }

    /// <summary>
    /// Removes this event setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.RemoveHandler(Event, Handler);
    }
}
