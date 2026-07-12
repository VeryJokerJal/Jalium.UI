using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Provides an abstract class that describes how to divide the items in a collection into groups.
/// </summary>
public abstract class GroupDescription : System.ComponentModel.GroupDescription, INotifyPropertyChanged
{
    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets a custom comparer for sorting groups using an object that implements IComparer.
    /// </summary>
    public new IComparer<object>? CustomSort
    {
        get => base.CustomSort switch
        {
            null => null,
            GenericComparerAdapter adapter => adapter.Comparer,
            IComparer<object> comparer => comparer,
            IComparer comparer => new NonGenericComparerAdapter(comparer),
        };
        set => base.CustomSort = value switch
        {
            null => null,
            IComparer comparer => comparer,
            _ => new GenericComparerAdapter(value),
        };
    }

    /// <summary>
    /// Gets or sets a value indicating whether the subgroups should be sorted in reverse order.
    /// </summary>
    public bool SortDescriptionsInGrouping { get; set; }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        PropertyChanged?.Invoke(this, e);
    }

    private sealed class GenericComparerAdapter : IComparer
    {
        public GenericComparerAdapter(IComparer<object> comparer)
        {
            Comparer = comparer;
        }

        public IComparer<object> Comparer { get; }

        public int Compare(object? x, object? y) => Comparer.Compare(x!, y!);
    }

    private sealed class NonGenericComparerAdapter : IComparer<object>
    {
        private readonly IComparer _comparer;

        public NonGenericComparerAdapter(IComparer comparer)
        {
            _comparer = comparer;
        }

        public int Compare(object? x, object? y) => _comparer.Compare(x, y);
    }
}

/// <summary>
/// Describes the grouping of items using a property name as the criteria.
/// </summary>
public sealed class PropertyGroupDescription : GroupDescription
{
    private static readonly IComparer CompareNameAscendingInstance = new NameComparer(ListSortDirection.Ascending);
    private static readonly IComparer CompareNameDescendingInstance = new NameComparer(ListSortDirection.Descending);

    private string? _propertyName;
    private IValueConverter? _converter;
    private StringComparison _stringComparison = StringComparison.Ordinal;

    /// <summary>
    /// Initializes a new instance of the PropertyGroupDescription class.
    /// </summary>
    public PropertyGroupDescription()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PropertyGroupDescription class with the specified property name.
    /// </summary>
    /// <param name="propertyName">The name of the property that specifies which group an item belongs to.</param>
    public PropertyGroupDescription(string propertyName)
    {
        _propertyName = propertyName;
    }

    /// <summary>
    /// Initializes a new instance of the PropertyGroupDescription class with the specified property name and converter.
    /// </summary>
    /// <param name="propertyName">The name of the property that specifies which group an item belongs to.</param>
    /// <param name="converter">An IValueConverter to apply to the property value.</param>
    public PropertyGroupDescription(string? propertyName, IValueConverter? converter)
    {
        _propertyName = propertyName;
        _converter = converter;
    }

    /// <summary>
    /// Initializes a new instance of the PropertyGroupDescription class with the specified property name, converter, and string comparison.
    /// </summary>
    /// <param name="propertyName">The name of the property that specifies which group an item belongs to.</param>
    /// <param name="converter">An IValueConverter to apply to the property value.</param>
    /// <param name="stringComparison">A StringComparison value that specifies the comparison between the value of an item and the name of a group.</param>
    public PropertyGroupDescription(string? propertyName, IValueConverter? converter, StringComparison stringComparison)
    {
        _propertyName = propertyName;
        _converter = converter;
        _stringComparison = stringComparison;
    }

    /// <summary>
    /// Gets or sets the name of the property that is used to determine which group(s) an item belongs to.
    /// </summary>
    public string? PropertyName
    {
        get => _propertyName;
        set
        {
            if (_propertyName != value)
            {
                _propertyName = value;
                OnPropertyChanged(nameof(PropertyName));
            }
        }
    }

    /// <summary>
    /// Gets or sets a converter to apply to the property value or the item to produce the final value used to determine which group(s) an item belongs to.
    /// </summary>
    public IValueConverter? Converter
    {
        get => _converter;
        set
        {
            if (_converter != value)
            {
                _converter = value;
                OnPropertyChanged(nameof(Converter));
            }
        }
    }

    /// <summary>
    /// Gets or sets a StringComparison value that specifies the comparison between the value of an item and the name of a group.
    /// </summary>
    public StringComparison StringComparison
    {
        get => _stringComparison;
        set
        {
            if (_stringComparison != value)
            {
                _stringComparison = value;
                OnPropertyChanged(nameof(StringComparison));
            }
        }
    }

    /// <summary>
    /// Gets a comparer that orders groups in ascending order of their names.
    /// </summary>
    public static IComparer CompareNameAscending => CompareNameAscendingInstance;

    /// <summary>
    /// Gets a comparer that orders groups in descending order of their names.
    /// </summary>
    public static IComparer CompareNameDescending => CompareNameDescendingInstance;

    /// <summary>
    /// Returns the group name(s) for the given item.
    /// </summary>
    public override object GroupNameFromItem(object item, int level, CultureInfo culture)
    {
        object? value;

        if (string.IsNullOrEmpty(_propertyName))
        {
            value = item;
        }
        else
        {
            value = GetPropertyValue(item, _propertyName);
        }

        if (_converter != null)
        {
            value = _converter.Convert(value, typeof(object), null, culture);
        }

        return value ?? DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// Returns a value that indicates whether the group name and the item name match.
    /// </summary>
    public override bool NamesMatch(object groupName, object itemName)
    {
        if (groupName is string groupStr && itemName is string itemStr)
        {
            return string.Equals(groupStr, itemStr, _stringComparison);
        }
        return base.NamesMatch(groupName, itemName);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "PropertyGroupDescription computes a group key by reflecting the property named in PropertyName on the runtime type of each user-defined data item (item.GetType()). The runtime type is supplied by the consumer's grouped source collection and cannot carry DynamicallyAccessedMembers. Keeping the grouped properties of bound model types reflectable is the documented consumer responsibility when using PropertyGroupDescription under trimming/AOT, mirroring the data-binding reflection fallback; it is not a defect of this site.")]
    private static object? GetPropertyValue(object item, string propertyName)
    {
        var type = item.GetType();
        var property = type.GetProperty(propertyName);
        if (property != null)
        {
            return property.GetValue(item);
        }
        return null;
    }

    private sealed class NameComparer : IComparer
    {
        private readonly ListSortDirection _direction;

        internal NameComparer(ListSortDirection direction)
        {
            _direction = direction;
        }

        public int Compare(object? x, object? y)
        {
            var xName = (x as CollectionViewGroup)?.Name ?? x;
            var yName = (y as CollectionViewGroup)?.Name ?? y;
            var result = Comparer.DefaultInvariant.Compare(xName, yName);
            return _direction == ListSortDirection.Ascending ? result : -result;
        }
    }
}
