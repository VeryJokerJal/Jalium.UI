using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a node in a Sankey diagram.
/// </summary>
public class SankeyNode : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string? _label;
    private Brush? _brush;
    private double _value;

    /// <summary>
    /// Gets or sets the unique identifier for this node.
    /// </summary>
    public string Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value ?? string.Empty;
                OnPropertyChanged(nameof(Id));
            }
        }
    }

    /// <summary>
    /// Gets or sets the display label for this node.
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to fill this node.
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
    /// Gets or sets the value (throughput) of this node.
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

/// <summary>
/// Represents a link (flow) between two nodes in a Sankey diagram.
/// </summary>
public class SankeyLink : INotifyPropertyChanged
{
    private string _sourceId = string.Empty;
    private string _targetId = string.Empty;
    private double _value;
    private Brush? _brush;

    /// <summary>
    /// Gets or sets the source node identifier.
    /// </summary>
    public string SourceId
    {
        get => _sourceId;
        set
        {
            if (_sourceId != value)
            {
                _sourceId = value ?? string.Empty;
                OnPropertyChanged(nameof(SourceId));
            }
        }
    }

    /// <summary>
    /// Gets or sets the target node identifier.
    /// </summary>
    public string TargetId
    {
        get => _targetId;
        set
        {
            if (_targetId != value)
            {
                _targetId = value ?? string.Empty;
                OnPropertyChanged(nameof(TargetId));
            }
        }
    }

    /// <summary>
    /// Gets or sets the flow value of this link.
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
    /// Gets or sets the brush used to render this link.
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
