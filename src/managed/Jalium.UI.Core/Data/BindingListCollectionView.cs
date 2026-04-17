using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jalium.UI.Data;

/// <summary>
/// Implements a CollectionView for collections that implement IBindingList.
/// </summary>
public sealed class BindingListCollectionView : CollectionView
{
    /// <summary>
    /// Initializes a new instance of the BindingListCollectionView class.
    /// </summary>
    public BindingListCollectionView(IList list) : base(list)
    {
    }

    /// <summary>
    /// Gets a value that indicates whether this view supports filtering.
    /// </summary>
    public override bool CanFilter => true;

    /// <summary>
    /// Gets a value that indicates whether this view supports sorting.
    /// </summary>
    public override bool CanSort => true;

    /// <summary>
    /// Gets a value that indicates whether this view supports grouping.
    /// </summary>
    public override bool CanGroup => true;

    /// <summary>
    /// Gets or sets the custom filter string.
    /// </summary>
    public string? CustomFilter { get; set; }

    /// <summary>
    /// Gets a value that indicates whether a new item can be added to the collection.
    /// </summary>
    public bool CanAddNew => true;

    /// <summary>
    /// Gets a value that indicates whether an item can be removed from the collection.
    /// </summary>
    public bool CanRemove => true;

    /// <summary>
    /// Gets a value that indicates whether the collection view supports canceling changes to an edit item.
    /// </summary>
    public bool CanCancelEdit => true;

    /// <summary>
    /// Gets the item that is being added during the current add transaction.
    /// </summary>
    public object? CurrentAddItem { get; private set; }

    /// <summary>
    /// Gets the item that is being edited during the current edit transaction.
    /// </summary>
    public object? CurrentEditItem { get; private set; }

    /// <summary>
    /// Gets a value that indicates whether an add transaction is in progress.
    /// </summary>
    public bool IsAddingNew => CurrentAddItem != null;

    /// <summary>
    /// Gets a value that indicates whether an edit transaction is in progress.
    /// </summary>
    public bool IsEditingItem => CurrentEditItem != null;

    /// <summary>
    /// Starts an add transaction and returns the pending new item.
    /// </summary>
    public object AddNew()
    {
        var newItem = new object();
        CurrentAddItem = newItem;
        return newItem;
    }

    /// <summary>
    /// Ends the add transaction and saves the pending new item.
    /// </summary>
    public void CommitNew()
    {
        CurrentAddItem = null;
    }

    /// <summary>
    /// Ends the add transaction and discards the pending new item.
    /// </summary>
    public void CancelNew()
    {
        CurrentAddItem = null;
    }

    /// <summary>
    /// Begins an edit transaction on the specified item.
    /// </summary>
    public void EditItem(object item)
    {
        CurrentEditItem = item;
    }

    /// <summary>
    /// Ends the edit transaction and saves the pending changes.
    /// </summary>
    public void CommitEdit()
    {
        CurrentEditItem = null;
    }

    /// <summary>
    /// Ends the edit transaction and discards the pending changes.
    /// </summary>
    public void CancelEdit()
    {
        CurrentEditItem = null;
    }

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    public void Remove(object item)
    {
        if (SourceCollection is IList list)
            list.Remove(item);
    }

    /// <summary>
    /// Removes the item at the specified position from the collection.
    /// </summary>
    public void RemoveAt(int index)
    {
        if (SourceCollection is IList list)
            list.RemoveAt(index);
    }
}

/// <summary>
/// Maps an XML namespace URI to a prefix for use with XmlDataProvider.
/// </summary>
public sealed class XmlNamespaceMapping
{
    /// <summary>
    /// Initializes a new instance of the XmlNamespaceMapping class.
    /// </summary>
    public XmlNamespaceMapping()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified prefix and URI.
    /// </summary>
    public XmlNamespaceMapping(string prefix, Uri uri)
    {
        Prefix = prefix;
        Uri = uri;
    }

    /// <summary>
    /// Gets or sets the prefix for the namespace.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URI of the namespace.
    /// </summary>
    public Uri? Uri { get; set; }

    public override bool Equals(object? obj) =>
        obj is XmlNamespaceMapping other && Prefix == other.Prefix && Uri == other.Uri;

    public override int GetHashCode() => HashCode.Combine(Prefix, Uri);
}

/// <summary>
/// Converts values between different types in an alternating fashion.
/// </summary>
public sealed class AlternationConverter
{
    /// <summary>
    /// Gets the list of objects in the converter.
    /// </summary>
    public IList<object> Values { get; } = new List<object>();

    /// <summary>
    /// Converts a value based on the alternation count.
    /// </summary>
    public object? Convert(object value, Type targetType, object? parameter)
    {
        if (Values.Count == 0) return null;
        int index = value is int i ? i % Values.Count : 0;
        if (index < 0) index += Values.Count;
        return Values[index];
    }
}

/// <summary>
/// Provides system font information.
/// </summary>
public static class SystemFonts
{
    public static string MessageFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double MessageFontSize => 14.0;
    public static string CaptionFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double CaptionFontSize => 14.0;
    public static string SmallCaptionFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double SmallCaptionFontSize => 11.0;
    public static string MenuFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double MenuFontSize => 14.0;
    public static string StatusFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double StatusFontSize => 14.0;
    public static string IconFontFamily => FrameworkElement.DefaultFontFamilyName;
    public static double IconFontSize => 9.0;
}

/// <summary>
/// A collection of XmlNamespaceMapping objects that provides support for
/// adding and managing XML namespace mappings for use with XmlDataProvider.
/// </summary>
public sealed class XmlNamespaceMappingCollection : Collection<XmlNamespaceMapping>, IAddChild
{
    /// <summary>
    /// Initializes a new instance of the XmlNamespaceMappingCollection class.
    /// </summary>
    public XmlNamespaceMappingCollection()
    {
    }

    /// <summary>
    /// Adds the specified object as a child. If the object is an XmlNamespaceMapping, it is added to the collection.
    /// </summary>
    /// <param name="value">The object to add as a child.</param>
    public void AddChild(object value)
    {
        if (value is XmlNamespaceMapping mapping)
        {
            Add(mapping);
        }
        else
        {
            throw new ArgumentException(
                $"Cannot add object of type '{value?.GetType().Name ?? "null"}' to XmlNamespaceMappingCollection. " +
                "Only XmlNamespaceMapping objects are accepted.",
                nameof(value));
        }
    }

    /// <summary>
    /// Adds the specified text content. This operation is not supported for XmlNamespaceMappingCollection
    /// and is silently ignored for whitespace text.
    /// </summary>
    /// <param name="text">The text to add.</param>
    public void AddText(string text)
    {
        // Ignore whitespace text; throw for non-whitespace
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("XmlNamespaceMappingCollection does not support adding text content.");
        }
    }
}

/// <summary>
/// The IAddChild interface is used for parsing objects that
/// allow objects or text underneath their tags in markup that
/// do not map directly to a property.
/// </summary>
public interface IAddChild
{
    /// <summary>
    /// Called to add the object as a child.
    /// </summary>
    /// <param name="value">Object to add as a child.</param>
    void AddChild(object value);

    /// <summary>
    /// Called when text appears under the tag in markup.
    /// </summary>
    /// <param name="text">Text to add to the object.</param>
    void AddText(string text);
}
