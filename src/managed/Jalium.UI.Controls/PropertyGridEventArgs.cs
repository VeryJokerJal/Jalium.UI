namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="PropertyGrid.PropertyValueChanged"/> event.
/// </summary>
public sealed class PropertyValueChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the name of the property whose value changed.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the old value of the property.
    /// </summary>
    public object? OldValue { get; }

    /// <summary>
    /// Gets the new value of the property.
    /// </summary>
    public object? NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyValueChangedEventArgs"/> class.
    /// </summary>
    public PropertyValueChangedEventArgs(RoutedEvent routedEvent, string propertyName, object? oldValue, object? newValue)
    {
        RoutedEvent = routedEvent;
        PropertyName = propertyName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Provides data for the <see cref="PropertyGrid.SelectedPropertyChanged"/> event.
/// </summary>
public sealed class SelectedPropertyChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the previously selected property.
    /// </summary>
    public PropertyItem? OldProperty { get; }

    /// <summary>
    /// Gets the newly selected property.
    /// </summary>
    public PropertyItem? NewProperty { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectedPropertyChangedEventArgs"/> class.
    /// </summary>
    public SelectedPropertyChangedEventArgs(RoutedEvent routedEvent, PropertyItem? oldProperty, PropertyItem? newProperty)
    {
        RoutedEvent = routedEvent;
        OldProperty = oldProperty;
        NewProperty = newProperty;
    }
}
