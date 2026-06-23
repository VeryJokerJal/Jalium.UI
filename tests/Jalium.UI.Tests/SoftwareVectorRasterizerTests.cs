using System;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level regression tests for <see cref="SoftwareVectorRasterizer"/> — the pure-CPU
/// rasterizer that backs SVG (&lt;Image Source="*.svg"&gt;) rendering. These reproduce the
/// three root causes diagnosed for "锯齿 (jaggies) + 部分线条乱飞 (some lines fly off)":
///   R1: SoftwareRenderContext.TransformPoint used a transposed (column-vector) matrix
///       convention, so any rotate/skew/non-symmetric transform mirrored or flung the
///       geometry to the wrong place (pure scale/translate was unaffected — hence "部分").
///   R2: single-figure fills were hard-coded to even-odd, ignoring PathGeometry.FillRule
///       (SVG default = nonzero), hollowing out self-intersecting single paths (stars, etc.).
///   AA: zero anti-aliasing — integer span truncation + single scanline sample meant every
///       edge was a hard staircase.
/// Being pure-CPU (Drawing -> byte[] BGRA), these run with no GPU/RenderContext dependency.
/// </summary>
public class SoftwareVectorRasterizerTests
{
    private static (byte B, byte G, byte R, byte A) GetPixel(byte[] px, int width, int x, int y)
    {
        int off = (y * width + x) * 4;
        return (px[off], px[off + 1], px[off + 2], px[off + 3]);
    }

    /// <summary>Counts pixels whose alpha is strictly between 0 and 255 — the signature of
    /// coverage-based anti-aliasing along an edge.</summary>
    private static int CountPartialAlpha(byte[] px)
    {
        int n = 0;
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] > 0 && px[i] < 255) n++;
        return n;
    }

    /// <summary>Five outer vertices connected in pentagram order (skip-one), producing a
    /// self-intersecting single figure whose center has winding number 2 (filled under
    /// nonzero, hollow under even-odd).</summary>
    private static Point[] Pentagram(double cx, double cy, double r)
    {
        var v = new Point[5];
        for (int k = 0; k < 5; k++)
        {
            double ang = (-90 + k * 72) * Math.PI / 180.0;
            v[k] = new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
        }
        return new[] { v[0], v[2], v[4], v[1], v[3] };
    }

    // ── R1: rotate transform must land geometry where the canonical Matrix.Transform puts it ──
    [Fact]
    public void Rotate_transform_lands_geometry_at_canonical_position_not_transposed()
    {
        // Red horizontal bar (5,5)-(15,10), rotated 90° about (20,20).
        // Canonical row-vector Matrix(0,1,-1,0,40,0): (x,y) -> (40 - y, x), so the bar maps
        // to a vertical bar at x∈[30,35], y∈[5,15]. The transposed (column-vector) bug maps
        // it to (y+40, -x) — entirely off the 40×40 buffer, i.e. the bar vanishes.
        var rect = new RectangleGeometry(new Rect(5, 5, 10, 5));
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 0, 0)), null, rect);
        var group = new DrawingGroup { Transform = new RotateTransform { Angle = 90, CenterX = 20, CenterY = 20 } };
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        // Center of the rotated vertical bar must be red.
        var (_, _, r, a) = GetPixel(pixels!, 40, 32, 10);
        Assert.True(r > 128 && a > 128, $"rotated bar should land at (32,10); got rgba r={r} a={a}");

        // The original (un-rotated) footprint must now be empty.
        var (_, _, r2, a2) = GetPixel(pixels!, 40, 10, 7);
        Assert.True(a2 < 64, $"original footprint (10,7) must be vacated after rotation; got a={a2}");
    }

    // ── R2: self-intersecting single figure must honor nonzero fill rule ──
    [Fact]
    public void Self_intersecting_single_figure_honors_nonzero_fill_rule()
    {
        var pts = Pentagram(20, 20, 16);
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        for (int i = 1; i < pts.Length; i++)
            fig.Segments.Add(new LineSegment { Point = pts[i] });
        var geo = new PathGeometry { FillRule = FillRule.Nonzero };
        geo.Figures.Add(fig);
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0, 0, 255)), null, geo);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        // Pentagram center has winding 2 → filled under nonzero. Even-odd would hollow it.
        var (b, _, _, a) = GetPixel(pixels!, 40, 20, 20);
        Assert.True(b > 128 && a > 128, $"pentagram center must be filled under nonzero; got b={b} a={a}");
    }

    // ── AA: a diagonal edge must produce partial-coverage pixels ──
    [Fact]
    public void Diagonal_edge_produces_antialiased_partial_coverage()
    {
        // Right triangle with a diagonal hypotenuse from (36,4) to (4,36).
        var fig = new PathFigure { StartPoint = new Point(4, 4), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment { Point = new Point(36, 4) });
        fig.Segments.Add(new LineSegment { Point = new Point(4, 36) });
        var geo = new PathGeometry { FillRule = FillRule.Nonzero };
        geo.Figures.Add(fig);
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0, 0, 0)), null, geo);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        int partial = CountPartialAlpha(pixels!);
        Assert.True(partial > 5, $"diagonal edge must yield AA partial-coverage pixels; found {partial}");
    }

    // ── Regression guard: pure scale/translate must remain pixel-correct (unaffected by R1 fix) ──
    [Fact]
    public void Axis_aligned_rect_fills_expected_region()
    {
        var rect = new RectangleGeometry(new Rect(10, 10, 20, 20));
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0, 200, 0)), null, rect);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        var (_, g, _, a) = GetPixel(pixels!, 40, 20, 20);
        Assert.True(g > 128 && a > 128, $"interior (20,20) must be filled; got g={g} a={a}");

        var (_, _, _, aOut) = GetPixel(pixels!, 40, 2, 2);
        Assert.True(aOut < 64, $"exterior (2,2) must be empty; got a={aOut}");
    }

    // ── DrawingGroup.ClipGeometry must confine drawing in the software (SVG) path ──
    [Fact]
    public void Group_clip_geometry_confines_drawing()
    {
        // A red rect covering the whole 40×40 buffer, but the group clips to (10,10,20,20).
        var bigRect = new RectangleGeometry(new Rect(0, 0, 40, 40));
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 0, 0)), null, bigRect);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(10, 10, 20, 20)) };
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        // Inside the clip region → red.
        var (_, _, r1, a1) = GetPixel(pixels!, 40, 20, 20);
        Assert.True(r1 > 128 && a1 > 128, $"inside clip should be red; got r={r1} a={a1}");

        // Outside the clip region → clipped away (transparent).
        var (_, _, _, a2) = GetPixel(pixels!, 40, 3, 3);
        Assert.True(a2 < 64, $"outside clip must be clipped away; got a={a2}");
    }

    // ── R1 second axis: skewX must skew horizontally, not vertically ──
    [Fact]
    public void SkewX_transform_skews_horizontally_not_vertically()
    {
        // Horizontal bar (10,10,20,4). skewX(45°) is row-vector Matrix(1,0,1,1):
        // (x,y) -> (x + y, y), shifting X by Y while leaving the Y range [10,14] intact.
        // The transposed bug turned skewX into skewY ((x,y)->(x, x+y)), smearing the bar
        // down to y≈39 — so a pixel at (20,30) must stay empty after the fix.
        var rect = new RectangleGeometry(new Rect(10, 10, 20, 4));
        var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 0, 0)), null, rect);
        var group = new DrawingGroup { Transform = new SkewTransform { AngleX = 45 } };
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 64, 64, new Rect(0, 0, 64, 64));
        Assert.NotNull(pixels);

        // skewX keeps Y within [10,14]; (20,30) is far below → must be empty.
        var (_, _, _, aBelow) = GetPixel(pixels!, 64, 20, 30);
        Assert.True(aBelow < 64, $"skewX must not smear vertically (transpose bug would); got a={aBelow}");

        // The bar is still present at its own row, shifted in X (source y=12 → x∈[22,42]).
        var (_, _, rBar, aBar) = GetPixel(pixels!, 64, 30, 12);
        Assert.True(rBar > 128 && aBar > 128, $"skewX bar should be present at its row; got r={rBar} a={aBar}");
    }

    // ── Stroke path: an open stroked polyline renders with anti-aliased coverage ──
    [Fact]
    public void Open_stroked_polyline_renders_with_antialiasing()
    {
        var fig = new PathFigure { StartPoint = new Point(8, 8), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new LineSegment { Point = new Point(32, 20) });
        fig.Segments.Add(new LineSegment { Point = new Point(8, 32) });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 3);
        var drawing = new GeometryDrawing(null, pen, geo);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        int strokePixels = 0, partial = 0;
        for (int i = 3; i < pixels!.Length; i += 4)
        {
            if (pixels[i] > 0) strokePixels++;
            if (pixels[i] > 0 && pixels[i] < 255) partial++;
        }
        // A thickness-3 polyline over ~50px of length must cover many pixels and have AA edges.
        Assert.True(strokePixels > 20, $"open stroked polyline must produce coverage; got {strokePixels}");
        Assert.True(partial > 3, $"stroke edges must be anti-aliased; got {partial}");
    }

    // ── Straight-alpha contract: translucent fill must NOT be pre-darkened ──
    [Fact]
    public void Translucent_fill_preserves_straight_color_not_premultiplied()
    {
        // 50%-alpha light gray (200,200,200) on a transparent background. The output buffer
        // is STRAIGHT alpha (the D3D12 upload premultiplies later), so a fully-covered interior
        // pixel must keep RGB≈200 with A≈128 — NOT RGB≈100 (which would be premultiplied here
        // and a second time downstream, the ≈2× darkening the review caught).
        var rect = new RectangleGeometry(new Rect(5, 5, 30, 30));
        var brush = new SolidColorBrush(Color.FromArgb(128, 200, 200, 200));
        var drawing = new GeometryDrawing(brush, null, rect);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        var (b, g, r, a) = GetPixel(pixels!, 40, 20, 20);
        Assert.True(a is > 100 and < 160, $"interior alpha should be ~128 (straight); got a={a}");
        Assert.True(r > 180 && g > 180 && b > 180,
            $"straight RGB must stay ~200, not premultiplied to ~100; got ({b},{g},{r})");
    }

    // ── Stroke max-coverage: translucent stroke overlaps must not double-darken ──
    [Fact]
    public void Translucent_stroke_overlap_does_not_double_darken()
    {
        // 50%-alpha thick stroke with a sharp corner. Per-segment quads + the round-join disk
        // overlap at the vertex; max-coverage compositing keeps overlap alpha ≈ the single layer
        // (128), not ~192 that naive two-layer over-blend would produce.
        var fig = new PathFigure { StartPoint = new Point(8, 30), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new LineSegment { Point = new Point(20, 8) });
        fig.Segments.Add(new LineSegment { Point = new Point(32, 30) });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 6);
        var drawing = new GeometryDrawing(null, pen, geo);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        var pixels = SoftwareVectorRasterizer.Rasterize(group, 40, 40, new Rect(0, 0, 40, 40));
        Assert.NotNull(pixels);

        byte maxAlpha = 0;
        for (int i = 3; i < pixels!.Length; i += 4)
            if (pixels[i] > maxAlpha) maxAlpha = pixels[i];
        Assert.True(maxAlpha > 80, $"translucent stroke should render; max alpha={maxAlpha}");
        Assert.True(maxAlpha <= 150, $"overlap must not darken beyond one layer (~128); max alpha={maxAlpha}");
    }
}
