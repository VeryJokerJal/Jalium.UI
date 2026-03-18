namespace Jalium.UI.Media;

/// <summary>
/// Abstract base class for objects that describe 2-D drawing operations.
/// </summary>
public abstract class Drawing
{
    /// <summary>
    /// Gets the axis-aligned bounding box of this Drawing's contents.
    /// </summary>
    public abstract Rect Bounds { get; }

    /// <summary>
    /// Renders this Drawing to the specified DrawingContext.
    /// </summary>
    /// <param name="context">The DrawingContext to render to.</param>
    public abstract void RenderTo(DrawingContext context);
}

/// <summary>
/// Represents a collection of Drawing objects.
/// </summary>
public sealed class DrawingCollection : List<Drawing>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCollection"/> class.
    /// </summary>
    public DrawingCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCollection"/> class
    /// with the specified drawings.
    /// </summary>
    public DrawingCollection(IEnumerable<Drawing> drawings) : base(drawings)
    {
    }
}
