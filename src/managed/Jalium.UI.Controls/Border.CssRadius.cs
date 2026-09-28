using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class Border
{
    internal override bool RendersBackdropEffectInOnRender => true;

    private CssRoundedRectangleGeometry? _cssOuterGeometry;
    private CssRoundedRectangleGeometry? _cssOverflowClipGeometry;
    private Geometry? _cssInnerGeometry;
    private PathGeometry? _cssBorderGeometry;
    private (Rect Bounds, CssUsedBorderRadii Radii, Thickness Border, ClipEdges Edges)? _cssGeometryKey;

    private bool TryGetCssRadius(out CssUsedBorderRadii radii)
    {
        // LiquidGlass and nonstandard native shapes keep their own contour.
        if (Shape == BorderShape.RoundedRectangle && !LiquidGlass &&
            CssBorderRadiusProperties.Get(this) is { } value)
        {
            radii = value.Resolve(RenderSize);
            return true;
        }
        radii = default;
        return false;
    }

    // CSS overflow clips descendants at the padding edge; its background still
    // paints under a translucent border. The native WPF path continues clipping itself.
    internal override bool LayoutClipIncludesSelf => Clip is not null ||
        !CssOverflowProperties.TryGetActiveClipMargin(this, out _) && !TryGetCssRadius(out _);

    private void EnsureCssRadiusGeometry(CssUsedBorderRadii radii)
    {
        var bounds = new Rect(RenderSize);
        var border = GetSnappedBorderThickness();
        var edges = CssBorderRadiusProperties.EffectiveClip(this).Edges;
        var key = (bounds, radii, border, edges);
        if (_cssGeometryKey == key) return;
        _cssGeometryKey = key;
        _cssOuterGeometry = new(bounds, radii);
        _cssOuterGeometry.Freeze();
        _cssBorderGeometry = null;
        var inner = GetInnerRect(bounds, border);
        _cssInnerGeometry = new CssRoundedRectangleGeometry(ExpandBoundsClip(inner, edges),
            radii.Inset(border).Mask(edges), edges, inner);
        _cssInnerGeometry.Freeze();
    }

    private CssRoundedRectangleGeometry GetCssOuterGeometry(CssUsedBorderRadii radii)
    {
        EnsureCssRadiusGeometry(radii);
        return _cssOuterGeometry!;
    }

    private Geometry? GetCssRadiusClip(CssUsedBorderRadii radii)
    {
        if (Clip is not null) return Clip;
        var clip = CssBorderRadiusProperties.EffectiveClip(this);
        if (!clip.Enabled || clip.Edges == ClipEdges.None) return null;
        if (CssOverflowProperties.TryGetActiveClipMargin(this, out var margin))
            return GetCssOverflowClip(margin, radii);
        EnsureCssRadiusGeometry(radii);
        return _cssInnerGeometry;
    }

    private Geometry GetCssOverflowClip(CssOverflowClipMargin margin, CssUsedBorderRadii radii)
    {
        var reference = margin.Apply(new Rect(RenderSize), GetSnappedBorderThickness(), Padding, out var outsets);
        var edges = ClipToBoundsEdges;
        var expanded = ExpandBoundsClip(reference, edges);
        var adjusted = radii.Offset(outsets, reference.Size).Mask(edges);
        if (_cssOverflowClipGeometry is { } cached && cached.Rect == expanded &&
            cached.Radii == adjusted && cached.ClipEdges == edges && cached.ClipReferenceRect == reference)
            return cached;
        var geometry = new CssRoundedRectangleGeometry(expanded, adjusted, edges, reference);
        geometry.Freeze();
        return _cssOverflowClipGeometry = geometry;
    }

    private void DrawCssRadiusBorder(DrawingContext dc, CssUsedBorderRadii radii)
    {
        if (BorderBrush is null) return;
        var border = GetSnappedBorderThickness();
        if (border.Left <= 0 && border.Top <= 0 && border.Right <= 0 && border.Bottom <= 0) return;
        dc.DrawGeometry(BorderBrush, null, GetCssBorderGeometry(radii));
    }

    private PathGeometry GetCssBorderGeometry(CssUsedBorderRadii radii)
    {
        EnsureCssRadiusGeometry(radii);
        if (_cssBorderGeometry is null)
        {
            var bounds = new Rect(RenderSize);
            var border = GetSnappedBorderThickness();
            var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                ring.Figures.Add(CssRoundedRectangleGeometry.Figure(bounds, radii));
                var inner = GetInnerRect(bounds, border);
                if (inner.Width > 0 && inner.Height > 0)
                    foreach (var figure in new CssRoundedRectangleGeometry(inner, radii.Inset(border)).Path.Figures)
                        ring.Figures.Add(figure);
            }
            ring.Freeze();
            _cssBorderGeometry = ring;
        }
        return _cssBorderGeometry;
    }

    protected override HitTestResult? HitTestCore(Point point)
    {
        var result = base.HitTestCore(point);
        if (!TryGetCssRadius(out var radii)) return result;
        if (result is not null && !ReferenceEquals(result.VisualHit, this)) return result;
        if (CssPointerEventsProperties.Effective(this) == CssPointerEventsMode.None) return null;
        var local = new Point(point.X - VisualBounds.X, point.Y - VisualBounds.Y);
        if (Visibility != Visibility.Visible || !IsHitTestVisible ||
            !GetCssOuterGeometry(radii).FillContains(local) || Clip is { } clip && !clip.FillContains(local)) return null;
        return result ?? HitTestResult.GetReusable(this);
    }
}
