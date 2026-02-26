using Jalium.UI.Media;
using Jalium.UI.Media.Effects;

namespace Jalium.UI;

/// <summary>
/// Manages a collection of Visual objects.
/// ContainerVisual is a lightweight Visual that can contain child Visuals
/// without participating in the layout system.
/// </summary>
public class ContainerVisual : Visual
{
    private readonly VisualCollection _children;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerVisual"/> class.
    /// </summary>
    public ContainerVisual()
    {
        _children = new VisualCollection(this);
    }

    /// <summary>
    /// Gets the child collection of this ContainerVisual.
    /// </summary>
    public VisualCollection Children => _children;

    /// <summary>
    /// Gets or sets the clip region of this ContainerVisual.
    /// </summary>
    public Geometry? Clip { get; set; }

    /// <summary>
    /// Gets or sets the opacity of this ContainerVisual.
    /// </summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the opacity mask of this ContainerVisual.
    /// </summary>
    public Brush? OpacityMask { get; set; }

    /// <summary>
    /// Gets or sets the Transform that is applied to this ContainerVisual.
    /// </summary>
    public Transform? Transform { get; set; }

    /// <summary>
    /// Gets or sets the BitmapEffect applied to this ContainerVisual.
    /// </summary>
    public Effect? Effect { get; set; }

    /// <summary>
    /// Gets or sets the X snapping guidelines.
    /// </summary>
    public IList<double>? XSnappingGuidelines { get; set; }

    /// <summary>
    /// Gets or sets the Y snapping guidelines.
    /// </summary>
    public IList<double>? YSnappingGuidelines { get; set; }

    /// <inheritdoc />
    public override int VisualChildrenCount => _children.Count;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index) => _children[index];

    /// <summary>
    /// Returns the bounding box for the contents of this ContainerVisual.
    /// </summary>
    public Rect ContentBounds
    {
        get
        {
            var bounds = Rect.Empty;
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                if (child is UIElement element)
                {
                    var childBounds = new Rect(element.RenderSize);
                    bounds.Union(childBounds);
                }
            }
            return bounds;
        }
    }

    /// <summary>
    /// Returns the bounding box that includes this visual and all its descendants.
    /// </summary>
    public Rect DescendantBounds => VisualTreeHelper.GetDescendantBounds(this);

    /// <summary>
    /// Hit tests at the specified point.
    /// </summary>
    public HitTestResult? HitTest(Point point)
    {
        return VisualTreeHelper.HitTest(this, point);
    }
}
