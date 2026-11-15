namespace Jalium.UI.Styling;

internal sealed partial class CssFloatSpace
{
    internal double InitialFloatBottom(CssFloatSide side, double lineY) => _regions
        .Where(region => region.IsInitial && region.Side == side && lineY > region.InitialLineTop + .000001)
        .Select(region => region.Bottom).DefaultIfEmpty(double.NegativeInfinity).Max();

    // Initial letters share the band/shape engine but are in-flow content.
    // Ordinary float clearance must not see them; subsequent blocks have a
    // separate clearance rule based on the initial's containing start side.
    internal double InitialBottom(CssClearSide clear = CssClearSide.Both) => clear == CssClearSide.None
        ? double.NegativeInfinity
        : _regions.Where(region => region.IsInitial && (clear == CssClearSide.Both ||
            clear == CssClearSide.Left && region.Side == CssFloatSide.Left ||
            clear == CssClearSide.Right && region.Side == CssFloatSide.Right))
            .Select(region => region.Bottom).DefaultIfEmpty(double.NegativeInfinity).Max();
}
