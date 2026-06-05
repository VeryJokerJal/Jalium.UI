using System.Collections.ObjectModel;

namespace Jalium.UI.Media;

/// <summary>
/// Represents a collection of Geometry objects.
/// </summary>
public sealed class GeometryCollection : Collection<Geometry>
{
    /// <summary>
    /// Initializes a new empty GeometryCollection.
    /// </summary>
    public GeometryCollection() { }

    /// <summary>
    /// Initializes a new GeometryCollection with the specified geometries.
    /// </summary>
    public GeometryCollection(IEnumerable<Geometry> collection)
    {
        foreach (var item in collection)
            Add(item);
    }
}

/// <summary>
/// Represents a collection of PathFigure objects.
/// </summary>
public sealed class PathFigureCollection : Collection<PathFigure>
{
    /// <summary>
    /// Initializes a new empty PathFigureCollection.
    /// </summary>
    public PathFigureCollection() { }

    /// <summary>
    /// Initializes a new PathFigureCollection with the specified figures.
    /// </summary>
    public PathFigureCollection(IEnumerable<PathFigure> collection)
    {
        foreach (var item in collection)
            Add(item);
    }
}

/// <summary>
/// Represents a collection of PathSegment objects.
/// </summary>
public sealed class PathSegmentCollection : Collection<PathSegment>
{
    /// <summary>
    /// Initializes a new empty PathSegmentCollection.
    /// </summary>
    public PathSegmentCollection() { }

    /// <summary>
    /// Initializes a new PathSegmentCollection with the specified segments.
    /// </summary>
    public PathSegmentCollection(IEnumerable<PathSegment> collection)
    {
        foreach (var item in collection)
            Add(item);
    }
}

/// <summary>
/// Represents a collection of <see cref="GradientStop"/> objects.
/// </summary>
/// <remarks>
/// Raises <see cref="Changed"/> whenever the collection is structurally mutated (add/insert/
/// remove/replace/clear) <em>or</em> when any contained stop's <c>Color</c>/<c>Offset</c> changes.
/// This lets an owning <see cref="GradientBrush"/> invalidate its cached content hash automatically
/// — closing the previous gap where a raw <c>List&lt;GradientStop&gt;</c> could not notify the brush
/// that a stop had been added or recoloured. Stays a <see cref="Collection{T}"/> so existing
/// index/Count/Add/foreach usage is unchanged.
/// </remarks>
public sealed class GradientStopCollection : Collection<GradientStop>
{
    /// <summary>Occurs when the collection or any contained stop changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new empty GradientStopCollection.
    /// </summary>
    public GradientStopCollection() { }

    /// <summary>
    /// Initializes a new GradientStopCollection with the specified stops.
    /// </summary>
    public GradientStopCollection(IEnumerable<GradientStop> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (var item in collection)
            Add(item);
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, GradientStop item)
    {
        base.InsertItem(index, item);
        Subscribe(item);
        OnChanged();
    }

    /// <inheritdoc />
    protected override void SetItem(int index, GradientStop item)
    {
        Unsubscribe(this[index]);
        base.SetItem(index, item);
        Subscribe(item);
        OnChanged();
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        Unsubscribe(this[index]);
        base.RemoveItem(index);
        OnChanged();
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        foreach (var stop in this)
            Unsubscribe(stop);
        base.ClearItems();
        OnChanged();
    }

    private void Subscribe(GradientStop? stop)
    {
        if (stop != null)
            stop.PropertyChangedInternal += OnStopPropertyChanged;
    }

    private void Unsubscribe(GradientStop? stop)
    {
        if (stop != null)
            stop.PropertyChangedInternal -= OnStopPropertyChanged;
    }

    private void OnStopPropertyChanged(DependencyProperty property, object? oldValue, object? newValue) => OnChanged();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents a collection of Transform objects.
/// </summary>
public sealed class TransformCollection : Collection<Transform>
{
    /// <summary>
    /// Initializes a new empty TransformCollection.
    /// </summary>
    public TransformCollection() { }
}

/// <summary>
/// Represents a set of guidelines used for rendering.
/// </summary>
public sealed class GuidelineSet : DependencyObject
{
    /// <summary>Gets or sets a collection of X coordinate guidelines.</summary>
    public DoubleCollection? GuidelinesX { get; set; }

    /// <summary>Gets or sets a collection of Y coordinate guidelines.</summary>
    public DoubleCollection? GuidelinesY { get; set; }
}
