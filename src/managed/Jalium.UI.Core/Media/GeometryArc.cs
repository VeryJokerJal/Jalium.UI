namespace Jalium.UI.Media;

/// <summary>SVG endpoint-to-center conversion shared by geometry measurements.</summary>
internal readonly record struct GeometryArc(double RadiusX, double RadiusY, double CenterX, double CenterY,
    double Cosine, double Sine, double StartAngle, double SweepAngle)
{
    internal static bool TryCreate(Point start, ArcSegment arc, out GeometryArc result)
    {
        result = default;
        var rx = Math.Abs(arc.Size.Width);
        var ry = Math.Abs(arc.Size.Height);
        var end = arc.Point;
        if (!(rx > 0) || !(ry > 0) || !double.IsFinite(rx) || !double.IsFinite(ry) || start == end)
            return false;
        var rotation = arc.RotationAngle % 360 * Math.PI / 180;
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);
        var dx = start.X / 2 - end.X / 2;
        var dy = start.Y / 2 - end.Y / 2;
        var x = cosine * dx + sine * dy;
        var y = -sine * dx + cosine * dy;
        var lambda = (x / rx) * (x / rx) + (y / ry) * (y / ry);
        if (!(lambda > 0) || !double.IsFinite(lambda)) return false;
        if (lambda > 1)
        {
            var correction = Math.Sqrt(lambda);
            rx *= correction;
            ry *= correction;
            lambda = 1;
        }
        // An exact diameter can round just below one after an affine map.
        // Taking sqrt of that residue creates a spurious center displacement.
        if (1 - lambda < 1e-14) lambda = 1;
        var sign = arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise) ? 1 : -1;
        var factor = sign * Math.Sqrt((1 - lambda) / lambda);
        var localX = factor * rx * (y / ry);
        var localY = -factor * ry * (x / rx);
        var centerX = cosine * localX - sine * localY + start.X / 2 + end.X / 2;
        var centerY = sine * localX + cosine * localY + start.Y / 2 + end.Y / 2;
        var startAngle = Math.Atan2((y - localY) / ry, (x - localX) / rx);
        var endAngle = Math.Atan2((-y - localY) / ry, (-x - localX) / rx);
        var sweep = endAngle - startAngle;
        if (arc.SweepDirection == SweepDirection.Clockwise && sweep < 0) sweep += 2 * Math.PI;
        if (arc.SweepDirection == SweepDirection.Counterclockwise && sweep > 0) sweep -= 2 * Math.PI;
        result = new GeometryArc(rx, ry, centerX, centerY, cosine, sine, startAngle, sweep);
        return true;
    }

    internal Point PointAt(double fraction)
    {
        var angle = StartAngle + SweepAngle * fraction;
        var x = RadiusX * Math.Cos(angle);
        var y = RadiusY * Math.Sin(angle);
        return new Point(CenterX + Cosine * x - Sine * y, CenterY + Sine * x + Cosine * y);
    }
}
