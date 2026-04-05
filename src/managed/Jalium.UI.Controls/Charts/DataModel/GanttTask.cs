using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a task in a Gantt chart.
/// </summary>
public class GanttTask : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private DateTime _startDate;
    private DateTime _endDate;
    private double _progress;
    private string? _group;
    private List<string>? _dependsOn;
    private bool _isMilestone;
    private Brush? _brush;
    private ObservableCollection<GanttTask>? _children;

    /// <summary>
    /// Gets or sets the unique identifier for this task.
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
    /// Gets or sets the display name for this task.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value ?? string.Empty;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>
    /// Gets or sets the start date of this task.
    /// </summary>
    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (_startDate != value)
            {
                _startDate = value;
                OnPropertyChanged(nameof(StartDate));
            }
        }
    }

    /// <summary>
    /// Gets or sets the end date of this task.
    /// </summary>
    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (_endDate != value)
            {
                _endDate = value;
                OnPropertyChanged(nameof(EndDate));
            }
        }
    }

    /// <summary>
    /// Gets or sets the progress of this task as a value between 0.0 and 1.0.
    /// </summary>
    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (_progress != clamped)
            {
                _progress = clamped;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    /// <summary>
    /// Gets or sets the group this task belongs to.
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
    /// Gets or sets the list of task IDs that this task depends on.
    /// </summary>
    public List<string> DependsOn
    {
        get => _dependsOn ??= new List<string>();
        set
        {
            if (_dependsOn != value)
            {
                _dependsOn = value;
                OnPropertyChanged(nameof(DependsOn));
            }
        }
    }

    /// <summary>
    /// Gets or sets whether this task is a milestone (zero-duration marker).
    /// </summary>
    public bool IsMilestone
    {
        get => _isMilestone;
        set
        {
            if (_isMilestone != value)
            {
                _isMilestone = value;
                OnPropertyChanged(nameof(IsMilestone));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to render this task.
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
    /// Gets or sets the child tasks for hierarchical Gantt charts.
    /// </summary>
    public ObservableCollection<GanttTask> Children
    {
        get => _children ??= new ObservableCollection<GanttTask>();
        set
        {
            if (_children != value)
            {
                _children = value;
                OnPropertyChanged(nameof(Children));
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
