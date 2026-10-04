using System.Xml.Linq;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private sealed class SvgTextLengthScope
    {
        internal required int Id;
        internal required int Start;
        internal required double TargetLength;
        internal required bool ScaleGlyphs;
        internal int End;
        internal List<SvgTextLengthScope> Children = [];
    }

    private static bool TryReadSvgTextLength(XElement element, double fontSize,
        out double? length)
    {
        length = null;
        if (element.Attribute("textLength") is null) return true;
        if (!TryReadSvgTextCoordinates(element, "textLength", fontSize, out var values) ||
            values.Length != 1 || values[0] < 0) return false;
        length = values[0];
        return true;
    }

    /// <summary>
    /// Resolve descendant lengths before their parents. A child with its own
    /// textLength counts as one unit in its parent's spacing calculation.
    /// </summary>
    private static bool TryPositionSvgTextRuns(List<SvgTextRun> runs,
        List<SvgTextLengthScope> scopes)
    {
        if (!Position(applyAnchor: false)) return false;
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            if (!Adjust(scopes[index]) || !Position(applyAnchor: false)) return false;
        }
        return Position(applyAnchor: true);

        bool Position(bool applyAnchor)
        {
            double cursorX = 0, cursorY = 0;
            for (var start = 0; start < runs.Count;)
            {
                var first = runs[start];
                if (first.AbsoluteX is { } x) cursorX = x;
                if (first.AbsoluteY is { } y) cursorY = y;
                var chunkStartX = cursorX;
                var end = start + 1;
                while (end < runs.Count && runs[end].AbsoluteX is null &&
                    runs[end].AbsoluteY is null &&
                    runs[end].PathScopeId == first.PathScopeId) end++;

                for (var index = start; index < end; index++)
                {
                    var run = runs[index];
                    cursorX += run.Dx;
                    cursorY += run.Dy;
                    run.X = cursorX;
                    run.Y = cursorY;
                    cursorX += run.Width + run.GapAfter;
                    if (!double.IsFinite(cursorX) || !double.IsFinite(cursorY)) return false;
                }

                if (applyAnchor)
                {
                    var advance = cursorX - chunkStartX;
                    var shift = first.Anchor switch
                    {
                        "middle" => advance / 2,
                        "end" => advance,
                        _ => 0,
                    };
                    for (var index = start; index < end; index++) runs[index].X -= shift;
                }
                start = end;
            }
            return true;
        }

        bool Adjust(SvgTextLengthScope scope)
        {
            if (scope.Start >= scope.End) return true;
            var units = new List<(int Start, int End)>();
            var direct = new List<int>();
            var childIndex = 0;
            for (var index = scope.Start; index < scope.End;)
            {
                while (childIndex < scope.Children.Count &&
                       scope.Children[childIndex].End <= index) childIndex++;
                if (childIndex < scope.Children.Count &&
                    scope.Children[childIndex].Start == index &&
                    scope.Children[childIndex].End > index)
                {
                    var child = scope.Children[childIndex++];
                    units.Add((index, child.End));
                    index = child.End;
                }
                else
                {
                    units.Add((index, index + 1));
                    direct.Add(index++);
                }
            }

            var delta = scope.TargetLength - Extent(scope);
            if (scope.ScaleGlyphs && direct.Count > 0)
            {
                double directAdvance = 0;
                foreach (var index in direct) directAdvance += runs[index].Width;
                if (directAdvance > 0)
                {
                    var ratio = Math.Max(0, 1 + delta / directAdvance);
                    if (!double.IsFinite(ratio) || ratio > 1_000_000) return false;
                    foreach (var index in direct)
                    {
                        runs[index].Width *= ratio;
                        runs[index].ScaleX *= ratio;
                    }
                    if (!Position(applyAnchor: false)) return false;
                    delta = scope.TargetLength - Extent(scope);
                }
            }
            if (units.Count > 1 && Math.Abs(delta) > 0.000001)
            {
                var gap = delta / (units.Count - 1);
                if (!double.IsFinite(gap)) return false;
                for (var index = 0; index < units.Count - 1; index++)
                    runs[units[index].End - 1].GapAfter += gap;
            }
            return true;
        }

        double Extent(SvgTextLengthScope scope)
            => runs[scope.End - 1].X + runs[scope.End - 1].Width - runs[scope.Start].X;
    }
}
