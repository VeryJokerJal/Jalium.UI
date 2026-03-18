namespace Jalium.UI.Media;

/// <summary>
/// Represents a collection of drawings that can be operated upon as a single drawing.
/// </summary>
public sealed class DrawingGroup : Drawing
{
    private DrawingCollection? _children;
    private bool _isOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingGroup"/> class.
    /// </summary>
    public DrawingGroup()
    {
    }

    /// <summary>
    /// Gets or sets the collection of Drawing objects that are contained in this DrawingGroup.
    /// </summary>
    public DrawingCollection Children
    {
        get => _children ??= new DrawingCollection();
        set => _children = value;
    }

    /// <summary>
    /// Gets or sets the Transform to apply to this DrawingGroup.
    /// </summary>
    public Transform? Transform { get; set; }

    /// <summary>
    /// Gets or sets the clip region of this DrawingGroup.
    /// </summary>
    public Geometry? ClipGeometry { get; set; }

    /// <summary>
    /// Gets or sets the opacity of this DrawingGroup.
    /// </summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the opacity mask of this DrawingGroup.
    /// </summary>
    public Brush? OpacityMask { get; set; }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (_children == null || _children.Count == 0)
            {
                return Rect.Empty;
            }

            var bounds = Rect.Empty;
            foreach (var child in _children)
            {
                if (child != null)
                {
                    bounds = bounds.Union(child.Bounds);
                }
            }

            // Apply transform to bounds if present
            if (Transform != null && !bounds.IsEmpty)
            {
                // For simplicity, we don't transform the bounds here
                // A full implementation would apply the transform matrix
            }

            // Apply clip if present
            if (ClipGeometry != null && !bounds.IsEmpty)
            {
                bounds = bounds.Intersect(ClipGeometry.Bounds);
            }

            return bounds;
        }
    }

    /// <summary>
    /// Opens the DrawingGroup for rendering. Clears any existing children.
    /// </summary>
    /// <returns>A DrawingContext that can be used to describe the group's contents.</returns>
    public DrawingContext Open()
    {
        if (_isOpen)
        {
            throw new InvalidOperationException("DrawingGroup is already open.");
        }

        _isOpen = true;
        _children = new DrawingCollection();
        return new DrawingGroupDrawingContext(this, append: false);
    }

    /// <summary>
    /// Opens the DrawingGroup for appending. Keeps existing children.
    /// </summary>
    /// <returns>A DrawingContext that can be used to add to the group's contents.</returns>
    public DrawingContext Append()
    {
        if (_isOpen)
        {
            throw new InvalidOperationException("DrawingGroup is already open.");
        }

        _isOpen = true;
        _children ??= new DrawingCollection();
        return new DrawingGroupDrawingContext(this, append: true);
    }

    /// <summary>
    /// Called when the DrawingContext is closed.
    /// </summary>
    internal void Close(DrawingCollection newChildren, bool append)
    {
        if (append)
        {
            foreach (var child in newChildren)
            {
                Children.Add(child);
            }
        }
        else
        {
            _children = newChildren;
        }

        _isOpen = false;
    }

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        var popCount = 0;

        // Push transform if set
        if (Transform != null)
        {
            context.PushTransform(Transform);
            popCount++;
        }

        // Push clip if set
        if (ClipGeometry != null)
        {
            context.PushClip(ClipGeometry);
            popCount++;
        }

        // Push opacity if not default
        if (Opacity < 1.0)
        {
            context.PushOpacity(Opacity);
            popCount++;
        }

        // Render children
        if (_children != null)
        {
            foreach (var child in _children)
            {
                child?.RenderTo(context);
            }
        }

        // Pop all pushed states
        for (var i = 0; i < popCount; i++)
        {
            context.Pop();
        }
    }
}

/// <summary>
/// A DrawingContext that renders to a DrawingGroup.
/// </summary>
internal sealed class DrawingGroupDrawingContext : DrawingContext
{
    private readonly DrawingGroup _owner;
    private readonly bool _append;
    private readonly DrawingCollection _drawings = new();
    private readonly Stack<DrawingGroup> _groupStack = new();
    private bool _isClosed;

    internal DrawingGroupDrawingContext(DrawingGroup owner, bool append)
    {
        _owner = owner;
        _append = append;
    }

    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        var geometry = new LineGeometry(point0, point1);
        _drawings.Add(new GeometryDrawing(null, pen, geometry));
    }

    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
    {
        var geometry = new RectangleGeometry(rectangle);
        _drawings.Add(new GeometryDrawing(brush, pen, geometry));
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        var geometry = new RectangleGeometry(rectangle, radiusX, radiusY);
        _drawings.Add(new GeometryDrawing(brush, pen, geometry));
    }

    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        var geometry = new EllipseGeometry { Center = center, RadiusX = radiusX, RadiusY = radiusY };
        _drawings.Add(new GeometryDrawing(brush, pen, geometry));
    }

    public override void DrawText(FormattedText formattedText, Point origin)
    {
        _drawings.Add(new GlyphRunDrawing(formattedText, origin));
    }

    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        _drawings.Add(new GeometryDrawing(brush, pen, geometry));
    }

    public override void DrawImage(ImageSource imageSource, Rect rectangle)
    {
        _drawings.Add(new ImageDrawing(imageSource, rectangle));
    }

    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
    {
        // Backdrop effects are not supported in Drawing trees
    }

    public override void PushTransform(Transform transform)
    {
        var group = new DrawingGroup { Transform = transform };
        _groupStack.Push(group);
    }

    public override void PushClip(Geometry clipGeometry)
    {
        var group = new DrawingGroup { ClipGeometry = clipGeometry };
        _groupStack.Push(group);
    }

    public override void PushOpacity(double opacity)
    {
        var group = new DrawingGroup { Opacity = opacity };
        _groupStack.Push(group);
    }

    public override void Pop()
    {
        if (_groupStack.Count > 0)
        {
            var group = _groupStack.Pop();
            group.Children = new DrawingCollection(_drawings);
            _drawings.Clear();
            _drawings.Add(group);
        }
    }

    public override void Close()
    {
        if (_isClosed) return;
        _isClosed = true;

        // Pop any remaining groups
        while (_groupStack.Count > 0)
        {
            Pop();
        }

        _owner.Close(_drawings, _append);
    }
}
