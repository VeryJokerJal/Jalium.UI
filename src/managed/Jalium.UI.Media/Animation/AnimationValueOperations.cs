using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Arithmetic shared by From/To/By and key-frame animations. Colors compose in
/// scRGB space, and rotations compose through quaternion multiplication.
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
        typeof(T) == typeof(Color) ||
        typeof(T) == typeof(Quaternion) ||
        typeof(T) == typeof(Rotation3D) ||
        typeof(T) == typeof(Size3D) ||
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
        bool isCumulative,
        bool useShortestPath = true)
        where T : struct
    {
        T from = GetZero<T>();
        T to = GetZero<T>();
        T foundation = GetZero<T>();

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

        T accumulated = GetZero<T>();
        if (isCumulative && currentIteration > 1)
        {
            accumulated = Scale(Subtract(to, from), currentIteration - 1d);
        }

        T interpolated = typeof(T) == typeof(Quaternion)
            ? Cast<T>(Quaternion.Slerp(Cast<Quaternion>(from), Cast<Quaternion>(to), progress, useShortestPath))
            : Interpolate(from, to, progress);
        return Add(foundation, Add(accumulated, interpolated));
    }

    internal static T GetZero<T>() => typeof(T) == typeof(Rotation3D)
        ? Cast<T>(Rotation3D.Identity) : default!;

    internal static double Distance<T>(T left, T right)
    {
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(short) || typeof(T) == typeof(int) || typeof(T) == typeof(long) ||
            typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(decimal))
            return Math.Abs(Convert.ToDouble(left) - Convert.ToDouble(right));
        static double Square(double value) => value * value;
        return (left, right) switch
        {
            (Point a, Point b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y)),
            (Vector a, Vector b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y)),
            (Size a, Size b) => Math.Sqrt(Square(a.Width - b.Width) + Square(a.Height - b.Height)),
            (Point3D a, Point3D b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Z - b.Z)),
            (Vector3D a, Vector3D b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Z - b.Z)),
            (Size3D a, Size3D b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Z - b.Z)),
            (Rect a, Rect b) => Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Width - b.Width) + Square(a.Height - b.Height)),
            (Thickness a, Thickness b) => Math.Sqrt(Square(a.Left - b.Left) + Square(a.Top - b.Top) + Square(a.Right - b.Right) + Square(a.Bottom - b.Bottom)),
            (Color a, Color b) => Math.Sqrt(Square(a.ScA - b.ScA) + Square(a.ScR - b.ScR) + Square(a.ScG - b.ScG) + Square(a.ScB - b.ScB)),
            (Matrix a, Matrix b) => Math.Sqrt(Square(a.M11 - b.M11) + Square(a.M12 - b.M12) + Square(a.M21 - b.M21) + Square(a.M22 - b.M22) + Square(a.OffsetX - b.OffsetX) + Square(a.OffsetY - b.OffsetY)),
            (Quaternion a, Quaternion b) => QuaternionDistance(a, b),
            (Rotation3D a, Rotation3D b) => QuaternionDistance(Rotation3DAnimation.ToQuaternion(a), Rotation3DAnimation.ToQuaternion(b)),
            _ => double.NaN
        };
    }

    private static double QuaternionDistance(Quaternion left, Quaternion right)
    {
        double dot = left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;
        double lengths = Math.Sqrt((left.X * left.X + left.Y * left.Y + left.Z * left.Z + left.W * left.W) *
            (right.X * right.X + right.Y * right.Y + right.Z * right.Z + right.W * right.W));
        return lengths > 0d ? 2d * Math.Acos(Math.Clamp(Math.Abs(dot) / lengths, 0d, 1d)) : 0d;
    }

    internal static T Add<T>(T left, T right)
    {
        if (typeof(T) == typeof(Color))
        {
            var a = Cast<Color>(left);
            var b = Cast<Color>(right);
            return Cast<T>(Color.FromScRgb(a.ScA + b.ScA, a.ScR + b.ScR, a.ScG + b.ScG, a.ScB + b.ScB));
        }
        if (typeof(T) == typeof(Quaternion))
            return Cast<T>(Cast<Quaternion>(left) * Cast<Quaternion>(right));
        if (typeof(T) == typeof(Rotation3D))
            return Cast<T>(new QuaternionRotation3D(RotationQuaternion(left) * RotationQuaternion(right)));
        if (typeof(T) == typeof(Size3D))
        {
            var a = Cast<Size3D>(left);
            var b = Cast<Size3D>(right);
            return Cast<T>(new Size3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z));
        }
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
        if (typeof(T) == typeof(Color))
        {
            var a = Cast<Color>(left);
            var b = Cast<Color>(right);
            return Cast<T>(Color.FromScRgb(a.ScA - b.ScA, a.ScR - b.ScR, a.ScG - b.ScG, a.ScB - b.ScB));
        }
        if (typeof(T) == typeof(Quaternion))
        {
            var inverse = Cast<Quaternion>(right);
            inverse.Invert();
            return Cast<T>(Cast<Quaternion>(left) * inverse);
        }
        if (typeof(T) == typeof(Rotation3D))
        {
            var inverse = RotationQuaternion(right);
            inverse.Invert();
            return Cast<T>(new QuaternionRotation3D(RotationQuaternion(left) * inverse));
        }
        if (typeof(T) == typeof(Size3D))
        {
            var a = Cast<Size3D>(left);
            var b = Cast<Size3D>(right);
            return Cast<T>(Size3D.CreateUnchecked(a.X - b.X, a.Y - b.Y, a.Z - b.Z));
        }
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
        if (typeof(T) == typeof(Color))
        {
            var color = Cast<Color>(value);
            return Cast<T>(Color.FromScRgb((float)(color.ScA * factor), (float)(color.ScR * factor),
                (float)(color.ScG * factor), (float)(color.ScB * factor)));
        }
        if (typeof(T) == typeof(Quaternion))
        {
            var rotation = Cast<Quaternion>(value);
            return Cast<T>(new Quaternion(rotation.Axis, rotation.Angle * factor));
        }
        if (typeof(T) == typeof(Rotation3D))
        {
            var rotation = RotationQuaternion(value);
            return Cast<T>(new QuaternionRotation3D(new Quaternion(rotation.Axis, rotation.Angle * factor)));
        }
        if (typeof(T) == typeof(Size3D))
        {
            var size = Cast<Size3D>(value);
            return Cast<T>(Size3D.CreateUnchecked(size.X * factor, size.Y * factor, size.Z * factor));
        }
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
        if (typeof(T) == typeof(Color))
        {
            if (progress == 0d) return from;
            if (progress == 1d) return to;
            var a = Cast<Color>(from);
            var b = Cast<Color>(to);
            static byte Channel(byte start, byte end, double amount) =>
                (byte)Math.Clamp(start + (end - start) * amount, 0d, 255d);
            return Cast<T>(Color.FromArgb(Channel(a.A, b.A, progress), Channel(a.R, b.R, progress),
                Channel(a.G, b.G, progress), Channel(a.B, b.B, progress)));
        }
        if (typeof(T) == typeof(Quaternion))
            return Cast<T>(Quaternion.Slerp(Cast<Quaternion>(from), Cast<Quaternion>(to), progress));
        if (typeof(T) == typeof(Rotation3D))
            return Cast<T>(new QuaternionRotation3D(Quaternion.Slerp(RotationQuaternion(from), RotationQuaternion(to), progress)));
        if (typeof(T) == typeof(Size3D))
        {
            var a = Cast<Size3D>(from);
            var b = Cast<Size3D>(to);
            return Cast<T>(Size3D.CreateUnchecked(a.X + (b.X - a.X) * progress,
                a.Y + (b.Y - a.Y) * progress, a.Z + (b.Z - a.Z) * progress));
        }
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

    private static Quaternion RotationQuaternion<T>(T rotation) =>
        Rotation3DAnimation.ToQuaternion((rotation as Rotation3D) ?? Rotation3D.Identity);

    private static NotSupportedException Unsupported<T>() =>
        new($"Animation arithmetic for {typeof(T).FullName} is not implemented.");
}
