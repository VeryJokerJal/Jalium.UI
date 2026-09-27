namespace Jalium.UI.Styling;

internal readonly record struct CssFloatRegion(CssFloatSide Side, double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal CssFloatRegion Offset(double x, double y) => this with { X = X + x, Y = Y + y };
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

internal sealed class CssFloatSpace
{
    private readonly List<CssFloatRegion> _regions;
    private readonly int _borrowed;
    private double _sourceTop;
    internal CssFloatSpace(CssFloatEnvironment environment) { _regions = [.. environment.Regions]; _borrowed = _regions.Count; }
    internal bool HasFloats => _regions.Count > 0;
    internal double LastTop => Math.Max(_sourceTop, _regions.Select(region => region.Y).DefaultIfEmpty(0).Max());
    internal double OwnBottom => _regions.Skip(_borrowed).Select(region => region.Bottom).DefaultIfEmpty(0).Max();
    internal CssFloatRegion[] OwnRegions => _regions.Skip(_borrowed).ToArray();
    internal void Add(CssFloatRegion region) => _regions.Add(region);
    internal void AddRange(IEnumerable<CssFloatRegion> regions) => _regions.AddRange(regions);
    internal void NoteSourceTop(double y) => _sourceTop = Math.Max(_sourceTop, y);
    internal CssFloatEnvironment At(double x, double y) => new(_regions.Select(region => region.Offset(-x, -y)).ToArray());

    internal (double Left, double Right, bool Obstructed) Band(double width, double y, double height)
    {
        var left = 0d; var right = width; var obstructed = false;
        foreach (var region in _regions)
        {
            if (region.Height <= 0 || region.Width <= 0 || region.Bottom <= y || region.Y >= y + Math.Max(height, 0.000001) ||
                region.X >= width || region.Right <= 0) continue;
            obstructed = true;
            if (region.Side == CssFloatSide.Left) left = Math.Max(left, region.Right);
            else right = Math.Min(right, region.X);
        }
        return (left, right, obstructed);
    }

    internal double NextBottom(double y) => _regions.Select(region => region.Bottom)
        .Where(bottom => bottom > y + 0.000001).DefaultIfEmpty(double.PositiveInfinity).Min();

    internal double ClearBottom(CssClearSide clear) => clear == CssClearSide.None ? double.NegativeInfinity
        : _regions.Where(region => clear == CssClearSide.Both || clear == CssClearSide.Left && region.Side == CssFloatSide.Left ||
            clear == CssClearSide.Right && region.Side == CssFloatSide.Right).Select(region => region.Bottom)
            .DefaultIfEmpty(double.NegativeInfinity).Max();
}
