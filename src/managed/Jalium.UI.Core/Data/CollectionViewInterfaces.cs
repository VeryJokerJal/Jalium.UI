using Jalium.UI.Data;
using ICollectionView = Jalium.UI.Data.ICollectionView;

namespace Jalium.UI;

/// <summary>
/// Creates a collection view for a source object that supplies its own view semantics.
/// </summary>
public interface ICollectionViewFactory
{
    /// <summary>
    /// Creates the collection view.
    /// </summary>
    ICollectionView CreateView();
}

/// <summary>
/// Provides methods and properties that a <see cref="CollectionView"/> implements to enable
/// live sorting, grouping, and filtering of a collection.
/// </summary>
public interface ICollectionViewLiveShaping : System.ComponentModel.ICollectionViewLiveShaping
{
}

/// <summary>
/// Defines methods and properties that a <see cref="CollectionView"/> implements to enable
/// adding items of a specific type.
/// </summary>
public interface IEditableCollectionViewAddNewItem :
    System.ComponentModel.IEditableCollectionViewAddNewItem,
    Jalium.UI.Data.IEditableCollectionView
{
}

