using Jalium.UI.Markup;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides the template for producing a container for an ItemsControl object.
/// </summary>
[DictionaryKeyProperty(nameof(ItemContainerTemplateKey))]
public class ItemContainerTemplate : DataTemplate
{
    /// <summary>
    /// Gets the implicit resource key for this item-container template.
    /// </summary>
    public object? ItemContainerTemplateKey =>
        DataType is { } dataType ? new ItemContainerTemplateKey(dataType) : null;
}

/// <summary>
/// Enables you to select an ItemContainerTemplate based on the data object and the data-bound element.
/// </summary>
public abstract partial class ItemContainerTemplateSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplateSelector"/> class.
    /// </summary>
    protected ItemContainerTemplateSelector()
    {
    }

    /// <summary>
    /// When overridden in a derived class, returns an ItemContainerTemplate based on custom logic.
    /// </summary>
    public virtual DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
    {
        return null;
    }
}
