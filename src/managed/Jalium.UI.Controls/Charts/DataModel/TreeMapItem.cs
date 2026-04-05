using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents an item in a tree map chart.
/// </summary>
public class TreeMapItem : INotifyPropertyChanged
{
    private double _value;
    private string _label = string.Empty;
    private Brush? _brush;
    private ObservableCollection<TreeMapItem>? _children;
    private object? _tag;

    /// <summary>
    /// Gets or sets the numeric value that determines the size of this item.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    /// <summary>
    /// Gets or sets the display label for this item.
    /// </summary>
    public string Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value ?? string.Empty;
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to fill this item.
    /// </summary>
    public Brush? Brush
    {
        get => _brush;
        set
        {
            if (_brush != value)
            {
                _brush = value;
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <summary>
    /// Gets or sets the child items for hierarchical tree maps.
    /// </summary>
    public ObservableCollection<TreeMapItem> Children
    {
        get => _children ??= new ObservableCollection<TreeMapItem>();
        set
        {
            if (_children != value)
            {
                _children = value;
                OnPropertyChanged(nameof(Children));
            }
        }
    }

    /// <summary>
    /// Gets or sets optional application-specific data associated with this item.
    /// </summary>
    public object? Tag
    {
        get => _tag;
        set
        {
            if (!Equals(_tag, value))
            {
                _tag = value;
                OnPropertyChanged(nameof(Tag));
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
