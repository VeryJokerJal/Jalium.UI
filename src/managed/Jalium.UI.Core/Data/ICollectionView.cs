using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Enables collections to have the functionalities of current record management,
/// custom sorting, filtering, and grouping.
/// </summary>
public interface ICollectionView : IEnumerable, INotifyCollectionChanged
{
    /// <summary>
    /// Gets a value that indicates whether this view supports filtering via the Filter property.
    /// </summary>
    bool CanFilter { get; }

    /// <summary>
    /// Gets a value that indicates whether this view supports grouping via GroupDescriptions.
    /// </summary>
    bool CanGroup { get; }

    /// <summary>
    /// Gets a value that indicates whether this view supports sorting via SortDescriptions.
    /// </summary>
    bool CanSort { get; }

    /// <summary>
    /// Gets or sets the culture information for any operations of the view
    /// that may differ by culture, such as sorting.
    /// </summary>
    CultureInfo Culture { get; set; }

    /// <summary>
    /// Gets the current item in the view.
    /// </summary>
    object? CurrentItem { get; }

    /// <summary>
    /// Gets the ordinal position of the CurrentItem within the view.
    /// </summary>
    int CurrentPosition { get; }

    /// <summary>
    /// Gets or sets a callback used to determine if an item is suitable for inclusion in the view.
    /// </summary>
    Predicate<object>? Filter { get; set; }

    /// <summary>
    /// Gets a collection of GroupDescription objects that describe how the items in the collection
    /// are grouped in the view.
    /// </summary>
    System.Collections.ObjectModel.ObservableCollection<GroupDescription> GroupDescriptions { get; }

    /// <summary>
    /// Gets the top-level groups.
    /// </summary>
    System.Collections.ObjectModel.ReadOnlyObservableCollection<object>? Groups { get; }

    /// <summary>
    /// Gets a value that indicates whether the CurrentItem of the view is beyond the end of the collection.
    /// </summary>
    bool IsCurrentAfterLast { get; }

    /// <summary>
    /// Gets a value that indicates whether the CurrentItem of the view is before the beginning of the collection.
    /// </summary>
    bool IsCurrentBeforeFirst { get; }

    /// <summary>
    /// Gets a value that indicates whether the view is empty.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets a collection of SortDescription objects that describe how the items in the collection
    /// are sorted in the view.
    /// </summary>
    SortDescriptionCollection SortDescriptions { get; }

    /// <summary>
    /// Returns the underlying collection.
    /// </summary>
    IEnumerable SourceCollection { get; }

    /// <summary>
    /// Occurs after the current item has been changed.
    /// </summary>
    event EventHandler? CurrentChanged;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    event CurrentChangingEventHandler? CurrentChanging;

    /// <summary>
    /// Returns a value that indicates whether a given item belongs to this collection view.
    /// </summary>
    /// <param name="item">The object to check.</param>
    /// <returns>true if the item belongs to this collection view; otherwise, false.</returns>
    bool Contains(object item);

    /// <summary>
    /// Enters a defer cycle that you can use to merge changes to the view and delay automatic refresh.
    /// </summary>
    /// <returns>An IDisposable object that you can use to dispose of the calling object.</returns>
    IDisposable DeferRefresh();

    /// <summary>
    /// Sets the specified item to be the CurrentItem in the view.
    /// </summary>
    /// <param name="item">The item to set as the CurrentItem.</param>
    /// <returns>true if the resulting CurrentItem is within the view; otherwise, false.</returns>
    bool MoveCurrentTo(object? item);

    /// <summary>
    /// Sets the first item in the view as the CurrentItem.
    /// </summary>
    /// <returns>true if the resulting CurrentItem is an item within the view; otherwise, false.</returns>
    bool MoveCurrentToFirst();

    /// <summary>
    /// Sets the last item in the view as the CurrentItem.
    /// </summary>
    /// <returns>true if the resulting CurrentItem is an item within the view; otherwise, false.</returns>
    bool MoveCurrentToLast();

    /// <summary>
    /// Sets the item after the CurrentItem in the view as the CurrentItem.
    /// </summary>
    /// <returns>true if the resulting CurrentItem is an item within the view; otherwise, false.</returns>
    bool MoveCurrentToNext();

    /// <summary>
    /// Sets the item at the specified index to be the CurrentItem in the view.
    /// </summary>
    /// <param name="position">The index to set the CurrentItem to.</param>
    /// <returns>true if the resulting CurrentItem is an item within the view; otherwise, false.</returns>
    bool MoveCurrentToPosition(int position);

    /// <summary>
    /// Sets the item before the CurrentItem in the view as the CurrentItem.
    /// </summary>
    /// <returns>true if the resulting CurrentItem is an item within the view; otherwise, false.</returns>
    bool MoveCurrentToPrevious();

    /// <summary>
    /// Recreates the view.
    /// </summary>
    void Refresh();
}

/// <summary>
/// Provides data for the CurrentChanging event.
/// </summary>
public sealed class CurrentChangingEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the CurrentChangingEventArgs class.
    /// </summary>
    public CurrentChangingEventArgs() : this(true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the CurrentChangingEventArgs class
    /// and sets the IsCancelable property to the specified value.
    /// </summary>
    /// <param name="isCancelable">true if the event is cancelable; otherwise, false.</param>
    public CurrentChangingEventArgs(bool isCancelable)
    {
        IsCancelable = isCancelable;
    }

    /// <summary>
    /// Gets a value that indicates whether the current item change is cancelable.
    /// </summary>
    public bool IsCancelable { get; }

    /// <summary>
    /// Gets or sets a value that indicates whether the event should be canceled.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Represents the method that will handle the CurrentChanging event.
/// </summary>
public delegate void CurrentChangingEventHandler(object sender, CurrentChangingEventArgs e);
