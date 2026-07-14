using System.Collections.ObjectModel;
namespace System.ComponentModel;

/// <summary>
/// Describes a property exposed by items in a collection view.
/// </summary>
public sealed class ItemPropertyInfo
{
    /// <summary>
    /// Initializes a new item-property description.
    /// </summary>
    public ItemPropertyInfo(string name, Type propertyType, object? descriptor)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        PropertyType = propertyType ?? throw new ArgumentNullException(nameof(propertyType));
        Descriptor = descriptor;
    }

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the property type.
    /// </summary>
    public Type PropertyType { get; }

    /// <summary>
    /// Gets the source-specific property descriptor.
    /// </summary>
    public object? Descriptor { get; }
}

/// <summary>
/// Exposes metadata for the properties available on collection items.
/// </summary>
public interface IItemProperties
{
    /// <summary>
    /// Gets metadata for item properties, or <see langword="null"/> when the
    /// item type cannot be determined.
    /// </summary>
    ReadOnlyCollection<ItemPropertyInfo>? ItemProperties { get; }
}

/// <summary>
/// Specifies where the editable-collection new-item placeholder appears.
/// </summary>
public enum NewItemPlaceholderPosition
{
    /// <summary>No placeholder is displayed.</summary>
    None = 0,

    /// <summary>The placeholder appears before all data items.</summary>
    AtBeginning = 1,

    /// <summary>The placeholder appears after all data items.</summary>
    AtEnd = 2,
}

/// <summary>
/// Creates a collection view for a source that supplies its own view semantics.
/// </summary>
public interface ICollectionViewFactory
{
    /// <summary>Creates the collection view.</summary>
    ICollectionView CreateView();
}
