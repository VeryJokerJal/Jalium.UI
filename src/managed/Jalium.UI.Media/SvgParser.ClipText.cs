using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Jalium.UI.Interop;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private sealed class SvgTextPosition
    {
        internal double[] X = [], Y = [], Dx = [], Dy = [], Rotate = [];
        internal int Consumed;
    }

    private sealed class SvgTextRun
    {
        internal required XElement Element;
        internal required string Text;
        internal required string Family;
        internal required double FontSize;
        internal required int Weight;
        internal required int Style;
        internal required string? Fill;
        internal required string? FillOpacity;
        internal required string? Color;
        internal required string? ClipRule;
        internal required string? Anchor;
        internal required bool Visible;
        internal int LengthScopeId;
        internal int PathScopeId;
        internal double? AbsoluteX, AbsoluteY;
        internal double Dx, Dy, Rotate;
        internal double ScaleX = 1, GapAfter;
        internal string PathData = string.Empty;
        internal double Width, Baseline, X, Y;
    }

    /// <summary>
    /// Lay out x/y/dx/dy/rotate character positions on text and nested tspan elements.
    /// Absolute coordinates start a new text chunk; anchor adjustment is made
    /// after all styled runs in that chunk have contributed their advance.
    /// </summary>
    private static bool TryLayoutSvgText(XElement element, bool requireOutlines,
        out List<SvgTextRun> runs)
    {
        runs = [];
        var items = runs;
        var pending = new List<SvgTextPosition>();
        var lengthScopes = new List<SvgTextLengthScope>();
        var activeLengthScopes = new List<SvgTextLengthScope>();
        var pathScopes = new List<SvgTextPathScope>();
        SvgTextPathScope? activePathScope = null;
        var totalCharacters = 0;
        double deferredDx = 0, deferredDy = 0;
        var inheritedFontSize = 16.0;
        var rootFontSize = 16.0;
        foreach (var ancestor in element.Ancestors().Reverse())
        {
            inheritedFontSize = ResolveSvgTextFontSize(ancestor,
                inheritedFontSize, rootFontSize);
            if (ancestor.Parent is null) rootFontSize = inheritedFontSize;
        }
        if (!Walk(element, inheritedFontSize)) return false;

        var measured = new Dictionary<(string Text, string Family, double Size,
            int Weight, int Style), (string Path, float Width, float Baseline)>();
        foreach (var run in items)
        {
            var key = (run.Text, run.Family, run.FontSize, run.Weight, run.Style);
            if (measured.TryGetValue(key, out var cached))
            {
                run.PathData = cached.Path;
                run.Width = cached.Width;
                run.Baseline = cached.Baseline;
            }
            else if (NativeTextOutline.TryGetPath(run.Text, run.Family, (float)run.FontSize,
                run.Weight, run.Style, out var path, out var width, out var baseline) &&
                float.IsFinite(width) && float.IsFinite(baseline))
            {
                run.PathData = path;
                run.Width = width;
                run.Baseline = baseline;
                measured.Add(key, (path, width, baseline));
            }
            else if (requireOutlines) return false;
            else
            {
                run.Width = run.Text.Length * run.FontSize * .55;
                run.Baseline = run.FontSize * .8;
            }
        }

        return TryPositionSvgTextRuns(items, lengthScopes) &&
            TryApplySvgTextPaths(items, pathScopes);

        bool Walk(XElement current, double parentFontSize)
        {
            if (GetResolvedAttribute(current, "display") == "none") return true;
            if (pending.Count >= 64) return false;
            var fontSize = ResolveSvgTextFontSize(current, parentFontSize, rootFontSize);
            if (!double.IsFinite(fontSize) || fontSize <= 0 || fontSize > 35791)
                return false;
            if (!TryReadSvgTextLength(current, fontSize, out var desiredLength))
                return false;
            var position = new SvgTextPosition();
            if (!TryReadSvgTextCoordinates(current, "x", fontSize, out position.X) ||
                !TryReadSvgTextCoordinates(current, "y", fontSize, out position.Y) ||
                !TryReadSvgTextCoordinates(current, "dx", fontSize, out position.Dx) ||
                !TryReadSvgTextCoordinates(current, "dy", fontSize, out position.Dy) ||
                !TryReadSvgTextRotations(current, out position.Rotate))
                return false;
            SvgTextPathScope? pathScope = null;
            if (current.Name.LocalName == "textPath")
            {
                if (activePathScope is not null || pathScopes.Count >= 64 ||
                    !TryCreateSvgTextPathScope(current, fontSize, rootFontSize,
                        pathScopes.Count + 1, out pathScope))
                    return true;
                pathScope.Start = items.Count;
                pathScopes.Add(pathScope);
                activePathScope = pathScope;
            }
            SvgTextLengthScope? lengthScope = null;
            if (desiredLength is { } targetLength)
            {
                if (lengthScopes.Count >= 512) return false;
                lengthScope = new SvgTextLengthScope
                {
                    Id = lengthScopes.Count + 1,
                    Start = items.Count,
                    TargetLength = targetLength,
                    ScaleGlyphs = current.Attribute("lengthAdjust")?.Value == "spacingAndGlyphs",
                };
                if (activeLengthScopes.Count > 0)
                    activeLengthScopes[^1].Children.Add(lengthScope);
                lengthScopes.Add(lengthScope);
                activeLengthScopes.Add(lengthScope);
            }
            pending.Add(position);
            try
            {
                foreach (var node in current.Nodes())
                {
                    if (node is XElement child && child.Name.LocalName is "tspan" or "text" or "textPath")
                    {
                        if (!Walk(child, fontSize)) return false;
                    }
                    else if (node is XText text && text.Value.Length > 0)
                    {
                        totalCharacters += text.Value.Length;
                        if (totalCharacters > 4096) return false;
                        var remaining = 0;
                        foreach (var candidate in pending)
                            remaining = Math.Max(remaining,
                                Math.Max(Math.Max(candidate.X.Length, candidate.Y.Length),
                                    Math.Max(candidate.Dx.Length, candidate.Dy.Length)) - candidate.Consumed);
                        if (activeLengthScopes.Count == 0 && activePathScope is null &&
                            remaining <= 1 && !pending.Any(candidate =>
                                candidate.Rotate.Any(angle => angle != 0)))
                        {
                            if (!Emit(text.Value, text.Value.Length)) return false;
                        }
                        else
                        {
                            foreach (var rune in text.Value.EnumerateRunes())
                                if (!Emit(rune.ToString(), rune.Utf16SequenceLength)) return false;
                        }
                    }
                }
                return true;
            }
            finally
            {
                pending.RemoveAt(pending.Count - 1);
                if (lengthScope is not null)
                {
                    lengthScope.End = items.Count;
                    activeLengthScopes.RemoveAt(activeLengthScopes.Count - 1);
                }
                if (pathScope is not null)
                {
                    pathScope.End = items.Count;
                    activePathScope = null;
                }
            }

            bool Emit(string content, int characters)
            {
                double? x = null, y = null, dx = null, dy = null, rotate = null;
                for (var index = pending.Count - 1; index >= 0; index--)
                {
                    var candidate = pending[index];
                    if (x is null && candidate.Consumed < candidate.X.Length)
                        x = candidate.X[candidate.Consumed];
                    if (y is null && candidate.Consumed < candidate.Y.Length)
                        y = candidate.Y[candidate.Consumed];
                    if (dx is null && candidate.Consumed < candidate.Dx.Length)
                        dx = candidate.Dx[candidate.Consumed];
                    if (dy is null && candidate.Consumed < candidate.Dy.Length)
                        dy = candidate.Dy[candidate.Consumed];
                    if (rotate is null && candidate.Rotate.Length > 0)
                        rotate = candidate.Rotate[Math.Min(candidate.Consumed,
                            candidate.Rotate.Length - 1)];
                }

                // SVG addresses positioning lists by UTF-16 code unit. A scalar
                // above U+FFFF consumes two entries, but its second dx/dy value
                // moves the following glyph rather than the scalar's own glyph.
                double nextDx = 0, nextDy = 0;
                for (var offset = 1; offset < characters; offset++)
                {
                    var foundDx = false;
                    var foundDy = false;
                    for (var index = pending.Count - 1; index >= 0; index--)
                    {
                        var candidate = pending[index];
                        var positionIndex = candidate.Consumed + offset;
                        if (!foundDx && positionIndex < candidate.Dx.Length)
                        {
                            nextDx += candidate.Dx[positionIndex];
                            foundDx = true;
                        }
                        if (!foundDy && positionIndex < candidate.Dy.Length)
                        {
                            nextDy += candidate.Dy[positionIndex];
                            foundDy = true;
                        }
                    }
                }
                foreach (var candidate in pending) candidate.Consumed += characters;
                var run = new SvgTextRun
                {
                    Element = current,
                    Text = content,
                    Family = ParseSvgFontFamily(current),
                    FontSize = fontSize,
                    Weight = ParseSvgFontWeight(GetResolvedAttribute(current, "font-weight")),
                    Style = ParseSvgFontStyle(GetResolvedAttribute(current, "font-style")),
                    Fill = GetResolvedAttribute(current, "fill"),
                    FillOpacity = GetResolvedAttribute(current, "fill-opacity"),
                    Color = GetResolvedAttribute(current, "color"),
                    ClipRule = GetResolvedAttribute(current, "clip-rule"),
                    Anchor = GetResolvedAttribute(current, "text-anchor"),
                    Visible = GetResolvedAttribute(current, "visibility") is not ("hidden" or "collapse"),
                    LengthScopeId = activeLengthScopes.Count == 0 ? 0 : activeLengthScopes[^1].Id,
                    PathScopeId = activePathScope?.Id ?? 0,
                    AbsoluteX = x,
                    // SVG 2 ignores absolute y positions for horizontal text
                    // on a path; dy still offsets glyphs along the normal.
                    AbsoluteY = activePathScope is null ? y : null,
                    Dx = (dx ?? 0) + deferredDx,
                    Dy = (dy ?? 0) + deferredDy,
                    Rotate = rotate ?? 0,
                };
                deferredDx = nextDx;
                deferredDy = nextDy;
                if (items.Count > 0 && CanMerge(items[^1], run, element)) items[^1].Text += run.Text;
                else
                {
                    if (items.Count >= 512) return false;
                    items.Add(run);
                }
                return true;
            }
        }
    }

    private static bool CanMerge(SvgTextRun previous, SvgTextRun next, XElement textRoot)
        => previous.LengthScopeId == 0 && next.LengthScopeId == 0 &&
           previous.PathScopeId == 0 && next.PathScopeId == 0 &&
           (ReferenceEquals(previous.Element, next.Element) ||
            (!HasOwnSvgTextEffect(previous.Element, textRoot) &&
             !HasOwnSvgTextEffect(next.Element, textRoot))) &&
           next.AbsoluteX is null && next.AbsoluteY is null && next.Dx == 0 && next.Dy == 0 &&
           previous.Rotate == 0 && next.Rotate == 0 &&
           previous.Family == next.Family && previous.FontSize == next.FontSize &&
           previous.Weight == next.Weight && previous.Style == next.Style &&
           previous.Fill == next.Fill && previous.FillOpacity == next.FillOpacity &&
           previous.Color == next.Color && previous.ClipRule == next.ClipRule &&
           previous.Anchor == next.Anchor && previous.Visible == next.Visible;

    private static bool HasOwnSvgTextEffect(XElement element, XElement textRoot)
    {
        for (var current = element; !ReferenceEquals(current, textRoot);
             current = current.Parent!)
            if (GetResolvedAttribute(current, "opacity") is not null ||
                GetResolvedAttribute(current, "transform") is not null ||
                GetResolvedAttribute(current, "clip-path") is not null)
                return true;
        return false;
    }

    private static double ResolveSvgTextFontSize(XElement element, double parentSize,
        double rootSize)
    {
        var style = element.Attribute("style")?.Value;
        string? raw = null;
        if (style is not null)
            ParseStyleProperties(style).TryGetValue("font-size", out raw);
        raw ??= element.Attribute("font-size")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return parentSize;
        raw = raw.Trim();
        var factor = 1.0;
        if (raw.EndsWith('%')) { factor = parentSize / 100; raw = raw[..^1]; }
        else if (raw.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        { factor = rootSize; raw = raw[..^3]; }
        else if (raw.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        { factor = parentSize; raw = raw[..^2]; }
        else if (raw.EndsWith("ex", StringComparison.OrdinalIgnoreCase))
        { factor = parentSize / 2; raw = raw[..^2]; }
        else
        {
            foreach (var (suffix, scale) in new (string Suffix, double Scale)[]
            {
                ("px", 1), ("pt", 96.0 / 72), ("pc", 16), ("in", 96),
                ("cm", 96.0 / 2.54), ("mm", 96.0 / 25.4),
            })
            {
                if (!raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                factor = scale;
                raw = raw[..^suffix.Length];
                break;
            }
        }
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value * factor) && value * factor > 0
            ? value * factor : parentSize;
    }

    private static bool TryReadSvgTextCoordinates(XElement element, string name,
        double fontSize, out double[] values)
    {
        values = [];
        var raw = element.Attribute(name)?.Value;
        if (raw is null) return true;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        var tokens = raw.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is 0 or > 512) return false;
        values = new double[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var factor = 1.0;
            if (token.EndsWith('%'))
            {
                factor = (name is "x" or "dx" or "textLength"
                    ? t_viewportWidth : t_viewportHeight) / 100;
                token = token[..^1];
            }
            else
            {
                foreach (var (suffix, scale) in new (string Suffix, double Scale)[]
                {
                    ("px", 1), ("pt", 96.0 / 72), ("pc", 16), ("in", 96),
                    ("cm", 96.0 / 2.54), ("mm", 96.0 / 25.4),
                    ("em", fontSize), ("ex", fontSize / 2),
                })
                {
                    if (!token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    factor = scale;
                    token = token[..^suffix.Length];
                    break;
                }
            }
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number * factor)) return false;
            values[index] = number * factor;
        }
        return true;
    }

    private static bool TryReadSvgTextRotations(XElement element, out double[] values)
    {
        values = [];
        var raw = element.Attribute("rotate")?.Value;
        if (raw is null) return true;
        var tokens = raw.Split([' ', '\t', '\r', '\n', ','],
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is 0 or > 512) return false;
        values = new double[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
            if (!double.TryParse(tokens[index], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[index]) ||
                !double.IsFinite(values[index])) return false;
        return true;
    }

    private static bool TryParseClipText(XElement element, out PathGeometry? geometry)
    {
        geometry = null;
        if (!TryLayoutSvgText(element, requireOutlines: true, out var runs)) return false;
        var parts = new List<PathGeometry>();
        foreach (var run in runs)
        {
            if (!run.Visible || run.PathData.Length == 0) continue;
            try
            {
                var path = PathMarkupParser.ParseSvgPathData(run.PathData);
                foreach (var figure in path.Figures) figure.IsClosed = true;
                path.FillRule = run.ClipRule == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero;
                var glyph = BakeTransform(path.GetFlattenedPathGeometry(), new TranslateTransform
                {
                    X = run.X, Y = run.Y - run.Baseline,
                });
                if (run.ScaleX != 1)
                    glyph = BakeTransform(glyph, new ScaleTransform
                    {
                        ScaleX = run.ScaleX, ScaleY = 1,
                        CenterX = run.X, CenterY = run.Y,
                    });
                if (run.Rotate != 0)
                    glyph = BakeTransform(glyph, new RotateTransform
                    {
                        Angle = run.Rotate, CenterX = run.X, CenterY = run.Y,
                    });
                for (var owner = run.Element; !ReferenceEquals(owner, element);
                     owner = owner.Parent!)
                    if (ParseTransform(owner) is { } transform)
                        glyph = BakeTransform(glyph, transform);
                parts.Add(glyph);
            }
            catch (FormatException) { return false; }
        }
        geometry = parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => UnionClipPaths(parts),
        };
        return parts.Count == 0 || geometry is not null;
    }

    private static string ParseSvgFontFamily(XElement element)
    {
        var family = GetResolvedAttribute(element, "font-family") ?? "Segoe UI";
        var comma = family.IndexOf(',');
        if (comma >= 0) family = family[..comma];
        family = family.Trim().Trim('\'', '"');
        return string.IsNullOrEmpty(family) ? "Segoe UI" : family;
    }
}
