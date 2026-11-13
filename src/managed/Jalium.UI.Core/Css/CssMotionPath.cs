using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

/// <summary>
/// Size keyword for the CSS <c>ray()</c> offset-path function.
/// </summary>
public enum CssRaySize : byte
{
    ClosestSide,
    ClosestCorner,
    FarthestSide,
    FarthestCorner,
    Sides,
}

/// <summary>
/// Kind of <c>offset-position</c> or <c>offset-anchor</c> value.
/// </summary>
public enum CssOffsetPositionKind : byte
{
    Auto,
    Normal,
    Explicit,
}

/// <summary>
/// Represents an <c>offset-position</c> or <c>offset-anchor</c> coordinate pair.
/// </summary>
internal readonly record struct CssOffsetPosition(
    CssOffsetPositionKind Kind,
    CssLength X,
    CssLength Y)
{
    public bool XFromFarEdge { get; init; }
    public bool YFromFarEdge { get; init; }
    internal double ResolveX(double basis, in CssLengthContext context) => new CssBackgroundPositionAxis(X, XFromFarEdge).Resolve(basis, context);
    internal double ResolveY(double basis, in CssLengthContext context) => new CssBackgroundPositionAxis(Y, YFromFarEdge).Resolve(basis, context);

    public static CssOffsetPosition Auto =>
        new(CssOffsetPositionKind.Auto, new CssLength(50, CssUnit.Percent), new CssLength(50, CssUnit.Percent));

    public static CssOffsetPosition Normal =>
        new(CssOffsetPositionKind.Normal, new CssLength(50, CssUnit.Percent), new CssLength(50, CssUnit.Percent));
}

/// <summary>
/// Represents an <c>offset-rotate</c> value (<c>auto</c>, <c>reverse</c>, or fixed/relative angle).
/// </summary>
public readonly record struct CssOffsetRotate(bool IsAuto, double AngleDegrees)
{
    public static CssOffsetRotate Auto => new(true, 0.0);
    public static CssOffsetRotate Reverse => new(true, 180.0);

    public CssOffsetRotate Interpolate(CssOffsetRotate to, double progress)
    {
        if (IsAuto != to.IsAuto)
            return progress < 0.5 ? this : to;
        return new CssOffsetRotate(IsAuto, AngleDegrees + (to.AngleDegrees - AngleDegrees) * progress);
    }
}

/// <summary>
/// Resolved 2D position, tangent/rotation angle, and composite <see cref="Transform"/> along a CSS Motion Path.
/// </summary>
public readonly record struct CssMotionPathPose(
    double X,
    double Y,
    double AngleDegrees,
    Transform Transform)
{
    public static CssMotionPathPose Identity => new(0.0, 0.0, 0.0, MatrixTransform.Identity);

    public Matrix Value => Transform?.Value ?? Matrix.Identity;

    public Point TransformPoint(Point point) => Value.Transform(point);

    public void Deconstruct(out double x, out double y, out double angleDegrees)
    {
        x = X;
        y = Y;
        angleDegrees = AngleDegrees;
    }

    public void Deconstruct(out double x, out double y, out double angleDegrees, out Transform transform)
    {
        x = X;
        y = Y;
        angleDegrees = AngleDegrees;
        transform = Transform;
    }

    public static implicit operator Transform(CssMotionPathPose pose) => pose.Transform;
}

/// <summary>
/// Kind of <c>offset-path</c> geometry.
/// </summary>
public enum CssOffsetPathKind : byte
{
    None,
    SvgPath,
    Ray,
    Circle,
    Ellipse,
    Inset,
    Rect,
    Xywh,
    Polygon,
}

/// <summary>
/// Compiled representation of an <c>offset-path</c> value.
/// </summary>
internal sealed class CssOffsetPathData
{
    public static readonly CssOffsetPathData None = new() { Kind = CssOffsetPathKind.None };

    public required CssOffsetPathKind Kind { get; init; }
    public string? SvgPathData { get; init; }
    public double RayAngleDegrees { get; init; }
    public CssRaySize RaySize { get; init; } = CssRaySize.ClosestSide;
    public bool RayContain { get; init; }
    public CssLength[] ShapeArgs { get; init; } = [];
    public CssOffsetPosition ShapeCenter { get; init; } = CssOffsetPosition.Normal;
    public Point[]? PolygonPoints { get; init; }
    public (CssLength X, CssLength Y)[]? PolygonLengths { get; init; }
}

/// <summary>
/// CSS Motion Path Module Level 1 implementation (<c>offset-path</c>, <c>offset-distance</c>,
/// <c>offset-rotate</c>, <c>offset-anchor</c>, <c>offset-position</c>, <c>offset</c>).
/// </summary>
public static class CssMotionPath
{
    private sealed class MotionTransformState
    {
        internal Transform? Pose;
        internal Transform? Base;
        internal Matrix Matrix;
        internal Point Origin;
        internal Transform? Composed;
        internal EventHandler? BaseChanged;
        internal void ObserveBase(UIElement element, Transform? original)
        {
            if (ReferenceEquals(Base, original)) return;
            if (Base is not null && BaseChanged is not null) Base.Changed -= BaseChanged;
            Base = original;
            if (original is null) return;
            var owner = new WeakReference<UIElement>(element);
            BaseChanged = (_, _) =>
            {
                if (!owner.TryGetTarget(out var target))
                {
                    if (Base is not null) Base.Changed -= BaseChanged;
                    return;
                }
                var previous = Composed;
                Composed = null;
                target.NotifyCssMotionTransformChanged(previous, target.RenderTransform);
            };
            original.Changed += BaseChanged;
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, MotionTransformState> s_transforms = new();

    internal static object? CoerceRenderTransform(DependencyObject owner, object? value)
    {
        if (owner is not UIElement element || !s_transforms.TryGetValue(element, out var state) || state.Pose is null)
            return value;
        var original = value as Transform;
        var matrix = original?.Value ?? Matrix.Identity;
        var origin = new Point(element.RenderTransformOrigin.X * element.RenderSize.Width,
            element.RenderTransformOrigin.Y * element.RenderSize.Height);
        if (state.Composed is not null && ReferenceEquals(original, state.Base) && state.Matrix == matrix && state.Origin == origin)
            return state.Composed;
        // RenderTransform is applied about its origin by every drawing/hit
        // consumer. Conjugate the path pose so that origin applies only to the
        // ordinary transform, not a second time to the offset anchor.
        var composed = Matrix.Multiply(matrix, new Matrix(1, 0, 0, 1, origin.X, origin.Y));
        composed = Matrix.Multiply(composed, state.Pose.Value);
        composed = Matrix.Multiply(composed, new Matrix(1, 0, 0, 1, -origin.X, -origin.Y));
        var transform = new MatrixTransform(composed);
        if (CssTransform3D.Has3D(original))
        {
            // The 2D projection is not sufficient for the 3D drawing and hit
            // consumers. Carry the original depth/perspective through the
            // same origin conjugation used by the ordinary transform.
            var full = CssTransform3D.GetTransformMatrix3D(original)
                * CssTransform3D.ToMatrix3D(new Matrix(1, 0, 0, 1, origin.X, origin.Y))
                * CssTransform3D.GetTransformMatrix3D(state.Pose)
                * CssTransform3D.ToMatrix3D(new Matrix(1, 0, 0, 1, -origin.X, -origin.Y));
            CssTransform3D.Attach(transform, full,
                [new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix3D, 0, 0, 0, 0, full)]);
        }
        transform.Freeze();
        state.ObserveBase(element, original);
        state.Matrix = matrix; state.Origin = origin; state.Composed = transform;
        return transform;
    }

    internal static bool RefreshTransformOrigin(UIElement element)
        => s_transforms.TryGetValue(element, out _) && SyncElementMotionTransform(element);
    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached(
            "Path",
            typeof(CssOffsetPathData),
            typeof(CssMotionPath),
            new PropertyMetadata(CssOffsetPathData.None, OnMotionPathPropertyChanged));

    public static readonly DependencyProperty DistanceProperty =
        DependencyProperty.RegisterAttached(
            "Distance",
            typeof(CssLength),
            typeof(CssMotionPath),
            new PropertyMetadata(new CssLength(0, CssUnit.Px), OnMotionPathPropertyChanged));

    public static readonly DependencyProperty RotateProperty =
        DependencyProperty.RegisterAttached(
            "Rotate",
            typeof(CssOffsetRotate),
            typeof(CssMotionPath),
            new PropertyMetadata(CssOffsetRotate.Auto, OnMotionPathPropertyChanged));

    public static readonly DependencyProperty AnchorProperty =
        DependencyProperty.RegisterAttached(
            "Anchor",
            typeof(CssOffsetPosition),
            typeof(CssMotionPath),
            new PropertyMetadata(CssOffsetPosition.Auto, OnMotionPathPropertyChanged));

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.RegisterAttached(
            "Position",
            typeof(CssOffsetPosition),
            typeof(CssMotionPath),
            new PropertyMetadata(CssOffsetPosition.Normal, OnMotionPathPropertyChanged));

    private static readonly DependencyProperty IsSizeHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsSizeHooked",
            typeof(bool),
            typeof(CssMotionPath),
            new PropertyMetadata(false));

    static CssMotionPath()
    {
        CssPropertyRegistry.RegisterExtension(Register);
    }

    /// <summary>
    /// Registers CSS Motion Path Level 1 longhands and the <c>offset</c> shorthand.
    /// </summary>
    public static void Register()
    {
        RegisterLonghand("offset-path", PathProperty, "none", ParseOffsetPath);
        RegisterLonghand("offset-distance", DistanceProperty, "0px", ParseOffsetDistance);
        RegisterLonghand("offset-rotate", RotateProperty, "auto", ParseOffsetRotate);
        RegisterLonghand("offset-anchor", AnchorProperty, "auto",
            static (ref CssTokenReader r, CssCompileContext _) => ParseOffsetPositionOrAnchor(ref r, isAnchor: true));
        RegisterLonghand("offset-position", PositionProperty, "normal",
            static (ref CssTokenReader r, CssCompileContext _) => ParseOffsetPositionOrAnchor(ref r, isAnchor: false));
        RegisterShorthand("offset", "normal none 0px auto / auto", ExpandOffsetShorthand);
    }

    internal static CssOffsetPathData GetPath(DependencyObject element)
        => (element.GetValue(PathProperty) as CssOffsetPathData) ?? CssOffsetPathData.None;

    internal static CssLength GetDistance(DependencyObject element)
        => element.GetValue(DistanceProperty) is CssLength len ? len : new CssLength(0, CssUnit.Px);

    public static CssOffsetRotate GetRotate(DependencyObject element)
        => element.GetValue(RotateProperty) is CssOffsetRotate rot ? rot : CssOffsetRotate.Auto;

    internal static CssOffsetPosition GetAnchor(DependencyObject element)
        => element.GetValue(AnchorProperty) is CssOffsetPosition anc ? anc : CssOffsetPosition.Auto;

    internal static CssOffsetPosition GetPosition(DependencyObject element)
        => element.GetValue(PositionProperty) is CssOffsetPosition pos ? pos : CssOffsetPosition.Normal;

    /// <summary>
    /// Resolves the motion path position, rotation angle, and transform for <paramref name="element"/>.
    /// </summary>
    public static CssMotionPathPose ResolveTransform(
        DependencyObject element,
        Size elementSize = default,
        Size containingSize = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        var pathData = GetPath(element);
        if (pathData.Kind == CssOffsetPathKind.None)
            return CssMotionPathPose.Identity;

        if (elementSize.Width <= 0 && elementSize.Height <= 0 && element is UIElement ui)
        {
            elementSize = ui.RenderSize;
            if (elementSize.Width <= 0 && elementSize.Height <= 0 && ui is FrameworkElement fe)
            {
                var w = fe.ActualWidth > 0 ? fe.ActualWidth : (double.IsFinite(fe.Width) ? fe.Width : 0);
                var h = fe.ActualHeight > 0 ? fe.ActualHeight : (double.IsFinite(fe.Height) ? fe.Height : 0);
                elementSize = new Size(w, h);
            }
        }

        if (containingSize.Width <= 0 && containingSize.Height <= 0)
        {
            if (element is Visual v && v.VisualParent is UIElement parentUi)
            {
                containingSize = parentUi.RenderSize;
                if (containingSize.Width <= 0 && containingSize.Height <= 0 && parentUi is FrameworkElement pfe)
                {
                    var pw = pfe.ActualWidth > 0 ? pfe.ActualWidth : (double.IsFinite(pfe.Width) ? pfe.Width : 0);
                    var ph = pfe.ActualHeight > 0 ? pfe.ActualHeight : (double.IsFinite(pfe.Height) ? pfe.Height : 0);
                    containingSize = new Size(pw, ph);
                }
            }

            if (containingSize.Width <= 0 && containingSize.Height <= 0)
                containingSize = new Size(Math.Max(100, elementSize.Width), Math.Max(100, elementSize.Height));
        }

        var lengths = BuildLengthContext(element, containingSize);
        var distanceSpec = GetDistance(element);
        var rotateSpec = GetRotate(element);
        var anchorSpec = GetAnchor(element);
        var positionSpec = GetPosition(element);

        double px;
        double py;
        double tangentDegrees;

        if (pathData.Kind == CssOffsetPathKind.Ray)
        {
            EvaluateRay(pathData, positionSpec, distanceSpec, elementSize, containingSize, in lengths,
                out px, out py, out tangentDegrees);
        }
        else
        {
            var contour = BuildPathContour(pathData, containingSize, in lengths, positionSpec);
            contour.SampleAtDistance(distanceSpec, in lengths, out px, out py, out tangentDegrees);
        }

        var finalAngle = rotateSpec.IsAuto
            ? tangentDegrees + rotateSpec.AngleDegrees
            : rotateSpec.AngleDegrees;

        var (anchorX, anchorY) = ResolveAnchorOffset(element, anchorSpec, elementSize, in lengths);

        var rad = finalAngle * (Math.PI / 180.0);
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        // Transform local point (x, y) around anchor (anchorX, anchorY) and translate anchor to (px, py):
        // x' = (x - anchorX) * cos - (y - anchorY) * sin + px
        // y' = (x - anchorX) * sin + (y - anchorY) * cos + py
        var offsetX = px - (anchorX * cos - anchorY * sin);
        var offsetY = py - (anchorX * sin + anchorY * cos);
        var matrix = new Matrix(cos, sin, -sin, cos, offsetX, offsetY);
        var matrixTransform = new MatrixTransform(matrix);
        if (matrixTransform.CanFreeze)
            matrixTransform.Freeze();

        return new CssMotionPathPose(px, py, finalAngle, matrixTransform);
    }

    internal static IAnimationTimeline? CreateTransitionAnimation(
        DependencyProperty property,
        object? from,
        object? to,
        CssTransition transition)
    {
        if (property == DistanceProperty && from is CssLength fromDist && to is CssLength toDist)
        {
            return new CssOffsetDistanceAnimation
            {
                From = fromDist,
                To = toDist,
                Duration = new Duration(transition.Duration),
                BeginTime = transition.Delay,
                EasingFunction = transition.Timing,
            };
        }

        if (property == RotateProperty && from is CssOffsetRotate fromRot && to is CssOffsetRotate toRot)
        {
            return new CssOffsetRotateAnimation
            {
                From = fromRot,
                To = toRot,
                Duration = new Duration(transition.Duration),
                BeginTime = transition.Delay,
                EasingFunction = transition.Timing,
            };
        }

        if ((property == AnchorProperty || property == PositionProperty) &&
            from is CssOffsetPosition { Kind: CssOffsetPositionKind.Explicit } before &&
            to is CssOffsetPosition { Kind: CssOffsetPositionKind.Explicit } after)
            return new CssOffsetPositionAnimation { From = before, To = after, Duration = new Duration(transition.Duration),
                BeginTime = transition.Delay, EasingFunction = transition.Timing };

        return null;
    }

    internal static object? SampleKeyframeValue(
        DependencyProperty property,
        object? from,
        object? to,
        double easedProgress)
    {
        if (property == DistanceProperty && from is CssLength fromDist && to is CssLength toDist)
        {
            return InterpolateLength(fromDist, toDist, easedProgress);
        }

        if (property == RotateProperty && from is CssOffsetRotate fromRot && to is CssOffsetRotate toRot)
        {
            return fromRot.Interpolate(toRot, easedProgress);
        }

        if ((property == AnchorProperty || property == PositionProperty) &&
            from is CssOffsetPosition { Kind: CssOffsetPositionKind.Explicit } before &&
            to is CssOffsetPosition { Kind: CssOffsetPositionKind.Explicit } after)
            return InterpolatePosition(before, after, easedProgress);

        return null;
    }

    internal static CssLength InterpolateLength(CssLength a, CssLength b, double t)
    {
        if (t == 0) return a;
        if (t == 1) return b;
        if (a.Unit == b.Unit && a.Expression is null && b.Expression is null)
            return new CssLength(a.Value + (b.Value - a.Value) * t, a.Unit);

        if (a.Expression is null && a.Value == 0 && b.Unit == CssUnit.Percent)
            return new CssLength(b.Value * t, CssUnit.Percent);
        if (b.Expression is null && b.Value == 0 && a.Unit == CssUnit.Percent)
            return new CssLength(a.Value * (1.0 - t), CssUnit.Percent);

        // Keep percentages and relative units until the real box/font/viewport
        // context is known, rather than sampling a fictitious 100px element.
        var first = Expression(a); var second = Expression(b);
        var type = new CssNumericType(Length: 1, PercentHint: CssNumericKind.Length);
        CssMathExpression Scale(CssMathExpression value, double weight) =>
            new("*", type, default, [value, new("value", CssNumericType.Of(CssNumericKind.Number), new(weight, CssUnit.None), [])]);
        return new CssLength(new CssMathExpression("+", type, default, [Scale(first, 1 - t), Scale(second, t)]));
    }

    private static CssMathExpression Expression(CssLength value) => value.Expression ??
        new("value", CssNumericType.Of(value.Unit == CssUnit.Percent ? CssNumericKind.Percent : CssNumericKind.Length), value, []);

    internal static CssOffsetPosition InterpolatePosition(CssOffsetPosition a, CssOffsetPosition b, double t)
    {
        if (t == 0) return a;
        if (t == 1) return b;
        static CssLength Near(CssLength length, bool far) => !far ? length : new(new CssMathExpression("-",
            new CssNumericType(Length: 1, PercentHint: CssNumericKind.Length), default,
            [Expression(new CssLength(100, CssUnit.Percent)), Expression(length)]));
        return new(CssOffsetPositionKind.Explicit,
            InterpolateLength(Near(a.X, a.XFromFarEdge), Near(b.X, b.XFromFarEdge), t),
            InterpolateLength(Near(a.Y, a.YFromFarEdge), Near(b.Y, b.YFromFarEdge), t));
    }

    internal static double ResolveLengthOrPercent(CssLength length, in CssLengthContext context, double basisPx)
    {
        if (length.Unit == CssUnit.Percent)
            return (length.Value / 100.0) * basisPx;
        if (length.Unit == CssUnit.Expression && length.Expression is { } expr)
            return expr.TryEvaluate(context, basisPx, out var eval) && double.IsFinite(eval) ? eval : 0.0;
        return length.TryResolve(in context, CssPercentBasis.Fraction, out var px) && double.IsFinite(px) ? px : 0.0;
    }

    private static void OnMotionPathPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement ui)
            return;

        if (d is FrameworkElement fe && !(bool)fe.GetValue(IsSizeHookedProperty)!)
        {
            fe.SetValue(IsSizeHookedProperty, true);
            fe.SizeChanged += (_, _) => SyncElementMotionTransform(fe);
            fe.LayoutUpdated += (_, _) => SyncElementMotionTransform(fe);
        }

        SyncElementMotionTransform(ui);
    }

    internal static bool SyncElementMotionTransform(UIElement ui)
    {
        var pathData = GetPath(ui);
        if (pathData.Kind == CssOffsetPathKind.None && !s_transforms.TryGetValue(ui, out _)) return false;
        var state = s_transforms.GetOrCreateValue(ui);
        var next = pathData.Kind == CssOffsetPathKind.None ? null : ResolveTransform(ui).Transform;
        var previous = state.Composed ?? ui.RenderTransform;
        if (state.Pose?.Value != next?.Value) state.Composed = null;
        state.Pose = next;
        if (next is null) state.ObserveBase(ui, null);
        return ui.NotifyCssMotionTransformChanged(previous, ui.RenderTransform);
    }

    private static (double X, double Y) ResolveAnchorOffset(
        DependencyObject element,
        CssOffsetPosition anchor,
        Size elementSize,
        in CssLengthContext lengths)
    {
        var reference = CssTransformReferenceBox.Current(element, elementSize);
        if (anchor.Kind == CssOffsetPositionKind.Explicit)
        {
            return (
                reference.X + anchor.ResolveX(reference.Width, lengths),
                reference.Y + anchor.ResolveY(reference.Height, lengths));
        }

        if (element is UIElement ui &&
            (ui.GetEffectiveValueLayer(UIElement.RenderTransformOriginProperty) is not null ||
             ui.HasAnimatedValue(UIElement.RenderTransformOriginProperty) ||
             ui.HasEffectiveCssValue(UIElement.RenderTransformOriginProperty)))
        {
            var origin = ui.RenderTransformOrigin;
            return (origin.X * elementSize.Width, origin.Y * elementSize.Height);
        }

        return (reference.X + reference.Width * .5, reference.Y + reference.Height * .5);
    }

    private static void EvaluateRay(
        CssOffsetPathData pathData,
        CssOffsetPosition positionSpec,
        CssLength distanceSpec,
        Size elementSize,
        Size containingSize,
        in CssLengthContext lengths,
        out double px,
        out double py,
        out double tangentDegrees)
    {
        double x0;
        double y0;
        var rayOrigin = pathData.ShapeCenter.Kind == CssOffsetPositionKind.Explicit ? pathData.ShapeCenter : positionSpec;
        if (rayOrigin.Kind == CssOffsetPositionKind.Explicit)
        {
            x0 = rayOrigin.ResolveX(containingSize.Width, lengths);
            y0 = rayOrigin.ResolveY(containingSize.Height, lengths);
        }
        else
        {
            x0 = containingSize.Width * 0.5;
            y0 = containingSize.Height * 0.5;
        }

        // CSS ray() angle: 0deg points up (0, -1), 90deg points right (+1, 0), 180deg points down (0, +1).
        var rad = pathData.RayAngleDegrees * (Math.PI / 180.0);
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        if (Math.Abs(dx) < 1e-12) dx = 0.0;
        if (Math.Abs(dy) < 1e-12) dy = 0.0;

        // Tangent angle in standard screen coordinates (+X = 0deg, +Y = 90deg) is (bearing - 90deg).
        tangentDegrees = pathData.RayAngleDegrees - 90.0;

        var rayLength = ComputeRayLength(x0, y0, dx, dy, containingSize, pathData.RaySize);
        if (pathData.RayContain && (elementSize.Width > 0 || elementSize.Height > 0))
        {
            var halfExtent = 0.5 * Math.Max(elementSize.Width, elementSize.Height);
            rayLength = Math.Max(0.0, rayLength - halfExtent);
        }

        var distPx = ResolveLengthOrPercent(distanceSpec, in lengths, rayLength);
        px = x0 + dx * distPx;
        py = y0 + dy * distPx;
    }

    private static double ComputeRayLength(
        double x0,
        double y0,
        double dx,
        double dy,
        Size box,
        CssRaySize raySize)
    {
        var w = Math.Max(0.0, box.Width);
        var h = Math.Max(0.0, box.Height);
        var dLeft = Math.Abs(x0);
        var dRight = Math.Abs(w - x0);
        var dTop = Math.Abs(y0);
        var dBottom = Math.Abs(h - y0);

        switch (raySize)
        {
            case CssRaySize.ClosestSide:
                return Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
            case CssRaySize.FarthestSide:
                return Math.Max(Math.Max(dLeft, dRight), Math.Max(dTop, dBottom));
            case CssRaySize.ClosestCorner:
            {
                var c0 = Hypot(x0, y0);
                var c1 = Hypot(w - x0, y0);
                var c2 = Hypot(x0, h - y0);
                var c3 = Hypot(w - x0, h - y0);
                return Math.Min(Math.Min(c0, c1), Math.Min(c2, c3));
            }
            case CssRaySize.FarthestCorner:
            {
                var c0 = Hypot(x0, y0);
                var c1 = Hypot(w - x0, y0);
                var c2 = Hypot(x0, h - y0);
                var c3 = Hypot(w - x0, h - y0);
                return Math.Max(Math.Max(c0, c1), Math.Max(c2, c3));
            }
            case CssRaySize.Sides:
            {
                if (x0 < 0 || x0 > w || y0 < 0 || y0 > h)
                    return 0.0;
                var tx = dx > 1e-9 ? (w - x0) / dx : (dx < -1e-9 ? -x0 / dx : double.PositiveInfinity);
                var ty = dy > 1e-9 ? (h - y0) / dy : (dy < -1e-9 ? -y0 / dy : double.PositiveInfinity);
                var t = Math.Min(tx, ty);
                return double.IsFinite(t) && t > 0 ? t : 0.0;
            }
            default:
                return 0.0;
        }
    }

    private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    private static PathContour BuildPathContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths,
        CssOffsetPosition position)
    {
        var center = data.ShapeCenter.Kind == CssOffsetPositionKind.Explicit ? data.ShapeCenter : position;
        return data.Kind switch
        {
            CssOffsetPathKind.SvgPath => ParseSvgContour(data.SvgPathData ?? string.Empty),
            CssOffsetPathKind.Circle => BuildCircleContour(data, box, in lengths, center),
            CssOffsetPathKind.Ellipse => BuildEllipseContour(data, box, in lengths, center),
            CssOffsetPathKind.Inset => BuildInsetContour(data, box, in lengths),
            CssOffsetPathKind.Rect => BuildRectContour(data, box, in lengths),
            CssOffsetPathKind.Xywh => BuildXywhContour(data, box, in lengths),
            CssOffsetPathKind.Polygon => BuildPolygonContour(data, box, in lengths),
            _ => PathContour.Empty,
        };
    }

    private static PathContour BuildCircleContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths,
        CssOffsetPosition center)
    {
        var cx = center.ResolveX(box.Width, lengths);
        var cy = center.ResolveY(box.Height, lengths);
        var refLen = Math.Sqrt(box.Width * box.Width + box.Height * box.Height) / Math.Sqrt(2.0);
        double r;
        if (data.ShapeArgs.Length > 0 && data.ShapeArgs[0].Unit != CssUnit.Auto)
        {
            r = Math.Max(0, ResolveLengthOrPercent(data.ShapeArgs[0], in lengths, refLen));
        }
        else
        {
            r = Math.Min(
                Math.Min(Math.Abs(cx), Math.Abs(box.Width - cx)),
                Math.Min(Math.Abs(cy), Math.Abs(box.Height - cy)));
        }

        return PathContour.CreateCircle(cx, cy, r);
    }

    private static PathContour BuildEllipseContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths,
        CssOffsetPosition center)
    {
        var cx = center.ResolveX(box.Width, lengths);
        var cy = center.ResolveY(box.Height, lengths);
        double rx;
        double ry;
        if (data.ShapeArgs.Length >= 2 && data.ShapeArgs[0].Unit != CssUnit.Auto && data.ShapeArgs[1].Unit != CssUnit.Auto)
        {
            rx = Math.Max(0, ResolveLengthOrPercent(data.ShapeArgs[0], in lengths, box.Width));
            ry = Math.Max(0, ResolveLengthOrPercent(data.ShapeArgs[1], in lengths, box.Height));
        }
        else
        {
            rx = Math.Min(Math.Abs(cx), Math.Abs(box.Width - cx));
            ry = Math.Min(Math.Abs(cy), Math.Abs(box.Height - cy));
        }

        return PathContour.CreateEllipse(cx, cy, rx, ry);
    }

    private static PathContour BuildInsetContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths)
    {
        var args = data.ShapeArgs;
        if (args.Length < 4)
            return PathContour.CreateRect(0, 0, box.Width, box.Height);

        var top = ResolveLengthOrPercent(args[0], in lengths, box.Height);
        var right = ResolveLengthOrPercent(args[1], in lengths, box.Width);
        var bottom = ResolveLengthOrPercent(args[2], in lengths, box.Height);
        var left = ResolveLengthOrPercent(args[3], in lengths, box.Width);
        var w = Math.Max(0, box.Width - left - right);
        var h = Math.Max(0, box.Height - top - bottom);
        return PathContour.CreateRect(left, top, w, h);
    }

    private static PathContour BuildRectContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths)
    {
        var args = data.ShapeArgs;
        if (args.Length < 4)
            return PathContour.CreateRect(0, 0, box.Width, box.Height);

        var top = args[0].Unit == CssUnit.Auto ? 0.0 : ResolveLengthOrPercent(args[0], in lengths, box.Height);
        var right = args[1].Unit == CssUnit.Auto ? box.Width : ResolveLengthOrPercent(args[1], in lengths, box.Width);
        var bottom = args[2].Unit == CssUnit.Auto ? box.Height : ResolveLengthOrPercent(args[2], in lengths, box.Height);
        var left = args[3].Unit == CssUnit.Auto ? 0.0 : ResolveLengthOrPercent(args[3], in lengths, box.Width);
        return PathContour.CreateRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static PathContour BuildXywhContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths)
    {
        var args = data.ShapeArgs;
        if (args.Length < 4)
            return PathContour.CreateRect(0, 0, box.Width, box.Height);

        var x = ResolveLengthOrPercent(args[0], in lengths, box.Width);
        var y = ResolveLengthOrPercent(args[1], in lengths, box.Height);
        var w = Math.Max(0, ResolveLengthOrPercent(args[2], in lengths, box.Width));
        var h = Math.Max(0, ResolveLengthOrPercent(args[3], in lengths, box.Height));
        return PathContour.CreateRect(x, y, w, h);
    }

    private static PathContour BuildPolygonContour(
        CssOffsetPathData data,
        Size box,
        in CssLengthContext lengths)
    {
        if (data.PolygonLengths is not { Length: >= 2 } pairs)
            return PathContour.Empty;

        var pts = new Point[pairs.Length];
        for (var i = 0; i < pairs.Length; i++)
        {
            pts[i] = new Point(
                ResolveLengthOrPercent(pairs[i].X, in lengths, box.Width),
                ResolveLengthOrPercent(pairs[i].Y, in lengths, box.Height));
        }

        return PathContour.CreatePolygon(pts, closed: true);
    }

    private static PathContour ParseSvgContour(string d)
    {
        var segments = new List<PathSegmentEntry>();
        var index = 0;
        var curX = 0.0;
        var curY = 0.0;
        var startX = 0.0;
        var startY = 0.0;
        var prevCtrlX = 0.0;
        var prevCtrlY = 0.0;
        var lastCmd = '\0';
        var isClosed = false;

        while (SkipSvgWsAndCommas(d, ref index))
        {
            char cmd;
            if (char.IsLetter(d[index]))
            {
                cmd = d[index++];
            }
            else if (lastCmd != '\0')
            {
                cmd = lastCmd switch
                {
                    'M' => 'L',
                    'm' => 'l',
                    _ => lastCmd,
                };
            }
            else
            {
                break;
            }

            var rel = char.IsLower(cmd);
            var upper = char.ToUpperInvariant(cmd);

            switch (upper)
            {
                case 'M':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    curX = rel ? curX + x : x;
                    curY = rel ? curY + y : y;
                    startX = curX;
                    startY = curY;
                    prevCtrlX = curX;
                    prevCtrlY = curY;
                    break;
                }
                case 'L':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var nx = rel ? curX + x : x;
                    var ny = rel ? curY + y : y;
                    AddLineSegment(segments, curX, curY, nx, ny);
                    curX = nx;
                    curY = ny;
                    prevCtrlX = curX;
                    prevCtrlY = curY;
                    break;
                }
                case 'H':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x))
                        break;
                    var nx = rel ? curX + x : x;
                    AddLineSegment(segments, curX, curY, nx, curY);
                    curX = nx;
                    prevCtrlX = curX;
                    prevCtrlY = curY;
                    break;
                }
                case 'V':
                {
                    if (!TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var ny = rel ? curY + y : y;
                    AddLineSegment(segments, curX, curY, curX, ny);
                    curY = ny;
                    prevCtrlX = curX;
                    prevCtrlY = curY;
                    break;
                }
                case 'C':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x1) || !TryReadSvgNumber(d, ref index, out var y1) ||
                        !TryReadSvgNumber(d, ref index, out var x2) || !TryReadSvgNumber(d, ref index, out var y2) ||
                        !TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var c1x = rel ? curX + x1 : x1;
                    var c1y = rel ? curY + y1 : y1;
                    var c2x = rel ? curX + x2 : x2;
                    var c2y = rel ? curY + y2 : y2;
                    var nx = rel ? curX + x : x;
                    var ny = rel ? curY + y : y;
                    AddCubicBezierSegments(segments, curX, curY, c1x, c1y, c2x, c2y, nx, ny);
                    prevCtrlX = c2x;
                    prevCtrlY = c2y;
                    curX = nx;
                    curY = ny;
                    break;
                }
                case 'S':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x2) || !TryReadSvgNumber(d, ref index, out var y2) ||
                        !TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var c1x = lastCmd is 'C' or 'c' or 'S' or 's' ? 2 * curX - prevCtrlX : curX;
                    var c1y = lastCmd is 'C' or 'c' or 'S' or 's' ? 2 * curY - prevCtrlY : curY;
                    var c2x = rel ? curX + x2 : x2;
                    var c2y = rel ? curY + y2 : y2;
                    var nx = rel ? curX + x : x;
                    var ny = rel ? curY + y : y;
                    AddCubicBezierSegments(segments, curX, curY, c1x, c1y, c2x, c2y, nx, ny);
                    prevCtrlX = c2x;
                    prevCtrlY = c2y;
                    curX = nx;
                    curY = ny;
                    break;
                }
                case 'Q':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x1) || !TryReadSvgNumber(d, ref index, out var y1) ||
                        !TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var c1x = rel ? curX + x1 : x1;
                    var c1y = rel ? curY + y1 : y1;
                    var nx = rel ? curX + x : x;
                    var ny = rel ? curY + y : y;
                    AddQuadraticBezierSegments(segments, curX, curY, c1x, c1y, nx, ny);
                    prevCtrlX = c1x;
                    prevCtrlY = c1y;
                    curX = nx;
                    curY = ny;
                    break;
                }
                case 'T':
                {
                    if (!TryReadSvgNumber(d, ref index, out var x) || !TryReadSvgNumber(d, ref index, out var y))
                        break;
                    var c1x = lastCmd is 'Q' or 'q' or 'T' or 't' ? 2 * curX - prevCtrlX : curX;
                    var c1y = lastCmd is 'Q' or 'q' or 'T' or 't' ? 2 * curY - prevCtrlY : curY;
                    var nx = rel ? curX + x : x;
                    var ny = rel ? curY + y : y;
                    AddQuadraticBezierSegments(segments, curX, curY, c1x, c1y, nx, ny);
                    prevCtrlX = c1x;
                    prevCtrlY = c1y;
                    curX = nx;
                    curY = ny;
                    break;
                }
                case 'Z':
                {
                    AddLineSegment(segments, curX, curY, startX, startY);
                    curX = startX;
                    curY = startY;
                    prevCtrlX = curX;
                    prevCtrlY = curY;
                    isClosed = true;
                    break;
                }
                default:
                    return new PathContour(segments.ToArray(), isClosed);
            }

            lastCmd = cmd;
        }

        return new PathContour(segments.ToArray(), isClosed);
    }

    private static void AddLineSegment(
        List<PathSegmentEntry> segments,
        double x0,
        double y0,
        double x1,
        double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 1e-9)
            return;
        var angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        segments.Add(new PathSegmentEntry(x0, y0, x1, y1, len, angle, angle));
    }

    private static void AddCubicBezierSegments(
        List<PathSegmentEntry> segments,
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3)
    {
        const int steps = 64;
        var prevX = x0;
        var prevY = y0;
        var prevAngle = EvalCubicTangentAngle(x0, y0, x1, y1, x2, y2, x3, y3, 0.0);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var u = 1.0 - t;
            var bx = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            var by = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            var curAngle = EvalCubicTangentAngle(x0, y0, x1, y1, x2, y2, x3, y3, t);
            var dx = bx - prevX;
            var dy = by - prevY;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-9)
            {
                segments.Add(new PathSegmentEntry(prevX, prevY, bx, by, len, prevAngle, curAngle));
            }
            prevX = bx;
            prevY = by;
            prevAngle = curAngle;
        }
    }

    private static double EvalCubicTangentAngle(
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        double t)
    {
        var u = 1.0 - t;
        var dx = 3 * u * u * (x1 - x0) + 6 * u * t * (x2 - x1) + 3 * t * t * (x3 - x2);
        var dy = 3 * u * u * (y1 - y0) + 6 * u * t * (y2 - y1) + 3 * t * t * (y3 - y2);
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            dx = x3 - x0;
            dy = y3 - y0;
        }
        return Math.Atan2(dy, dx) * (180.0 / Math.PI);
    }

    private static void AddQuadraticBezierSegments(
        List<PathSegmentEntry> segments,
        double x0, double y0,
        double x1, double y1,
        double x2, double y2)
    {
        var c1x = x0 + (2.0 / 3.0) * (x1 - x0);
        var c1y = y0 + (2.0 / 3.0) * (y1 - y0);
        var c2x = x2 + (2.0 / 3.0) * (x1 - x2);
        var c2y = y2 + (2.0 / 3.0) * (y1 - y2);
        AddCubicBezierSegments(segments, x0, y0, c1x, c1y, c2x, c2y, x2, y2);
    }

    private static bool SkipSvgWsAndCommas(string s, ref int index)
    {
        while (index < s.Length && (char.IsWhiteSpace(s[index]) || s[index] == ','))
            index++;
        return index < s.Length;
    }

    private static bool TryReadSvgNumber(string s, ref int index, out double value)
    {
        value = 0;
        if (!SkipSvgWsAndCommas(s, ref index))
            return false;

        var start = index;
        if (index < s.Length && (s[index] == '+' || s[index] == '-'))
            index++;
        var hasDigits = false;
        while (index < s.Length && char.IsDigit(s[index]))
        {
            hasDigits = true;
            index++;
        }
        if (index < s.Length && s[index] == '.')
        {
            index++;
            while (index < s.Length && char.IsDigit(s[index]))
            {
                hasDigits = true;
                index++;
            }
        }
        if (!hasDigits)
        {
            index = start;
            return false;
        }
        if (index < s.Length && (s[index] == 'e' || s[index] == 'E'))
        {
            index++;
            if (index < s.Length && (s[index] == '+' || s[index] == '-'))
                index++;
            while (index < s.Length && char.IsDigit(s[index]))
                index++;
        }

        return double.TryParse(s.AsSpan(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct PathSegmentEntry(
        double X0,
        double Y0,
        double X1,
        double Y1,
        double Length,
        double StartAngleDegrees,
        double EndAngleDegrees);

    private sealed class PathContour
    {
        public static readonly PathContour Empty = new([], false);

        private readonly PathSegmentEntry[] _segments;
        private readonly double _totalLength;
        private readonly bool _isClosed;
        private readonly bool _isAnalyticCircle;
        private readonly double _cx;
        private readonly double _cy;
        private readonly double _radius;

        public PathContour(PathSegmentEntry[] segments, bool isClosed)
        {
            _segments = segments;
            _isClosed = isClosed;
            var sum = 0.0;
            for (var i = 0; i < segments.Length; i++)
                sum += segments[i].Length;
            _totalLength = sum;
        }

        private PathContour(double cx, double cy, double radius)
        {
            _segments = [];
            _isClosed = true;
            _isAnalyticCircle = true;
            _cx = cx;
            _cy = cy;
            _radius = Math.Max(0, radius);
            _totalLength = 2.0 * Math.PI * _radius;
        }

        public static PathContour CreateCircle(double cx, double cy, double r)
            => new(cx, cy, r);

        public static PathContour CreateEllipse(double cx, double cy, double rx, double ry)
        {
            if (Math.Abs(rx - ry) < 1e-6)
                return CreateCircle(cx, cy, rx);

            const int steps = 128;
            var segs = new List<PathSegmentEntry>(steps);
            var prevX = cx + rx;
            var prevY = cy;
            var prevAngle = 90.0;
            for (var i = 1; i <= steps; i++)
            {
                var phi = (2.0 * Math.PI * i) / steps;
                var nx = cx + rx * Math.Cos(phi);
                var ny = cy + ry * Math.Sin(phi);
                var tx = -rx * Math.Sin(phi);
                var ty = ry * Math.Cos(phi);
                var angle = Math.Atan2(ty, tx) * (180.0 / Math.PI);
                var len = Hypot(nx - prevX, ny - prevY);
                if (len > 1e-9)
                    segs.Add(new PathSegmentEntry(prevX, prevY, nx, ny, len, prevAngle, angle));
                prevX = nx;
                prevY = ny;
                prevAngle = angle;
            }
            return new PathContour(segs.ToArray(), isClosed: true);
        }

        public static PathContour CreateRect(double x, double y, double w, double h)
        {
            var pts = new[]
            {
                new Point(x, y),
                new Point(x + w, y),
                new Point(x + w, y + h),
                new Point(x, y + h),
            };
            return CreatePolygon(pts, closed: true);
        }

        public static PathContour CreatePolygon(Point[] pts, bool closed)
        {
            if (pts.Length < 2)
                return Empty;
            var segs = new List<PathSegmentEntry>(pts.Length);
            for (var i = 0; i < pts.Length - 1; i++)
                AddLineSegment(segs, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
            if (closed && pts.Length >= 2)
                AddLineSegment(segs, pts[^1].X, pts[^1].Y, pts[0].X, pts[0].Y);
            return new PathContour(segs.ToArray(), closed);
        }

        public void SampleAtDistance(
            CssLength distanceSpec,
            in CssLengthContext lengths,
            out double px,
            out double py,
            out double tangentDegrees)
        {
            if (_isAnalyticCircle)
            {
                if (_totalLength <= 1e-9)
                {
                    px = _cx;
                    py = _cy;
                    tangentDegrees = 0.0;
                    return;
                }

                var d = ResolveLengthOrPercent(distanceSpec, in lengths, _totalLength);
                var u = d / _totalLength;
                var phi = 2.0 * Math.PI * u;
                // Motion Path 1 equivalent paths start at the rightmost point
                // and proceed clockwise, just like an SVG circle or ellipse.
                px = _cx + _radius * Math.Cos(phi);
                py = _cy + _radius * Math.Sin(phi);
                tangentDegrees = u * 360.0 + 90.0;
                return;
            }

            if (_segments.Length == 0 || _totalLength <= 1e-9)
            {
                px = 0.0;
                py = 0.0;
                tangentDegrees = 0.0;
                return;
            }

            var targetDist = ResolveLengthOrPercent(distanceSpec, in lengths, _totalLength);
            if (_isClosed)
            {
                if (targetDist < 0 || targetDist > _totalLength)
                {
                    targetDist %= _totalLength;
                    if (targetDist < 0)
                        targetDist += _totalLength;
                }
            }
            else
            {
                targetDist = Math.Clamp(targetDist, 0.0, _totalLength);
            }

            var acc = 0.0;
            for (var i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (targetDist <= acc + seg.Length || i == _segments.Length - 1)
                {
                    var localT = seg.Length > 1e-9 ? Math.Clamp((targetDist - acc) / seg.Length, 0.0, 1.0) : 0.0;
                    px = seg.X0 + (seg.X1 - seg.X0) * localT;
                    py = seg.Y0 + (seg.Y1 - seg.Y0) * localT;
                    tangentDegrees = InterpolateAngleDegrees(seg.StartAngleDegrees, seg.EndAngleDegrees, localT);
                    return;
                }

                acc += seg.Length;
            }

            var last = _segments[^1];
            px = last.X1;
            py = last.Y1;
            tangentDegrees = last.EndAngleDegrees;
        }

        private static double InterpolateAngleDegrees(double a, double b, double t)
        {
            var diff = b - a;
            while (diff > 180.0) diff -= 360.0;
            while (diff < -180.0) diff += 360.0;
            return a + diff * t;
        }
    }

    private static CssLengthContext BuildLengthContext(DependencyObject target, Size containingSize)
    {
        var node = CssNode.Get(target);
        var context = CssEngine.BuildLengthContext(node);
        if (context.ViewportWidth > 0 && context.ViewportHeight > 0) return context;
        var viewport = new Size(Math.Max(1, containingSize.Width), Math.Max(1, containingSize.Height));
        return new CssLengthContext(context.ElementFontSize, context.InheritedFontSize, context.RootFontSize,
            viewport.Width, viewport.Height, context.Containers, context.Fonts,
            CssViewportMetrics.Uniform(viewport), node, node);
    }

    private static void RegisterLonghand(
        string name,
        DependencyProperty property,
        string initialValue,
        CssParseValueDelegate parse)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = property,
            TransitionTargetDpName = property.Name,
            Parse = parse,
        });
    }

    private static void RegisterShorthand(string name, string initialValue, CssExpandDelegate expand)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Shorthand,
            Expand = expand,
        });
    }

    private static CssCompiledValue? ParseOffsetPath(ref CssTokenReader reader, CssCompileContext context)
    {
        if (!TryReadOffsetPathData(ref reader, out var data) || !reader.AtEnd)
            return null;

        return new CssImmediateValue(PathProperty, data);
    }

    internal static bool TryReadOffsetPathData(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var probe = reader;

        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                data = CssOffsetPathData.None;
                return true;
            }
            return false;
        }

        if (!probe.TryReadFunction(out var func, out var argsReader))
            return false;

        if (func.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            var inner = argsReader;
            if (inner.TryReadIdent(out var ruleIdent) &&
                (ruleIdent.Equals("nonzero", StringComparison.OrdinalIgnoreCase) ||
                 ruleIdent.Equals("evenodd", StringComparison.OrdinalIgnoreCase)) &&
                inner.TryReadComma())
            {
                argsReader = inner;
            }

            if (!argsReader.TryReadString(out var pathText) || !argsReader.AtEnd)
                return false;

            reader = probe;
            data = new CssOffsetPathData
            {
                Kind = CssOffsetPathKind.SvgPath,
                SvgPathData = pathText,
            };
            return true;
        }

        if (func.Equals("ray", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseRayFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCircleFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseEllipseFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("inset", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseInsetFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("rect", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseRectFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("xywh", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseXywhFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        if (func.Equals("polygon", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePolygonFunction(ref argsReader, out data))
                return false;
            reader = probe;
            return true;
        }

        return false;
    }

    private static bool TryParseRayFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        double? angle = null;
        var raySize = CssRaySize.ClosestSide;
        var hasSize = false;
        var contain = false;
        var center = CssOffsetPosition.Normal;
        var hasCenter = false;

        while (!reader.AtEnd)
        {
            if (angle is null && TryReadAngleDegrees(ref reader, out var deg))
            {
                angle = deg;
                continue;
            }

            if (reader.TryReadIdent(out var ident))
            {
                if (!hasCenter && ident.Equals("at", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositionPair(ref reader, out center)) return false;
                    hasCenter = true;
                    continue;
                }

                if (!contain && ident.Equals("contain", StringComparison.OrdinalIgnoreCase))
                {
                    contain = true;
                    continue;
                }

                if (!hasSize && TryParseRaySizeKeyword(ident, out var parsedSize))
                {
                    raySize = parsedSize;
                    hasSize = true;
                    continue;
                }
            }

            return false;
        }

        if (angle is null)
            return false;

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Ray,
            RayAngleDegrees = angle.Value,
            RaySize = raySize,
            RayContain = contain,
            ShapeCenter = center,
        };
        return true;
    }

    private static bool TryReadAngleDegrees(ref CssTokenReader reader, out double degrees)
    {
        var probe = reader;
        if (probe.TryReadNumber(out var value, out var unit) &&
            unit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn &&
            CssUnitConversion.TryToDegrees(value, unit, out degrees))
        {
            reader = probe;
            return true;
        }

        degrees = 0.0;
        return false;
    }

    private static bool TryParseRaySizeKeyword(ReadOnlySpan<char> ident, out CssRaySize size)
    {
        if (ident.Equals("closest-side", StringComparison.OrdinalIgnoreCase))
        {
            size = CssRaySize.ClosestSide;
            return true;
        }
        if (ident.Equals("closest-corner", StringComparison.OrdinalIgnoreCase))
        {
            size = CssRaySize.ClosestCorner;
            return true;
        }
        if (ident.Equals("farthest-side", StringComparison.OrdinalIgnoreCase))
        {
            size = CssRaySize.FarthestSide;
            return true;
        }
        if (ident.Equals("farthest-corner", StringComparison.OrdinalIgnoreCase))
        {
            size = CssRaySize.FarthestCorner;
            return true;
        }
        if (ident.Equals("sides", StringComparison.OrdinalIgnoreCase))
        {
            size = CssRaySize.Sides;
            return true;
        }
        size = default;
        return false;
    }

    private static bool TryParseCircleFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var radius = new CssLength(0, CssUnit.Auto);
        var center = CssOffsetPosition.Normal;

        var probe = reader;
        if (probe.TryReadLength(out var r))
        {
            if (r.Value < 0) return false;
            reader = probe;
            radius = r;
        }
        else if (probe.TryReadIdent(out var rIdent) &&
                 (rIdent.Equals("closest-side", StringComparison.OrdinalIgnoreCase) ||
                  rIdent.Equals("farthest-side", StringComparison.OrdinalIgnoreCase)))
        {
            reader = probe;
        }

        var atProbe = reader;
        if (atProbe.TryReadIdent(out var atIdent) && atIdent.Equals("at", StringComparison.OrdinalIgnoreCase))
        {
            reader = atProbe;
            if (!TryReadPositionPair(ref reader, out center))
                return false;
        }

        if (!reader.AtEnd)
            return false;

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Circle,
            ShapeArgs = [radius],
            ShapeCenter = center,
        };
        return true;
    }

    private static bool TryParseEllipseFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var rx = new CssLength(0, CssUnit.Auto);
        var ry = new CssLength(0, CssUnit.Auto);
        var center = CssOffsetPosition.Normal;

        var probe = reader;
        if (probe.TryReadLength(out var r1) && probe.TryReadLength(out var r2))
        {
            if (r1.Value < 0 || r2.Value < 0) return false;
            reader = probe;
            rx = r1;
            ry = r2;
        }

        var atProbe = reader;
        if (atProbe.TryReadIdent(out var atIdent) && atIdent.Equals("at", StringComparison.OrdinalIgnoreCase))
        {
            reader = atProbe;
            if (!TryReadPositionPair(ref reader, out center))
                return false;
        }

        if (!reader.AtEnd)
            return false;

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Ellipse,
            ShapeArgs = [rx, ry],
            ShapeCenter = center,
        };
        return true;
    }

    private static bool TryParseInsetFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var values = new List<CssLength>(4);
        while (values.Count < 4)
        {
            var probe = reader;
            if (probe.TryReadLength(out var len))
            {
                reader = probe;
                values.Add(len);
            }
            else
            {
                break;
            }
        }

        if (values.Count == 0)
            return false;

        // Skip optional "round ..." clause until end of arguments.
        var roundProbe = reader;
        if (roundProbe.TryReadIdent(out var roundIdent) && roundIdent.Equals("round", StringComparison.OrdinalIgnoreCase))
        {
            reader = roundProbe;
            while (!reader.AtEnd)
            {
                if (!reader.TryReadLength(out _) && !reader.TryReadSlash())
                    return false;
            }
        }
        else if (!reader.AtEnd)
        {
            return false;
        }

        var (t, r, b, l) = values.Count switch
        {
            1 => (values[0], values[0], values[0], values[0]),
            2 => (values[0], values[1], values[0], values[1]),
            3 => (values[0], values[1], values[2], values[1]),
            _ => (values[0], values[1], values[2], values[3]),
        };

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Inset,
            ShapeArgs = [t, r, b, l],
        };
        return true;
    }

    private static bool TryParseRectFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var values = new CssLength[4];
        for (var i = 0; i < 4; i++)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                values[i] = new CssLength(0, CssUnit.Auto);
            }
            else if (reader.TryReadLength(out var len))
            {
                values[i] = len;
            }
            else
            {
                return false;
            }
        }

        SkipOptionalRoundClause(ref reader, out var closed);
        if (!closed) return false;

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Rect,
            ShapeArgs = values,
        };
        return true;
    }

    private static bool TryParseXywhFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var values = new CssLength[4];
        for (var i = 0; i < 4; i++)
        {
            if (!reader.TryReadLength(out values[i]))
                return false;
        }

        SkipOptionalRoundClause(ref reader, out var closed);
        if (!closed) return false;

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Xywh,
            ShapeArgs = values,
        };
        return true;
    }

    private static void SkipOptionalRoundClause(ref CssTokenReader reader, out bool closed)
    {
        if (reader.AtEnd)
        {
            closed = true;
            return;
        }

        var probe = reader;
        if (probe.TryReadIdent(out var roundIdent) && roundIdent.Equals("round", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            while (!reader.AtEnd)
            {
                if (!reader.TryReadLength(out _) && !reader.TryReadSlash())
                {
                    closed = false;
                    return;
                }
            }
            closed = true;
            return;
        }

        closed = false;
    }

    private static bool TryParsePolygonFunction(ref CssTokenReader reader, out CssOffsetPathData data)
    {
        data = CssOffsetPathData.None;
        var ruleProbe = reader;
        if (ruleProbe.TryReadIdent(out var ruleIdent) &&
            (ruleIdent.Equals("nonzero", StringComparison.OrdinalIgnoreCase) ||
             ruleIdent.Equals("evenodd", StringComparison.OrdinalIgnoreCase)) &&
            ruleProbe.TryReadComma())
        {
            reader = ruleProbe;
        }

        var points = new List<(CssLength X, CssLength Y)>();
        while (true)
        {
            if (!reader.TryReadLength(out var x) || !reader.TryReadLength(out var y))
                return false;
            points.Add((x, y));
            if (reader.AtEnd)
                break;
            if (!reader.TryReadComma())
                return false;
        }

        data = new CssOffsetPathData
        {
            Kind = CssOffsetPathKind.Polygon,
            PolygonLengths = points.ToArray(),
        };
        return true;
    }

    private static CssCompiledValue? ParseOffsetDistance(ref CssTokenReader reader, CssCompileContext context)
    {
        if (!reader.TryReadLength(out var distance) || !reader.AtEnd)
            return null;

        return new CssImmediateValue(DistanceProperty, distance);
    }

    private static CssCompiledValue? ParseOffsetRotate(ref CssTokenReader reader, CssCompileContext context)
    {
        if (!TryReadOffsetRotate(ref reader, out var rotate) || !reader.AtEnd)
            return null;

        return new CssImmediateValue(RotateProperty, rotate);
    }

    internal static bool TryReadOffsetRotate(ref CssTokenReader reader, out CssOffsetRotate rotate)
    {
        rotate = CssOffsetRotate.Auto;
        var probe = reader;
        var hasAutoKeyword = false;
        var isReverse = false;
        double? angle = null;

        while (!probe.AtEnd)
        {
            var tokenProbe = probe;
            if (!hasAutoKeyword && tokenProbe.TryReadIdent(out var ident))
            {
                if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    hasAutoKeyword = true;
                    isReverse = false;
                    probe = tokenProbe;
                    continue;
                }

                if (ident.Equals("reverse", StringComparison.OrdinalIgnoreCase))
                {
                    hasAutoKeyword = true;
                    isReverse = true;
                    probe = tokenProbe;
                    continue;
                }
            }

            if (angle is null && TryReadAngleDegrees(ref probe, out var deg))
            {
                angle = deg;
                continue;
            }

            break;
        }

        if (!hasAutoKeyword && angle is null)
            return false;

        reader = probe;
        var baseAngle = isReverse ? 180.0 : 0.0;
        rotate = new CssOffsetRotate(hasAutoKeyword, baseAngle + (angle ?? 0.0));
        return true;
    }

    private static CssCompiledValue? ParseOffsetPositionOrAnchor(ref CssTokenReader reader, bool isAnchor)
    {
        if (!TryReadPositionOrAnchor(ref reader, isAnchor, out var pos) || !reader.AtEnd)
            return null;

        return new CssImmediateValue(isAnchor ? AnchorProperty : PositionProperty, pos);
    }

    private static bool TryReadPositionOrAnchor(ref CssTokenReader reader, bool isAnchor, out CssOffsetPosition pos)
    {
        pos = isAnchor ? CssOffsetPosition.Auto : CssOffsetPosition.Normal;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                pos = CssOffsetPosition.Auto;
                return true;
            }
            if (!isAnchor && ident.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                pos = CssOffsetPosition.Normal;
                return true;
            }
        }

        return TryReadPositionPair(ref reader, out pos);
    }

    private static bool TryReadPositionPair(ref CssTokenReader reader, out CssOffsetPosition pos)
    {
        pos = CssOffsetPosition.Normal;
        if (!CssCoreProperties.TryReadBackgroundPosition(ref reader, out var position)) return false;
        pos = new CssOffsetPosition(CssOffsetPositionKind.Explicit, position.X.Offset, position.Y.Offset)
            { XFromFarEdge = position.X.FromFarEdge, YFromFarEdge = position.Y.FromFarEdge };
        return true;
    }

    private static bool ExpandOffsetShorthand(
        ref CssTokenReader reader,
        CssCompileContext context,
        List<CssCompiledDeclaration> output)
    {
        var position = CssOffsetPosition.Normal;
        var path = CssOffsetPathData.None;
        var distance = new CssLength(0, CssUnit.Px);
        var rotate = CssOffsetRotate.Auto;
        var anchor = CssOffsetPosition.Auto;

        var hasPath = false;
        var hasDistance = false;
        var hasRotate = false;

        // Optional leading offset-position
        var posProbe = reader;
        if (TryReadPositionOrAnchor(ref posProbe, isAnchor: false, out var parsedPos) &&
            !posProbe.AtEnd)
        {
            var pathCheck = posProbe;
            if (TryReadOffsetPathData(ref pathCheck, out _))
            {
                reader = posProbe;
                position = parsedPos;
            }
        }

        if (TryReadOffsetPathData(ref reader, out var parsedPath))
        {
            path = parsedPath;
            hasPath = true;

            while (!reader.AtEnd)
            {
                var slashProbe = reader;
                if (slashProbe.TryReadSlash())
                    break;

                if (!hasDistance)
                {
                    var distProbe = reader;
                    if (distProbe.TryReadLength(out var parsedDist))
                    {
                        reader = distProbe;
                        distance = parsedDist;
                        hasDistance = true;
                        continue;
                    }
                }

                if (!hasRotate && TryReadOffsetRotate(ref reader, out var parsedRot))
                {
                    rotate = parsedRot;
                    hasRotate = true;
                    continue;
                }

                break;
            }
        }
        else if (TryReadPositionOrAnchor(ref reader, isAnchor: false, out var onlyPos))
        {
            position = onlyPos;
        }
        else
        {
            return false;
        }

        if (reader.TryReadSlash())
        {
            if (!TryReadPositionOrAnchor(ref reader, isAnchor: true, out anchor))
                return false;
        }

        if (!reader.AtEnd)
            return false;

        output.Add(new CssCompiledDeclaration("offset-position", new CssImmediateValue(PositionProperty, position), false));
        output.Add(new CssCompiledDeclaration("offset-path", new CssImmediateValue(PathProperty, path), false));
        output.Add(new CssCompiledDeclaration("offset-distance", new CssImmediateValue(DistanceProperty, distance), false));
        output.Add(new CssCompiledDeclaration("offset-rotate", new CssImmediateValue(RotateProperty, rotate), false));
        output.Add(new CssCompiledDeclaration("offset-anchor", new CssImmediateValue(AnchorProperty, anchor), false));
        return hasPath || position != CssOffsetPosition.Normal;
    }
}

internal sealed class CssOffsetPositionAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssOffsetPositionAnimation), new PropertyMetadata(null));
    public CssOffsetPosition From { get; set; }
    public CssOffsetPosition To { get; set; }
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }
    public override Type TargetPropertyType => typeof(CssOffsetPosition);
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return CssMotionPath.InterpolatePosition(From, To, EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress);
    }
    protected override Freezable CreateInstanceCore() => new CssOffsetPositionAnimation { From = From, To = To };
}

internal sealed class CssOffsetDistanceAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(
            nameof(EasingFunction),
            typeof(IEasingFunction),
            typeof(CssOffsetDistanceAnimation),
            new PropertyMetadata(null));

    public CssLength From { get; set; }
    public CssLength To { get; set; }
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(CssLength);

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var t = EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress;
        return CssMotionPath.InterpolateLength(From, To, t);
    }

    protected override Freezable CreateInstanceCore() => new CssOffsetDistanceAnimation
    {
        From = From,
        To = To,
    };
}

internal sealed class CssOffsetRotateAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(
            nameof(EasingFunction),
            typeof(IEasingFunction),
            typeof(CssOffsetRotateAnimation),
            new PropertyMetadata(null));

    public CssOffsetRotate From { get; set; }
    public CssOffsetRotate To { get; set; }
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(CssOffsetRotate);

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var t = EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress;
        return From.Interpolate(To, t);
    }

    protected override Freezable CreateInstanceCore() => new CssOffsetRotateAnimation
    {
        From = From,
        To = To,
    };
}
