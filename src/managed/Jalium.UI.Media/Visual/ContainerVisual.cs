using Jalium.UI.Media;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Media;

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
    public Geometry? Clip
    {
        get => VisualClip;
        set => VisualClip = value;
    }

    /// <summary>
    /// Gets or sets the opacity of this ContainerVisual.
    /// </summary>
    public double Opacity
    {
        get => VisualOpacity;
        set => VisualOpacity = value;
    }

    /// <summary>
    /// Gets or sets the opacity mask of this ContainerVisual.
    /// </summary>
    public Brush? OpacityMask
    {
        get => VisualOpacityMask;
        set => VisualOpacityMask = value;
    }

    /// <summary>
    /// Gets or sets the Transform that is applied to this ContainerVisual.
    /// </summary>
    public Transform? Transform
    {
        get => VisualTransform;
        set => VisualTransform = value;
    }

    /// <summary>
    /// Gets or sets the BitmapEffect applied to this ContainerVisual.
    /// </summary>
    public Effect? Effect
    {
        get => VisualEffect;
        set => VisualEffect = value;
    }

    /// <summary>
    /// Gets or sets the X snapping guidelines.
    /// </summary>
    public DoubleCollection? XSnappingGuidelines
    {
        get => VisualXSnappingGuidelines;
        set => VisualXSnappingGuidelines = value;
    }

    /// <summary>
    /// Gets or sets the Y snapping guidelines.
    /// </summary>
    public DoubleCollection? YSnappingGuidelines
    {
        get => VisualYSnappingGuidelines;
        set => VisualYSnappingGuidelines = value;
    }

    /// <summary>Gets or sets the deprecated bitmap effect associated with this visual.</summary>
    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public BitmapEffect? BitmapEffect
    {
        get => VisualBitmapEffect;
        set => VisualBitmapEffect = value;
    }

    /// <summary>Gets or sets the deprecated bitmap-effect input.</summary>
    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public BitmapEffectInput? BitmapEffectInput
    {
        get => VisualBitmapEffectInput;
        set => VisualBitmapEffectInput = value;
    }

    /// <summary>Gets or sets the retained composition cache mode.</summary>
    public CacheMode? CacheMode
    {
        get => VisualCacheMode;
        set => VisualCacheMode = value;
    }

    /// <summary>Gets or sets the composition offset.</summary>
    public Vector Offset
    {
        get => VisualOffset;
        set => VisualOffset = value;
    }

    /// <summary>Gets this visual's parent.</summary>
    public DependencyObject? Parent => VisualParent;

    /// <inheritdoc />
    protected sealed override int VisualChildrenCount => _children.Count;

    /// <inheritdoc />
    protected sealed override Visual? GetVisualChild(int index) => _children[index];

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

    internal override Rect ContentBoundsCore => ContentBounds;

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

    /// <summary>Performs a callback-based hit test against this visual subtree.</summary>
    public void HitTest(
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters hitTestParameters)
    {
        VisualTreeHelper.HitTest(this, filterCallback, resultCallback, hitTestParameters);
    }
}
