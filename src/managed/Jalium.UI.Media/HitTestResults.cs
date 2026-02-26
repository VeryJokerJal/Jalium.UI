namespace Jalium.UI.Media;

/// <summary>
/// Returns the result of a hit test that uses a Point as a hit test parameter.
/// </summary>
public class PointHitTestResult : HitTestResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PointHitTestResult"/> class.
    /// </summary>
    public PointHitTestResult(Visual visualHit, Point pointHit) : base(visualHit)
    {
        PointHit = pointHit;
    }

    /// <summary>
    /// Gets the point value that is returned from a hit test result.
    /// </summary>
    public Point PointHit { get; }
}

/// <summary>
/// Returns the results of a hit test that uses a Geometry as a hit test parameter.
/// </summary>
public class GeometryHitTestResult : HitTestResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryHitTestResult"/> class.
    /// </summary>
    public GeometryHitTestResult(Visual visualHit, IntersectionDetail intersectionDetail)
        : base(visualHit)
    {
        IntersectionDetail = intersectionDetail;
    }

    /// <summary>
    /// Gets the IntersectionDetail value of the hit test.
    /// </summary>
    public IntersectionDetail IntersectionDetail { get; }
}

/// <summary>
/// Represents the hit test parameters for a point-based hit test.
/// </summary>
public class PointHitTestParameters : HitTestParameters
{
    /// <summary>
    /// Initializes a new instance with the specified point.
    /// </summary>
    public PointHitTestParameters(Point point)
    {
        HitPoint = point;
    }

    /// <summary>Gets the point to hit test against.</summary>
    public Point HitPoint { get; }
}

/// <summary>
/// Represents the hit test parameters for a geometry-based hit test.
/// </summary>
public class GeometryHitTestParameters : HitTestParameters
{
    /// <summary>
    /// Initializes a new instance with the specified geometry.
    /// </summary>
    public GeometryHitTestParameters(Geometry hitTestArea)
    {
        HitTestArea = hitTestArea ?? throw new ArgumentNullException(nameof(hitTestArea));
    }

    /// <summary>Gets the Geometry that defines the hit test area.</summary>
    public Geometry HitTestArea { get; }
}

/// <summary>
/// Base class for hit test parameters.
/// </summary>
public abstract class HitTestParameters
{
}

/// <summary>
/// Describes the intersection between the geometries in a hit test.
/// </summary>
public enum IntersectionDetail
{
    /// <summary>The visual was not hit.</summary>
    NotCalculated,

    /// <summary>The geometries are not intersecting.</summary>
    Empty,

    /// <summary>The hit test geometry is fully contained within the visual.</summary>
    FullyContains,

    /// <summary>The visual is fully contained within the hit test geometry.</summary>
    FullyInside,

    /// <summary>The geometries intersect but neither is fully contained.</summary>
    Intersects
}

/// <summary>
/// Provides data for the <see cref="CompositionTarget.Rendering"/> event.
/// </summary>
public class RenderingEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderingEventArgs"/> class.
    /// </summary>
    public RenderingEventArgs(TimeSpan renderingTime)
    {
        RenderingTime = renderingTime;
    }

    /// <summary>Gets the estimated target time for the rendering frame.</summary>
    public TimeSpan RenderingTime { get; }
}
