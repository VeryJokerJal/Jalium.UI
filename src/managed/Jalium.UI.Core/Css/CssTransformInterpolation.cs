using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

/// <summary>Interpolates the supported 2D CSS transform functions and matrix suffixes.</summary>
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
