using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private static bool RequiresSvgTextOutline(SvgTextRun run)
        => t_rasterTextOutlines || run.HasStroke || run.OutlineAvailable;

    private static bool HasSvgTextStroke(XElement element)
    {
        var stroke = GetResolvedAttribute(element, "stroke");
        if (stroke is null) return false;
        var reader = new CssTokenReader(stroke);
        if (reader.TryReadIdent(out var name) && name.Equals("none", StringComparison.OrdinalIgnoreCase) && reader.AtEnd)
            return false;
        return ResolveStrokeLength(element, "stroke-width", 1) > 0;
    }

    private static bool SameSvgTextPaint(SvgTextRun a, SvgTextRun b)
        => a.Fill == b.Fill && a.FillOpacity == b.FillOpacity && a.Color == b.Color &&
            (!(a.HasStroke || b.HasStroke) || a.StrokeStyle == b.StrokeStyle) &&
            a.ScaleX == b.ScaleX && (a.ScaleX == 1 || a.Rotate == b.Rotate);

    private static Drawing? CreateSvgTextFill(SvgTextRun run, Dictionary<string, XElement> defs)
    {
        var fill = ResolveFillBrush(run.Element, defs);
        if (fill is null && run.Fill is null) fill = new SolidColorBrush(Colors.Black);
        if (fill is null) return null;
        var formatted = new FormattedText(run.Text, run.Family, run.FontSize)
        {
            Foreground = fill,
            FontWeight = run.Weight,
            FontStyle = run.Style,
        };
        var drawing = new GlyphRunDrawing(formatted, new Point(run.X, run.Y - run.Baseline))
            { ForegroundBrush = fill };
        return drawing;
    }

    private static Drawing TransformSvgTextRun(Drawing drawing, SvgTextRun run)
    {
        if (run.ScaleX != 1)
        {
            var scaled = new DrawingGroup { Transform = new ScaleTransform(run.ScaleX, 1, run.X, run.Y) };
            scaled.Children.Add(drawing); drawing = scaled;
        }
        if (run.Rotate != 0)
        {
            var rotated = new DrawingGroup { Transform = new RotateTransform(run.Rotate, run.X, run.Y) };
            rotated.Children.Add(drawing); drawing = rotated;
        }
        return drawing;
    }

    private static Drawing? CreateSvgTextOutlinePaint(List<SvgTextRun> runs, Dictionary<string, XElement> defs)
    {
        var first = runs[0];
        if (first.ScaleX == 0) return null;
        // Factor a common glyph stretch out of the equivalent path so strokes
        // and dashes stretch along with the glyphs. Rotation alone is safe to
        // bake into individual glyphs because the pen remains isotropic.
        var angle = first.ScaleX == 1 ? 0 : first.Rotate * Math.PI / 180;
        var common = new Matrix(first.ScaleX * Math.Cos(angle), first.ScaleX * Math.Sin(angle),
            -Math.Sin(angle), Math.Cos(angle), 0, 0);
        if (!common.TryInvert(out var inverse)) return null;
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var run in runs)
        {
            if (!run.OutlineAvailable)
                throw new NotSupportedException("SVG text strokes require native glyph outlines");
            if (run.PathData.Length == 0) continue;
            var path = PathMarkupParser.ParseSvgPathData(run.PathData);
            foreach (var figure in path.Figures) figure.IsClosed = true;
            var glyph = BakeTransform(path.GetFlattenedPathGeometry(.01, ToleranceType.Absolute),
                new TranslateTransform(run.X, run.Y - run.Baseline));
            if (run.ScaleX != 1)
                glyph = BakeTransform(glyph, new ScaleTransform(run.ScaleX, 1, run.X, run.Y));
            if (run.Rotate != 0)
                glyph = BakeTransform(glyph, new RotateTransform(run.Rotate, run.X, run.Y));
            if (!common.IsIdentity) glyph = BakeTransform(glyph, new MatrixTransform(inverse));
            foreach (var figure in glyph.Figures) geometry.Figures.Add(figure);
        }
        if (geometry.Figures.Count == 0) return null;
        // One equivalent path per contiguous paint/chunk group: all glyph fills
        // precede all glyph strokes (or the reverse), with shared coverage and
        // paint-server bounds even for dx, rotate, textLength and textPath runs.
        Drawing drawing;
        if (!t_rasterTextOutlines && !first.HasStroke && runs.Count == 1)
        {
            if (CreateSvgTextFill(first, defs) is not GlyphRunDrawing glyph) return null;
            // Keep placement in the SVG coordinate system, including glyph-only
            // rotation. Moving the drawing context instead would move absolute
            // paint servers and give userSpaceOnUse gradients a second origin.
            glyph.SetSvgOutline(geometry);
            drawing = glyph;
        }
        else
        {
            var fill = ResolveFillBrush(first.Element, defs);
            if (fill is null && first.Fill is null) fill = new SolidColorBrush(Colors.Black);
            drawing = SvgPaintOrder.Create(fill, ResolveStrokePen(first.Element, defs, geometry), geometry,
                GetResolvedAttribute(first.Element, "paint-order"), nonScalingStroke:
                GetResolvedAttribute(first.Element, "vector-effect") == "non-scaling-stroke");
        }
        if (common.IsIdentity) return drawing;
        var transformed = new DrawingGroup { Transform = new MatrixTransform(common) };
        transformed.Children.Add(drawing);
        return transformed;
    }
}
