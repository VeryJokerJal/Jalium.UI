namespace Jalium.UI.Styling;

internal readonly record struct CssFloatRegion(CssFloatSide Side, double X, double Y, double Width, double Height,
    CssFloatShape? Shape = null, bool IsInitial = false, double InitialLineTop = 0)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal CssFloatRegion Offset(double x, double y) => this with
        { X = X + x, Y = Y + y, InitialLineTop = IsInitial ? InitialLineTop + y : 0 };
}

/// <summary>Immutable exclusions crossing a normal block boundary; coordinates are relative to that block's border box.</summary>
internal sealed class CssFloatEnvironment(CssFloatRegion[] regions) : IEquatable<CssFloatEnvironment>
{
    internal static readonly CssFloatEnvironment Empty = new([]);
    internal CssFloatRegion[] Regions { get; } = regions;
    internal CssFloatEnvironment Offset(double x, double y) => Regions.Length == 0 ? Empty : new(Regions.Select(region => region.Offset(x, y)).ToArray());
    public bool Equals(CssFloatEnvironment? other) => other is not null && Regions.AsSpan().SequenceEqual(other.Regions);
    public override bool Equals(object? other) => other is CssFloatEnvironment environment && Equals(environment);
    public override int GetHashCode()
    {
        var hash = new HashCode(); foreach (var region in Regions) hash.Add(region); return hash.ToHashCode();
    }
}

internal sealed partial class CssFloatSpace
{
    private readonly List<CssFloatRegion> _regions;
    private readonly int _borrowed;
    private double _sourceTop;
    internal CssFloatSpace(CssFloatEnvironment environment) { _regions = [.. environment.Regions]; _borrowed = _regions.Count; }
    internal bool HasFloats => _regions.Count > 0;
    internal double LastTop => Math.Max(_sourceTop, _regions.Select(region => region.Y).DefaultIfEmpty(0).Max());
    internal double OwnBottom => _regions.Skip(_borrowed).Select(region => region.Bottom).DefaultIfEmpty(0).Max();
    internal CssFloatRegion[] OwnRegions => _regions.Skip(_borrowed).ToArray();
    internal double OwnIntrinsicWidth
    {
        get
        {
            var result = 0d;
            for (var i = _borrowed; i < _regions.Count; i++)
            {
                var y = _regions[i].Y; var left = 0d; var right = 0d;
                for (var j = _borrowed; j < _regions.Count; j++)
                {
                    var region = _regions[j];
                    if (region.Width <= 0 || region.Height <= 0 || region.Y > y || region.Bottom <= y) continue;
                    if (region.Side == CssFloatSide.Left) left = Math.Max(left, region.Right);
                    else right += region.Width;
                }
                result = Math.Max(result, left + right);
            }
            return result;
        }
    }
    internal void Add(CssFloatRegion region) => _regions.Add(region);
    internal IDisposable Temporary(CssFloatRegion? region)
    {
        if (region is null) return new TemporaryRegion(null, 0, default);
        var index = _regions.Count; _regions.Add(region.Value);
        return new TemporaryRegion(this, index, region.Value);
    }
    private sealed class TemporaryRegion(CssFloatSpace? space, int index, CssFloatRegion region) : IDisposable
    {
        public void Dispose()
        {
            if (space is null) return;
            if (index < space._regions.Count && space._regions[index].Equals(region)) space._regions.RemoveAt(index);
            space = null;
        }
    }
    internal void AddRange(IEnumerable<CssFloatRegion> regions) => _regions.AddRange(regions);
    internal void NoteSourceTop(double y) => _sourceTop = Math.Max(_sourceTop, y);
    internal readonly record struct Checkpoint(int Count, double SourceTop);
    internal Checkpoint Capture() => new(_regions.Count, _sourceTop);
    internal void Restore(Checkpoint checkpoint)
    {
        if (_regions.Count > checkpoint.Count) _regions.RemoveRange(checkpoint.Count, _regions.Count - checkpoint.Count);
        _sourceTop = checkpoint.SourceTop;
    }
    internal CssFloatEnvironment At(double x, double y) => new(_regions.Select(region => region.Offset(-x, -y)).ToArray());

    internal (double Left, double Right, bool Obstructed) Band(double width, double y, double height, int count = int.MaxValue,
        bool useShapes = false, CssFloatRegion? additional = null)
    {
        var left = 0d; var right = width; var obstructed = false;
        foreach (var region in _regions.Take(count)) Include(region);
        if (additional is { } candidate) Include(candidate);
        return (left, right, obstructed);

        void Include(CssFloatRegion region)
        {
            if (region.Height <= 0 || region.Width <= 0 || region.Bottom <= y || region.Y >= y + Math.Max(height, 0.000001) ||
                region.X >= width || region.Right <= 0) return;
            var start = region.X; var end = region.Right;
            if (useShapes && region.Shape is { } shape)
            {
                var band = shape.Band(y - region.Y, Math.Max(height, .000001));
                if (!band.Active) return;
                start = region.X + band.Left; end = region.X + band.Right;
                if (start >= width || end <= 0) return;
            }
            obstructed = true;
            if (region.Side == CssFloatSide.Left) left = Math.Max(left, end);
            else if (double.IsFinite(width)) right = Math.Min(right, start);
        }
    }

    internal double NextBottom(double y, int count = int.MaxValue) => _regions.Take(count).Select(region => region.Bottom)
        .Where(bottom => bottom > y + 0.000001).DefaultIfEmpty(double.PositiveInfinity).Min();

    // An indefinite right edge has no coordinate. Keep its width contribution
    // separate during max-content probing; finite layout uses ordinary bands.
    internal double IntrinsicRightWidth(double y, double height) => _regions
        .Where(region => region.Side == CssFloatSide.Right && region.Width > 0 && region.Height > 0 &&
            region.Bottom > y && region.Y < y + Math.Max(height, .000001))
        .Sum(region => region.Width);

    internal double ClearBottom(CssClearSide clear) => clear == CssClearSide.None ? double.NegativeInfinity
        : _regions.Where(region => !region.IsInitial && (clear == CssClearSide.Both || clear == CssClearSide.Left && region.Side == CssFloatSide.Left ||
            clear == CssClearSide.Right && region.Side == CssFloatSide.Right)).Select(region => region.Bottom)
            .DefaultIfEmpty(double.NegativeInfinity).Max();
}
