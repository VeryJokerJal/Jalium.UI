namespace Jalium.UI.Controls;

/// <summary>
/// Defines the functionality required to support a shared-size group
/// that is used by the ColumnDefinitionCollection and RowDefinitionCollection classes.
/// </summary>
public abstract class DefinitionBase : DependencyObject
{
    /// <summary>
    /// Identifies the SharedSizeGroup dependency property.
    /// </summary>
    public static readonly DependencyProperty SharedSizeGroupProperty =
        DependencyProperty.Register(nameof(SharedSizeGroup), typeof(string), typeof(DefinitionBase),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets a value that identifies a ColumnDefinition or RowDefinition
    /// as a member of a defined group that shares sizing properties.
    /// </summary>
    public string? SharedSizeGroup
    {
        get => (string?)GetValue(SharedSizeGroupProperty);
        set => SetValue(SharedSizeGroupProperty, value);
    }
}
