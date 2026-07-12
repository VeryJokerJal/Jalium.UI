using System.ComponentModel;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Effects;
using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;

namespace Jalium.UI.Media.Media3D;

/// <summary>Provides services common to 3-D visual objects.</summary>
public abstract class Visual3D : DependencyObject, IAnimatable
{
    private readonly Visual3DCollection _children;
    private readonly Dictionary<DependencyProperty, AnimationClock> _animationClocks = new();
    private DependencyObject? _visual3DParent;
    private Model3D? _visual3DModel;

    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(
            nameof(Transform),
            typeof(Transform3D),
            typeof(Visual3D),
            new PropertyMetadata(Transform3D.Identity));

    protected Visual3D()
    {
        _children = new Visual3DCollection(this);
    }

    public Transform3D? Transform
    {
        get => (Transform3D?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    public bool HasAnimatedProperties => _animationClocks.Count != 0;

    protected Model3D? Visual3DModel
    {
        get => _visual3DModel;
        set => _visual3DModel = value;
    }

    protected internal Visual3DCollection InternalChildren => _children;

    protected internal DependencyObject? Visual3DParent => _visual3DParent;

    internal Model3D? SceneModel => _visual3DModel;

    protected virtual int Visual3DChildrenCount => _children.Count;

    protected virtual Visual3D GetVisual3DChild(int index) => _children[index];

    public bool IsAncestorOf(DependencyObject descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);
        for (DependencyObject? current = GetVisualParent(descendant);
             current is not null;
             current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsDescendantOf(DependencyObject ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        for (DependencyObject? current = GetVisualParent(this);
             current is not null;
             current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    public DependencyObject? FindCommonVisualAncestor(DependencyObject otherVisual)
    {
        ArgumentNullException.ThrowIfNull(otherVisual);
        var ancestors = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        for (DependencyObject? current = this;
             current is not null;
             current = GetVisualParent(current))
        {
            ancestors.Add(current);
        }

        for (DependencyObject? current = otherVisual;
             current is not null;
             current = GetVisualParent(current))
        {
            if (ancestors.Contains(current))
            {
                return current;
            }
        }

        return null;
    }

    public GeneralTransform3D TransformToAncestor(Visual3D ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        Matrix3D matrix = GetTransformToAncestorMatrix(ancestor);
        return new MatrixTransform3D(matrix);
    }

    public GeneralTransform3D TransformToDescendant(Visual3D descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);
        Matrix3D matrix = descendant.GetTransformToAncestorMatrix(this);
        if (!matrix.HasInverse)
        {
            throw new InvalidOperationException("The transform to the descendant is not invertible.");
        }

        matrix.Invert();
        return new MatrixTransform3D(matrix);
    }

    public GeneralTransform3DTo2D TransformToAncestor(Visual ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        Matrix3D localToViewport = Matrix3D.Identity;
        Visual3D current = this;
        while (true)
        {
            localToViewport *= current.Transform?.Value ?? Matrix3D.Identity;
            if (current.Visual3DParent is Visual3D parent3D)
            {
                current = parent3D;
                continue;
            }

            if (current.Visual3DParent is not Viewport3DVisual viewport)
            {
                throw new InvalidOperationException("The visual is not connected to a Viewport3DVisual.");
            }

            bool found = false;
            for (Visual? visual = viewport; visual is not null; visual = visual.VisualParent)
            {
                if (ReferenceEquals(visual, ancestor))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException("The specified visual is not an ancestor.");
            }

            return new GeneralTransform3DTo2D(localToViewport * viewport.GetProjectionToViewportMatrix());
        }
    }

    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock) =>
        ApplyAnimationClock(dp, clock, AnimationHandoffBehavior.SnapshotAndReplace);

    public void ApplyAnimationClock(
        DependencyProperty dp,
        AnimationClock? clock,
        AnimationHandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }

        if (clock is null)
        {
            _animationClocks.Remove(dp);
            return;
        }

        if (clock.Timeline is not AnimationTimeline timeline)
        {
            throw new ArgumentException("The clock must be backed by an AnimationTimeline.", nameof(clock));
        }

        if (!dp.PropertyType.IsAssignableFrom(timeline.TargetPropertyType) &&
            !timeline.TargetPropertyType.IsAssignableFrom(dp.PropertyType))
        {
            throw new ArgumentException("The animation does not target the dependency property's value type.", nameof(clock));
        }

        _animationClocks[dp] = clock;
        if (!clock.IsRunning)
        {
            clock.Begin();
        }
    }

    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation) =>
        BeginAnimation(dp, animation, AnimationHandoffBehavior.SnapshotAndReplace);

    public void BeginAnimation(
        DependencyProperty dp,
        AnimationTimeline? animation,
        AnimationHandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ApplyAnimationClock(
            dp,
            animation is null ? null : (AnimationClock)animation.CreateClock(),
            handoffBehavior);
    }

    public new object? GetAnimationBaseValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return base.GetAnimationBaseValue(dp);
    }

    protected void AddVisual3DChild(Visual3D child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Visual3DParent is not null)
        {
            throw new ArgumentException("The Visual3D already has a visual parent.", nameof(child));
        }

        child.SetVisual3DParent(this);
        OnVisualChildrenChanged(child, null);
    }

    protected void RemoveVisual3DChild(Visual3D child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Visual3DParent, this))
        {
            return;
        }

        child.SetVisual3DParent(null);
        OnVisualChildrenChanged(null, child);
    }

    internal void AttachVisual3DChild(Visual3D child) => AddVisual3DChild(child);

    internal void DetachVisual3DChild(Visual3D child) => RemoveVisual3DChild(child);

    internal void SetVisual3DParent(DependencyObject? parent)
    {
        if (ReferenceEquals(_visual3DParent, parent))
        {
            return;
        }

        DependencyObject? oldParent = _visual3DParent;
        _visual3DParent = parent;
        OnVisualParentChanged(oldParent!);
    }

    protected internal virtual void OnVisualParentChanged(DependencyObject oldParent)
    {
    }

    protected internal virtual void OnVisualChildrenChanged(
        DependencyObject? visualAdded,
        DependencyObject? visualRemoved)
    {
    }

    private Matrix3D GetTransformToAncestorMatrix(Visual3D ancestor)
    {
        Matrix3D matrix = Matrix3D.Identity;
        for (Visual3D? current = this; !ReferenceEquals(current, ancestor);)
        {
            matrix *= current.Transform?.Value ?? Matrix3D.Identity;
            current = current.Visual3DParent as Visual3D;
            if (current is null)
            {
                throw new InvalidOperationException("The specified Visual3D is not an ancestor.");
            }
        }

        return matrix;
    }

    private static DependencyObject? GetVisualParent(DependencyObject visual) => visual switch
    {
        Visual3D visual3D => visual3D.Visual3DParent,
        Visual visual2D => visual2D.VisualParent,
        _ => null,
    };
}

/// <summary>Renders a Model3D and contains child Visual3D objects.</summary>
[ContentProperty(nameof(Children))]
public class ModelVisual3D : Visual3D, IAddChild
{
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(
            nameof(Content),
            typeof(Model3D),
            typeof(ModelVisual3D),
            new PropertyMetadata(null, OnContentChanged));

    public static new readonly DependencyProperty TransformProperty =
        Visual3D.TransformProperty.AddOwner(typeof(ModelVisual3D));

    public Model3D? Content
    {
        get => (Model3D?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public new Transform3D? Transform
    {
        get => (Transform3D?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Visual3DCollection Children => InternalChildren;

    protected sealed override int Visual3DChildrenCount => Children.Count;

    protected sealed override Visual3D GetVisual3DChild(int index) => Children[index];

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Visual3D child)
        {
            throw new ArgumentException(
                $"{nameof(ModelVisual3D)} children must derive from {nameof(Visual3D)}.",
                nameof(value));
        }

        Children.Add(child);
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(static character => !char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException($"{nameof(ModelVisual3D)} does not accept text content.");
        }
    }

    private static void OnContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((ModelVisual3D)dependencyObject).Visual3DModel = (Model3D?)e.NewValue;
    }
}

/// <summary>Renders 3-D content within a two-dimensional visual.</summary>
public sealed partial class Viewport3DVisual : Visual
{
    private readonly Visual3DCollection _children;
    private double _opacity = 1d;
    private Brush? _opacityMask;
    private Geometry? _clip;
    private Jalium.UI.Media.Transform? _transform;
    private Vector _offset;
#pragma warning disable CS0618 // Required for WPF's legacy Viewport3DVisual compatibility surface.
    private BitmapEffect? _bitmapEffect;
    private BitmapEffectInput? _bitmapEffectInput;
#pragma warning restore CS0618

    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(
            nameof(Camera),
            typeof(Camera),
            typeof(Viewport3DVisual),
            new PropertyMetadata(CreateDefaultCamera()));

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(
            nameof(Viewport),
            typeof(Rect),
            typeof(Viewport3DVisual),
            new PropertyMetadata(Rect.Empty));

    public Viewport3DVisual()
    {
        _children = new Visual3DCollection(this);
    }

    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    public Rect Viewport
    {
        get => (Rect)(GetValue(ViewportProperty) ?? Rect.Empty);
        set => SetValue(ViewportProperty, value);
    }

    public Visual3DCollection Children => _children;

    public DependencyObject? Parent => VisualParent;

    public double Opacity
    {
        get => _opacity;
        set => _opacity = value;
    }

    public Brush? OpacityMask
    {
        get => _opacityMask;
        set => _opacityMask = value;
    }

    public Geometry? Clip
    {
        get => _clip;
        set => _clip = value;
    }

    public Jalium.UI.Media.Transform? Transform
    {
        get => _transform;
        set => _transform = value;
    }

    public Vector Offset
    {
        get => _offset;
        set => _offset = value;
    }

    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public BitmapEffect? BitmapEffect
    {
        get => _bitmapEffect;
        set => _bitmapEffect = value;
    }

    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public BitmapEffectInput? BitmapEffectInput
    {
        get => _bitmapEffectInput;
        set => _bitmapEffectInput = value;
    }

    public Rect ContentBounds => CalculateProjectedBounds(includeDescendants: false);

    public Rect DescendantBounds => CalculateProjectedBounds(includeDescendants: true);

    internal Matrix3D GetProjectionToViewportMatrix()
    {
        Rect viewport = Viewport;
        Camera? camera = Camera;
        if (viewport.IsEmpty || viewport.Width == 0d || viewport.Height == 0d || camera is null)
        {
            return Matrix3D.Identity;
        }

        Matrix3D view = camera.GetCombinedViewMatrix();
        Matrix3D projection = camera.GetProjectionMatrix(viewport.Width / viewport.Height);
        Matrix3D viewportMatrix = new(
            viewport.Width / 2d, 0d, 0d, 0d,
            0d, -viewport.Height / 2d, 0d, 0d,
            0d, 0d, 1d, 0d,
            viewport.X + viewport.Width / 2d,
            viewport.Y + viewport.Height / 2d,
            0d,
            1d);
        return view * projection * viewportMatrix;
    }

    private static Camera CreateDefaultCamera()
    {
        var camera = new PerspectiveCamera();
        camera.Freeze();
        return camera;
    }
}
