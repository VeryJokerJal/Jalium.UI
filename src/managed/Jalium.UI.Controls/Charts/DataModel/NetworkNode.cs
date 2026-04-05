using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a node in a network graph.
/// </summary>
public class NetworkNode : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string? _label;
    private double _x;
    private double _y;
    private Brush? _brush;
    private double _radius = 10;
    private string? _group;
    private object? _tag;

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
    /// Gets or sets the computed X coordinate of this node.
    /// </summary>
    public double X
    {
        get => _x;
        set
        {
            if (_x != value)
            {
                _x = value;
                OnPropertyChanged(nameof(X));
            }
        }
    }

    /// <summary>
    /// Gets or sets the computed Y coordinate of this node.
    /// </summary>
    public double Y
    {
        get => _y;
        set
        {
            if (_y != value)
            {
                _y = value;
                OnPropertyChanged(nameof(Y));
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
    /// Gets or sets the radius of this node when rendered.
    /// </summary>
    public double Radius
    {
        get => _radius;
        set
        {
            if (_radius != value)
            {
                _radius = value;
                OnPropertyChanged(nameof(Radius));
            }
        }
    }

    /// <summary>
    /// Gets or sets the group this node belongs to.
    /// </summary>
    public string? Group
    {
        get => _group;
        set
        {
            if (_group != value)
            {
                _group = value;
                OnPropertyChanged(nameof(Group));
            }
        }
    }

    /// <summary>
    /// Gets or sets optional application-specific data.
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

/// <summary>
/// Represents a link (edge) between two nodes in a network graph.
/// </summary>
public class NetworkLink : INotifyPropertyChanged
{
    private string _sourceId = string.Empty;
    private string _targetId = string.Empty;
    private double _weight = 1.0;
    private Brush? _brush;
    private string? _label;

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
    /// Gets or sets the weight of this link.
    /// </summary>
    public double Weight
    {
        get => _weight;
        set
        {
            if (_weight != value)
            {
                _weight = value;
                OnPropertyChanged(nameof(Weight));
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

    /// <summary>
    /// Gets or sets the label for this link.
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
