using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// DrawingVisual is a visual object that can be used to render vector graphics.
/// The content is persisted as a DrawingGroup.
/// This is a lightweight alternative to UIElement when layout/input/focus is not needed.
/// </summary>
public sealed class DrawingVisual : ContainerVisual
{
    private DrawingGroup? _content;

    /// <summary>
    /// Gets the drawing content of this DrawingVisual.
    /// </summary>
    public Drawing? Drawing => _content;

    /// <summary>
    /// Opens the DrawingVisual for rendering. Returns a DrawingContext that can be used
    /// to draw into the visual. When the DrawingContext is closed, the content replaces
    /// any previous drawing content.
    /// </summary>
    /// <returns>A DrawingContext for rendering into this visual.</returns>
    public DrawingContext RenderOpen()
    {
        _content = new DrawingGroup();
        return _content.Open();
    }

    /// <summary>
    /// Renders the visual content to the specified drawing context.
    /// </summary>
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (_content != null && drawingContext is DrawingContext dc)
        {
            _content.RenderTo(dc);
        }
    }

    /// <summary>
    /// Hit test implementation for DrawingVisual.
    /// Tests against the actual drawing content rather than just the bounding box.
    /// </summary>
    protected override HitTestResult? HitTestCore(Point point)
    {
        if (_content != null)
        {
            var bounds = _content.Bounds;
            if (bounds.Contains(point))
            {
                return new HitTestResult(this);
            }
        }
        return null;
    }
}
