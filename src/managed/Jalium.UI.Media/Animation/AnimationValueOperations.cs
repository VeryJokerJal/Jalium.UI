using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// WPF-compatible arithmetic shared by From/To/By and key-frame animations.
/// Deliberately excludes color and rotation composition, whose scRGB and
/// quaternion semantics require their own dedicated implementation.
/// </summary>
internal static class AnimationValueOperations
{
    internal static bool IsSupported<T>() =>
        typeof(T) == typeof(byte) ||
        typeof(T) == typeof(short) ||
        typeof(T) == typeof(int) ||
        typeof(T) == typeof(long) ||
        typeof(T) == typeof(float) ||
        typeof(T) == typeof(double) ||
        typeof(T) == typeof(decimal) ||
        typeof(T) == typeof(Point) ||
        typeof(T) == typeof(Rect) ||
        typeof(T) == typeof(Size) ||
        typeof(T) == typeof(Vector) ||
        typeof(T) == typeof(Thickness) ||
        typeof(T) == typeof(Point3D) ||
        typeof(T) == typeof(Vector3D);

    internal static T EvaluateFromToBy<T>(
        T defaultOriginValue,
        T defaultDestinationValue,
        T? fromValue,
        T? toValue,
        T? byValue,
        double progress,
        int currentIteration,
        bool isAdditive,
        bool isCumulative)
        where T : struct
    {
        T from = default;
        T to = default;
        T foundation = default;

        if (fromValue.HasValue)
        {
            from = fromValue.Value;
            if (toValue.HasValue)
            {
                // From + To wins over By.
                to = toValue.Value;
                if (isAdditive)
                {
                    foundation = defaultOriginValue;
                }
            }
            else if (byValue.HasValue)
            {
                to = Add(from, byValue.Value);
                if (isAdditive)
                {
                    foundation = defaultOriginValue;
                }
            }
            else
            {
                to = defaultDestinationValue;
            }
        }
        else if (toValue.HasValue)
        {
            from = defaultOriginValue;
            to = toValue.Value;
        }
        else if (byValue.HasValue)
        {
            // WPF By animations always use the suggested origin as their
            // foundation, while interpolating from the type's zero value.
            to = byValue.Value;
            foundation = defaultOriginValue;
        }
        else
        {
            from = defaultOriginValue;
            to = defaultDestinationValue;
        }

        T accumulated = default;
        if (isCumulative && currentIteration > 1)
        {
            accumulated = Scale(Subtract(to, from), currentIteration - 1d);
        }

        return Add(foundation, Add(accumulated, Interpolate(from, to, progress)));
    }

    internal static T GetZero<T>() => default!;

    internal static T Add<T>(T left, T right)
    {
        unchecked
        {
            if (typeof(T) == typeof(byte))
                return Cast<T>((byte)(Cast<byte>(left) + Cast<byte>(right)));
            if (typeof(T) == typeof(short))
                return Cast<T>((short)(Cast<short>(left) + Cast<short>(right)));
            if (typeof(T) == typeof(int))
                return Cast<T>(Cast<int>(left) + Cast<int>(right));
            if (typeof(T) == typeof(long))
                return Cast<T>(Cast<long>(left) + Cast<long>(right));
        }

        if (typeof(T) == typeof(float))
            return Cast<T>(Cast<float>(left) + Cast<float>(right));
        if (typeof(T) == typeof(double))
            return Cast<T>(Cast<double>(left) + Cast<double>(right));
        if (typeof(T) == typeof(decimal))
            return Cast<T>(Cast<decimal>(left) + Cast<decimal>(right));
        if (typeof(T) == typeof(Matrix))
        {
            var a = Cast<Matrix>(left);
            var b = Cast<Matrix>(right);
            return Cast<T>(new Matrix(a.M11 + b.M11, a.M12 + b.M12, a.M21 + b.M21, a.M22 + b.M22, a.OffsetX + b.OffsetX, a.OffsetY + b.OffsetY));
        }
        if (typeof(T) == typeof(Point))
        {
            var a = Cast<Point>(left);
            var b = Cast<Point>(right);
            return Cast<T>(new Point(a.X + b.X, a.Y + b.Y));
        }
        if (typeof(T) == typeof(Rect))
        {
            var a = Cast<Rect>(left);
            var b = Cast<Rect>(right);
            return Cast<T>(new Rect(a.X + b.X, a.Y + b.Y, a.Width + b.Width, a.Height + b.Height));
        }
        if (typeof(T) == typeof(Size))
        {
            var a = Cast<Size>(left);
            var b = Cast<Size>(right);
            return Cast<T>(new Size(a.Width + b.Width, a.Height + b.Height));
        }
        if (typeof(T) == typeof(Vector))
            return Cast<T>(Cast<Vector>(left) + Cast<Vector>(right));
        if (typeof(T) == typeof(Thickness))
        {
            var a = Cast<Thickness>(left);
            var b = Cast<Thickness>(right);
            return Cast<T>(new Thickness(
                a.Left + b.Left,
                a.Top + b.Top,
                a.Right + b.Right,
                a.Bottom + b.Bottom));
        }
        if (typeof(T) == typeof(Point3D))
        {
            var a = Cast<Point3D>(left);
            var b = Cast<Point3D>(right);
            return Cast<T>(new Point3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z));
        }
        if (typeof(T) == typeof(Vector3D))
            return Cast<T>(Cast<Vector3D>(left) + Cast<Vector3D>(right));

        throw Unsupported<T>();
    }

    internal static T Subtract<T>(T left, T right)
    {
        unchecked
        {
            if (typeof(T) == typeof(byte))
                return Cast<T>((byte)(Cast<byte>(left) - Cast<byte>(right)));
            if (typeof(T) == typeof(short))
                return Cast<T>((short)(Cast<short>(left) - Cast<short>(right)));
            if (typeof(T) == typeof(int))
                return Cast<T>(Cast<int>(left) - Cast<int>(right));
            if (typeof(T) == typeof(long))
                return Cast<T>(Cast<long>(left) - Cast<long>(right));
        }

        if (typeof(T) == typeof(float))
            return Cast<T>(Cast<float>(left) - Cast<float>(right));
        if (typeof(T) == typeof(double))
            return Cast<T>(Cast<double>(left) - Cast<double>(right));
        if (typeof(T) == typeof(decimal))
            return Cast<T>(Cast<decimal>(left) - Cast<decimal>(right));
        if (typeof(T) == typeof(Matrix))
        {
            var a = Cast<Matrix>(left);
            var b = Cast<Matrix>(right);
            return Cast<T>(new Matrix(a.M11 - b.M11, a.M12 - b.M12, a.M21 - b.M21, a.M22 - b.M22, a.OffsetX - b.OffsetX, a.OffsetY - b.OffsetY));
        }
        if (typeof(T) == typeof(Point))
        {
            var a = Cast<Point>(left);
            var b = Cast<Point>(right);
            return Cast<T>(new Point(a.X - b.X, a.Y - b.Y));
        }
        if (typeof(T) == typeof(Rect))
        {
            var a = Cast<Rect>(left);
            var b = Cast<Rect>(right);
            return Cast<T>(new Rect(a.X - b.X, a.Y - b.Y, a.Width - b.Width, a.Height - b.Height));
        }
        if (typeof(T) == typeof(Size))
        {
            var a = Cast<Size>(left);
            var b = Cast<Size>(right);
            return Cast<T>(new Size(a.Width - b.Width, a.Height - b.Height));
        }
        if (typeof(T) == typeof(Vector))
            return Cast<T>(Cast<Vector>(left) - Cast<Vector>(right));
        if (typeof(T) == typeof(Thickness))
        {
            var a = Cast<Thickness>(left);
            var b = Cast<Thickness>(right);
            return Cast<T>(new Thickness(
                a.Left - b.Left,
                a.Top - b.Top,
                a.Right - b.Right,
                a.Bottom - b.Bottom));
        }
        if (typeof(T) == typeof(Point3D))
        {
            var a = Cast<Point3D>(left);
            var b = Cast<Point3D>(right);
            return Cast<T>(new Point3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z));
        }
        if (typeof(T) == typeof(Vector3D))
            return Cast<T>(Cast<Vector3D>(left) - Cast<Vector3D>(right));

        throw Unsupported<T>();
    }

    internal static T Scale<T>(T value, double factor)
    {
        unchecked
        {
            if (typeof(T) == typeof(byte))
                return Cast<T>((byte)(Cast<byte>(value) * factor));
            if (typeof(T) == typeof(short))
                return Cast<T>((short)(Cast<short>(value) * factor));
            if (typeof(T) == typeof(int))
                return Cast<T>((int)(Cast<int>(value) * factor));
            if (typeof(T) == typeof(long))
                return Cast<T>((long)(Cast<long>(value) * factor));
        }

        if (typeof(T) == typeof(float))
            return Cast<T>((float)(Cast<float>(value) * factor));
        if (typeof(T) == typeof(double))
            return Cast<T>(Cast<double>(value) * factor);
        if (typeof(T) == typeof(decimal))
            return Cast<T>(Cast<decimal>(value) * (decimal)factor);
        if (typeof(T) == typeof(Matrix))
        {
            var matrix = Cast<Matrix>(value);
            return Cast<T>(new Matrix(matrix.M11 * factor, matrix.M12 * factor, matrix.M21 * factor, matrix.M22 * factor, matrix.OffsetX * factor, matrix.OffsetY * factor));
        }
        if (typeof(T) == typeof(Point))
        {
            var point = Cast<Point>(value);
            return Cast<T>(new Point(point.X * factor, point.Y * factor));
        }
        if (typeof(T) == typeof(Rect))
        {
            var rect = Cast<Rect>(value);
            return Cast<T>(new Rect(rect.X * factor, rect.Y * factor, rect.Width * factor, rect.Height * factor));
        }
        if (typeof(T) == typeof(Size))
        {
            var size = Cast<Size>(value);
            return Cast<T>(new Size(size.Width * factor, size.Height * factor));
        }
        if (typeof(T) == typeof(Vector))
            return Cast<T>(Cast<Vector>(value) * factor);
        if (typeof(T) == typeof(Thickness))
        {
            var thickness = Cast<Thickness>(value);
            return Cast<T>(new Thickness(
                thickness.Left * factor,
                thickness.Top * factor,
                thickness.Right * factor,
                thickness.Bottom * factor));
        }
        if (typeof(T) == typeof(Point3D))
        {
            var point = Cast<Point3D>(value);
            return Cast<T>(new Point3D(point.X * factor, point.Y * factor, point.Z * factor));
        }
        if (typeof(T) == typeof(Vector3D))
            return Cast<T>(Cast<Vector3D>(value) * factor);

        throw Unsupported<T>();
    }

    internal static T Interpolate<T>(T from, T to, double progress)
    {
        unchecked
        {
            if (typeof(T) == typeof(byte))
            {
                int start = Cast<byte>(from);
                int delta = Cast<byte>(to) - start;
                return Cast<T>((byte)(start + (int)((delta + 0.5d) * progress)));
            }
            if (typeof(T) == typeof(short))
            {
                short start = Cast<short>(from);
                short end = Cast<short>(to);
                if (progress == 0) return Cast<T>(start);
                if (progress == 1) return Cast<T>(end);
                double addend = (end - start) * progress;
                addend += addend > 0 ? 0.5 : -0.5;
                return Cast<T>((short)(start + (short)addend));
            }
            if (typeof(T) == typeof(int))
            {
                int start = Cast<int>(from);
                int end = Cast<int>(to);
                if (progress == 0) return Cast<T>(start);
                if (progress == 1) return Cast<T>(end);
                double addend = (double)(end - start) * progress;
                addend += addend > 0 ? 0.5 : -0.5;
                return Cast<T>(start + (int)addend);
            }
            if (typeof(T) == typeof(long))
            {
                long start = Cast<long>(from);
                long end = Cast<long>(to);
                if (progress == 0) return Cast<T>(start);
                if (progress == 1) return Cast<T>(end);
                double addend = (double)(end - start) * progress;
                addend += addend > 0 ? 0.5 : -0.5;
                return Cast<T>(start + (long)addend);
            }
        }

        if (typeof(T) == typeof(float))
        {
            float start = Cast<float>(from);
            return Cast<T>(start + (float)((Cast<float>(to) - start) * progress));
        }
        if (typeof(T) == typeof(double))
        {
            double start = Cast<double>(from);
            return Cast<T>(start + (Cast<double>(to) - start) * progress);
        }
        if (typeof(T) == typeof(decimal))
        {
            decimal start = Cast<decimal>(from);
            return Cast<T>(start + (Cast<decimal>(to) - start) * (decimal)progress);
        }
        if (typeof(T) == typeof(Matrix))
        {
            var start = Cast<Matrix>(from);
            var end = Cast<Matrix>(to);
            return Cast<T>(new Matrix(
                start.M11 + (end.M11 - start.M11) * progress,
                start.M12 + (end.M12 - start.M12) * progress,
                start.M21 + (end.M21 - start.M21) * progress,
                start.M22 + (end.M22 - start.M22) * progress,
                start.OffsetX + (end.OffsetX - start.OffsetX) * progress,
                start.OffsetY + (end.OffsetY - start.OffsetY) * progress));
        }
        if (typeof(T) == typeof(Point))
        {
            var start = Cast<Point>(from);
            var end = Cast<Point>(to);
            return Cast<T>(new Point(
                start.X + (end.X - start.X) * progress,
                start.Y + (end.Y - start.Y) * progress));
        }
        if (typeof(T) == typeof(Rect))
        {
            var start = Cast<Rect>(from);
            var end = Cast<Rect>(to);
            return Cast<T>(new Rect(
                start.X + (end.X - start.X) * progress,
                start.Y + (end.Y - start.Y) * progress,
                start.Width + (end.Width - start.Width) * progress,
                start.Height + (end.Height - start.Height) * progress));
        }
        if (typeof(T) == typeof(Size))
        {
            var start = Cast<Size>(from);
            var end = Cast<Size>(to);
            return Cast<T>(new Size(
                start.Width + (end.Width - start.Width) * progress,
                start.Height + (end.Height - start.Height) * progress));
        }
        if (typeof(T) == typeof(Vector))
        {
            var start = Cast<Vector>(from);
            return Cast<T>(start + (Cast<Vector>(to) - start) * progress);
        }
        if (typeof(T) == typeof(Thickness))
        {
            var start = Cast<Thickness>(from);
            var end = Cast<Thickness>(to);
            return Cast<T>(new Thickness(
                start.Left + (end.Left - start.Left) * progress,
                start.Top + (end.Top - start.Top) * progress,
                start.Right + (end.Right - start.Right) * progress,
                start.Bottom + (end.Bottom - start.Bottom) * progress));
        }
        if (typeof(T) == typeof(Point3D))
        {
            var start = Cast<Point3D>(from);
            var end = Cast<Point3D>(to);
            return Cast<T>(new Point3D(
                start.X + (end.X - start.X) * progress,
                start.Y + (end.Y - start.Y) * progress,
                start.Z + (end.Z - start.Z) * progress));
        }
        if (typeof(T) == typeof(Vector3D))
        {
            var start = Cast<Vector3D>(from);
            return Cast<T>(start + (Cast<Vector3D>(to) - start) * progress);
        }

        throw Unsupported<T>();
    }

    private static TTarget Cast<TTarget>(object? value) => (TTarget)value!;

    private static NotSupportedException Unsupported<T>() =>
        new($"Animation arithmetic for {typeof(T).FullName} is not implemented.");
}
