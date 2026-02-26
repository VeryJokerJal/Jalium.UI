namespace Jalium.UI.Controls;

/// <summary>
/// Provides the template for producing a container for an ItemsControl object.
/// </summary>
public sealed class ItemContainerTemplate : DataTemplate
{
}

/// <summary>
/// Enables you to select an ItemContainerTemplate based on the data object and the data-bound element.
/// </summary>
public sealed class ItemContainerTemplateSelector
{
    /// <summary>
    /// When overridden in a derived class, returns an ItemContainerTemplate based on custom logic.
    /// </summary>
    public DataTemplate? SelectTemplate(Type itemType, ItemsControl parentItemsControl)
    {
        return null;
    }
}
