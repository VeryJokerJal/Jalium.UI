using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Styling;

/// <summary>Interpolates the supported 2D and 3D CSS transform functions and matrix suffixes.</summary>
internal static class CssTransformInterpolation
{
    private static readonly ConditionalWeakTable<SkewTransform, SkewFunctionInfo> s_skewFunctions = new();

    internal static void MarkSkewFunction(SkewTransform transform, CssSkewFunction kind)
        => s_skewFunctions.Add(transform, new SkewFunctionInfo(kind));

    private static CssSkewFunction SkewKind(SkewTransform transform)
        => s_skewFunctions.TryGetValue(transform, out var info) ? info.Kind : CssSkewFunction.Skew;

    internal static Transform? Interpolate(Transform? from, Transform? to, double progress)
    {
        if (progress == 0) return from;
        if (progress == 1) return to;

        if (CssTransform3D.Has3D(from) || CssTransform3D.Has3D(to))
        {
            return Interpolate3D(from, to, progress);
        }

        var first = Functions(from);
        var second = Functions(to);
        var result = new List<Transform>(Math.Max(first.Length, second.Length));
        for (var index = 0; index < Math.Max(first.Length, second.Length); index++)
        {
            var before = index < first.Length ? first[index] : IdentityLike(second[index]);
            var after = index < second.Length ? second[index] : IdentityLike(first[index]);
            if (TryInterpolateFunction(before, after, progress, out var function))
            {
                result.Add(function);
                continue;
            }

            var a = SuffixMatrix(first, index);
            var b = SuffixMatrix(second, index);
            if (!TryInterpolateMatrix(a, b, progress, out var matrix))
                return progress < .5 ? from : to;
            result.Add(new MatrixTransform(matrix));
            break;
        }

        return result.Count switch
        {
            0 => null,
            1 => result[0],
            _ => new TransformGroup(result.ToArray()),
        };
    }

    private static Transform? Interpolate3D(Transform? from, Transform? to, double progress)
    {
        var first = CssTransform3D.GetPrimitives(from);
        var second = CssTransform3D.GetPrimitives(to);
        int count = Math.Max(first.Length, second.Length);
        if (count == 0) return null;

        var result = new List<CssTransformPrimitive>(count);
        for (int index = 0; index < count; index++)
        {
            var before = index < first.Length ? first[index] : CssTransform3D.IdentityPrimitiveLike(second[index]);
            var after = index < second.Length ? second[index] : CssTransform3D.IdentityPrimitiveLike(first[index]);
            if (TryInterpolatePrimitive3D(before, after, progress, out var primitive))
            {
                result.Add(primitive);
                continue;
            }

            var a = CssTransform3D.ComposePrimitives(first.AsSpan(index));
            var b = CssTransform3D.ComposePrimitives(second.AsSpan(index));
            if (!TryInterpolateMatrix3D(a, b, progress, out var matrix3D))
                return progress < 0.5 ? from : to;
            result.Add(new CssTransformPrimitive(CssTransformPrimitiveKind.Matrix3D, 0, 0, 0, 0, matrix3D));
            break;
        }

        return CssTransform3D.CreateTransformFromPrimitives(result.ToArray());
    }

    private static bool TryInterpolatePrimitive3D(
        CssTransformPrimitive a,
        CssTransformPrimitive b,
        double progress,
        out CssTransformPrimitive result)
    {
        result = default;
        if (IsTranslatePrimitive(a.Kind) && IsTranslatePrimitive(b.Kind))
        {
            result = new CssTransformPrimitive(
                CssTransformPrimitiveKind.Translate3D,
                Lerp(a.X, b.X, progress),
                Lerp(a.Y, b.Y, progress),
                Lerp(a.Z, b.Z, progress),
                0);
            return true;
        }

        if (IsScalePrimitive(a.Kind) && IsScalePrimitive(b.Kind))
        {
            result = new CssTransformPrimitive(
                CssTransformPrimitiveKind.Scale3D,
                Lerp(a.X, b.X, progress),
                Lerp(a.Y, b.Y, progress),
                Lerp(a.Z, b.Z, progress),
                0);
            return true;
        }

        if (IsRotatePrimitive(a.Kind) && IsRotatePrimitive(b.Kind))
        {
            var axisA = new Vector3D(a.X, a.Y, a.Z);
            var axisB = new Vector3D(b.X, b.Y, b.Z);
            var lenA = axisA.Length;
            var lenB = axisB.Length;
            if (lenA > 1e-9 && lenB > 1e-9)
            {
                var normA = axisA / lenA;
                var normB = axisB / lenB;
                if (Math.Abs(normA.X - normB.X) < 1e-6 &&
                    Math.Abs(normA.Y - normB.Y) < 1e-6 &&
                    Math.Abs(normA.Z - normB.Z) < 1e-6)
                {
                    result = new CssTransformPrimitive(
                        CssTransformPrimitiveKind.Rotate3D,
                        normA.X,
                        normA.Y,
                        normA.Z,
                        Lerp(a.Angle, b.Angle, progress));
                    return true;
                }
            }

            var qA = lenA > 1e-9 ? new Quaternion(axisA, a.Angle) : Quaternion.Identity;
            var qB = lenB > 1e-9 ? new Quaternion(axisB, b.Angle) : Quaternion.Identity;
            var slerped = Quaternion.Slerp(qA, qB, progress);
            var axis = slerped.Axis;
            result = new CssTransformPrimitive(
                CssTransformPrimitiveKind.Rotate3D,
                axis.X,
                axis.Y,
                axis.Z,
                slerped.Angle);
            return true;
        }

        if (a.Kind == b.Kind && a.Kind is CssTransformPrimitiveKind.Skew or CssTransformPrimitiveKind.SkewX or CssTransformPrimitiveKind.SkewY)
        {
            result = new CssTransformPrimitive(
                a.Kind,
                Lerp(a.X, b.X, progress),
                Lerp(a.Y, b.Y, progress),
                0,
                0);
            return true;
        }

        if (a.Kind == CssTransformPrimitiveKind.Perspective && b.Kind == CssTransformPrimitiveKind.Perspective)
        {
            var invA = double.IsFinite(a.X) && a.X > 0 ? 1.0 / Math.Max(1.0, a.X) : 0.0;
            var invB = double.IsFinite(b.X) && b.X > 0 ? 1.0 / Math.Max(1.0, b.X) : 0.0;
            var inv = Lerp(invA, invB, progress);
            var d = inv <= 1e-9 ? double.PositiveInfinity : Math.Max(1.0, 1.0 / inv);
            result = new CssTransformPrimitive(CssTransformPrimitiveKind.Perspective, d, 0, 0, 0);
            return true;
        }

        return false;
    }

    private static bool IsTranslatePrimitive(CssTransformPrimitiveKind kind) =>
        kind is CssTransformPrimitiveKind.Translate2D or CssTransformPrimitiveKind.TranslateX or
            CssTransformPrimitiveKind.TranslateY or CssTransformPrimitiveKind.TranslateZ or
            CssTransformPrimitiveKind.Translate3D;

    private static bool IsScalePrimitive(CssTransformPrimitiveKind kind) =>
        kind is CssTransformPrimitiveKind.Scale2D or CssTransformPrimitiveKind.ScaleX or
            CssTransformPrimitiveKind.ScaleY or CssTransformPrimitiveKind.ScaleZ or
            CssTransformPrimitiveKind.Scale3D;

    private static bool IsRotatePrimitive(CssTransformPrimitiveKind kind) =>
        kind is CssTransformPrimitiveKind.Rotate2D or CssTransformPrimitiveKind.RotateX or
            CssTransformPrimitiveKind.RotateY or CssTransformPrimitiveKind.RotateZ or
            CssTransformPrimitiveKind.Rotate3D;

    internal static bool TryInterpolateMatrix3D(Matrix3D before, Matrix3D after, double progress, out Matrix3D result)
    {
        result = Matrix3D.Identity;
        if (!TryDecomposeMatrix3D(before, out var a) || !TryDecomposeMatrix3D(after, out var b))
            return false;

        var translation = new Vector3D(
            Lerp(a.Translation.X, b.Translation.X, progress),
            Lerp(a.Translation.Y, b.Translation.Y, progress),
            Lerp(a.Translation.Z, b.Translation.Z, progress));

        var scale = new Vector3D(
            Lerp(a.Scale.X, b.Scale.X, progress),
            Lerp(a.Scale.Y, b.Scale.Y, progress),
            Lerp(a.Scale.Z, b.Scale.Z, progress));

        var skew = new Vector3D(
            Lerp(a.Skew.X, b.Skew.X, progress),
            Lerp(a.Skew.Y, b.Skew.Y, progress),
            Lerp(a.Skew.Z, b.Skew.Z, progress));

        var perspective = new Point4D(
            Lerp(a.Perspective.X, b.Perspective.X, progress),
            Lerp(a.Perspective.Y, b.Perspective.Y, progress),
            Lerp(a.Perspective.Z, b.Perspective.Z, progress),
            Lerp(a.Perspective.W, b.Perspective.W, progress));

        var rotation = Quaternion.Slerp(a.Rotation, b.Rotation, progress);

        result = RecomposeMatrix3D(translation, scale, skew, perspective, rotation);
        return true;
    }

    private readonly record struct Decomposed3D(
        Vector3D Translation,
        Vector3D Scale,
        Vector3D Skew,
        Point4D Perspective,
        Quaternion Rotation);

    private static bool TryDecomposeMatrix3D(Matrix3D matrix, out Decomposed3D value)
    {
        value = default;
        if (!double.IsFinite(matrix.M44) || Math.Abs(matrix.M44) < 1e-12)
            return false;

        double inv44 = 1.0 / matrix.M44;
        var m = new Matrix3D(
            matrix.M11 * inv44, matrix.M12 * inv44, matrix.M13 * inv44, matrix.M14 * inv44,
            matrix.M21 * inv44, matrix.M22 * inv44, matrix.M23 * inv44, matrix.M24 * inv44,
            matrix.M31 * inv44, matrix.M32 * inv44, matrix.M33 * inv44, matrix.M34 * inv44,
            matrix.OffsetX * inv44, matrix.OffsetY * inv44, matrix.OffsetZ * inv44, 1.0);

        var perspectiveMatrix = m;
        perspectiveMatrix.M14 = 0;
        perspectiveMatrix.M24 = 0;
        perspectiveMatrix.M34 = 0;
        perspectiveMatrix.M44 = 1;
        if (!perspectiveMatrix.HasInverse)
            return false;

        Point4D perspective;
        if (Math.Abs(m.M14) > 1e-12 || Math.Abs(m.M24) > 1e-12 || Math.Abs(m.M34) > 1e-12)
        {
            var rhs = new Point4D(m.M14, m.M24, m.M34, m.M44);
            var inv = perspectiveMatrix;
            inv.Invert();
            var invTranspose = new Matrix3D(
                inv.M11, inv.M21, inv.M31, inv.OffsetX,
                inv.M12, inv.M22, inv.M32, inv.OffsetY,
                inv.M13, inv.M23, inv.M33, inv.OffsetZ,
                inv.M14, inv.M24, inv.M34, inv.M44);
            perspective = invTranspose.Transform(rhs);
        }
        else
        {
            perspective = new Point4D(0, 0, 0, 1);
        }

        var translation = new Vector3D(m.OffsetX, m.OffsetY, m.OffsetZ);
        var row0 = new Vector3D(m.M11, m.M12, m.M13);
        var row1 = new Vector3D(m.M21, m.M22, m.M23);
        var row2 = new Vector3D(m.M31, m.M32, m.M33);

        double scaleX = row0.Length;
        if (scaleX < 1e-12) return false;
        row0 /= scaleX;

        double skewXY = Vector3D.DotProduct(row0, row1);
        row1 -= skewXY * row0;

        double scaleY = row1.Length;
        if (scaleY < 1e-12) return false;
        row1 /= scaleY;
        skewXY /= scaleY;

        double skewXZ = Vector3D.DotProduct(row0, row2);
        row2 -= skewXZ * row0;
        double skewYZ = Vector3D.DotProduct(row1, row2);
        row2 -= skewYZ * row1;

        double scaleZ = row2.Length;
        if (scaleZ < 1e-12) return false;
        row2 /= scaleZ;
        skewXZ /= scaleZ;
        skewYZ /= scaleZ;

        if (Vector3D.DotProduct(row0, Vector3D.CrossProduct(row1, row2)) < 0)
        {
            scaleX = -scaleX;
            scaleY = -scaleY;
            scaleZ = -scaleZ;
            row0 = -row0;
            row1 = -row1;
            row2 = -row2;
        }

        double qx = 0.5 * Math.Sqrt(Math.Max(0.0, 1.0 + row0.X - row1.Y - row2.Z));
        double qy = 0.5 * Math.Sqrt(Math.Max(0.0, 1.0 - row0.X + row1.Y - row2.Z));
        double qz = 0.5 * Math.Sqrt(Math.Max(0.0, 1.0 - row0.X - row1.Y + row2.Z));
        double qw = 0.5 * Math.Sqrt(Math.Max(0.0, 1.0 + row0.X + row1.Y + row2.Z));
        if (row2.Y > row1.Z) qx = -qx;
        if (row0.Z > row2.X) qy = -qy;
        if (row1.X > row0.Y) qz = -qz;

        value = new Decomposed3D(
            translation,
            new Vector3D(scaleX, scaleY, scaleZ),
            new Vector3D(skewXY, skewXZ, skewYZ),
            perspective,
            new Quaternion(qx, qy, qz, qw));
        return true;
    }

    private static Matrix3D RecomposeMatrix3D(
        Vector3D translation,
        Vector3D scale,
        Vector3D skew,
        Point4D perspective,
        Quaternion rotation)
    {
        // Row-vector composition matching CSS Transforms 2 recomposition order:
        // scale -> skew -> rotation -> translation -> perspective
        var m = Matrix3D.Identity;
        m.Scale(scale);

        if (Math.Abs(skew.X) > 1e-12)
        {
            var sxy = Matrix3D.Identity;
            sxy.M21 = skew.X;
            m *= sxy;
        }
        if (Math.Abs(skew.Y) > 1e-12)
        {
            var sxz = Matrix3D.Identity;
            sxz.M31 = skew.Y;
            m *= sxz;
        }
        if (Math.Abs(skew.Z) > 1e-12)
        {
            var syz = Matrix3D.Identity;
            syz.M32 = skew.Z;
            m *= syz;
        }

        m.Rotate(rotation);
        m.Translate(translation);

        var p = Matrix3D.Identity;
        p.M14 = perspective.X;
        p.M24 = perspective.Y;
        p.M34 = perspective.Z;
        p.M44 = perspective.W;
        m *= p;
        return m;
    }

    private static Transform[] Functions(Transform? transform) => transform switch
    {
        null => [],
        TransformGroup group => group.Children.ToArray(),
        _ => [transform],
    };

    private static Transform IdentityLike(Transform transform) => transform switch
    {
        TranslateTransform => new TranslateTransform(),
        ScaleTransform => new ScaleTransform(),
        RotateTransform => new RotateTransform(),
        SkewTransform skew => SkewIdentity(skew),
        _ => new MatrixTransform(Matrix.Identity),
    };

    private static SkewTransform SkewIdentity(SkewTransform source)
    {
        var identity = new SkewTransform();
        MarkSkewFunction(identity, SkewKind(source));
        return identity;
    }

    private static bool TryInterpolateFunction(Transform before, Transform after,
        double progress, out Transform result)
    {
        switch (before, after)
        {
            case (TranslateTransform a, TranslateTransform b):
                result = new TranslateTransform(Lerp(a.X, b.X, progress), Lerp(a.Y, b.Y, progress));
                return true;
            case (ScaleTransform a, ScaleTransform b):
                result = new ScaleTransform(Lerp(a.ScaleX, b.ScaleX, progress),
                    Lerp(a.ScaleY, b.ScaleY, progress), Lerp(a.CenterX, b.CenterX, progress),
                    Lerp(a.CenterY, b.CenterY, progress));
                return true;
            case (RotateTransform a, RotateTransform b):
                // Matched rotate() functions preserve authored turns, including 360deg.
                result = new RotateTransform(Lerp(a.Angle, b.Angle, progress),
                    Lerp(a.CenterX, b.CenterX, progress), Lerp(a.CenterY, b.CenterY, progress));
                return true;
            case (SkewTransform a, SkewTransform b):
                if (SkewKind(a) != SkewKind(b))
                {
                    result = null!;
                    return false;
                }
                result = new SkewTransform(Lerp(a.AngleX, b.AngleX, progress),
                    Lerp(a.AngleY, b.AngleY, progress), Lerp(a.CenterX, b.CenterX, progress),
                    Lerp(a.CenterY, b.CenterY, progress));
                MarkSkewFunction((SkewTransform)result, SkewKind(a));
                return true;
            default:
                result = null!;
                return false;
        }
    }

    private static Matrix SuffixMatrix(Transform[] functions, int start)
    {
        var matrix = Matrix.Identity;
        for (var index = start; index < functions.Length; index++)
            matrix = Matrix.Multiply(matrix, functions[index].Value);
        return matrix;
    }

    private static bool TryInterpolateMatrix(Matrix before, Matrix after,
        double progress, out Matrix result)
    {
        result = default;
        if (!TryDecompose(before, out var a) || !TryDecompose(after, out var b)) return false;

        var angleA = a.Angle;
        var angleB = b.Angle;
        if ((a.ScaleX < 0 && b.ScaleY < 0) ||
            (a.ScaleY < 0 && b.ScaleX < 0))
        {
            // Keep both reflected matrices on the same scale axis before
            // choosing the shortest rotation between their decompositions.
            a = a with { ScaleX = -a.ScaleX, ScaleY = -a.ScaleY };
            angleA += angleA < 0 ? Math.PI : -Math.PI;
        }
        if (Math.Abs(angleA - angleB) > Math.PI)
        {
            if (angleA > angleB) angleA -= Math.Tau;
            else angleB -= Math.Tau;
        }
        var angle = Lerp(angleA, angleB, progress);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var scaleX = Lerp(a.ScaleX, b.ScaleX, progress);
        var scaleY = Lerp(a.ScaleY, b.ScaleY, progress);
        var shear = Lerp(a.Shear, b.Shear, progress);
        result = new Matrix(scaleX * cos, scaleX * sin,
            scaleY * (shear * cos - sin), scaleY * (shear * sin + cos),
            Lerp(a.OffsetX, b.OffsetX, progress), Lerp(a.OffsetY, b.OffsetY, progress));
        return true;
    }

    private static bool TryDecompose(Matrix matrix, out Decomposed value)
    {
        value = default;
        var determinant = matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21;
        if (!double.IsFinite(matrix.M11) || !double.IsFinite(matrix.M12) ||
            !double.IsFinite(matrix.M21) || !double.IsFinite(matrix.M22) ||
            !double.IsFinite(matrix.OffsetX) || !double.IsFinite(matrix.OffsetY) ||
            !double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
            return false;

        var scaleX = Hypot(matrix.M11, matrix.M12);
        var row1Length = Hypot(matrix.M21, matrix.M22);
        if (!double.IsFinite(scaleX) || scaleX < 1e-12 ||
            !double.IsFinite(row1Length) || row1Length < 1e-12) return false;
        // CSS Transforms 1 selects the reflected axis from the diagonal
        // components before normalizing either row.
        if (determinant < 0 && matrix.M11 < matrix.M22)
            scaleX = -scaleX;
        var cos = matrix.M11 / scaleX;
        var sin = matrix.M12 / scaleX;
        var scaleY = -matrix.M21 * sin + matrix.M22 * cos;
        if (!double.IsFinite(scaleY) || Math.Abs(scaleY) < 1e-12) return false;
        var shear = (matrix.M21 * cos + matrix.M22 * sin) / scaleY;
        value = new Decomposed(scaleX, scaleY, shear, Math.Atan2(sin, cos),
            matrix.OffsetX, matrix.OffsetY);
        return double.IsFinite(shear);
    }

    private static double Lerp(double a, double b, double progress) => a + (b - a) * progress;

    private static double Hypot(double x, double y)
    {
        var major = Math.Max(Math.Abs(x), Math.Abs(y));
        return major == 0 ? 0 : major * Math.Sqrt((x / major) * (x / major) +
            (y / major) * (y / major));
    }

    private readonly record struct Decomposed(double ScaleX, double ScaleY,
        double Shear, double Angle, double OffsetX, double OffsetY);

    private sealed record SkewFunctionInfo(CssSkewFunction Kind);
}

internal enum CssSkewFunction { Skew, SkewX, SkewY }

internal sealed class CssTransformAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssTransformAnimation),
        new PropertyMetadata(null));

    internal Transform? From { get; init; }
    internal Transform? To { get; init; }
    internal IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(Transform);

    public override object GetCurrentValue(object defaultOriginValue,
        object defaultDestinationValue, AnimationClock animationClock)
        => CssTransformInterpolation.Interpolate(From, To,
            EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress)!;

    protected override Freezable CreateInstanceCore() => new CssTransformAnimation
    {
        From = From, To = To,
    };
}
