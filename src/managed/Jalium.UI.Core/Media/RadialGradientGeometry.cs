namespace Jalium.UI.Media;

/// <summary>Immutable radial geometry in paint space. NaN means no paint;
/// positive infinity is SVG's collapsed repeating boundary ramp.</summary>
internal readonly struct RadialGradientGeometry
{
    private readonly Point _center;
    private readonly double _sx, _sy, _fx, _fy, _fr, _dr, _a;
    private readonly double _apexX, _apexY, _apexLimit, _apexTolerance;
    private readonly bool _svg, _empty, _degenerate, _boundary;

    internal uint Flags => _svg ? (_boundary ? 3u : _a == 0 ? 5u : 1u) : 0u;

    internal RadialGradientGeometry(Point center, Point origin, double radiusX, double radiusY,
        double focalRadius, bool svg)
    {
        _center = center; _svg = svg;
        radiusX = Math.Abs(radiusX); radiusY = Math.Abs(radiusY);
        _degenerate = radiusX <= 1e-12 || radiusY <= 1e-12;
        if (svg && radiusX == 0 && radiusY == 0)
        {
            _sx = _sy = Math.Max(1, Math.Max(focalRadius,
                Math.Max(Math.Abs(origin.X - center.X), Math.Abs(origin.Y - center.Y))));
        }
        else { _sx = radiusX; _sy = radiusY; }
        _fx = _sx > 0 ? (origin.X - center.X) / _sx : 0;
        _fy = _sy > 0 ? (origin.Y - center.Y) / _sy : 0;
        if (!svg)
        {
            var length = Math.Sqrt(_fx * _fx + _fy * _fy);
            if (length > .99) { _fx *= .99 / length; _fy *= .99 / length; }
        }
        _fr = svg && _sx > 0 ? focalRadius / _sx : 0;
        _dr = (radiusX > 0 ? 1 : 0) - _fr;
        var distanceSquared = _fx * _fx + _fy * _fy;
        _boundary = svg && _fr == 0 && radiusX > 0 &&
            Math.Abs(distanceSquared - 1) <= 16 * 2.2204460492503131e-16;
        var a = distanceSquared - _dr * _dr;
        _a = _boundary || Math.Abs(a) <= 16 * 2.2204460492503131e-16 *
            Math.Max(distanceSquared, _dr * _dr) ? 0 : a;
        if (svg && _a < 0)
        {
            _apexLimit = -focalRadius / (radiusX - focalRadius);
            _apexX = origin.X + _apexLimit * (center.X - origin.X);
            _apexY = origin.Y + _apexLimit * (center.Y - origin.Y);
            _apexTolerance = 16 * 2.2204460492503131e-16 * Math.Max(1,
                Math.Max(Math.Max(Math.Abs(_apexX), Math.Abs(_apexY)), Math.Max(radiusX, radiusY)));
        }
        _empty = svg && ((_fx == 0 && _fy == 0 && _dr == 0) ||
            (radiusX == 0 != (radiusY == 0)) || focalRadius < 0);
    }

    internal double Evaluate(double x, double y)
    {
        if (_empty) return double.NaN;
        if (!_svg && _degenerate) return 1;
        // Nested circles cover a neighborhood of their zero-radius endpoint.
        // Sampling the excluded mathematical point as transparent creates a
        // visible hole at half-pixel centers. Use its continuous one-sided
        // limit; a shrinking Repeat approaches the seam from below.
        if (_svg && _a < 0 && Math.Abs(x - _apexX) <= _apexTolerance && Math.Abs(y - _apexY) <= _apexTolerance)
            return _dr < 0 ? Math.BitDecrement(_apexLimit) : _apexLimit;
        var px = (x - _center.X) / _sx;
        var py = (y - _center.Y) / _sy;
        var qx = px - _fx; var qy = py - _fy;
        if (!_svg)
        {
            var a = qx * qx + qy * qy;
            if (a <= 1e-12) return 0;
            var b = 2 * (_fx * qx + _fy * qy);
            var c = _fx * _fx + _fy * _fy - 1;
            var s = (-b + Math.Sqrt(Math.Max(b * b - 4 * a * c, 0))) / (2 * a);
            return s > 1e-12 ? 1 / s : 0;
        }
        // |P-F-t(C-F)|² = (fr+t(r-fr))². Paint the largest
        // intersection whose circle has positive radius (Canvas/SVG2).
        var b2 = 2 * (qx * _fx + qy * _fy - _fr * _dr);
        var c2 = qx * qx + qy * qy - _fr * _fr;
        var result = double.NaN;
        var fr = _fr; var dr = _dr;
        void Accept(double t)
        {
            if (double.IsFinite(t) && fr + t * dr > 0 && (double.IsNaN(result) || t > result)) result = t;
        }
        if (_a == 0) { if (b2 != 0) Accept(-c2 / b2); }
        else
        {
            var discriminant = b2 * b2 - 4 * _a * c2;
            if (discriminant >= 0)
            {
                var q = -.5 * (b2 + Math.CopySign(Math.Sqrt(discriminant), b2));
                Accept(q / _a);
                if (q != 0) Accept(c2 / q);
            }
        }
        return double.IsNaN(result) && _boundary ? double.PositiveInfinity : result;
    }
}
