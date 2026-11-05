using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Styling;

public enum CssTransformStyle
{
    Flat,
    Preserve3D,
}

public enum CssBackfaceVisibility
{
    Visible,
    Hidden,
}

internal enum CssTransformPrimitiveKind
{
    Translate2D,
    TranslateX,
    TranslateY,
    TranslateZ,
    Translate3D,
    Scale2D,
    ScaleX,
    ScaleY,
    ScaleZ,
    Scale3D,
    Rotate2D,
    RotateX,
    RotateY,
    RotateZ,
    Rotate3D,
    Skew,
    SkewX,
    SkewY,
    Perspective,
    Matrix2D,
    Matrix3D,
}

internal readonly record struct CssTransformPrimitive(
    CssTransformPrimitiveKind Kind,
    double X,
    double Y,
    double Z,
    double Angle,
    Matrix3D CustomMatrix = default)
{
    internal Matrix3D ToMatrix3D()
    {
        switch (Kind)
        {
            case CssTransformPrimitiveKind.Translate2D:
            case CssTransformPrimitiveKind.TranslateX:
            case CssTransformPrimitiveKind.TranslateY:
            case CssTransformPrimitiveKind.TranslateZ:
            case CssTransformPrimitiveKind.Translate3D:
            {
                var m = Matrix3D.Identity;
                m.Translate(new Vector3D(X, Y, Z));
                return m;
            }
            case CssTransformPrimitiveKind.Scale2D:
            case CssTransformPrimitiveKind.ScaleX:
            case CssTransformPrimitiveKind.ScaleY:
            case CssTransformPrimitiveKind.ScaleZ:
            case CssTransformPrimitiveKind.Scale3D:
            {
                var m = Matrix3D.Identity;
                m.Scale(new Vector3D(X, Y, Z));
                return m;
            }
            case CssTransformPrimitiveKind.Rotate2D:
            case CssTransformPrimitiveKind.RotateZ:
                return CssTransform3D.CreateRotationMatrix3D(new Vector3D(0, 0, 1), Angle);
            case CssTransformPrimitiveKind.RotateX:
                return CssTransform3D.CreateRotationMatrix3D(new Vector3D(1, 0, 0), Angle);
            case CssTransformPrimitiveKind.RotateY:
                return CssTransform3D.CreateRotationMatrix3D(new Vector3D(0, 1, 0), Angle);
            case CssTransformPrimitiveKind.Rotate3D:
                return CssTransform3D.CreateRotationMatrix3D(new Vector3D(X, Y, Z), Angle);
            case CssTransformPrimitiveKind.Skew:
            case CssTransformPrimitiveKind.SkewX:
            case CssTransformPrimitiveKind.SkewY:
            {
                var tanX = Math.Tan(X * Math.PI / 180.0);
                var tanY = Math.Tan(Y * Math.PI / 180.0);
                return new Matrix3D(
                    1, tanY, 0, 0,
                    tanX, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1);
            }
            case CssTransformPrimitiveKind.Perspective:
            {
                if (!double.IsFinite(X))
                    return Matrix3D.Identity;
                var d = Math.Max(1.0, X);
                var m = Matrix3D.Identity;
                m.M34 = -1.0 / d;
                return m;
            }
            case CssTransformPrimitiveKind.Matrix2D:
            case CssTransformPrimitiveKind.Matrix3D:
                return CustomMatrix;
            default:
                return Matrix3D.Identity;
        }
    }
}

internal sealed class CssTransform3DInfo(Matrix3D matrix, CssTransformPrimitive[] primitives)
{
    public Matrix3D Matrix { get; } = matrix;
    public CssTransformPrimitive[] Primitives { get; } = primitives;
}

internal static class CssTransform3D
{
    private static readonly ConditionalWeakTable<Transform, CssTransform3DInfo> s_transformInfo = new();
    private static readonly object s_initialCssOrigin = new Point(0.5, 0.5);
    [ThreadStatic] private static Visual? s_suppressChildrenVisual;
    [ThreadStatic] private static Visual? s_activePlaneVisual;

    public static readonly DependencyProperty PerspectiveProperty =
        DependencyProperty.RegisterAttached(
            "Perspective",
            typeof(double),
            typeof(CssTransform3D),
            new PropertyMetadata(double.NaN, OnPerspectiveOrStyleChanged));

    public static readonly DependencyProperty PerspectiveOriginProperty =
        DependencyProperty.RegisterAttached(
            "PerspectiveOrigin",
            typeof(Point),
            typeof(CssTransform3D),
            new PropertyMetadata(new Point(0.5, 0.5), OnRender3DPropertyChanged));

    public static readonly DependencyProperty TransformStyleProperty =
        DependencyProperty.RegisterAttached(
            "TransformStyle",
            typeof(CssTransformStyle),
            typeof(CssTransform3D),
            new PropertyMetadata(CssTransformStyle.Flat, OnPerspectiveOrStyleChanged));

    public static readonly DependencyProperty BackfaceVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "BackfaceVisibility",
            typeof(CssBackfaceVisibility),
            typeof(CssTransform3D),
            new PropertyMetadata(CssBackfaceVisibility.Visible, OnRender3DPropertyChanged));

    public static readonly DependencyProperty OriginZProperty =
        DependencyProperty.RegisterAttached(
            "OriginZ",
            typeof(double),
            typeof(CssTransform3D),
            new PropertyMetadata(0.0, OnRender3DPropertyChanged));

    private static void OnPerspectiveOrStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement ui)
        {
            CssPositionedLayout.ContainingBlockChanged(ui);
            UIElement.InvalidateHitTestCache();
            ui.InvalidateVisual();
            (ui.VisualParent as UIElement)?.InvalidateVisual();
        }
    }

    private static void OnRender3DPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement ui)
        {
            UIElement.InvalidateHitTestCache();
            ui.InvalidateVisual();
            (ui.VisualParent as UIElement)?.InvalidateVisual();
        }
    }

    public static double GetPerspective(DependencyObject element)
        => element.GetValue(PerspectiveProperty) is double d ? d : double.NaN;

    public static void SetPerspective(DependencyObject element, double value)
        => element.SetValue(PerspectiveProperty, value);

    public static bool HasPerspective(DependencyObject element)
    {
        var d = GetPerspective(element);
        return double.IsFinite(d) && d >= 0;
    }

    public static Point GetPerspectiveOrigin(DependencyObject element)
        => element.GetValue(PerspectiveOriginProperty) is Point p ? p : new Point(0.5, 0.5);

    public static void SetPerspectiveOrigin(DependencyObject element, Point value)
        => element.SetValue(PerspectiveOriginProperty, value);

    public static CssTransformStyle GetTransformStyle(DependencyObject element)
        => element.GetValue(TransformStyleProperty) is CssTransformStyle s ? s : CssTransformStyle.Flat;

    public static void SetTransformStyle(DependencyObject element, CssTransformStyle value)
        => element.SetValue(TransformStyleProperty, value);

    public static CssBackfaceVisibility GetBackfaceVisibility(DependencyObject element)
        => element.GetValue(BackfaceVisibilityProperty) is CssBackfaceVisibility b ? b : CssBackfaceVisibility.Visible;

    public static void SetBackfaceVisibility(DependencyObject element, CssBackfaceVisibility value)
        => element.SetValue(BackfaceVisibilityProperty, value);

    public static double GetOriginZ(DependencyObject element)
        => element.GetValue(OriginZProperty) is double z && double.IsFinite(z) ? z : 0.0;

    public static void SetOriginZ(DependencyObject element, double value)
        => element.SetValue(OriginZProperty, value);

    internal static bool SuppressesChildrenOf(Visual visual)
        => ReferenceEquals(s_suppressChildrenVisual, visual);

    internal static bool IsActivePlane(Visual visual)
        => ReferenceEquals(s_activePlaneVisual, visual);

    internal static void Attach(Transform transform, Matrix3D matrix, CssTransformPrimitive[] primitives)
    {
        s_transformInfo.Remove(transform);
        s_transformInfo.Add(transform, new CssTransform3DInfo(matrix, primitives));
    }

    internal static bool Has3D(Transform? transform)
    {
        if (transform is null) return false;
        if (s_transformInfo.TryGetValue(transform, out _)) return true;
        if (transform is TransformGroup group)
        {
            foreach (var child in group.Children)
                if (Has3D(child)) return true;
        }
        return false;
    }

    internal static Matrix3D GetTransformMatrix3D(Transform? transform)
    {
        if (transform is null)
            return Matrix3D.Identity;
        if (s_transformInfo.TryGetValue(transform, out var info))
            return info.Matrix;
        if (transform is TransformGroup group)
        {
            var has3DChild = false;
            foreach (var child in group.Children)
            {
                if (Has3D(child))
                {
                    has3DChild = true;
                    break;
                }
            }

            if (has3DChild)
            {
                var acc = Matrix3D.Identity;
                foreach (var child in group.Children)
                {
                    acc.Prepend(GetTransformMatrix3D(child));
                }
                return acc;
            }
        }

        return ToMatrix3D(transform.Value);
    }

    internal static CssTransformPrimitive[] GetPrimitives(Transform? transform)
    {
        if (transform is null) return [];
        if (s_transformInfo.TryGetValue(transform, out var info))
            return info.Primitives;
        if (transform is TransformGroup group)
        {
            var list = new List<CssTransformPrimitive>(group.Children.Count);
            foreach (var child in group.Children)
                list.AddRange(GetPrimitives(child));
            return list.ToArray();
        }
        return [PrimitiveFrom2DTransform(transform)];
    }

    internal static CssTransformPrimitive PrimitiveFrom2DTransform(Transform transform) => transform switch
    {
        TranslateTransform t => new CssTransformPrimitive(CssTransformPrimitiveKind.Translate2D, t.X, t.Y, 0, 0),
        ScaleTransform s => new CssTransformPrimitive(CssTransformPrimitiveKind.Scale2D, s.ScaleX, s.ScaleY, 1, 0),
        RotateTransform r => new CssTransformPrimitive(CssTransformPrimitiveKind.Rotate2D, 0, 0, 1, r.Angle),
        SkewTransform sk => new CssTransformPrimitive(CssTransformPrimitiveKind.Skew, sk.AngleX, sk.AngleY, 0, 0),
        _ => new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix2D, 0, 0, 0, 0, ToMatrix3D(transform.Value)),
    };

    internal static CssTransformPrimitive IdentityPrimitiveLike(CssTransformPrimitive sample) => sample.Kind switch
    {
        CssTransformPrimitiveKind.Translate2D or CssTransformPrimitiveKind.TranslateX or
        CssTransformPrimitiveKind.TranslateY or CssTransformPrimitiveKind.TranslateZ or
        CssTransformPrimitiveKind.Translate3D => new(CssTransformPrimitiveKind.Translate3D, 0, 0, 0, 0),
        CssTransformPrimitiveKind.Scale2D or CssTransformPrimitiveKind.ScaleX or
        CssTransformPrimitiveKind.ScaleY or CssTransformPrimitiveKind.ScaleZ or
        CssTransformPrimitiveKind.Scale3D => new(CssTransformPrimitiveKind.Scale3D, 1, 1, 1, 0),
        CssTransformPrimitiveKind.RotateX => new(CssTransformPrimitiveKind.RotateX, 1, 0, 0, 0),
        CssTransformPrimitiveKind.RotateY => new(CssTransformPrimitiveKind.RotateY, 0, 1, 0, 0),
        CssTransformPrimitiveKind.Rotate2D or CssTransformPrimitiveKind.RotateZ => new(CssTransformPrimitiveKind.RotateZ, 0, 0, 1, 0),
        CssTransformPrimitiveKind.Rotate3D => new(CssTransformPrimitiveKind.Rotate3D, sample.X, sample.Y, sample.Z, 0),
        CssTransformPrimitiveKind.Skew or CssTransformPrimitiveKind.SkewX or CssTransformPrimitiveKind.SkewY =>
            new(sample.Kind, 0, 0, 0, 0),
        CssTransformPrimitiveKind.Perspective => new(CssTransformPrimitiveKind.Perspective, double.PositiveInfinity, 0, 0, 0),
        _ => new(CssTransformPrimitiveKind.Matrix3D, 0, 0, 0, 0, Matrix3D.Identity),
    };

    internal static Matrix3D ComposePrimitives(ReadOnlySpan<CssTransformPrimitive> primitives)
    {
        var acc = Matrix3D.Identity;
        for (int i = 0; i < primitives.Length; i++)
        {
            acc.Prepend(primitives[i].ToMatrix3D());
        }
        return acc;
    }

    internal static Transform CreateTransformFromPrimitives(CssTransformPrimitive[] primitives)
    {
        var matrix3D = ComposePrimitives(primitives);
        var transform = new MatrixTransform(ToAffineMatrix2D(matrix3D));
        Attach(transform, matrix3D, primitives);
        return transform;
    }

    internal static Transform CreateTransformFromMatrix3D(Matrix3D matrix3D)
    {
        var primitive = new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix3D, 0, 0, 0, 0, matrix3D);
        var transform = new MatrixTransform(ToAffineMatrix2D(matrix3D));
        Attach(transform, matrix3D, [primitive]);
        return transform;
    }

    internal static Matrix3D CreateRotationMatrix3D(Vector3D axis, double angleInDegrees)
    {
        var lenSq = axis.LengthSquared;
        if (!double.IsFinite(lenSq) || lenSq <= 1e-24 || Math.Abs(angleInDegrees) <= 1e-12)
            return Matrix3D.Identity;
        var m = Matrix3D.Identity;
        m.Rotate(new Quaternion(axis, angleInDegrees));
        return m;
    }

    internal static Matrix3D ToMatrix3D(Matrix m) => new(
        m.M11, m.M12, 0, 0,
        m.M21, m.M22, 0, 0,
        0,     0,     1, 0,
        m.OffsetX, m.OffsetY, 0, 1);

    internal static Matrix ToAffineMatrix2D(Matrix3D m)
    {
        var w = Math.Abs(m.M44) > 1e-9 ? m.M44 : 1.0;
        var invW = 1.0 / w;
        return new Matrix(
            m.M11 * invW, m.M12 * invW,
            m.M21 * invW, m.M22 * invW,
            m.OffsetX * invW, m.OffsetY * invW);
    }

    internal static Matrix3D Flatten(Matrix3D m) => new(
        m.M11, m.M12, 0, m.M14,
        m.M21, m.M22, 0, m.M24,
        0,     0,     1, 0,
        m.OffsetX, m.OffsetY, 0, m.M44);

    /// <summary>
    /// True when the authored transform-style is preserve-3d (regardless of flattening grouping properties).
    /// Note: per CSS Transforms 2, `transform-style: preserve-3d` establishes a containing block for all
    /// positioned descendants and a stacking context even when flattened by overflow/opacity.
    /// </summary>
    internal static bool IsPreserve3DAuthored(DependencyObject element)
        => GetTransformStyle(element) == CssTransformStyle.Preserve3D;

    /// <summary>
    /// True when the used value of transform-style is preserve-3d (not flattened by grouping properties
    /// such as overflow != visible, opacity &lt; 1, filter, backdrop-filter, clip-path, mask, or mix-blend-mode).
    /// </summary>
    internal static bool IsEffectivePreserve3D(UIElement element)
    {
        if (GetTransformStyle(element) != CssTransformStyle.Preserve3D)
            return false;

        if (element.Opacity < 1.0 || element.ClipToBounds || element.Clip is not null)
            return false;

        if (element.BackdropEffect is { HasEffect: true })
            return false;

        if (element.Effect is Effect effect && (!effect.CssBoxShadowLayer || effect.CssFilterLayer))
            return false;

        if (CssMaskProperties.Active(element))
            return false;

        if (element.GetValue(CssClipPathProperties.ValueProperty) is CssClipPathValue { Shape: not CssClipShape.None })
            return false;

        if (CssCompositingProperties.Mix(element) != CssBlendMode.Normal || CssCompositingProperties.Isolate(element))
            return false;

        if (CssOverflowProperties.Get(element) is { } overflow &&
            (overflow.X != CssOverflowMode.Visible || overflow.Y != CssOverflowMode.Visible))
            return false;

        return true;
    }

    internal static bool Is3DRenderingContextRoot(UIElement element)
    {
        if (!IsEffectivePreserve3D(element))
            return false;
        return element.VisualParent is not UIElement parent || !IsEffectivePreserve3D(parent);
    }

    internal static bool IsInPreserve3DContext(UIElement element)
        => element.VisualParent is UIElement parent && IsEffectivePreserve3D(parent);

    internal static bool HasLocal3DTransform(UIElement element)
    {
        if (Has3D(element.RenderTransform) || GetOriginZ(element) != 0.0)
            return true;
        if (element.VisualParent is UIElement parent && HasPerspective(parent))
            return true;
        return false;
    }

    internal static bool Needs3DRendering(UIElement child)
        => HasLocal3DTransform(child) || IsEffectivePreserve3D(child) || IsInPreserve3DContext(child);

    internal static object? CoerceRenderTransformOrigin(DependencyObject owner, object? value)
    {
        // Native transforms retain the native metadata origin. Only an effective
        // CSS transform supplies the CSS initial value, and no authored origin
        // (including a current value, binding, or animation) is replaced.
        if (owner.GetEffectiveValueLayer(UIElement.RenderTransformOriginProperty) is not null ||
            owner.HasAnimatedValue(UIElement.RenderTransformOriginProperty) ||
            owner.HasEffectiveCssValue(UIElement.RenderTransformOriginProperty)) return value;
        if (!owner.HasEffectiveCssValue(UIElement.RenderTransformProperty)) return value;
        return owner is UIElement element &&
            element.GetValue(CssTransformReferenceBox.ValueProperty) is CssTransformBox.ContentBox or CssTransformBox.FillBox
                ? CssTransformReferenceBox.InitialOrigin(element) : s_initialCssOrigin;
    }

    internal static bool SubtreeHas3D(Visual visual)
    {
        if (visual is UIElement ui &&
            (Has3D(ui.RenderTransform) || HasPerspective(ui) || IsPreserve3DAuthored(ui) ||
             GetBackfaceVisibility(ui) == CssBackfaceVisibility.Hidden || GetOriginZ(ui) != 0.0))
        {
            return true;
        }

        int n = visual.InternalVisualChildrenCount;
        for (int i = 0; i < n; i++)
        {
            if (visual.InternalGetVisualChild(i) is { } c && SubtreeHas3D(c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes the 4x4 transformation matrix mapping <paramref name="element"/>'s local 3D coordinate
    /// space into its immediate visual parent's coordinate space (including the element's 3D
    /// transform-origin, layout/render offset, and the parent's perspective matrix).
    /// </summary>
    internal static Matrix3D GetElementMatrix3DInParent(UIElement element, bool includeRenderOffset = true)
    {
        var size = element.RenderSize;
        var origin = element.RenderTransformOrigin;
        double ox = origin.X * size.Width;
        double oy = origin.Y * size.Height;
        double oz = GetOriginZ(element);

        var vb = element.VisualBounds;
        var ro = includeRenderOffset ? element.RenderOffset : default;
        double offX = vb.X + ro.X;
        double offY = vb.Y + ro.Y;

        var rt = element.RenderTransform;
        var m = Matrix3D.Identity;
        if (rt is not null)
        {
            var r = GetTransformMatrix3D(rt);
            if (!r.IsIdentity)
            {
                var pre = Matrix3D.Identity;
                pre.Translate(new Vector3D(-ox, -oy, -oz));
                var post = Matrix3D.Identity;
                post.Translate(new Vector3D(ox, oy, oz));
                m = pre * r * post;
            }
        }

        m.Translate(new Vector3D(offX, offY, 0));

        if (element.VisualParent is UIElement parent && HasPerspective(parent))
        {
            var persp = GetParentPerspectiveMatrix3D(parent);
            m *= persp;
        }

        return m;
    }

    internal static Matrix3D GetParentPerspectiveMatrix3D(UIElement parent)
    {
        var d = GetPerspective(parent);
        if (!double.IsFinite(d) || d < 0)
            return Matrix3D.Identity;

        var clampedD = Math.Max(1.0, d);
        var po = GetPerspectiveOrigin(parent);
        var pSize = parent.RenderSize;
        double pox = po.X * pSize.Width;
        double poy = po.Y * pSize.Height;

        var pre = Matrix3D.Identity;
        pre.Translate(new Vector3D(-pox, -poy, 0));
        var p = Matrix3D.Identity;
        p.M34 = -1.0 / clampedD;
        var post = Matrix3D.Identity;
        post.Translate(new Vector3D(pox, poy, 0));
        return pre * p * post;
    }

    /// <summary>
    /// Computes the accumulated 3D matrix for <paramref name="element"/> up to the root of its
    /// 3D rendering context (if inside a preserve-3d parent chain) or in its immediate parent.
    /// Also outputs the parent visual of that 3D rendering context root.
    /// </summary>
    internal static Matrix3D GetAccumulated3DContextMatrix(
        UIElement element,
        out Visual? contextParent,
        out bool hiddenByBackface,
        bool includeRenderOffset = true)
    {
        hiddenByBackface = false;
        var acc = GetElementMatrix3DInParent(element, includeRenderOffset);
        var current = element;

        while (current.VisualParent is UIElement parent && IsEffectivePreserve3D(parent))
        {
            if (GetBackfaceVisibility(current) == CssBackfaceVisibility.Hidden &&
                IsBackfaceMatrix3D(acc, element.RenderSize.Width, element.RenderSize.Height))
            {
                hiddenByBackface = true;
            }

            var parentMatrix = GetElementMatrix3DInParent(parent, includeRenderOffset);
            acc *= parentMatrix;
            current = parent;
        }

        contextParent = current.VisualParent;
        if (GetBackfaceVisibility(element) == CssBackfaceVisibility.Hidden &&
            IsBackfaceMatrix3D(acc, element.RenderSize.Width, element.RenderSize.Height))
        {
            hiddenByBackface = true;
        }
        else if (current != element &&
                 GetBackfaceVisibility(current) == CssBackfaceVisibility.Hidden &&
                 IsBackfaceMatrix3D(acc, element.RenderSize.Width, element.RenderSize.Height))
        {
            hiddenByBackface = true;
        }

        return acc;
    }

    /// <summary>
    /// Computes the full 3D homography matrix mapping <paramref name="element"/>'s local z=0 plane
    /// up to <paramref name="ancestor"/> (or the window root when null), flattening at each
    /// non-preserve-3d boundary per CSS Transforms Level 2.
    /// </summary>
    internal static Matrix3D GetWorldMatrix3DTo(
        UIElement element,
        Visual? ancestor,
        bool includeRenderOffset = true)
    {
        var acc = Matrix3D.Identity;
        Visual? current = element;
        bool inPreserve3D = false;

        while (current is not null && current != ancestor && current is not IWindowHost)
        {
            if (current is UIElement ui && !CssContentsLayout.IsContents(ui))
            {
                var level = GetElementMatrix3DInParent(ui, includeRenderOffset);
                if (!inPreserve3D && current != element)
                {
                    acc = Flatten(acc);
                }

                acc *= level;
                inPreserve3D = ui.VisualParent is UIElement p && IsEffectivePreserve3D(p);
            }

            current = current.VisualParent;
        }

        return Flatten(acc);
    }

    internal static bool IsBackfaceCulled(UIElement element)
    {
        if (!Needs3DRendering(element) && GetBackfaceVisibility(element) == CssBackfaceVisibility.Visible)
            return false;

        _ = GetAccumulated3DContextMatrix(element, out _, out var hidden);
        return hidden;
    }

    internal static bool IsBackfaceMatrix3D(Matrix3D m, double width, double height)
    {
        double w = width > 1e-3 ? width : 100.0;
        double h = height > 1e-3 ? height : 100.0;
        if (!TryProjectPoint(m, new Point(0, 0), out var p0) ||
            !TryProjectPoint(m, new Point(w, 0), out var p1) ||
            !TryProjectPoint(m, new Point(0, h), out var p3))
        {
            return true;
        }

        double cross = (p1.X - p0.X) * (p3.Y - p0.Y) - (p1.Y - p0.Y) * (p3.X - p0.X);
        return cross <= 1e-7;
    }

    internal static bool TryProjectPoint(Matrix3D m, Point local, out Point projected)
    {
        double x = local.X * m.M11 + local.Y * m.M21 + m.OffsetX;
        double y = local.X * m.M12 + local.Y * m.M22 + m.OffsetY;
        double w = local.X * m.M14 + local.Y * m.M24 + m.M44;
        if (!double.IsFinite(w) || w <= 1e-6)
        {
            projected = default;
            return false;
        }

        double invW = 1.0 / w;
        projected = new Point(x * invW, y * invW);
        return double.IsFinite(projected.X) && double.IsFinite(projected.Y);
    }

    /// <summary>
    /// Inverts the 2D-to-2D homography induced by <paramref name="m"/> on the z=0 plane,
    /// mapping a point in the target space back to the local z=0 plane.
    /// </summary>
    internal static bool TryInverseProjectPoint(Matrix3D m, Point targetPoint, out Point localPoint)
    {
        localPoint = default;
        if (!TryInvertHomography(m, out var inv))
            return false;

        double u = targetPoint.X * inv.H11 + targetPoint.Y * inv.H21 + inv.H31;
        double v = targetPoint.X * inv.H12 + targetPoint.Y * inv.H22 + inv.H32;
        double w = targetPoint.X * inv.H13 + targetPoint.Y * inv.H23 + inv.H33;
        if (!double.IsFinite(w) || Math.Abs(w) <= 1e-9)
            return false;

        double x = u / w;
        double y = v / w;
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return false;

        // Verify that the mapped local point is in front of the camera under the forward matrix.
        double fwdW = x * m.M14 + y * m.M24 + m.M44;
        if (fwdW <= 1e-6)
            return false;

        localPoint = new Point(x, y);
        return true;
    }

    internal readonly record struct Homography3x3(
        double H11, double H12, double H13,
        double H21, double H22, double H23,
        double H31, double H32, double H33);

    internal static bool TryInvertHomography(Matrix3D m, out Homography3x3 inv)
    {
        double a11 = m.M11, a12 = m.M12, a13 = m.M14;
        double a21 = m.M21, a22 = m.M22, a23 = m.M24;
        double a31 = m.OffsetX, a32 = m.OffsetY, a33 = m.M44;

        double c11 = a22 * a33 - a23 * a32;
        double c12 = -(a12 * a33 - a13 * a32);
        double c13 = a12 * a23 - a13 * a22;

        double c21 = -(a21 * a33 - a23 * a31);
        double c22 = a11 * a33 - a13 * a31;
        double c23 = -(a11 * a23 - a13 * a21);

        double c31 = a21 * a32 - a22 * a31;
        double c32 = -(a11 * a32 - a12 * a31);
        double c33 = a11 * a22 - a12 * a21;

        double det = a11 * c11 + a21 * c12 + a31 * c13;
        if (!double.IsFinite(det) || Math.Abs(det) <= 1e-12)
        {
            inv = default;
            return false;
        }

        double invDet = 1.0 / det;
        inv = new Homography3x3(
            c11 * invDet, c12 * invDet, c13 * invDet,
            c21 * invDet, c22 * invDet, c23 * invDet,
            c31 * invDet, c32 * invDet, c33 * invDet);
        return true;
    }

    internal static Rect TransformLocalRectBounds(Matrix3D m, Rect localRect)
    {
        if (localRect.IsEmpty)
            return Rect.Empty;

        Span<Point> pts =
        [
            new Point(localRect.Left, localRect.Top),
            new Point(localRect.Right, localRect.Top),
            new Point(localRect.Right, localRect.Bottom),
            new Point(localRect.Left, localRect.Bottom),
        ];

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        int valid = 0;

        for (int i = 0; i < 4; i++)
        {
            if (TryProjectPoint(m, pts[i], out var p))
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                valid++;
            }
        }

        return valid == 0
            ? Rect.Empty
            : new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    internal static GeneralTransform GetGeneralTransformTo(UIElement element, Visual? ancestor, bool includeRenderOffset)
    {
        var m3d = GetWorldMatrix3DTo(element, ancestor, includeRenderOffset);
        if (Math.Abs(m3d.M14) < 1e-9 && Math.Abs(m3d.M24) < 1e-9)
            return new MatrixTransform(ToAffineMatrix2D(m3d));
        return new CssProjectiveGeneralTransform(m3d, inverted: false);
    }

    internal static bool TryMapParentPointToChildLocal(UIElement child, Point pointInParent, out Point childLocalPoint)
    {
        if (!Needs3DRendering(child) && child.RenderTransform is null)
        {
            var vb = child.VisualBounds;
            var ro = child.RenderOffset;
            childLocalPoint = new Point(pointInParent.X - vb.X - ro.X, pointInParent.Y - vb.Y - ro.Y);
            return true;
        }

        var m3d = GetElementMatrix3DInParent(child, includeRenderOffset: true);
        if (GetBackfaceVisibility(child) == CssBackfaceVisibility.Hidden &&
            IsBackfaceMatrix3D(m3d, child.RenderSize.Width, child.RenderSize.Height))
        {
            childLocalPoint = default;
            return false;
        }

        return TryInverseProjectPoint(m3d, pointInParent, out childLocalPoint);
    }

    internal static bool ChainHas3DTransform(UIElement element, Visual? stopAt = null)
    {
        Visual? current = element;
        while (current is not null && current != stopAt && current is not IWindowHost)
        {
            if (current is UIElement ui &&
                (Has3D(ui.RenderTransform) || HasPerspective(ui) || IsPreserve3DAuthored(ui) || GetOriginZ(ui) != 0.0))
            {
                return true;
            }
            current = current.VisualParent;
        }
        return false;
    }

    internal readonly record struct PlaneItem(
        UIElement Element,
        Matrix3D MatrixInContextParent,
        double CentroidZ,
        int ZIndex,
        int OrderIndex,
        bool OwnBoxOnly);

    internal static List<PlaneItem> Collect3DContextPlanes(
        UIElement contextRoot,
        bool includeRenderOffset = true,
        bool inRootLocalSpace = false)
    {
        var planes = new List<PlaneItem>();
        int orderCounter = 0;
        var rootMatrix = inRootLocalSpace
            ? Matrix3D.Identity
            : GetElementMatrix3DInParent(contextRoot, includeRenderOffset);
        CollectRecursive(contextRoot, rootMatrix, hiddenAncestor: false, planes, ref orderCounter, includeRenderOffset);
        return planes;

        static void CollectRecursive(
            UIElement current,
            Matrix3D currentMatrixInRootParent,
            bool hiddenAncestor,
            List<PlaneItem> output,
            ref int orderCounter,
            bool includeRenderOffset)
        {
            if (current.Visibility != Visibility.Visible || current.Opacity <= 0)
                return;

            var size = current.RenderSize;
            bool selfBackfaceHidden = hiddenAncestor ||
                (GetBackfaceVisibility(current) == CssBackfaceVisibility.Hidden &&
                 IsBackfaceMatrix3D(currentMatrixInRootParent, size.Width, size.Height));

            if (IsEffectivePreserve3D(current))
            {
                // If this preserve-3d container itself paints a background/border, emit its own plane at z=0.
                if (!selfBackfaceHidden && HasOwnBoxPaint(current))
                {
                    double cz = ComputeEyeZ(currentMatrixInRootParent, size.Width * 0.5, size.Height * 0.5);
                    output.Add(new PlaneItem(
                        current,
                        currentMatrixInRootParent,
                        cz,
                        CssCompositingProperties.EffectiveZIndex(current),
                        orderCounter++,
                        OwnBoxOnly: true));
                }

                var paintChildren = CssPositionedLayout.PaintChildren(current);
                int childCount = paintChildren?.Count ?? current.InternalVisualChildrenCount;
                for (int i = 0; i < childCount; i++)
                {
                    var rawChild = paintChildren is not null ? paintChildren[i].Visual : current.InternalGetVisualChild(i);
                    if (rawChild is not UIElement child)
                        continue;
                    bool promoted = paintChildren is not null && paintChildren[i].Promoted;
                    if (!promoted && CssPositionedLayout.DefersPaint(child))
                        continue;
                    var childInCurrent = GetElementMatrix3DInParent(child, includeRenderOffset);
                    if (promoted)
                    {
                        var po = paintChildren![i].ParentOrigin;
                        childInCurrent.Translate(new Vector3D(po.X, po.Y, 0));
                    }
                    var childInRootParent = childInCurrent * currentMatrixInRootParent;
                    bool nextHidden = hiddenAncestor ||
                        (GetBackfaceVisibility(current) == CssBackfaceVisibility.Hidden &&
                         IsBackfaceMatrix3D(childInRootParent, child.RenderSize.Width, child.RenderSize.Height));
                    CollectRecursive(child, childInRootParent, nextHidden, output, ref orderCounter, includeRenderOffset);
                }
            }
            else if (!selfBackfaceHidden)
            {
                double cz = ComputeEyeZ(currentMatrixInRootParent, size.Width * 0.5, size.Height * 0.5);
                output.Add(new PlaneItem(
                    current,
                    currentMatrixInRootParent,
                    cz,
                    CssCompositingProperties.EffectiveZIndex(current),
                    orderCounter++,
                    OwnBoxOnly: false));
            }
        }
    }

    private static bool HasOwnBoxPaint(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            if (CssBackgroundPaintProperties.Get(fe) is { } bg && (bg.Color is not null || bg.Layers.Length > 0))
                return true;
            if (CssBorderPaintProperties.Get(fe) is not null)
                return true;
            var bgDp = DependencyProperty.FromName(fe.GetType(), "Background");
            if (bgDp is not null && fe.GetValue(bgDp) is not null)
                return true;
            var borderDp = DependencyProperty.FromName(fe.GetType(), "BorderBrush");
            if (borderDp is not null && fe.GetValue(borderDp) is not null)
                return true;
        }
        return false;
    }

    private static double ComputeEyeZ(Matrix3D m, double cx, double cy)
        => cx * m.M13 + cy * m.M23 + m.OffsetZ;

    internal static void SortPlanesForPainting(List<PlaneItem> planes)
    {
        planes.Sort(static (a, b) =>
        {
            int zCmp = a.CentroidZ.CompareTo(b.CentroidZ);
            if (Math.Abs(a.CentroidZ - b.CentroidZ) > 1e-4 && zCmp != 0)
                return zCmp;
            int zIdx = a.ZIndex.CompareTo(b.ZIndex);
            if (zIdx != 0)
                return zIdx;
            return a.OrderIndex.CompareTo(b.OrderIndex);
        });
    }

    internal static HitTestResult? HitTest3DContext(
        UIElement contextRoot,
        Point pointInTargetSpace,
        bool inRootLocalSpace,
        Func<UIElement, Point, HitTestResult?> testPlane)
    {
        var rootInOwnSpace = inRootLocalSpace
            ? GetElementMatrix3DInParent(contextRoot, includeRenderOffset: false)
            : Matrix3D.Identity;
        var planes = Collect3DContextPlanes(contextRoot, includeRenderOffset: true, inRootLocalSpace: inRootLocalSpace);
        if (planes.Count == 0)
            return null;

        SortPlanesForPainting(planes);
        for (int i = planes.Count - 1; i >= 0; i--)
        {
            var item = planes[i];
            var m = inRootLocalSpace ? item.MatrixInContextParent * rootInOwnSpace : item.MatrixInContextParent;
            if (!TryInverseProjectPoint(m, pointInTargetSpace, out var localPt))
                continue;

            var prevSuppress = s_suppressChildrenVisual;
            var prevPlane = s_activePlaneVisual;
            if (item.OwnBoxOnly) s_suppressChildrenVisual = item.Element;
            s_activePlaneVisual = item.Element;
            try
            {
                var hit = testPlane(item.Element, localPt);
                if (hit != null)
                    return hit;
            }
            finally
            {
                s_activePlaneVisual = prevPlane;
                s_suppressChildrenVisual = prevSuppress;
            }
        }

        return null;
    }

    internal static HitTestFilterBehavior HitTest3DContextWithFilter(
        UIElement contextRoot,
        Point pointInTargetSpace,
        bool inRootLocalSpace,
        Func<UIElement, Point, HitTestFilterBehavior> testPlane)
    {
        var rootInOwnSpace = inRootLocalSpace
            ? GetElementMatrix3DInParent(contextRoot, includeRenderOffset: false)
            : Matrix3D.Identity;
        var planes = Collect3DContextPlanes(contextRoot, includeRenderOffset: true, inRootLocalSpace: inRootLocalSpace);
        if (planes.Count == 0)
            return HitTestFilterBehavior.Continue;

        SortPlanesForPainting(planes);
        for (int i = planes.Count - 1; i >= 0; i--)
        {
            var item = planes[i];
            var m = inRootLocalSpace ? item.MatrixInContextParent * rootInOwnSpace : item.MatrixInContextParent;
            if (!TryInverseProjectPoint(m, pointInTargetSpace, out var localPt))
                continue;

            var prevSuppress = s_suppressChildrenVisual;
            var prevPlane = s_activePlaneVisual;
            if (item.OwnBoxOnly) s_suppressChildrenVisual = item.Element;
            s_activePlaneVisual = item.Element;
            try
            {
                if (testPlane(item.Element, localPt) == HitTestFilterBehavior.Stop)
                    return HitTestFilterBehavior.Stop;
            }
            finally
            {
                s_activePlaneVisual = prevPlane;
                s_suppressChildrenVisual = prevSuppress;
            }
        }

        return HitTestFilterBehavior.Continue;
    }

    internal static T WithSuppressedChildren<T>(Visual visual, Func<T> action)
    {
        var prev = s_suppressChildrenVisual;
        s_suppressChildrenVisual = visual;
        try
        {
            return action();
        }
        finally
        {
            s_suppressChildrenVisual = prev;
        }
    }

    internal static bool RenderRoot3DContext(
        DrawingContext drawingContext,
        UIElement root)
    {
        var baseOffset = drawingContext is IOffsetDrawingContext off ? off.Offset : default;
        var rootInOwnSpace = GetElementMatrix3DInParent(root, includeRenderOffset: false);
        var planes = Collect3DContextPlanes(root, includeRenderOffset: true, inRootLocalSpace: true);
        if (planes.Count == 0)
            return false;

        SortPlanesForPainting(planes);
        foreach (var item in planes)
        {
            var m = item.MatrixInContextParent * rootInOwnSpace;
            RenderPlaneWithMatrix3D(drawingContext, item.Element, m, baseOffset, item.OwnBoxOnly);
        }
        return true;
    }

    internal static bool RenderChild3D(
        DrawingContext drawingContext,
        UIElement child,
        Point parentOffset)
    {
        if (IsEffectivePreserve3D(child))
        {
            var planes = Collect3DContextPlanes(child, includeRenderOffset: true, inRootLocalSpace: false);
            if (planes.Count == 0)
                return false;

            SortPlanesForPainting(planes);
            foreach (var item in planes)
            {
                RenderPlaneWithMatrix3D(drawingContext, item.Element, item.MatrixInContextParent, parentOffset, item.OwnBoxOnly);
            }
            return true;
        }

        var m3d = GetElementMatrix3DInParent(child, includeRenderOffset: true);
        if (GetBackfaceVisibility(child) == CssBackfaceVisibility.Hidden &&
            IsBackfaceMatrix3D(m3d, child.RenderSize.Width, child.RenderSize.Height))
        {
            return false;
        }

        return RenderPlaneWithMatrix3D(drawingContext, child, m3d, parentOffset, ownBoxOnly: false);
    }

    internal static bool RenderPlaneWithMatrix3D(
        DrawingContext drawingContext,
        UIElement plane,
        Matrix3D m3d,
        Point baseOffset,
        bool ownBoxOnly)
    {
        if (drawingContext is not IOffsetDrawingContext offsetContext)
            return false;

        var size = plane.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return false;

        // Affine 3D projection (M14 == 0 && M24 == 0): exact 2D affine matrix!
        if (Math.Abs(m3d.M14) < 1e-9 && Math.Abs(m3d.M24) < 1e-9)
        {
            if (m3d.M44 <= 1e-6)
                return false;

            var matrix2D = ToAffineMatrix2D(m3d);
            var savedOffset = offsetContext.Offset;
            offsetContext.Offset = baseOffset;
            bool pushedOpacity = false;
            if (plane.Opacity < 1.0 && drawingContext is IOpacityDrawingContext opacityCtx)
            {
                opacityCtx.PushOpacity(plane.Opacity);
                pushedOpacity = true;
            }

            drawingContext.PushTransform(new MatrixTransform(matrix2D));
            var prevSuppressVisual = s_suppressChildrenVisual;
            var prevActivePlane = s_activePlaneVisual;
            if (ownBoxOnly) s_suppressChildrenVisual = plane;
            s_activePlaneVisual = plane;
            try
            {
                plane.Render(drawingContext);
            }
            finally
            {
                s_activePlaneVisual = prevActivePlane;
                s_suppressChildrenVisual = prevSuppressVisual;
                if (drawingContext is ITransformDrawingContext transformPop)
                    transformPop.PopTransform();
                else
                    drawingContext.Pop();
                if (pushedOpacity && drawingContext is IOpacityDrawingContext opacityPop)
                    opacityPop.PopOpacity();
                offsetContext.Offset = savedOffset;
            }
            return true;
        }

        // Projective 3D homography (M14 != 0 || M24 != 0):
        var projectedBounds = TransformLocalRectBounds(m3d, new Rect(0, 0, size.Width, size.Height));
        if (projectedBounds.IsEmpty || projectedBounds.Width <= 0.5 || projectedBounds.Height <= 0.5)
            return false;

        if (!TryInvertHomography(m3d, out var invH))
            return false;

        double dpi = Math.Max(1.0, Math.Max(plane.DpiScale.DpiScaleX, plane.DpiScale.DpiScaleY));
        if (dpi > 2.0) dpi = 2.0;

        int srcW = Math.Clamp((int)Math.Ceiling(size.Width * dpi), 1, 2048);
        int srcH = Math.Clamp((int)Math.Ceiling(size.Height * dpi), 1, 2048);
        double srcScaleX = srcW / size.Width;
        double srcScaleY = srcH / size.Height;

        var rtb = new RenderTargetBitmap(srcW, srcH, 96.0 * srcScaleX, 96.0 * srcScaleY, PixelFormat.Bgra32);
        var prevSuppress = s_suppressChildrenVisual;
        var prevPlane = s_activePlaneVisual;
        if (ownBoxOnly) s_suppressChildrenVisual = plane;
        s_activePlaneVisual = plane;
        try
        {
            var captureDc = new SoftwareDrawingContext(rtb);
            if (Math.Abs(srcScaleX - 1.0) > 1e-6 || Math.Abs(srcScaleY - 1.0) > 1e-6)
                captureDc.PushTransform(new ScaleTransform(srcScaleX, srcScaleY));
            plane.Render(captureDc);
            captureDc.Close();
        }
        finally
        {
            s_activePlaneVisual = prevPlane;
            s_suppressChildrenVisual = prevSuppress;
        }

        int srcStride = srcW * 4;
        var srcPixels = new byte[srcStride * srcH];
        rtb.CopyPixels(new Int32Rect(0, 0, srcW, srcH), srcPixels, srcStride, 0);

        int dstLeft = (int)Math.Floor(projectedBounds.Left) - 1;
        int dstTop = (int)Math.Floor(projectedBounds.Top) - 1;
        int dstDipW = Math.Clamp((int)Math.Ceiling(projectedBounds.Right) - dstLeft + 1, 1, 2048);
        int dstDipH = Math.Clamp((int)Math.Ceiling(projectedBounds.Bottom) - dstTop + 1, 1, 2048);

        int dstPixelW = Math.Clamp((int)Math.Round(dstDipW * dpi), 1, 2048);
        int dstPixelH = Math.Clamp((int)Math.Round(dstDipH * dpi), 1, 2048);
        double dstStepX = (double)dstDipW / dstPixelW;
        double dstStepY = (double)dstDipH / dstPixelH;

        var dstPixels = new byte[dstPixelW * dstPixelH * 4];
        bool hasAnyOpaque = false;

        for (int py = 0; py < dstPixelH; py++)
        {
            double Y = dstTop + (py + 0.5) * dstStepY;
            int rowOffset = py * dstPixelW * 4;
            for (int px = 0; px < dstPixelW; px++)
            {
                double X = dstLeft + (px + 0.5) * dstStepX;
                double u = X * invH.H11 + Y * invH.H21 + invH.H31;
                double v = X * invH.H12 + Y * invH.H22 + invH.H32;
                double w = X * invH.H13 + Y * invH.H23 + invH.H33;
                if (Math.Abs(w) <= 1e-9) continue;

                double lx = u / w;
                double ly = v / w;
                double fwdW = lx * m3d.M14 + ly * m3d.M24 + m3d.M44;
                if (fwdW <= 1e-6) continue;

                // Smooth 1px anti-aliased edge feather at quad boundaries
                if (lx < -0.5 || lx > size.Width + 0.5 || ly < -0.5 || ly > size.Height + 0.5)
                    continue;

                double edgeCoverage =
                    Math.Clamp(lx + 0.5, 0.0, 1.0) *
                    Math.Clamp(size.Width + 0.5 - lx, 0.0, 1.0) *
                    Math.Clamp(ly + 0.5, 0.0, 1.0) *
                    Math.Clamp(size.Height + 0.5 - ly, 0.0, 1.0);
                if (edgeCoverage <= 0) continue;

                double sx = Math.Clamp(lx * srcScaleX - 0.5, 0.0, srcW - 1.0001);
                double sy = Math.Clamp(ly * srcScaleY - 0.5, 0.0, srcH - 1.0001);
                SampleBilinear(srcPixels, srcW, srcH, srcStride, sx, sy, edgeCoverage, dstPixels, rowOffset + px * 4, ref hasAnyOpaque);
            }
        }

        var saved = offsetContext.Offset;
        offsetContext.Offset = baseOffset;
        bool pushedOp = false;
        if (plane.Opacity < 1.0 && drawingContext is IOpacityDrawingContext opCtx)
        {
            opCtx.PushOpacity(plane.Opacity);
            pushedOp = true;
        }

        try
        {
            if (hasAnyOpaque)
            {
                var warpedImage = BitmapImage.FromPixels(dstPixels, dstPixelW, dstPixelH);
                drawingContext.DrawImage(warpedImage, new Rect(dstLeft, dstTop, dstDipW, dstDipH));
            }

            // SoftwareDrawingContext skips DrawText/DrawGlyphRun; when rendering to a native
            // RenderTargetDrawingContext, replay text calls with their local 3-point projected affine matrix.
            if (drawingContext is not SoftwareDrawingContext)
            {
                var textDc = new ProjectiveTextOverlayDrawingContext(drawingContext, m3d, baseOffset);
                var prevSuppressText = s_suppressChildrenVisual;
                var prevPlaneText = s_activePlaneVisual;
                if (ownBoxOnly) s_suppressChildrenVisual = plane;
                s_activePlaneVisual = plane;
                try
                {
                    plane.Render(textDc);
                }
                finally
                {
                    s_activePlaneVisual = prevPlaneText;
                    s_suppressChildrenVisual = prevSuppressText;
                }
            }
        }
        finally
        {
            if (pushedOp && drawingContext is IOpacityDrawingContext opPop)
                opPop.PopOpacity();
            offsetContext.Offset = saved;
        }

        return true;
    }

    private static void SampleBilinear(
        byte[] src, int srcW, int srcH, int srcStride,
        double sx, double sy, double coverage,
        byte[] dst, int dstIdx, ref bool hasAnyOpaque)
    {
        int x0 = (int)Math.Floor(sx);
        int y0 = (int)Math.Floor(sy);
        int x1 = Math.Min(x0 + 1, srcW - 1);
        int y1 = Math.Min(y0 + 1, srcH - 1);
        double fx = sx - x0;
        double fy = sy - y0;

        int i00 = y0 * srcStride + x0 * 4;
        int i10 = y0 * srcStride + x1 * 4;
        int i01 = y1 * srcStride + x0 * 4;
        int i11 = y1 * srcStride + x1 * 4;

        double w00 = (1 - fx) * (1 - fy);
        double w10 = fx * (1 - fy);
        double w01 = (1 - fx) * fy;
        double w11 = fx * fy;

        double a = (src[i00 + 3] * w00 + src[i10 + 3] * w10 + src[i01 + 3] * w01 + src[i11 + 3] * w11) * coverage;
        if (a < 0.5) return;

        double b = src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11;
        double g = src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11;
        double r = src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11;

        dst[dstIdx] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
        dst[dstIdx + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
        dst[dstIdx + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
        dst[dstIdx + 3] = (byte)Math.Clamp((int)Math.Round(a), 0, 255);
        hasAnyOpaque = true;
    }

    private sealed class ProjectiveTextOverlayDrawingContext(
        DrawingContext inner,
        Matrix3D m3d,
        Point baseOffset) : DrawingContextAdapter, IOffsetDrawingContext, ITransformDrawingContext, IOpacityDrawingContext
    {
        private readonly Stack<Matrix> _localMatrices = new();
        private Matrix _currentLocal = Matrix.Identity;
        public Point Offset { get; set; }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
            double w = Math.Max(1.0, formattedText.Width);
            double h = Math.Max(1.0, formattedText.Height);
            var p00Local = _currentLocal.Transform(new Point(Offset.X + origin.X, Offset.Y + origin.Y));
            var p10Local = _currentLocal.Transform(new Point(Offset.X + origin.X + w, Offset.Y + origin.Y));
            var p01Local = _currentLocal.Transform(new Point(Offset.X + origin.X, Offset.Y + origin.Y + h));

            if (!TryProjectPoint(m3d, p00Local, out var p00) ||
                !TryProjectPoint(m3d, p10Local, out var p10) ||
                !TryProjectPoint(m3d, p01Local, out var p01))
            {
                return;
            }

            var affine = new Matrix(
                (p10.X - p00.X) / w,
                (p10.Y - p00.Y) / w,
                (p01.X - p00.X) / h,
                (p01.Y - p00.Y) / h,
                p00.X,
                p00.Y);

            if (inner is IOffsetDrawingContext innerOffset)
            {
                var saved = innerOffset.Offset;
                innerOffset.Offset = baseOffset;
                inner.PushTransform(new MatrixTransform(affine));
                try
                {
                    inner.DrawText(formattedText, new Point(0, 0));
                }
                finally
                {
                    if (inner is ITransformDrawingContext popCtx)
                        popCtx.PopTransform();
                    else
                        inner.Pop();
                    innerOffset.Offset = saved;
                }
            }
        }

        public override void PushTransform(Transform transform)
        {
            _localMatrices.Push(_currentLocal);
            if (transform is not null)
            {
                var m = transform.Value;
                var ox = Offset.X;
                var oy = Offset.Y;
                var incoming = new Matrix(
                    m.M11, m.M12, m.M21, m.M22,
                    (-ox * m.M11) + (-oy * m.M21) + m.OffsetX + ox,
                    (-ox * m.M12) + (-oy * m.M22) + m.OffsetY + oy);
                _currentLocal = incoming * _currentLocal;
            }
        }

        void ITransformDrawingContext.PushTransform(Transform transform, double originX, double originY)
        {
            if (originX == 0 && originY == 0)
            {
                PushTransform(transform);
                return;
            }
            var combined = Matrix.Multiply(
                Matrix.Multiply(new Matrix(1, 0, 0, 1, -originX, -originY), transform.Value),
                new Matrix(1, 0, 0, 1, originX, originY));
            PushTransform(new MatrixTransform(combined));
        }

        public void PopTransform() => Pop();

        public override void PushOpacity(double opacity) => inner.PushOpacity(opacity);
        public void PopOpacity() => inner.Pop();
        public override void PushClip(Geometry clipGeometry) => _localMatrices.Push(_currentLocal);
        public override void Pop()
        {
            if (_localMatrices.Count > 0)
                _currentLocal = _localMatrices.Pop();
        }
        public override void Close() { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
    }
}

internal sealed class CssProjectiveGeneralTransform : GeneralTransform
{
    private readonly Matrix3D _matrix;
    private readonly bool _inverted;

    public CssProjectiveGeneralTransform()
    {
        _matrix = Matrix3D.Identity;
    }

    internal CssProjectiveGeneralTransform(Matrix3D matrix, bool inverted)
    {
        _matrix = matrix;
        _inverted = inverted;
    }

    public override GeneralTransform? Inverse
        => CssTransform3D.TryInvertHomography(_matrix, out _)
            ? new CssProjectiveGeneralTransform(_matrix, !_inverted)
            : null;

    public override bool TryTransform(Point inPoint, out Point result)
        => _inverted
            ? CssTransform3D.TryInverseProjectPoint(_matrix, inPoint, out result)
            : CssTransform3D.TryProjectPoint(_matrix, inPoint, out result);

    protected override Freezable CreateInstanceCore()
        => new CssProjectiveGeneralTransform(_matrix, _inverted);
}

internal sealed class CssPerspectiveValue(CssLength length) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        length.ObserveContainerDependencies(context.Lengths);
        if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px) ||
            !double.IsFinite(px) || px < 0)
        {
            return false;
        }

        sink.Set(CssTransform3D.PerspectiveProperty, px);
        return true;
    }
}

internal sealed class CssPerspectiveOriginValue(CssBackgroundPosition position) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var element = context.Element;
        position.ObserveDependencies(context.Lengths);
        var state = CssEngine.EnsureState(element);
        if (!state.ObservesOwnSize)
        {
            state.ObservesOwnSize = true;
            element.SizeChanged += (_, _) => CssEvaluationScheduler.InvalidateElement(element);
        }

        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var x = position.X.Resolve(width, context.Lengths);
        var y = position.Y.Resolve(height, context.Lengths);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return false;

        sink.Set(CssTransform3D.PerspectiveOriginProperty,
            new Point(
                width == 0 ? ZeroSizeOrigin(position.X, context.Lengths) : x / width,
                height == 0 ? ZeroSizeOrigin(position.Y, context.Lengths) : y / height));
        return true;
    }

    private static double ZeroSizeOrigin(CssBackgroundPositionAxis axis, in CssLengthContext lengths)
        => axis.Offset.Unit == CssUnit.Percent && axis.Offset.Expression is null
            ? axis.Resolve(1, lengths) : 0.5;
}
