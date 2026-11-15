namespace Jalium.UI.Styling;

/// <summary>Breaks continuous block coordinates without cutting a shaped line or an atomic box.</summary>
internal sealed class CssColumnFragmentation
{
    internal readonly record struct Keep(double Start, double End, bool Soft = false);
    internal readonly record struct Paragraph(double[] Tops, double[] Bottoms, int Orphans, int Widows);
    internal readonly List<Keep> Keeps = [];
    internal readonly List<Paragraph> Paragraphs = [];
    internal readonly List<(double Start, double End)> Margins = [];
    internal readonly SortedSet<double> Forced = [];
    private const double Epsilon = .00001;

    internal double Balance(double start, double end, int count, double limit)
    {
        if (end <= start) return 0;
        count = Math.Max(count, Forced.Count(value => value > start + Epsilon && value < end - Epsilon) + 1);
        var low = 0d; var high = end - start;
        // Search in layout coordinates, not integer pixels: DPI and fractional line heights survive.
        for (var iteration = 0; iteration < 48 && high - low > Epsilon; iteration++)
        {
            var height = (low + high) / 2;
            var fragments = Breaks(start, end, height, count + 1, preserveSoft: true);
            if (fragments.Count <= count && fragments.All(fragment => fragment.End - fragment.Start <= height + Epsilon)) high = height;
            else low = height;
        }
        return Math.Min(high, limit);
    }

    internal List<(double Start, double End)> Breaks(double start, double end, double height, int stopAfter = int.MaxValue, bool preserveSoft = false)
    {
        var result = new List<(double, double)>();
        if (end <= start + Epsilon) return result;
        height = height <= Epsilon ? 1 : height;
        var resourceLimit = Math.Min(stopAfter, 16384);
        while (start < end - Epsilon && result.Count < resourceLimit)
        {
            var target = Math.Min(end, start + height);
            var forced = Forced.GetViewBetween(start + Epsilon, target).FirstOrDefault(double.NaN);
            if (double.IsFinite(forced)) target = forced;
            var next = target >= end - Epsilon || double.IsFinite(forced) ? target : Previous(start, target, false);
            if (next <= start + Epsilon && !preserveSoft) next = Previous(start, target, true);
            if (next <= start + Epsilon)
            {
                // An unbreakable box taller than the fragmentainer overflows intact.
                next = target;
                foreach (var keep in Keeps.Where(keep => !keep.Soft || preserveSoft).OrderBy(keep => keep.Start))
                    if (keep.Start < next - Epsilon && keep.End > next + Epsilon) next = keep.End;
                if (preserveSoft)
                    foreach (var paragraph in Paragraphs)
                        if (paragraph.Tops.Length > 0 && next > paragraph.Tops[0] + Epsilon && next < paragraph.Bottoms[^1] - Epsilon &&
                            (paragraph.Bottoms.Count(bottom => bottom > start + Epsilon && bottom <= next + Epsilon) < paragraph.Orphans ||
                             paragraph.Tops.Count(top => top >= next - Epsilon) < paragraph.Widows)) next = paragraph.Bottoms[^1];
                next = Math.Min(end, next);
            }
            // Epsilon permits finding a legal boundary, but must not leave a
            // shaped line split a few millionths before its exact bottom edge.
            if (Math.Abs(next - end) <= Epsilon) next = end;
            else
            {
                var distance = Epsilon; var candidate = next;
                foreach (var keep in Keeps)
                {
                    var delta = Math.Abs(candidate - keep.End);
                    if (delta < distance && keep.End > start && keep.End <= end)
                    { next = keep.End; distance = delta; }
                }
            }
            if (next <= start + Epsilon) next = end;
            if (result.Count == resourceLimit - 1 && stopAfter == int.MaxValue) next = end;
            var nextStart = next;
            if (!double.IsFinite(forced) && next < end - Epsilon)
            {
                foreach (var margin in Margins)
                {
                    if (margin.Start > start + Epsilon && margin.Start <= next + Epsilon && margin.End >= next - Epsilon)
                    {
                        next = margin.Start;
                        nextStart = Math.Min(end, margin.End);
                        break;
                    }
                }
            }
            result.Add((start, next)); start = nextStart;
        }
        return result;
    }

    private double Previous(double start, double target, bool relax)
    {
        var candidate = target;
        for (var iteration = 0; iteration <= Keeps.Count + Paragraphs.Sum(paragraph => paragraph.Tops.Length) + 1; iteration++)
        {
            var before = candidate;
            foreach (var keep in Keeps)
                if ((!keep.Soft || !relax) && keep.Start < candidate - Epsilon && keep.End > candidate + Epsilon)
                    candidate = Math.Min(candidate, keep.Start);
            if (!relax)
                foreach (var paragraph in Paragraphs)
                {
                    if (paragraph.Tops.Length == 0 || candidate <= paragraph.Tops[0] + Epsilon || candidate >= paragraph.Bottoms[^1] - Epsilon) continue;
                    var completed = paragraph.Bottoms.Count(bottom => bottom > start + Epsilon && bottom <= candidate + Epsilon);
                    var remaining = paragraph.Tops.Count(top => top >= candidate - Epsilon);
                    if (completed < paragraph.Orphans) candidate = Math.Min(candidate, paragraph.Tops[0]);
                    else if (remaining < paragraph.Widows)
                    {
                        var index = Math.Max(0, paragraph.Tops.Length - paragraph.Widows);
                        candidate = Math.Min(candidate, paragraph.Tops[index]);
                    }
                }
            if (candidate <= start + Epsilon || before == candidate) break;
        }
        // Leading space is attached to the first content. It must not become an
        // empty first column when an orphan/avoid constraint moves the break back.
        if (candidate < target - Epsilon && candidate > start + Epsilon &&
            Keeps.Any(keep => !keep.Soft && keep.Start >= candidate - Epsilon) &&
            !Keeps.Any(keep => !keep.Soft && keep.End > start + Epsilon && keep.End <= candidate + Epsilon)) return start;
        return candidate;
    }
}
