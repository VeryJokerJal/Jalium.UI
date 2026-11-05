using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>A category's data and expansion state, independent of any presentation instance.</summary>
public sealed class PropertyGridCategory : INotifyPropertyChanged
{
    private readonly ObservableCollection<PropertyItem> _properties = new();
    private bool _expanded = true;
    internal PropertyGridCategory(string name) { Name = name; Properties = new(_properties); }
    public string Name { get; }
    public ReadOnlyObservableCollection<PropertyItem> Properties { get; }
    public bool IsExpanded { get => _expanded; set { if (_expanded == value) return; _expanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetProperties(IEnumerable<PropertyItem> properties)
    {
        _properties.Clear();
        foreach (var item in properties) _properties.Add(item);
    }
}

/// <summary>Describes one typed value in an enum editor, including an optional null entry.</summary>
public sealed record PropertyGridEnumOption(object? Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
