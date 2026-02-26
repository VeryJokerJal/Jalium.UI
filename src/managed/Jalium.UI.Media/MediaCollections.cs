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
/// Represents a collection of GradientStop objects.
/// </summary>
public sealed class GradientStopCollection : Collection<GradientStop>
{
    /// <summary>
    /// Initializes a new empty GradientStopCollection.
    /// </summary>
    public GradientStopCollection() { }

    /// <summary>
    /// Initializes a new GradientStopCollection with the specified stops.
    /// </summary>
    public GradientStopCollection(IEnumerable<GradientStop> collection)
    {
        foreach (var item in collection)
            Add(item);
    }
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
