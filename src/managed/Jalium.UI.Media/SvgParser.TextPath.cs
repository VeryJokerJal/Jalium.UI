using System.Globalization;
using System.Xml.Linq;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private sealed class SvgTextPathScope
    {
        internal required int Id;
        internal required SvgTextPathMetric Metric;
        internal required double StartOffset;
        internal required bool Reverse;
        internal int Start, End;
    }

    private readonly record struct SvgTextPathLine(
        Point Start, Point End, double From, double Length);

    private sealed class SvgTextPathMetric
    {
        private readonly List<SvgTextPathLine> _lines = [];
        internal bool ClosedLoop { get; private set; }
        internal double Length { get; private set; }

        internal static SvgTextPathMetric? Create(PathGeometry geometry)
        {
            var flat = geometry.GetFlattenedPathGeometry(0.15, ToleranceType.Absolute);
            var metric = new SvgTextPathMetric
            {
                ClosedLoop = flat.Figures.Count == 1 && flat.Figures[0].IsClosed,
            };
            foreach (var figure in flat.Figures)
            {
                var previous = figure.StartPoint;
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                    {
                        if (!metric.Add(previous, line.Point)) return null;
                        previous = line.Point;
                    }
                    else if (segment is PolyLineSegment polyline)
                    {
                        foreach (var point in polyline.Points)
                        {
                            if (!metric.Add(previous, point)) return null;
                            previous = point;
                        }
                    }
                    else return null;
                }
                if (figure.IsClosed && !metric.Add(previous, figure.StartPoint)) return null;
            }
            return metric.Length > 0 ? metric : null;
        }

        private bool Add(Point start, Point end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || Length + length > 10_000_000) return false;
            if (length <= 0.0000001) return true;
            if (_lines.Count >= 4096) return false;
            _lines.Add(new SvgTextPathLine(start, end, Length, length));
            Length += length;
            return true;
        }

        internal Point EndPoint(bool reverse)
            => reverse ? _lines[0].Start : _lines[^1].End;

        internal bool TrySample(double distance, bool reverse,
            out Point point, out double tangentX, out double tangentY)
        {
            point = default;
            tangentX = tangentY = 0;
            if (!double.IsFinite(distance)) return false;
            if (ClosedLoop)
            {
                distance %= Length;
                if (distance < 0) distance += Length;
            }
            else if (distance < 0 || distance > Length) return false;
            if (reverse) distance = Length - distance;
            var low = 0;
            var high = _lines.Count - 1;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_lines[middle].From + _lines[middle].Length < distance)
                    low = middle + 1;
                else high = middle;
            }
            var line = _lines[low];
            var portion = Math.Clamp((distance - line.From) / line.Length, 0, 1);
            point = new Point(line.Start.X + (line.End.X - line.Start.X) * portion,
                line.Start.Y + (line.End.Y - line.Start.Y) * portion);
            var direction = reverse ? -1 : 1;
            tangentX = direction * (line.End.X - line.Start.X) / line.Length;
            tangentY = direction * (line.End.Y - line.Start.Y) / line.Length;
            return true;
        }
    }

    private static bool TryCreateSvgTextPathScope(XElement element,
        double fontSize, double rootFontSize, int id,
        out SvgTextPathScope? scope)
    {
        scope = null;
        PathGeometry? geometry = null;
        double? authoredPathLength = null;
        var inlinePath = element.Attribute("path")?.Value;
        if (inlinePath is not null)
        {
            if (inlinePath.Length == 0) return false;
            try { geometry = PathMarkupParser.ParseSvgPathData(inlinePath); }
            catch (FormatException) { /* A valid href may still supply the path. */ }
        }
        if (geometry is null)
        {
            var href = element.Attribute("href")?.Value ??
                element.Attribute(XlinkNs + "href")?.Value;
            if (href is null || !href.StartsWith('#') || href.Length == 1) return false;
            var root = element.AncestorsAndSelf().Last();
            var target = root.DescendantsAndSelf().FirstOrDefault(candidate =>
                candidate.Attribute("id")?.Value == href[1..]);
            if (target is null) return false;
            try
            {
                if (target.Name.LocalName == "path")
                {
                    var d = target.Attribute("d")?.Value;
                    if (string.IsNullOrWhiteSpace(d)) return false;
                    geometry = PathMarkupParser.ParseSvgPathData(d);
                }
                else if (target.Name.LocalName is "rect" or "circle" or "ellipse" or "polygon")
                    geometry = ParseClipGeometry(target)?.GetFlattenedPathGeometry(
                        0.15, ToleranceType.Absolute);
                else if (target.Name.LocalName is "line" or "polyline")
                    geometry = ParseSvgTextPathLines(target);
                else return false;
                if (geometry is null) return false;
                if (ParseTransform(target) is { } transform)
                    geometry = BakeTransform(geometry.GetFlattenedPathGeometry(
                        0.15, ToleranceType.Absolute), transform);
                if (target.Attribute("pathLength") is { } pathLengthAttribute)
                {
                    if (!double.TryParse(pathLengthAttribute.Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var declaredLength) ||
                        !double.IsFinite(declaredLength) || declaredLength < 0)
                        return false;
                    authoredPathLength = declaredLength;
                }
            }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }

        var metric = SvgTextPathMetric.Create(geometry);
        if (metric is null || !TryReadSvgTextPathOffset(element, fontSize,
            rootFontSize, metric.Length, authoredPathLength, out var offset)) return false;
        scope = new SvgTextPathScope
        {
            Id = id,
            Metric = metric,
            StartOffset = offset,
            Reverse = element.Attribute("side")?.Value == "right",
        };
        return true;
    }

    private static PathGeometry? ParseSvgTextPathLines(XElement element)
    {
        var figure = new PathFigure();
        if (element.Name.LocalName == "line")
        {
            figure.StartPoint = new Point(ParseDouble(element, "x1"), ParseDouble(element, "y1"));
            figure.Segments.Add(new LineSegment(new Point(
                ParseDouble(element, "x2"), ParseDouble(element, "y2"))));
        }
        else
        {
            var points = ParseNumberList(element.Attribute("points")?.Value ?? string.Empty);
            if (points.Count < 4) return null;
            figure.StartPoint = new Point(points[0], points[1]);
            for (var index = 2; index + 1 < points.Count; index += 2)
                figure.Segments.Add(new LineSegment(new Point(points[index], points[index + 1])));
        }
        return new PathGeometry([figure]);
    }

    private static bool TryReadSvgTextPathOffset(XElement element,
        double fontSize, double rootFontSize, double pathLength,
        double? authoredPathLength, out double offset)
    {
        offset = 0;
        var raw = element.Attribute("startOffset")?.Value?.Trim();
        if (raw is null) return true;
        if (raw.Length == 0) return false;
        var factor = 1.0;
        var percentage = raw.EndsWith('%');
        if (percentage) { factor = pathLength / 100; raw = raw[..^1]; }
        else
        {
            foreach (var (suffix, scale) in new (string Suffix, double Scale)[]
            {
                ("rem", rootFontSize), ("em", fontSize), ("ex", fontSize / 2),
                ("px", 1), ("pt", 96.0 / 72), ("pc", 16),
                ("in", 96), ("cm", 96.0 / 2.54), ("mm", 96.0 / 25.4),
            })
            {
                if (!raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                factor = scale;
                raw = raw[..^suffix.Length];
                break;
            }
        }
        if (!double.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value)) return false;
        offset = value * factor;
        if (!double.IsFinite(offset)) return false;
        if (!percentage && authoredPathLength is { } declaredLength)
        {
            // pathLength calibrates absolute distance along the referenced path.
            // Percentage offsets always use its measured geometric length.
            if (declaredLength == 0)
            {
                offset = offset == 0 ? 0 : Math.CopySign(double.PositiveInfinity, offset);
                return true;
            }
            offset *= pathLength / declaredLength;
        }
        return double.IsFinite(offset);
    }

    private static bool TryApplySvgTextPaths(List<SvgTextRun> runs,
        List<SvgTextPathScope> scopes)
    {
        foreach (var scope in scopes)
        {
            if (scope.Start >= scope.End) continue;
            var first = runs[scope.Start];
            var flatStartY = first.Y - first.Dy;
            for (var chunkStart = scope.Start; chunkStart < scope.End;)
            {
                var chunkEnd = chunkStart + 1;
                while (chunkEnd < scope.End && runs[chunkEnd].AbsoluteX is null)
                    chunkEnd++;
                var chunkFirst = runs[chunkStart];
                var chunkLast = runs[chunkEnd - 1];
                var flatStartX = chunkFirst.X;
                var advance = chunkLast.X + chunkLast.Width - flatStartX;
                var anchorShift = chunkFirst.Anchor switch
                {
                    "middle" => advance / 2,
                    "end" => advance,
                    _ => 0,
                };
                var pathStart = scope.StartOffset + (chunkFirst.AbsoluteX ?? 0) +
                    chunkFirst.Dx - anchorShift;
                var unoffsetStart = (chunkFirst.AbsoluteX ?? 0) +
                    chunkFirst.Dx - anchorShift;
                for (var index = chunkStart; index < chunkEnd; index++)
                {
                    var run = runs[index];
                    var relative = run.X - flatStartX;
                    var distance = pathStart + relative + run.Width / 2;
                    if (scope.Metric.ClosedLoop)
                    {
                        var withoutOffset = unoffsetStart + relative + run.Width / 2;
                        var (minimum, maximum) = chunkFirst.Anchor switch
                        {
                            "middle" => (-scope.Metric.Length / 2, scope.Metric.Length / 2),
                            "end" => (-scope.Metric.Length, 0.0),
                            _ => (0.0, scope.Metric.Length),
                        };
                        if (withoutOffset < minimum || withoutOffset > maximum)
                        {
                            run.Visible = false;
                            continue;
                        }
                    }
                    if (!scope.Metric.TrySample(distance, scope.Reverse,
                        out var point, out var tx, out var ty))
                    {
                        run.Visible = false;
                        continue;
                    }
                    var baselineOffset = run.Y - flatStartY;
                    run.X = point.X - tx * run.Width / 2 - ty * baselineOffset;
                    run.Y = point.Y - ty * run.Width / 2 + tx * baselineOffset;
                    run.Rotate += Math.Atan2(ty, tx) * 180 / Math.PI;
                    if (!double.IsFinite(run.X) || !double.IsFinite(run.Y) ||
                        !double.IsFinite(run.Rotate)) return false;
                }
                chunkStart = chunkEnd;
            }

            if (scope.End >= runs.Count) continue;
            var endpoint = scope.Metric.EndPoint(scope.Reverse);
            var shiftX = endpoint.X - runs[scope.End].X;
            var shiftY = endpoint.Y - runs[scope.End].Y;
            for (var index = scope.End; index < runs.Count &&
                 runs[index].PathScopeId == 0; index++)
            {
                var run = runs[index];
                if (run.AbsoluteX is not null) shiftX = 0;
                if (run.AbsoluteY is not null) shiftY = 0;
                run.X += shiftX;
                run.Y += shiftY;
                if (shiftX == 0 && shiftY == 0) break;
            }
        }
        return true;
    }
}
