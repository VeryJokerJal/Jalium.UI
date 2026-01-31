namespace Jalium.UI;

/// <summary>
/// Contains property setters that can be shared between instances of a type.
/// </summary>
[ContentProperty("Setters")]
public class Style
{
    private readonly List<Setter> _setters = new();
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
        // Apply base style first
        BasedOn?.Apply(element);

        // Apply setters
        foreach (var setter in _setters)
        {
            setter.Apply(element);
        }

        // Apply triggers
        foreach (var trigger in _triggers)
        {
            trigger.Attach(element);
        }
    }

    /// <summary>
    /// Removes this style from the specified framework element.
    /// </summary>
    /// <param name="element">The element to remove the style from.</param>
    internal void Remove(FrameworkElement element)
    {
        // Remove triggers
        foreach (var trigger in _triggers)
        {
            trigger.Detach(element);
        }

        // Remove setters (in reverse order)
        for (int i = _setters.Count - 1; i >= 0; i--)
        {
            _setters[i].Remove(element);
        }

        // Remove base style
        BasedOn?.Remove(element);
    }
}

/// <summary>
/// Represents a setter that sets a property value.
/// </summary>
public class Setter
{
    /// <summary>
    /// Gets or sets the property to set.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Gets or sets the property name (used for runtime resolution when TargetName is specified).
    /// </summary>
    internal string? PropertyName { get; set; }

    /// <summary>
    /// Gets or sets the value to set.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to apply the setter to.
    /// </summary>
    public string? TargetName { get; set; }

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
    /// Gets the resolved DependencyProperty for the given target element.
    /// When TargetName is specified, resolves against the target element's type.
    /// </summary>
    internal DependencyProperty? GetResolvedProperty(FrameworkElement target)
    {
        // If no TargetName or no PropertyName stored, use the pre-resolved Property
        if (string.IsNullOrEmpty(TargetName) || string.IsNullOrEmpty(PropertyName))
            return Property;

        // Resolve property against the actual target element's type
        var targetType = target.GetType();
        var dpField = targetType.GetField($"{PropertyName}Property",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);

        if (dpField?.GetValue(null) is DependencyProperty resolvedDp)
            return resolvedDp;

        // Fall back to pre-resolved property
        return Property;
    }

    /// <summary>
    /// Applies this setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        if (Property == null) return;

        var target = GetTarget(element);
        if (target != null)
        {
            // Store original value for restoration
            if (!target._styleOriginalValues.ContainsKey(Property))
            {
                target._styleOriginalValues[Property] = target.GetValue(Property);
            }

            // Convert value to the correct type if needed
            var valueToSet = ConvertValueIfNeeded(Value, Property.PropertyType);
            target.SetValue(Property, valueToSet);
        }
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
        }

        return value;
    }

    /// <summary>
    /// Removes this setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        if (Property == null) return;

        var target = GetTarget(element);
        if (target != null && target._styleOriginalValues.TryGetValue(Property, out var originalValue))
        {
            target.SetValue(Property, originalValue);
            target._styleOriginalValues.Remove(Property);
        }
    }

    private FrameworkElement? GetTarget(FrameworkElement element)
    {
        if (string.IsNullOrEmpty(TargetName))
            return element;

        // Look up named element in the template scope
        // First, try using the element's FindName method
        var found = element.FindName(TargetName) as FrameworkElement;
        if (found != null)
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
    /// Stores pre-trigger values for restoration when the trigger deactivates.
    /// Key is (element, property) to support multiple elements if needed.
    /// </summary>
    private readonly Dictionary<(FrameworkElement, DependencyProperty), object?> _preTriggerValues = new();

    /// <summary>
    /// Attaches this trigger to the specified element.
    /// </summary>
    internal abstract void Attach(FrameworkElement element);

    /// <summary>
    /// Detaches this trigger from the specified element.
    /// </summary>
    internal abstract void Detach(FrameworkElement element);

    /// <summary>
    /// Applies the trigger's setters, storing pre-trigger values for later restoration.
    /// </summary>
    protected void ApplyTriggerSetters(FrameworkElement element)
    {
        foreach (var setter in Setters)
        {
            // Get the actual target element (may be different from element if TargetName is specified)
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null) continue;

            // Resolve the property against the actual target type
            var property = setter.GetResolvedProperty(target);
            if (property == null) continue;

            // Store the current value (which is the style's base value or local value)
            var key = (target, property);
            if (!_preTriggerValues.ContainsKey(key))
            {
                _preTriggerValues[key] = target.GetValue(property);
            }

            // Apply the trigger's value
            target.SetValue(property, setter.Value);
        }

        // Invalidate visual to ensure re-render
        element.InvalidateVisual();
    }

    /// <summary>
    /// Removes the trigger's setters, restoring pre-trigger values.
    /// </summary>
    protected void RemoveTriggerSetters(FrameworkElement element)
    {
        foreach (var setter in Setters)
        {
            // Get the actual target element (may be different from element if TargetName is specified)
            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null) continue;

            // Resolve the property against the actual target type
            var property = setter.GetResolvedProperty(target);
            if (property == null) continue;

            var key = (target, property);
            if (_preTriggerValues.TryGetValue(key, out var preTriggerValue))
            {
                // Restore the pre-trigger value (style's base value)
                target.SetValue(property, preTriggerValue);
                _preTriggerValues.Remove(key);
            }
        }

        // Invalidate visual to ensure re-render
        element.InvalidateVisual();
    }

    /// <summary>
    /// Gets the target element for a setter, resolving TargetName if specified.
    /// </summary>
    private static FrameworkElement? GetSetterTarget(FrameworkElement element, string? targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return element;

        // Look up named element in the template scope
        var found = element.FindName(targetName) as FrameworkElement;
        if (found != null)
            return found;

        // If that fails, search the visual tree
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

    /// <summary>
    /// Clears all stored pre-trigger values for an element.
    /// </summary>
    protected void ClearPreTriggerValues(FrameworkElement element)
    {
        var keysToRemove = _preTriggerValues.Keys.Where(k => k.Item1 == element).ToList();
        foreach (var key in keysToRemove)
        {
            _preTriggerValues.Remove(key);
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when a property value equals a specified value.
/// </summary>
public class PropertyTrigger : Trigger
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
        if (Property != null)
        {
            var currentValue = element.GetValue(Property);
            EvaluateTriggerForElement(element, currentValue);
        }
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

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (Property == null) return;
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
/// Represents a trigger that applies property values when data equals a specified value.
/// </summary>
public class DataTrigger : Trigger
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
public class EventTrigger : Trigger
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

    private void OnEventRaised(object sender, RoutedEventArgs e)
    {
        foreach (var action in Actions)
        {
            action.Invoke(_attachedElement);
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
