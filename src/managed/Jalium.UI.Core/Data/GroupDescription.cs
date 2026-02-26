using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Provides an abstract class that describes how to divide the items in a collection into groups.
/// </summary>
public abstract class GroupDescription : INotifyPropertyChanged
{
    private readonly ObservableCollection<object> _groupNames = new();

    /// <summary>
    /// Initializes a new instance of the GroupDescription class.
    /// </summary>
    protected GroupDescription()
    {
        CustomSort = null;
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the collection of names used to initialize a group with a set of subgroups.
    /// </summary>
    public ObservableCollection<object> GroupNames => _groupNames;

    /// <summary>
    /// Gets or sets a custom comparer for sorting groups using an object that implements IComparer.
    /// </summary>
    public IComparer<object>? CustomSort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subgroups should be sorted in reverse order.
    /// </summary>
    public bool SortDescriptionsInGrouping { get; set; }

    /// <summary>
    /// Returns the group name(s) for the given item.
    /// </summary>
    /// <param name="item">The item to return group names for.</param>
    /// <param name="level">The level of grouping.</param>
    /// <param name="culture">The CultureInfo to supply to the converter.</param>
    /// <returns>The group name(s) for the given item.</returns>
    public abstract object GroupNameFromItem(object item, int level, CultureInfo culture);

    /// <summary>
    /// Returns a value that indicates whether the group name and the item name match.
    /// </summary>
    /// <param name="groupName">The name of the group to check.</param>
    /// <param name="itemName">The name of the item to check.</param>
    /// <returns>true if the names match; otherwise, false.</returns>
    public virtual bool NamesMatch(object groupName, object itemName)
    {
        return Equals(groupName, itemName);
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Describes the grouping of items using a property name as the criteria.
/// </summary>
public sealed class PropertyGroupDescription : GroupDescription
{
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
}
