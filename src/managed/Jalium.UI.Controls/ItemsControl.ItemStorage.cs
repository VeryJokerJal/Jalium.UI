using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

public partial class ItemsControl : IContainItemStorage
{
    private readonly Dictionary<object, Dictionary<DependencyProperty, object?>> _storedItemValues = new();

    void IContainItemStorage.StoreItemValue(object item, DependencyProperty dp, object value)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (!_storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values))
        {
            values = new Dictionary<DependencyProperty, object?>();
            _storedItemValues.Add(item, values);
        }

        values[dp] = value;
    }

    object? IContainItemStorage.ReadItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        return _storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values)
            && values.TryGetValue(dp, out object? value)
                ? value
                : DependencyProperty.UnsetValue;
    }

    void IContainItemStorage.ClearItemValue(object item, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(dp);
        if (!_storedItemValues.TryGetValue(item, out Dictionary<DependencyProperty, object?>? values))
        {
            return;
        }

        values.Remove(dp);
        if (values.Count == 0)
        {
            _storedItemValues.Remove(item);
        }
    }

    void IContainItemStorage.ClearValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        foreach (Dictionary<DependencyProperty, object?> values in _storedItemValues.Values)
        {
            values.Remove(dp);
        }

        foreach (object item in _storedItemValues
            .Where(static pair => pair.Value.Count == 0)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _storedItemValues.Remove(item);
        }
    }

    void IContainItemStorage.Clear() => _storedItemValues.Clear();
}
