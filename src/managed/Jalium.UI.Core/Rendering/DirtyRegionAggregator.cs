namespace Jalium.UI.Rendering;

/// <summary>
/// Aggregates dirty rectangles with smart absorption, containment, and adjacency merging.
/// Replaces the previous "single bounding-rect Union" approach that caused scattered
/// small dirty rects to balloon into a full-window bounding box.
///
/// Design goals:
///   1. Absorb redundant rects (new rect already contained in an existing one).
///   2. Replace shadowed rects (existing rect contained in the new one).
///   3. Merge overlapping / adjacent rects only when the merge doesn't waste
///      significantly more pixels than it saves (configurable wasteRatio).
///   4. Enforce a hard capacity via forced merges that minimize total wasted area.
///   5. Compute the TRUE covered area (via sweep-line) for threshold decisions —
///      a bounding-box of two distant small rects would otherwise promote to full.
/// </summary>
public sealed class DirtyRegionAggregator
{
    private readonly List<Rect> _rects;
    private readonly int _capacity;
    private readonly double _mergeWasteRatio;
    private readonly double _adjacencyEpsilon;

    /// <summary>
    /// Creates a new aggregator.
    /// </summary>
    /// <param name="capacity">
    /// Soft capacity. When exceeded, the two rects whose merge produces the least
    /// extra bounding area are combined. Default 32 balances Present1 rect arrays
    /// (DXGI caps out efficiently around 32) with post-process merge cost.
    /// </param>
    /// <param name="mergeWasteRatio">
    /// How much "wasted" area (bounding area minus sum of the two rects' areas)
    /// is acceptable relative to the larger rect's area when speculatively merging
    /// two non-overlapping rects. 0.25 = merge only when waste is &lt;= 25 % of the
    /// larger input. Higher → fewer rects but more overdraw. Default 0.3.
    /// </param>
    /// <param name="adjacencyEpsilon">
    /// Two rects whose edges are within this distance (DIPs) are treated as
    /// touching — collapses anti-aliasing / margin gaps. Default 1.0.
    /// </param>
    public DirtyRegionAggregator(
        int capacity = 32,
        double mergeWasteRatio = 0.3,
        double adjacencyEpsilon = 1.0)
    {
        if (capacity < 2) capacity = 2;
        _rects = new List<Rect>(capacity);
        _capacity = capacity;
        _mergeWasteRatio = mergeWasteRatio;
        _adjacencyEpsilon = adjacencyEpsilon;
    }

    /// <summary>
    /// Number of discrete rects currently in the aggregator (post-merge).
    /// </summary>
    public int Count => _rects.Count;

    /// <summary>
    /// Whether the aggregator holds zero rects.
    /// </summary>
    public bool IsEmpty => _rects.Count == 0;

    /// <summary>
    /// Returns the internal rect list. The caller must not mutate it.
    /// </summary>
    public IReadOnlyList<Rect> Rects => _rects;

    /// <summary>
    /// Clears all rects.
    /// </summary>
    public void Clear() => _rects.Clear();

    /// <summary>
    /// Adds a rect, applying absorption / containment / adjacency merging.
    /// Empty rects are ignored.
    /// </summary>
    public void Add(Rect r)
    {
        // WPF Rect.Empty is a distinct sentinel; zero-area rectangles are valid
        // Rect values but still cover no pixels and must not reach Present1.
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return;

        // 1. Absorption: r is already covered.
        for (int i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].Contains(r)) return;
        }

        // 2. Replacement: remove existing rects that r swallows entirely.
        //    Walk in reverse so RemoveAt indices stay valid.
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            if (r.Contains(_rects[i])) _rects.RemoveAt(i);
        }

        // 3. Beneficial merge: overlap / near-adjacency that doesn't waste too much.
        //    Iterate to a fixed point — merging two rects can make `r` eligible
        //    to merge with yet another.
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < _rects.Count; i++)
            {
                if (ShouldMerge(_rects[i], r))
                {
                    r = Rect.Union(_rects[i], r);
                    _rects.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        _rects.Add(r);

        // 4. Capacity: if we've exceeded the soft cap, force the minimum-waste
        //    merge until we're back under. This guarantees bounded memory and
        //    bounded Present1 rect-array size, and avoids the old "give up and
        //    go full" fallback.
        while (_rects.Count > _capacity)
        {
            ForceMinimumWasteMerge();
        }
    }

    /// <summary>
    /// Union-merges another aggregator into this one.
    /// </summary>
    public void UnionWith(DirtyRegionAggregator other)
    {
        if (other == null) return;
        for (int i = 0; i < other._rects.Count; i++) Add(other._rects[i]);
    }

    /// <summary>
    /// Intersects every rect with <paramref name="clip"/>. Rects fully outside
    /// are dropped; partially-outside rects are clipped.
    /// </summary>
    public void ClipToBounds(Rect clip)
    {
        if (clip.IsEmpty || clip.Width <= 0 || clip.Height <= 0) { _rects.Clear(); return; }
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            var clipped = Rect.Intersect(_rects[i], clip);
            if (clipped.IsEmpty || clipped.Width <= 0 || clipped.Height <= 0) _rects.RemoveAt(i);
            else _rects[i] = clipped;
        }
    }

    /// <summary>
    /// Inflates every rect by <paramref name="margin"/> DIPs and then re-applies
    /// merging (inflation often triggers new overlaps). Inflated rects are then
    /// clipped to <paramref name="clampBounds"/> if supplied.
    /// </summary>
    public void Inflate(double margin, Rect? clampBounds = null)
    {
        if (margin <= 0)
        {
            if (clampBounds is { } c) ClipToBounds(c);
            return;
        }

        var inflated = new List<Rect>(_rects.Count);
        foreach (var r in _rects)
        {
            var x = r.X - margin;
            var y = r.Y - margin;
            var w = r.Width + margin * 2;
            var h = r.Height + margin * 2;
            var ir = new Rect(x, y, w, h);
            if (clampBounds is { } cb) ir = Rect.Intersect(ir, cb);
            if (!ir.IsEmpty && ir.Width > 0 && ir.Height > 0) inflated.Add(ir);
        }

        _rects.Clear();
        foreach (var r in inflated) Add(r);
    }

    /// <summary>
    /// Returns the bounding rectangle enclosing every rect. <see cref="Rect.Empty"/> when empty.
    /// </summary>
    public Rect GetBoundingBox()
    {
        var u = Rect.Empty;
        for (int i = 0; i < _rects.Count; i++) u = Rect.Union(u, _rects[i]);
        return u;
    }

    /// <summary>
    /// Sum of each rect's area (counts overlap twice). Always &gt;= <see cref="ComputeRealArea"/>.
    /// </summary>
    public double ComputeSumArea()
    {
        double total = 0;
        for (int i = 0; i < _rects.Count; i++) total += _rects[i].Width * _rects[i].Height;
        return total;
    }

    /// <summary>
    /// Computes the true covered area (union area, not counting overlap twice).
    /// Uses coordinate-compression + sweep over Y strips. O(N^2) in the worst
    /// case, which is fine for N &lt;= capacity (~32).
    /// </summary>
    public double ComputeRealArea()
    {
        int n = _rects.Count;
        if (n == 0) return 0;
        if (n == 1) return _rects[0].Width * _rects[0].Height;

        // Coordinate compression on Y.
        var ys = new double[n * 2];
        for (int i = 0; i < n; i++)
        {
            ys[i * 2] = _rects[i].Y;
            ys[i * 2 + 1] = _rects[i].Y + _rects[i].Height;
        }
        Array.Sort(ys);

        double total = 0;
        for (int i = 0; i < ys.Length - 1; i++)
        {
            double y0 = ys[i];
            double y1 = ys[i + 1];
            double dy = y1 - y0;
            if (dy <= 0) continue;

            // Collect active X-intervals at this Y strip and merge them in 1D
            // to get the covered horizontal length.
            double coveredX = ComputeCoveredXLength(y0, y1);
            total += coveredX * dy;
        }
        return total;
    }

    /// <summary>
    /// Enumerates the rects — callers use this when submitting to Present1 or
    /// pushing per-rect scissor clips.
    /// </summary>
    public IEnumerable<Rect> EnumerateRects() => _rects;

    // ── Internals ──────────────────────────────────────────────────────────

    private double ComputeCoveredXLength(double yLo, double yHi)
    {
        // Gather X intervals of rects that span this Y strip.
        var intervals = new List<(double x0, double x1)>();
        for (int i = 0; i < _rects.Count; i++)
        {
            var r = _rects[i];
            if (r.Y >= yHi || r.Y + r.Height <= yLo) continue;
            intervals.Add((r.X, r.X + r.Width));
        }
        if (intervals.Count == 0) return 0;

        intervals.Sort(static (a, b) => a.x0.CompareTo(b.x0));

        double total = 0;
        double curX0 = intervals[0].x0;
        double curX1 = intervals[0].x1;
        for (int i = 1; i < intervals.Count; i++)
        {
            var (x0, x1) = intervals[i];
            if (x0 <= curX1)
            {
                if (x1 > curX1) curX1 = x1;
            }
            else
            {
                total += curX1 - curX0;
                curX0 = x0;
                curX1 = x1;
            }
        }
        total += curX1 - curX0;
        return total;
    }

    private bool ShouldMerge(Rect a, Rect b)
    {
        // True overlap → always merge (keeps the aggregator from storing
        // redundant pixels).
        if (a.IntersectsWith(b)) return true;

        // Near-adjacency within adjacencyEpsilon — treat AA / margin gaps as touching.
        bool xClose = a.Right + _adjacencyEpsilon >= b.X && b.Right + _adjacencyEpsilon >= a.X;
        bool yClose = a.Bottom + _adjacencyEpsilon >= b.Y && b.Bottom + _adjacencyEpsilon >= a.Y;
        if (xClose && yClose)
        {
            // Touching or nearly touching — merging here almost always saves
            // GPU work because the two shapes likely share sub-pixel fringe.
            return true;
        }

        // Speculative merge for disjoint rects: only when the waste is small
        // relative to the larger rect. Guards against merging a caret (2×20)
        // with a progress bar (400×8) and losing partial-redraw wins.
        double aArea = a.Width * a.Height;
        double bArea = b.Width * b.Height;
        var u = Rect.Union(a, b);
        double uArea = u.Width * u.Height;
        double waste = uArea - (aArea + bArea);
        double larger = Math.Max(aArea, bArea);
        if (larger <= 0) return false;
        return waste / larger <= _mergeWasteRatio;
    }

    private void ForceMinimumWasteMerge()
    {
        // Find the pair whose merge adds the least bounding area; combine them.
        // O(N^2) but N <= capacity (small). Runs only when we overflow.
        int n = _rects.Count;
        int bestI = 0, bestJ = 1;
        double bestExtra = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double ai = _rects[i].Width * _rects[i].Height;
            for (int j = i + 1; j < n; j++)
            {
                var u = Rect.Union(_rects[i], _rects[j]);
                double extra = u.Width * u.Height - ai - _rects[j].Width * _rects[j].Height;
                if (extra < bestExtra)
                {
                    bestExtra = extra;
                    bestI = i;
                    bestJ = j;
                }
            }
        }
        var merged = Rect.Union(_rects[bestI], _rects[bestJ]);
        // Remove higher index first so the lower one stays valid.
        _rects.RemoveAt(bestJ);
        _rects.RemoveAt(bestI);
        // Re-Add so additional absorptions can fire (merged may now contain others).
        Add(merged);
    }
}
