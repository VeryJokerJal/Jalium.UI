using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI;

/// <summary>
/// Provides methods and properties that a <see cref="CollectionView"/> implements to enable
/// live sorting, grouping, and filtering of a collection.
/// </summary>
public interface ICollectionViewLiveShaping
{
    /// <summary>
    /// Gets a value indicating whether the collection view supports live sorting.
    /// </summary>
    bool CanChangeLiveSorting { get; }

    /// <summary>
    /// Gets a value indicating whether the collection view supports live filtering.
    /// </summary>
    bool CanChangeLiveFiltering { get; }

    /// <summary>
    /// Gets a value indicating whether the collection view supports live grouping.
    /// </summary>
    bool CanChangeLiveGrouping { get; }

    /// <summary>
    /// Gets or sets a value indicating whether live sorting is enabled.
    /// </summary>
    bool? IsLiveSorting { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether live filtering is enabled.
    /// </summary>
    bool? IsLiveFiltering { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether live grouping is enabled.
    /// </summary>
    bool? IsLiveGrouping { get; set; }

    /// <summary>
    /// Gets the property names that participate in sorting.
    /// </summary>
    ObservableCollection<string> LiveSortingProperties { get; }

    /// <summary>
    /// Gets the property names that participate in filtering.
    /// </summary>
    ObservableCollection<string> LiveFilteringProperties { get; }

    /// <summary>
    /// Gets the property names that participate in grouping.
    /// </summary>
    ObservableCollection<string> LiveGroupingProperties { get; }
}

/// <summary>
/// Defines methods and properties that a <see cref="CollectionView"/> implements to enable
/// adding items of a specific type.
/// </summary>
public interface IEditableCollectionViewAddNewItem : IEditableCollectionView
{
    /// <summary>
    /// Gets a value indicating whether a specified object can be added to the collection.
    /// </summary>
    bool CanAddNewItem { get; }

    /// <summary>
    /// Adds the specified object to the collection.
    /// </summary>
    /// <param name="newItem">The object to add.</param>
    /// <returns>The object that was added.</returns>
    object AddNewItem(object newItem);
}

/// <summary>
/// Provides data for the <see cref="Binding.TargetUpdated"/> and <see cref="Binding.SourceUpdated"/> events.
/// </summary>
public sealed class DataTransferEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataTransferEventArgs"/> class.
    /// </summary>
    public DataTransferEventArgs(DependencyObject targetObject, DependencyProperty property)
    {
        TargetObject = targetObject;
        Property = property;
    }

    /// <summary>
    /// Gets the binding target object of the binding that raised the event.
    /// </summary>
    public DependencyObject TargetObject { get; }

    /// <summary>
    /// Gets the specific binding target property that is involved in the binding that raised the event.
    /// </summary>
    public DependencyProperty Property { get; }

    /// <summary>
    /// Gets or sets the item in the binding source that transferred the data.
    /// </summary>
    public object? Item { get; set; }
}

