using System.Globalization;
using System.Xml.Linq;
using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

/// <summary>
/// Parses SVG (Scalable Vector Graphics) documents into Jalium.UI Drawing objects.
/// Supports core SVG shape elements, gradients, transforms, clip paths, and styling.
/// </summary>
internal static partial class SvgParser
{
    [ThreadStatic] private static int t_elementDepth;
    [ThreadStatic] private static SvgCssStyles? t_styles;
    private sealed class StyleScope(SvgCssStyles? previous) : IDisposable
    { public void Dispose() => t_styles = previous; }
    private static IDisposable EnterStyles(SvgCssStyles styles)
    { var previous = t_styles; t_styles = styles; return new StyleScope(previous); }
    [ThreadStatic] private static Stack<(XElement Target, XElement Use)>? t_useInheritance;
    [ThreadStatic] private static bool t_rasterTextOutlines;

    internal static (DrawingGroup Drawing, double Width, double Height) ParseWithTextOutlines(string content)
    {
        var previous = t_rasterTextOutlines;
        try { t_rasterTextOutlines = true; return Parse(content); }
        finally { t_rasterTextOutlines = previous; }
    }
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// Guards <c>&lt;use&gt;</c> expansion against reference cycles (a → b → a), which
    /// otherwise recurse without bound and kill the process with a StackOverflowException —
    /// one malformed document must never crash the host. 32 levels is far beyond any
    /// legitimate nesting. Thread-static because parsing is a synchronous single-threaded
    /// recursion rooted at <see cref="Parse"/>; the entry point resets it.
    /// </summary>
    [ThreadStatic] private static int t_useDepth;
    private const int MaxUseDepth = 32;

    /// <summary>
    /// Viewport size percentage lengths resolve against (SVG: width/x percentages →
    /// viewport width, height/y → viewport height, undirected lengths like r → the
    /// normalized diagonal). Set once by <see cref="Parse"/> before children parse.
    /// </summary>
    [ThreadStatic] private static double t_viewportWidth;
    [ThreadStatic] private static double t_viewportHeight;

    /// <summary>
    /// Parses an SVG document string into a DrawingGroup with optional viewport dimensions.
    /// </summary>
    public static (DrawingGroup Drawing, double Width, double Height) Parse(string svgContent)
        => ParseDocument(svgContent, null, out _);

    internal static (DrawingGroup Drawing, double Width, double Height, SvgIntrinsicDimensions Intrinsic) ParseImage(string content, bool textOutlines)
    {
        var old = t_rasterTextOutlines;
        try
        {
            t_rasterTextOutlines = textOutlines;
            var parsed = ParseDocument(content, null, out var intrinsic);
            return (parsed.Drawing, parsed.Width, parsed.Height, intrinsic);
        }
        finally { t_rasterTextOutlines = old; }
    }

    internal static (DrawingGroup Drawing, double Width, double Height) ParseViewport(string content, Size viewport, bool textOutlines)
    {
        var old = t_rasterTextOutlines;
        try { t_rasterTextOutlines = textOutlines; return ParseDocument(content, viewport, out _); }
        finally { t_rasterTextOutlines = old; }
    }

    private static (DrawingGroup Drawing, double Width, double Height) ParseDocument(string content, Size? viewport, out SvgIntrinsicDimensions intrinsic)
    {
        var oldWidth = t_viewportWidth; var oldHeight = t_viewportHeight;
        var oldUse = t_useDepth; var oldDepth = t_elementDepth; var oldInheritance = t_useInheritance;
        try
        {
            t_useDepth = 0; t_elementDepth = 0; t_useInheritance = null;
            return ParseCore(content, viewport, out intrinsic);
        }
        finally
        {
            t_viewportWidth = oldWidth; t_viewportHeight = oldHeight;
            t_useDepth = oldUse; t_elementDepth = oldDepth; t_useInheritance = oldInheritance;
        }
    }

    private static (DrawingGroup Drawing, double Width, double Height) ParseCore(string svgContent, Size? concreteViewport, out SvgIntrinsicDimensions intrinsic)
    {
        intrinsic = default;
        var doc = XDocument.Parse(svgContent);
        var svgElement = doc.Root;
        if (svgElement == null)
            return (new DrawingGroup(), 0, 0);
        using var markerScope = EnterMarkers();

        var defs = new Dictionary<string, XElement>(StringComparer.Ordinal);
        CollectDefs(svgElement, defs);

        // Parse viewBox and dimensions
        ParseViewBoxAndDimensions(svgElement, out var width, out var height, out var viewBox);

        // Percentage lengths inside the document resolve against the viewport, which is
        // the explicit width/height when present, else the viewBox size.
        t_useDepth = 0;
        t_viewportWidth = concreteViewport?.Width ?? (width > 0 ? width : (viewBox?.Width ?? 0));
        t_viewportHeight = concreteViewport?.Height ?? (height > 0 ? height : (viewBox?.Height ?? 0));

        var styles = new SvgCssStyles(svgElement, new(t_viewportWidth, t_viewportHeight));
        using var styling = EnterStyles(styles);
        var styledWidth = styles.Get(svgElement, "width"); var styledHeight = styles.Get(svgElement, "height");
        if (styledWidth is not null && styledWidth != "auto") width = ParseDouble(svgElement, "width");
        if (styledHeight is not null && styledHeight != "auto") height = ParseDouble(svgElement, "height");
        if (concreteViewport is { } concrete)
        {
            if (styledWidth is null or "auto") width = concrete.Width;
            if (styledHeight is null or "auto") height = concrete.Height;
        }
        // Percentages measure the viewport in user coordinates after viewBox.
        // Media queries continue to see the document's CSS viewport dimensions.
        var cssViewport = concreteViewport ?? new Size(width > 0 ? width : viewBox?.Width ?? 0, height > 0 ? height : viewBox?.Height ?? 0);
        if (styles.Viewport != cssViewport) t_styles = styles = new SvgCssStyles(svgElement, cssViewport);
        double? NaturalLength(string axis)
        {
            var value = styles.Get(svgElement, axis);
            if (value is null) return null;
            var reader = new CssTokenReader(value, new CssNumericReadContext(styles.Lengths(svgElement)));
            return reader.TryReadLength(out var length) && reader.AtEnd && !length.UsesPercent &&
                TryResolveSvgLength(svgElement, axis, value, out var pixels) && pixels >= 0 && double.IsFinite(pixels) ? pixels : null;
        }
        var naturalWidth = NaturalLength("width"); var naturalHeight = NaturalLength("height");
        var ratio = naturalWidth.HasValue && naturalHeight.HasValue
            ? naturalWidth > 0 && naturalHeight > 0 ? naturalWidth / naturalHeight : 0 :
            viewBox is { Width: > 0, Height: > 0 } naturalBox ? naturalBox.Width / naturalBox.Height : 0;
        intrinsic = new(naturalWidth ?? 0, naturalHeight ?? 0, ratio ?? 0)
        { HasWidth = naturalWidth.HasValue, HasHeight = naturalHeight.HasValue };
        t_viewportWidth = viewBox is { Width: > 0 } vbWidth ? vbWidth.Width : width > 0 ? width : cssViewport.Width;
        t_viewportHeight = viewBox is { Height: > 0 } vbHeight ? vbHeight.Height : height > 0 ? height : cssViewport.Height;
        var group = new DrawingGroup { IsSvgViewport = true };
        ApplyOpacity(svgElement, group);
        group.Isolated = true; // An SVG image cannot blend with pixels outside that image.
        group.BlendMode = CssBlendMode.Normal; // The root's own blend sees a transparent image backdrop.
        if (GetResolvedAttribute(svgElement, "display") != "none") ParseChildren(svgElement, group, defs);

        if (concreteViewport is { } output)
        {
            // Without a viewBox, absolute root dimensions provide an intrinsic
            // user space. Percent/auto dimensions use the allocated viewport.
            // CSS media and viewport units still see the concrete image size.
            if (viewBox is null)
            {
                var sx = width > 0 ? output.Width / width : 1;
                var sy = height > 0 ? output.Height / height : 1;
                if (sx != 1 || sy != 1) group.Transform = new ScaleTransform(sx, sy);
            }
            width = output.Width; height = output.Height;
        }

        // Apply viewBox transform if needed
        if (viewBox != null && width > 0 && height > 0)
        {
            var vb = viewBox.Value;
            if (vb.Width > 0 && vb.Height > 0 &&
                (vb.X != 0 || vb.Y != 0 ||
                 vb.Width != width || vb.Height != height))
            {
                group.Transform = new MatrixTransform(MarkerViewBox(svgElement.Attribute("preserveAspectRatio")?.Value, vb, width, height));
            }
        }
        else if (viewBox != null && (width <= 0 || height <= 0))
        {
            // No explicit width/height; the viewBox supplies the viewport size — and its
            // min-x/min-y still shifts user space. Skipping the translation painted all
            // content offset by (minX, minY), i.e. fully outside the viewport for
            // documents like viewBox="100 200 24 24".
            var vb = viewBox.Value;
            width = vb.Width;
            height = vb.Height;
            if (vb.X != 0 || vb.Y != 0)
            {
                group.Transform = new TranslateTransform { X = -vb.X, Y = -vb.Y };
            }
        }

        ApplyTransform(svgElement, group);
        ResolveMarkerContextPaints(group);
        return (group, width, height);
    }

    private static void ParseViewBoxAndDimensions(XElement svg, out double width, out double height, out Rect? viewBox)
    {
        width = ParseLengthAttribute(svg, "width", 0);
        height = ParseLengthAttribute(svg, "height", 0);
        viewBox = null;

        var vbAttr = svg.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(vbAttr))
        {
            var parts = vbAttr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 &&
                double.TryParse(parts[0], CultureInfo.InvariantCulture, out var vx) &&
                double.TryParse(parts[1], CultureInfo.InvariantCulture, out var vy) &&
                double.TryParse(parts[2], CultureInfo.InvariantCulture, out var vw) &&
                double.TryParse(parts[3], CultureInfo.InvariantCulture, out var vh))
            {
                viewBox = new Rect(vx, vy, vw, vh);
            }
        }
    }

    private static void CollectDefs(XElement root, Dictionary<string, XElement> defs)
    {
        foreach (var defsElement in root.Descendants().Where(e =>
            e.Name.LocalName == "defs" || e.Name == SvgNs + "defs"))
        {
            foreach (var child in defsElement.Elements())
            {
                var id = child.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id))
                    defs[id] = child;
            }
        }

        // Also collect any element with an id (for <use> references)
        foreach (var element in root.Descendants())
        {
            var id = element.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id) && !defs.ContainsKey(id))
                defs[id] = element;
        }
    }

    private static void ParseChildren(XElement parent, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        foreach (var child in parent.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip defs, they are referenced not rendered directly
            if (localName is "defs" or "mask" or "style" or "metadata" or "title" or "desc")
                continue;

            ParseElement(child, group, defs);
        }
    }

    private static void ParseElement(XElement element, DrawingGroup parentGroup, Dictionary<string, XElement> defs)
    {
        if (t_elementDepth >= 256) throw new InvalidDataException("SVG nesting exceeds the rendering depth budget");
        t_elementDepth++;
        try { ParseElementCore(element, parentGroup, defs); }
        finally { t_elementDepth--; }
    }

    private static void ParseElementCore(XElement element, DrawingGroup parentGroup, Dictionary<string, XElement> defs)
    {
        var localName = element.Name.LocalName;

        if (GetResolvedAttribute(element, "display") == "none") return;
        var visibility = GetResolvedAttribute(element, "visibility");
        if (localName is not ("g" or "svg" or "text") &&
            (visibility is "hidden" or "collapse"))
            return;

        switch (localName)
        {
            case "mask":
            case "defs":
            case "style":
                return;
            case "g":
                ParseGroup(element, parentGroup, defs);
                break;
            case "svg":
                ParseNestedSvg(element, parentGroup, defs);
                break;
            case "rect":
                ParseRect(element, parentGroup, defs);
                break;
            case "circle":
                ParseCircle(element, parentGroup, defs);
                break;
            case "ellipse":
                ParseEllipse(element, parentGroup, defs);
                break;
            case "line":
                ParseLine(element, parentGroup, defs);
                break;
            case "polyline":
                ParsePolyline(element, parentGroup, defs, closed: false);
                break;
            case "polygon":
                ParsePolyline(element, parentGroup, defs, closed: true);
                break;
            case "path":
                ParsePath(element, parentGroup, defs);
                break;
            case "text":
                ParseText(element, parentGroup, defs);
                break;
            case "image":
                ParseImage(element, parentGroup, defs);
                break;
            case "use":
                ParseUse(element, parentGroup, defs);
                break;
            case "clipPath":
            case "linearGradient":
            case "radialGradient":
            case "symbol":
            case "marker":
                // These are definitions, not rendered directly
                break;
            default:
                // Unknown element - try parsing children
                ParseChildren(element, parentGroup, defs);
                break;
        }
    }

    #region Shape Parsing

    private static void ParseNestedSvg(XElement element, DrawingGroup parent, Dictionary<string, XElement> defs)
    {
        var width = GetResolvedAttribute(element, "width") is null or "auto" ? t_viewportWidth : ParseDouble(element, "width");
        var height = GetResolvedAttribute(element, "height") is null or "auto" ? t_viewportHeight : ParseDouble(element, "height");
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))) return;
        var x = ParseDouble(element, "x"); var y = ParseDouble(element, "y");
        Rect? box = null;
        if (element.Attribute("viewBox")?.Value is { } raw)
        {
            var values = ParseNumberList(raw);
            if (values.Count == 4 && values.All(double.IsFinite))
            {
                if (values[2] <= 0 || values[3] <= 0) return;
                box = new(values[0], values[1], values[2], values[3]);
            }
        }
        var oldWidth = t_viewportWidth; var oldHeight = t_viewportHeight;
        var contents = new DrawingGroup { Transform = new MatrixTransform(MarkerViewBox(element.Attribute("preserveAspectRatio")?.Value, box, width, height)) };
        try { t_viewportWidth = box?.Width ?? width; t_viewportHeight = box?.Height ?? height; ParseChildren(element, contents, defs); }
        finally { t_viewportWidth = oldWidth; t_viewportHeight = oldHeight; }
        var viewport = new DrawingGroup { Transform = new TranslateTransform(x, y) };
        viewport.Children.Add(contents);
        // Nested SVG establishes a viewport; it does not reset the host space
        // used by vector-effect. Its overflow clip precedes x/y and transform.
        if (!t_styles!.IsSpecified(element, "overflow") || GetResolvedAttribute(element, "overflow") != "visible")
            viewport.ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height));
        AddDrawing(element, viewport, parent, defs);
    }

    private static void ParseGroup(XElement element, DrawingGroup parentGroup, Dictionary<string, XElement> defs)
    {
        var childGroup = new DrawingGroup();

        // Apply group-level attributes
        ApplyOpacity(element, childGroup);
        ParseChildren(element, childGroup, defs);
        ApplyTransform(element, childGroup);

        if (childGroup.Children.Count > 0)
        {
            ApplyClipPath(element, childGroup, defs);
            var bounds = Rect.Empty;
            foreach (var child in childGroup.Children) bounds.Union(SvgObjectBounds(child));
            childGroup.SvgMask = ResolveLocalMask(element, defs, bounds);
            parentGroup.Children.Add(childGroup);
        }
    }

    private static void ParseRect(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var x = ParseDouble(element, "x");
        var y = ParseDouble(element, "y");
        var w = ParseDouble(element, "width");
        var h = ParseDouble(element, "height");
        var rx = ParseDouble(element, "rx");
        var ry = ParseDouble(element, "ry");

        if (w <= 0 || h <= 0) return;

        // If only rx or ry is specified, use the same value for both
        if (rx > 0 && ry <= 0) ry = rx;
        if (ry > 0 && rx <= 0) rx = ry;

        Geometry geometry;
        if (rx > 0 || ry > 0)
            geometry = new RectangleGeometry(new Rect(x, y, w, h), rx, ry);
        else
            geometry = new RectangleGeometry(new Rect(x, y, w, h));

        AddShapeDrawing(element, geometry, group, defs);
    }

    private static void ParseCircle(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var cx = ParseDouble(element, "cx");
        var cy = ParseDouble(element, "cy");
        var r = ParseDouble(element, "r");

        if (r <= 0) return;

        var geometry = new EllipseGeometry { Center = new Point(cx, cy), RadiusX = r, RadiusY = r };
        AddShapeDrawing(element, geometry, group, defs);
    }

    private static void ParseEllipse(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var cx = ParseDouble(element, "cx");
        var cy = ParseDouble(element, "cy");
        var rx = ParseDouble(element, "rx");
        var ry = ParseDouble(element, "ry");

        if (rx <= 0 || ry <= 0) return;

        var geometry = new EllipseGeometry { Center = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
        AddShapeDrawing(element, geometry, group, defs);
    }

    private static void ParseLine(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var x1 = ParseDouble(element, "x1");
        var y1 = ParseDouble(element, "y1");
        var x2 = ParseDouble(element, "x2");
        var y2 = ParseDouble(element, "y2");

        var geometry = new LineGeometry(new Point(x1, y1), new Point(x2, y2));
        AddShapeDrawing(element, geometry, group, defs);
    }

    private static void ParsePolyline(XElement element, DrawingGroup group, Dictionary<string, XElement> defs, bool closed)
    {
        var pointsStr = GetAttribute(element, "points");
        if (string.IsNullOrWhiteSpace(pointsStr)) return;

        var numbers = ParseNumberList(pointsStr);
        if (numbers.Count < 2) return;

        var figure = new PathFigure
        {
            StartPoint = new Point(numbers[0], numbers[1]),
            IsClosed = closed,
            // SVG fills an open <polyline> as if implicitly closed (the fill and the
            // stroke have independent topology); whether anything is actually painted
            // is decided by the resolved fill brush, not by the figure flag.
            IsFilled = true
        };

        for (int i = 2; i + 1 < numbers.Count; i += 2)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(numbers[i], numbers[i + 1]) });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        // Apply fill-rule (SVG default is nonzero)
        var fillRule = GetResolvedAttribute(element, "fill-rule");
        if (fillRule == "evenodd")
            geometry.FillRule = FillRule.EvenOdd;
        else
            geometry.FillRule = FillRule.Nonzero;

        AddShapeDrawing(element, geometry, group, defs);
    }

    private static void ParsePath(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var d = GetAttribute(element, "d");
        if (string.IsNullOrWhiteSpace(d)) return;

        try
        {
            var geometry = PathMarkupParser.Parse(d);

            // Apply fill-rule from element.
            // SVG default is "nonzero" (differs from PathGeometry default "EvenOdd").
            var fillRule = GetResolvedAttribute(element, "fill-rule");
            if (fillRule == "evenodd")
                geometry.FillRule = FillRule.EvenOdd;
            else
                geometry.FillRule = FillRule.Nonzero; // SVG default

            AddShapeDrawing(element, geometry, group, defs);
        }
        catch
        {
            // Invalid path data - skip silently
        }
    }

    /// <summary>
    /// Parses an SVG &lt;text&gt; element into positioned glyph runs. Nested
    /// &lt;tspan&gt; elements can change the font, fill, and scalar x/y/dx/dy
    /// coordinates; text-anchor is applied to each absolute-position chunk.
    ///
    /// Stroked text uses the measured glyph outlines on every drawing path.
    /// Fill-only text retains glyph/font metadata together with the vector ink
    /// required by software drawing and cached image rasterization.
    /// </summary>
    private static void ParseText(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        if (TryLayoutSvgText(element, requireOutlines: t_rasterTextOutlines, out var runs))
        {
            var contents = new DrawingGroup();
            var openSpans = new List<(XElement Element, DrawingGroup Drawing)>();
            var paintRuns = new List<SvgTextRun>();
            DrawingGroup? paintTarget = null;
            void FlushPaint()
            {
                if (paintRuns.Count == 0) return;
                if (CreateSvgTextOutlinePaint(paintRuns, defs) is { } painted)
                    paintTarget!.Children.Add(painted);
                paintRuns.Clear();
            }
            foreach (var run in runs)
            {
                if (!run.Visible || string.IsNullOrWhiteSpace(run.Text))
                    continue;

                // Keep the text/tspan tree so each span's opacity, transform and
                // local clip apply to its complete subtree rather than being lost
                // when neighboring glyph runs happen to share the same paint.
                var ancestors = new List<XElement>();
                for (var current = run.Element; !ReferenceEquals(current, element);
                     current = current.Parent!)
                    ancestors.Add(current);
                ancestors.Reverse();
                var shared = 0;
                while (shared < openSpans.Count && shared < ancestors.Count &&
                       ReferenceEquals(openSpans[shared].Element, ancestors[shared]))
                    shared++;
                if (shared != openSpans.Count || shared != ancestors.Count) FlushPaint();
                while (openSpans.Count > shared)
                {
                    var last = openSpans[^1];
                    openSpans.RemoveAt(openSpans.Count - 1);
                    AddDrawing(last.Element, last.Drawing,
                        openSpans.Count == 0 ? contents : openSpans[^1].Drawing, defs);
                }
                for (var index = shared; index < ancestors.Count; index++)
                    openSpans.Add((ancestors[index], new DrawingGroup()));

                var target = openSpans.Count == 0 ? contents : openSpans[^1].Drawing;
                if (RequiresSvgTextOutline(run))
                {
                    if (paintRuns.Count > 0 && (!ReferenceEquals(paintTarget, target) ||
                        paintRuns[0].ChunkId != run.ChunkId || !SameSvgTextPaint(paintRuns[0], run)))
                        FlushPaint();
                    paintTarget = target;
                    paintRuns.Add(run);
                    continue;
                }
                FlushPaint();
                var glyphDrawing = CreateSvgTextFill(run, defs);
                if (glyphDrawing is null) continue;
                target.Children.Add(TransformSvgTextRun(glyphDrawing, run));
            }
            FlushPaint();
            while (openSpans.Count > 0)
            {
                var last = openSpans[^1];
                openSpans.RemoveAt(openSpans.Count - 1);
                AddDrawing(last.Element, last.Drawing,
                    openSpans.Count == 0 ? contents : openSpans[^1].Drawing, defs);
            }
            if (contents.Children.Count == 0) return;
            AddDrawing(element, contents.Children.Count == 1 ? contents.Children[0] : contents,
                group, defs);
            return;
        }

        if (t_rasterTextOutlines)
            throw new NotSupportedException("SVG image text requires native glyph outlines");

        // Retain the old single-run fallback for unsupported character-position
        // lists and invalid positioning values.
        var sb = new System.Text.StringBuilder();
        CollectTextContent(element, sb);
        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var x = ParseDoubleAttribute(element, "x", 0);
        var y = ParseDoubleAttribute(element, "y", 0);
        var fontSize = ParseDoubleAttribute(element, "font-size", 16);
        if (fontSize <= 0) return;

        var fontFamily = ParseSvgFontFamily(element);

        // fill="none" means "don't paint the text" (distinct from an absent fill, which
        // defaults to black per SVG). ResolveFillBrush returns null for both, so check the
        // raw attribute to avoid rendering none-filled text as black.
        if (GetResolvedAttribute(element, "fill") == "none") return;
        var fill = ResolveFillBrush(element, defs) ?? new SolidColorBrush(Color.FromRgb(0, 0, 0));

        var ft = new FormattedText(text, fontFamily, fontSize)
        {
            Foreground = fill,
            FontWeight = ParseSvgFontWeight(GetResolvedAttribute(element, "font-weight")),
            FontStyle = ParseSvgFontStyle(GetResolvedAttribute(element, "font-style")),
        };

        // SVG (x,y) is the baseline anchor; FormattedText's origin is top-left.
        // Use the same native metrics as text clip-path when available.
        var anchor = GetResolvedAttribute(element, "text-anchor");
        var runWidth = text.Length * fontSize * 0.55;
        var baseline = fontSize * 0.8;
        if (NativeTextOutline.TryGetPath(text, fontFamily, (float)fontSize,
            ft.FontWeight, ft.FontStyle, out _, out var measuredWidth, out var measuredBaseline) &&
            float.IsFinite(measuredWidth) && float.IsFinite(measuredBaseline))
        {
            runWidth = measuredWidth;
            baseline = measuredBaseline;
        }
        var originX = anchor switch
        {
            "middle" => x - runWidth / 2,
            "end" => x - runWidth,
            _ => x,
        };
        var originY = y - baseline;

        var drawing = new GlyphRunDrawing(ft, new Point(originX, originY)) { ForegroundBrush = fill };
        AddDrawing(element, drawing, group, defs);
    }

    /// <summary>Recursively concatenates the text content of a &lt;text&gt;/&lt;tspan&gt; element.</summary>
    private static void CollectTextContent(XElement element, System.Text.StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText t)
            {
                sb.Append(t.Value);
            }
            else if (node is XElement child &&
                     (child.Name.LocalName is "tspan" or "text" or "textPath"))
            {
                CollectTextContent(child, sb);
            }
        }
    }

    private static int ParseSvgFontWeight(string? weight)
    {
        if (string.IsNullOrEmpty(weight)) return 400;
        if (int.TryParse(weight, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return Math.Clamp(n, 1, 999);
        return weight switch
        {
            "bold" or "bolder" => 700,
            "lighter" => 300,
            _ => 400,
        };
    }

    private static int ParseSvgFontStyle(string? style) => style switch
    {
        "italic" => 1,
        "oblique" => 2,
        _ => 0,
    };

    private static void ParseUse(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var href = element.Attribute(XlinkNs + "href")?.Value
                   ?? element.Attribute("href")?.Value;
        if (string.IsNullOrEmpty(href)) return;

        // Remove leading '#'
        if (href.StartsWith('#'))
            href = href.Substring(1);

        if (!defs.TryGetValue(href, out var referenced)) return;

        // Reference cycles (a → b → a) recurse forever and a StackOverflowException
        // cannot be caught — cap the expansion depth so a malformed document degrades
        // to partial output instead of killing the process.
        if (t_useDepth >= MaxUseDepth) return;

        var useGroup = new DrawingGroup();

        // Apply x/y translation
        var x = ParseDouble(element, "x");
        var y = ParseDouble(element, "y");
        if (x != 0 || y != 0)
        {
            useGroup.Transform = new TranslateTransform { X = x, Y = y };
        }

        // Apply transform on the <use> element itself
        ApplyTransform(element, useGroup);

        t_useDepth++;
        var inheritance = t_useInheritance ??= new();
        inheritance.Push((referenced, element));
        using var paintContext = EnterUseContext(element, useGroup, defs);
        using var styleInstance = t_styles?.EnterUse(referenced, element);
        try
        {
            // Parse the referenced element
            if (referenced.Name.LocalName == "symbol")
            {
                ParseChildren(referenced, useGroup, defs);
            }
            else
            {
                ParseElement(referenced, useGroup, defs);
            }
        }
        finally
        {
            inheritance.Pop();
            t_useDepth--;
        }

        if (useGroup.Children.Count > 0)
        {
            ApplyClipPath(element, useGroup, defs);
            ApplyOpacity(element, useGroup);
            var bounds = Rect.Empty;
            foreach (var child in useGroup.Children) bounds.Union(SvgObjectBounds(child));
            if (t_markers?.Contexts.TryGetValue(useGroup, out var useContext) == true) useContext.Bounds = bounds;
            useGroup.SvgMask = ResolveLocalMask(element, defs, bounds);
            group.Children.Add(useGroup);
        }
    }

    private static void ParseImage(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var x = ParseDouble(element, "x");
        var y = ParseDouble(element, "y");
        var w = ParseDouble(element, "width");
        var h = ParseDouble(element, "height");

        // SVG requires a positive width/height for an <image> to be rendered.
        if (w <= 0 || h <= 0) return;

        // SVG2 uses "href"; SVG1.1 uses "xlink:href". Accept either form.
        var href = element.Attribute(XlinkNs + "href")?.Value
                   ?? element.Attribute("href")?.Value;
        if (string.IsNullOrWhiteSpace(href)) return;

        if (!TryDecodeImageBytes(href, out var bytes))
            return; // External reference or non-base64 payload — not embedded.

        BitmapImage bitmap;
        try
        {
            bitmap = BitmapImage.FromBytes(bytes);
        }
        catch
        {
            // Decoder unavailable, or the embedded payload is corrupt/unsupported.
            // Skip only this <image> rather than aborting the whole SVG parse,
            // mirroring ParsePath's tolerant handling of bad path data.
            return;
        }

        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return;

        // The decoded BGRA pixels live on this BitmapImage for the lifetime of the
        // SvgImage's Drawing tree and are intentionally not reclaimed: a resize
        // triggers a cache-miss re-rasterization (SoftwareVectorRasterizer) that
        // must re-sample the embedded source. Bounded to one buffer per embedded
        // image and freed when the owning SvgImage is collected.
        var imageDrawing = new ImageDrawing(bitmap, new Rect(x, y, w, h));
        AddDrawing(element, imageDrawing, group, defs);
    }

    /// <summary>
    /// Decodes the bytes referenced by an &lt;image&gt; element's href when it is a
    /// base64 <c>data:</c> URI (e.g. <c>data:image/png;base64,iVBOR…</c>) — the
    /// standard way raster images are embedded inline in SVG. External references
    /// (http/file/relative) and percent-encoded text payloads return false.
    /// </summary>
    private static bool TryDecodeImageBytes(string href, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        href = href.Trim();

        const string scheme = "data:";
        if (!href.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var comma = href.IndexOf(',');
        if (comma < 0) return false;

        // Metadata sits between "data:" and the first comma, e.g. "image/png;base64".
        var meta = href.Substring(scheme.Length, comma - scheme.Length);
        if (meta.IndexOf("base64", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        var payload = href.Substring(comma + 1);
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            // Convert.FromBase64String ignores embedded whitespace, but some
            // authoring tools wrap payloads with characters it still rejects;
            // strip all whitespace and retry once before giving up.
            try
            {
                bytes = Convert.FromBase64String(RemoveWhitespace(payload));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return bytes.Length > 0;
    }

    private static string RemoveWhitespace(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    #endregion

    #region Shape Drawing Helper

    private static void AddShapeDrawing(XElement element, Geometry geometry, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var fill = ResolveFillBrush(element, defs);
        var pen = ResolveStrokePen(element, defs, geometry);

        // SVG's initial value for fill is black, independent of stroke: a shape that
        // declares only a stroke still paints a black interior. The old code skipped the
        // default whenever a pen existed, leaving <rect stroke="red"/> hollow.
        if (fill == null)
        {
            var fillAttr = GetResolvedAttribute(element, "fill");
            if (fillAttr == null) // No fill attribute at all = default black
                fill = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }

        var context = CreateMarkerContext(element, geometry, defs);
        var markers = CreateMarkers(element, geometry, defs, context);
        var drawing = SvgPaintOrder.Create(fill, pen, geometry, GetResolvedAttribute(element, "paint-order"), markers,
            GetResolvedAttribute(element, "vector-effect") == "non-scaling-stroke");
        if (markers is not null)
        {
            t_markers!.Contexts.Add(drawing, context!);
            t_markers.ObjectBounds.Add(drawing, geometry.Bounds);
        }
        AddDrawing(element, drawing, group, defs);
    }

    /// <summary>
    /// Appends <paramref name="drawing"/> to <paramref name="group"/>, wrapping it
    /// in a <see cref="DrawingGroup"/> when the source element carries a transform
    /// or reduced opacity. Shared by shape (<see cref="AddShapeDrawing"/>) and
    /// raster image (<see cref="ParseImage"/>) parsing so both honor element-level
    /// transform / opacity identically.
    /// </summary>
    private static void AddDrawing(XElement element, Drawing drawing, DrawingGroup group,
        Dictionary<string, XElement> defs)
    {
        var transform = ParseTransform(element, SvgObjectBounds(drawing));
        var opacity = ParseDoubleAttribute(element, "opacity", 1.0);
        var clip = ResolveLocalClipPath(element, defs, SvgObjectBounds(drawing));
        var mask = ResolveLocalMask(element, defs, SvgObjectBounds(drawing));
        var blendReader = new CssTokenReader(GetResolvedAttribute(element, "mix-blend-mode") ?? "normal");
        CssCompositingProperties.TryRead(ref blendReader, out var blend);
        var isolated = GetResolvedAttribute(element, "isolation") == "isolate";

        if (transform != null || opacity < 1.0 || clip != null || mask != null || blend != CssBlendMode.Normal || isolated)
        {
            var wrapper = new DrawingGroup();
            if (transform != null)
                wrapper.Transform = transform;
            if (opacity < 1.0)
                wrapper.Opacity = opacity;
            wrapper.ClipGeometry = clip;
            wrapper.SvgMask = mask;
            wrapper.BlendMode = blend;
            wrapper.Isolated = isolated;
            wrapper.Children.Add(drawing);
            group.Children.Add(wrapper);
        }
        else
        {
            group.Children.Add(drawing);
        }
    }

    #endregion

    #region Fill & Stroke Resolution

    /// <summary>
    /// Resolves the CSS <c>currentColor</c> keyword against the inherited SVG
    /// <c>color</c> property (falling back to black per the CSS initial value),
    /// so icon sets authored as <c>stroke="currentColor"</c> pick up the color
    /// set on an ancestor instead of always rendering black.
    /// </summary>
    private static string ResolveCurrentColor(XElement element, string value)
        => value == "currentColor"
            ? GetResolvedAttribute(element, "color") ?? "black"
            : value;

    private static Brush? ResolveFillBrush(XElement element, Dictionary<string, XElement> defs)
    {
        var fillStr = GetResolvedAttribute(element, "fill");
        if (fillStr == null) return null;
        if (fillStr == "none") return null;

        fillStr = ResolveCurrentColor(element, fillStr);
        var brush = ResolveBrush(fillStr, defs);
        if (brush != null)
        {
            var fillOpacity = ParseDoubleAttribute(element, "fill-opacity", 1.0);
            if (fillOpacity < 1.0)
                brush.Opacity = fillOpacity;
        }
        return brush;
    }

    private static Pen? ResolveStrokePen(XElement element, Dictionary<string, XElement> defs, Geometry geometry)
    {
        var strokeStr = GetResolvedAttribute(element, "stroke");
        if (string.IsNullOrEmpty(strokeStr) || strokeStr == "none") return null;

        strokeStr = ResolveCurrentColor(element, strokeStr);
        var strokeBrush = ResolveBrush(strokeStr, defs);
        if (strokeBrush == null) return null;

        var strokeWidth = ResolveStrokeLength(element, "stroke-width", 1.0);
        if (strokeWidth <= 0 || !double.IsFinite(strokeWidth)) return null;
        var strokeOpacity = ParseDoubleAttribute(element, "stroke-opacity", 1.0);
        if (strokeOpacity < 1.0)
            strokeBrush.Opacity = strokeOpacity;

        var pen = new Pen
        {
            Brush = strokeBrush,
            Thickness = strokeWidth,
            DashCap = PenLineCap.Flat,
        };

        // Stroke line cap
        var lineCap = GetResolvedAttribute(element, "stroke-linecap");
        if (lineCap != null)
        {
            pen.StartLineCap = pen.EndLineCap = pen.DashCap = lineCap switch
            {
                "round" => PenLineCap.Round,
                "square" => PenLineCap.Square,
                _ => PenLineCap.Flat
            };
        }

        // Stroke line join
        var lineJoin = GetResolvedAttribute(element, "stroke-linejoin");
        if (lineJoin != null)
        {
            pen.LineJoin = lineJoin switch
            {
                "round" => PenLineJoin.Round,
                "bevel" => PenLineJoin.Bevel,
                _ => PenLineJoin.Miter
            };
        }

        // Miter limit
        var miterLimit = ParseDoubleAttribute(element, "stroke-miterlimit", -1);
        if (miterLimit > 0)
            pen.MiterLimit = miterLimit;

        return ApplyStrokeDashes(element, geometry, pen) ? pen : null;
    }

    private static Brush? ResolveBrush(string value, Dictionary<string, XElement> defs)
    {
        if (value is "context-fill" or "context-stroke") return ResolveMarkerContextBrush(value);
        if (string.IsNullOrEmpty(value) || value == "none")
            return null;

        // Check for url() reference (gradients, patterns)
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractUrlId(value);
            if (id != null && defs.TryGetValue(id, out var defElement))
            {
                return ParseGradient(defElement, defs);
            }
            return null;
        }

        // Try to parse as color
        var color = ParseSvgColor(value);
        if (color != null)
            return new SolidColorBrush(color.Value);

        return null;
    }

    private static Brush? ParseGradient(XElement element, Dictionary<string, XElement> defs)
    {
        // Templates may be indirect. Keep original elements for stop styling;
        // cloning XML here would lose the definition's computed CSS context.
        var templates = new List<XElement>(); var seen = new HashSet<XElement>();
        var current = element;
        while (seen.Add(current))
        {
            if (templates.Count >= 256) return null;
            templates.Add(current);
            var href = current.Attribute("href")?.Value ?? current.Attribute(XlinkNs + "href")?.Value;
            if (href is null || !href.StartsWith('#') || !defs.TryGetValue(href[1..], out var next) ||
                next.Name.LocalName is not ("linearGradient" or "radialGradient")) break;
            current = next;
        }
        return element.Name.LocalName switch
        {
            "linearGradient" => ParseLinearGradient(element, templates),
            "radialGradient" => ParseRadialGradient(element, templates),
            _ => null,
        };
    }

    private static string? GradientAttribute(IReadOnlyList<XElement> templates, string name)
    {
        foreach (var template in templates)
            if (template.Attribute(name)?.Value is { } value) return value;
        return null;
    }

    private static void ApplyGradientTransform(GradientBrush brush, IReadOnlyList<XElement> templates)
    {
        foreach (var template in templates)
        {
            Transform? transform;
            if (t_styles?.IsSpecified(template, "transform") == true)
                transform = ParseTransform(template, brush.MappingMode == BrushMappingMode.Absolute
                    ? new Rect(0, 0, t_viewportWidth, t_viewportHeight) : new Rect(0, 0, 1, 1));
            else if (template.Attribute("gradientTransform")?.Value is { } legacy)
                transform = ParseTransformString(legacy);
            else continue;
            if (brush.MappingMode == BrushMappingMode.Absolute) brush.Transform = transform;
            else brush.RelativeTransform = transform;
            return;
        }
    }

    /// <summary>
    /// Parses one gradient geometry attribute with SVG percentage support and
    /// xlink:href fallback to the parent gradient. Percentages are fractions of the
    /// object bounding box in the default gradientUnits (50% → 0.5); under
    /// userSpaceOnUse they resolve against the viewport axis instead. The previous
    /// plain double parse silently rejected "50%", so every gradient authored with
    /// percentage coordinates fell back to the default direction.
    /// </summary>
    private static double ParseGradientCoordinate(
        IReadOnlyList<XElement> templates, string attribute,
        bool userSpace, double viewportBasis, double defaultValue)
    {
        var str = GradientAttribute(templates, attribute);
        if (str == null) return defaultValue;

        str = str.Trim();
        if (str.EndsWith('%'))
        {
            if (double.TryParse(str.AsSpan(0, str.Length - 1), CultureInfo.InvariantCulture, out var pct) && double.IsFinite(pct))
            {
                var fraction = pct / 100.0;
                return userSpace ? fraction * viewportBasis : fraction;
            }
            return defaultValue;
        }

        if (str.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            str = str.Substring(0, str.Length - 2);
        return double.TryParse(str, CultureInfo.InvariantCulture, out var val) && double.IsFinite(val) ? val : defaultValue;
    }

    /// <summary>Normalized diagonal (SVG spec basis for undirected percentage lengths like r).</summary>
    private static double ViewportDiagonal()
        => Math.Sqrt((t_viewportWidth * t_viewportWidth + t_viewportHeight * t_viewportHeight) / 2.0);

    private static LinearGradientBrush ParseLinearGradient(XElement element, IReadOnlyList<XElement> templates)
    {
        var unitsAttr = GradientAttribute(templates, "gradientUnits");
        var userSpace = unitsAttr == "userSpaceOnUse";

        var x1 = ParseGradientCoordinate(templates, "x1", userSpace, t_viewportWidth, 0);
        var y1 = ParseGradientCoordinate(templates, "y1", userSpace, t_viewportHeight, 0);
        var x2 = ParseGradientCoordinate(templates, "x2", userSpace, t_viewportWidth, userSpace ? t_viewportWidth : 1);
        var y2 = ParseGradientCoordinate(templates, "y2", userSpace, t_viewportHeight, 0);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            UsesNormalizedInterpolation = !userSpace,
            ColorInterpolationMode = GradientInterpolationMode(element)
        };

        // gradientUnits
        if (userSpace)
            brush.MappingMode = BrushMappingMode.Absolute;

        // Spread method
        var spread = GradientAttribute(templates, "spreadMethod");
        if (spread != null)
        {
            brush.SpreadMethod = spread switch
            {
                "reflect" => GradientSpreadMethod.Reflect,
                "repeat" => GradientSpreadMethod.Repeat,
                _ => GradientSpreadMethod.Pad
            };
        }

        // Gradient stops
        var stops = CollectGradientStops(templates);
        foreach (var stop in stops)
            brush.GradientStops.Add(stop);

        // Gradient transform
        ApplyGradientTransform(brush, templates);

        return brush;
    }

    private static RadialGradientBrush? ParseRadialGradient(XElement element, IReadOnlyList<XElement> templates)
    {
        var unitsAttr = GradientAttribute(templates, "gradientUnits");
        var userSpace = unitsAttr == "userSpaceOnUse";

        var cx = ParseGradientCoordinate(templates, "cx", userSpace, t_viewportWidth, userSpace ? t_viewportWidth / 2 : 0.5);
        var cy = ParseGradientCoordinate(templates, "cy", userSpace, t_viewportHeight, userSpace ? t_viewportHeight / 2 : 0.5);
        var r = ParseGradientCoordinate(templates, "r", userSpace, ViewportDiagonal(), userSpace ? ViewportDiagonal() / 2 : 0.5);
        var fx = ParseGradientCoordinate(templates, "fx", userSpace, t_viewportWidth, cx);
        var fy = ParseGradientCoordinate(templates, "fy", userSpace, t_viewportHeight, cy);
        var fr = ParseGradientCoordinate(templates, "fr", userSpace, ViewportDiagonal(), 0);
        if (r < 0 || fr < 0) return null;

        var brush = new RadialGradientBrush
        {
            Center = new Point(cx, cy),
            GradientOrigin = new Point(fx, fy),
            RadiusX = r,
            RadiusY = r,
            SvgFocalRadius = fr,
            UsesSvgRadialInterpolation = true,
            UsesNormalizedInterpolation = !userSpace,
            ColorInterpolationMode = GradientInterpolationMode(element)
        };

        // gradientUnits
        if (userSpace)
            brush.MappingMode = BrushMappingMode.Absolute;

        // Spread method
        var spread = GradientAttribute(templates, "spreadMethod");
        if (spread != null)
        {
            brush.SpreadMethod = spread switch
            {
                "reflect" => GradientSpreadMethod.Reflect,
                "repeat" => GradientSpreadMethod.Repeat,
                _ => GradientSpreadMethod.Pad
            };
        }

        // Gradient stops
        var stops = CollectGradientStops(templates);
        foreach (var stop in stops)
            brush.GradientStops.Add(stop);

        // Gradient transform
        ApplyGradientTransform(brush, templates);

        return brush;
    }

    // This inherited CSS property comes from the gradient actually referenced,
    // even when its stops and geometry are obtained through href templates.
    private static ColorInterpolationMode GradientInterpolationMode(XElement element) =>
        GetResolvedAttribute(element, "color-interpolation")?.Equals("linearRGB", StringComparison.OrdinalIgnoreCase) == true
            ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation;

    private static List<GradientStop> CollectGradientStops(IReadOnlyList<XElement> templates)
    {
        var stops = new List<GradientStop>();

        var stopElements = templates.Select(template => template.Elements().Where(e => e.Name.LocalName == "stop").ToList())
            .FirstOrDefault(stops => stops.Count > 0) ?? [];

        // SVG stop-offset rules: clamp into [0,1], and force the sequence
        // non-decreasing — an out-of-order stop is promoted to the largest
        // offset seen so far, matching browser behaviour. Passing raw values
        // through produced reversed/garbled ramps on sloppy exports.
        double runningMax = 0;
        foreach (var stopEl in stopElements)
        {
            var offset = Math.Clamp(ParseStopOffset(stopEl), 0.0, 1.0);
            if (offset < runningMax) offset = runningMax;
            runningMax = offset;

            var color = ParseStopColor(stopEl);
            stops.Add(new GradientStop { Offset = offset, Color = color });
        }

        return stops;
    }

    private static double ParseStopOffset(XElement stopElement)
    {
        var offsetStr = GetAttribute(stopElement, "offset") ?? "0";
        offsetStr = offsetStr.Trim();

        if (offsetStr.EndsWith('%'))
        {
            if (double.TryParse(offsetStr.AsSpan(0, offsetStr.Length - 1), CultureInfo.InvariantCulture, out var pct) && double.IsFinite(pct))
                return pct / 100.0;
        }
        else
        {
            if (double.TryParse(offsetStr, CultureInfo.InvariantCulture, out var val))
                return val;
        }
        return 0;
    }

    private static Color ParseStopColor(XElement stopElement)
    {
        var color = Color.FromRgb(0, 0, 0);

        var stopColorStr = GetResolvedAttribute(stopElement, "stop-color");
        var stopOpacityStr = GetResolvedAttribute(stopElement, "stop-opacity");

        if (stopColorStr != null)
        {
            var parsed = ParseSvgColor(ResolveCurrentColor(stopElement, stopColorStr));
            if (parsed != null) color = parsed.Value;
        }

        if (stopOpacityStr != null && double.TryParse(stopOpacityStr, CultureInfo.InvariantCulture, out var opacity))
        {
            color = Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * color.A), color.R, color.G, color.B);
        }

        return color;
    }

    #endregion

    #region Transform Parsing

    private static void ApplyTransform(XElement element, DrawingGroup group)
    {
        var transform = ParseTransform(element, group.LocalBounds);
        if (transform != null)
        {
            if (group.Transform != null)
            {
                var tg = new TransformGroup();
                tg.Add(group.Transform);
                tg.Add(transform);
                group.Transform = tg;
            }
            else
            {
                group.Transform = transform;
            }
        }
    }

    private static void ApplyOpacity(XElement element, DrawingGroup group)
    {
        var reader = new CssTokenReader(GetResolvedAttribute(element, "mix-blend-mode") ?? "normal");
        if (CssCompositingProperties.TryRead(ref reader, out var blend)) group.BlendMode = blend;
        group.Isolated = GetResolvedAttribute(element, "isolation") == "isolate";
        var opacity = ParseDoubleAttribute(element, "opacity", 1.0);
        if (opacity < 1.0)
            group.Opacity = opacity;
    }

    private static void ApplyClipPath(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var bounds = Rect.Empty;
        foreach (var child in group.Children)
            bounds = Rect.Union(bounds, SvgObjectBounds(child));
        group.ClipGeometry = ResolveLocalClipPath(element, defs, bounds);
    }

    private static Geometry? ParseClipGeometry(XElement element)
    {
        return element.Name.LocalName switch
        {
            "rect" => ParseClipRect(element),
            "circle" => ParseClipCircle(element),
            "ellipse" => ParseClipEllipse(element),
            "path" => ParseClipPath(element),
            "polygon" => ParseClipPolygon(element),
            _ => null
        };
    }

    private static Geometry ParseClipRect(XElement e)
    {
        var x = ParseDouble(e, "x"); var y = ParseDouble(e, "y");
        var w = ParseDouble(e, "width"); var h = ParseDouble(e, "height");
        var rx = ParseDouble(e, "rx"); var ry = ParseDouble(e, "ry");
        if (rx > 0 && ry <= 0) ry = rx;
        if (ry > 0 && rx <= 0) rx = ry;
        return (rx > 0 || ry > 0)
            ? new RectangleGeometry(new Rect(x, y, w, h), rx, ry)
            : new RectangleGeometry(new Rect(x, y, w, h));
    }

    private static Geometry ParseClipCircle(XElement e)
    {
        var cx = ParseDouble(e, "cx"); var cy = ParseDouble(e, "cy"); var r = ParseDouble(e, "r");
        return new EllipseGeometry { Center = new Point(cx, cy), RadiusX = r, RadiusY = r };
    }

    private static Geometry ParseClipEllipse(XElement e)
    {
        var cx = ParseDouble(e, "cx"); var cy = ParseDouble(e, "cy");
        var rx = ParseDouble(e, "rx"); var ry = ParseDouble(e, "ry");
        return new EllipseGeometry { Center = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
    }

    private static Geometry? ParseClipPath(XElement e)
    {
        var d = e.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(d)) return null;
        try
        {
            var geo = PathMarkupParser.Parse(d);
            geo.FillRule = FillRule.Nonzero; // SVG clip-rule defaults to nonzero
            return geo;
        }
        catch { return null; }
    }

    private static Geometry? ParseClipPolygon(XElement e)
    {
        var pointsStr = e.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(pointsStr)) return null;
        var numbers = ParseNumberList(pointsStr);
        if (numbers.Count < 4) return null;
        var figure = new PathFigure { StartPoint = new Point(numbers[0], numbers[1]), IsClosed = true };
        for (int i = 2; i + 1 < numbers.Count; i += 2)
            figure.Segments.Add(new LineSegment { Point = new Point(numbers[i], numbers[i + 1]) });
        var geo = new PathGeometry { FillRule = FillRule.Nonzero }; // SVG clip-rule default
        geo.Figures.Add(figure);
        return geo;
    }

    private static Transform? ParseTransform(XElement element, Rect referenceBounds = default)
    {
        var transformStr = GetResolvedAttribute(element, "transform");
        if (string.IsNullOrWhiteSpace(transformStr)) return null;
        var reader = new CssTokenReader(transformStr);
        if (CssTransformParser.TryParseTransformList(ref reader, out var parsed, t_styles?.Lengths(element), referenceBounds.Size) && reader.AtEnd) return parsed;
        return ParseTransformString(transformStr);
    }

    internal static bool TryParseLegacyTransform(string value) => ParseTransformString(value) is not null;

    private static Transform? ParseTransformString(string transformStr)
    {
        if (string.IsNullOrWhiteSpace(transformStr)) return null;

        var transforms = new List<Transform>();
        var pos = 0;

        while (pos < transformStr.Length)
        {
            // Skip whitespace
            while (pos < transformStr.Length && char.IsWhiteSpace(transformStr[pos]))
                pos++;
            if (pos >= transformStr.Length) break;

            // Read function name
            var nameStart = pos;
            while (pos < transformStr.Length && transformStr[pos] != '(')
                pos++;
            if (pos >= transformStr.Length) break;

            var name = transformStr.Substring(nameStart, pos - nameStart).Trim();
            pos++; // skip '('

            // Read arguments
            var argsStart = pos;
            while (pos < transformStr.Length && transformStr[pos] != ')')
                pos++;
            if (pos >= transformStr.Length) break;

            var argsStr = transformStr.Substring(argsStart, pos - argsStart);
            pos++; // skip ')'

            var args = ParseNumberList(argsStr);

            Transform? t = name switch
            {
                "translate" when args.Count >= 2 => new TranslateTransform { X = args[0], Y = args[1] },
                "translate" when args.Count == 1 => new TranslateTransform { X = args[0], Y = 0 },
                "scale" when args.Count >= 2 => new ScaleTransform { ScaleX = args[0], ScaleY = args[1] },
                "scale" when args.Count == 1 => new ScaleTransform { ScaleX = args[0], ScaleY = args[0] },
                "rotate" when args.Count >= 3 => new RotateTransform { Angle = args[0], CenterX = args[1], CenterY = args[2] },
                "rotate" when args.Count >= 1 => new RotateTransform { Angle = args[0] },
                "skewX" when args.Count >= 1 => new SkewTransform { AngleX = args[0] },
                "skewY" when args.Count >= 1 => new SkewTransform { AngleY = args[0] },
                "matrix" when args.Count >= 6 => new MatrixTransform(new Matrix(args[0], args[1], args[2], args[3], args[4], args[5])),
                _ => null
            };

            if (t != null) transforms.Add(t);

            // Skip optional comma/whitespace between transforms
            while (pos < transformStr.Length && (char.IsWhiteSpace(transformStr[pos]) || transformStr[pos] == ','))
                pos++;
        }

        if (transforms.Count == 0) return null;
        if (transforms.Count == 1) return transforms[0];

        // SVG composes a transform list right-to-left: in "translate(10) scale(2)" the
        // scale applies to the content first, then the translation. TransformGroup is
        // row-vector — Children[0] acts on the point first — so the list must be added
        // REVERSED. Appending in source order scaled the translation ("translate(10)
        // scale(2)" moved content by 20 instead of 10), skewing every multi-function
        // transform attribute exported by Figma / Illustrator.
        var group = new TransformGroup();
        for (int i = transforms.Count - 1; i >= 0; i--)
            group.Add(transforms[i]);
        return group;
    }

    #endregion

    #region Color Parsing

    private static Color? ParseSvgColor(string value)
    {
        var reader = new CssTokenReader(value);
        return CssColorParser.TryParse(ref reader, out var color, out _) && reader.AtEnd ? color : null;
    }

    #endregion

    #region Attribute Helpers

    private static string? GetAttribute(XElement element, string name)
    {
        if (t_styles?.Get(element, name) is { } computed) return computed;
        // Direct attribute
        var attr = element.Attribute(name)?.Value;
        if (attr != null) return attr;

        // Check style attribute
        var style = element.Attribute("style")?.Value;
        if (style != null)
        {
            var properties = ParseStyleProperties(style);
            if (properties.TryGetValue(name, out var styleVal))
                return styleVal;
        }

        return null;
    }

    /// <summary>
    /// Gets an attribute value, checking inline style, direct attribute, and inheriting from parent.
    /// </summary>
    private static string? GetResolvedAttribute(XElement element, string name)
    {
        if (t_styles?.Get(element, name) is { } computed) return computed;
        // A use instance inherits from the use element, not the original defs
        // container. Keep XML identities for local URL references and substitute
        // only the inheritance edge while parsing that instance.
        XElement? current = element;
        for (var depth = 0; current is not null && depth < 512; depth++)
        {
            var style = current.Attribute("style")?.Value;
            if (style is not null && ParseStyleProperties(style).TryGetValue(name, out var styled)) return styled;
            if (current.Attribute(name)?.Value is { } value) return value;
            if (!IsInheritableProperty(name)) return null;
            var parent = current.Parent;
            if (t_useInheritance is { Count: > 0 } instances)
                foreach (var instance in instances)
                    if (ReferenceEquals(instance.Target, current)) { parent = instance.Use; break; }
            current = parent;
        }
        return null;
    }

    // Which presentation attributes resolve through the ancestor chain. Mirrors the CSS
    // "Inherited: yes" table — notably `opacity` is NOT inherited: it is a group effect
    // applied once per element. Treating it as inheritable made a child inside
    // <g opacity="0.5"> pick up 0.5 again and render at 0.25.
    private static bool IsInheritableProperty(string name) => name switch
    {
        "fill" or "stroke" or "stroke-width" or "stroke-linecap" or "stroke-linejoin" or
        "stroke-miterlimit" or "stroke-dasharray" or "stroke-dashoffset" or
        "fill-rule" or "clip-rule" or "fill-opacity" or "stroke-opacity" or
        "font-size" or "font-family" or "font-weight" or "font-style" or
        "text-anchor" or "color" or "visibility" => true,
        _ => false
    };

    private static Dictionary<string, string> ParseStyleProperties(string style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = style.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = part.Substring(0, colonIndex).Trim();
                var val = part.Substring(colonIndex + 1).Trim();
                result[key] = val;
            }
        }
        return result;
    }

    private static bool TryResolveSvgLength(XElement element, string attribute, string value, out double result)
    {
        result = 0; var lengths = t_styles!.Lengths(element);
        var reader = new CssTokenReader(value, new CssNumericReadContext(lengths));
        if (!reader.TryReadLength(out var length) || !reader.AtEnd) return false;
        var basis = attribute switch
        {
            "x" or "cx" or "x1" or "x2" or "width" or "rx" or "dx" => t_viewportWidth,
            "y" or "cy" or "y1" or "y2" or "height" or "ry" or "dy" => t_viewportHeight,
            "opacity" or "fill-opacity" or "stroke-opacity" or "stop-opacity" => 1,
            _ => ViewportDiagonal(),
        };
        return length.Expression is { } expression ? expression.TryEvaluate(lengths, basis, out result)
            : length.Unit == CssUnit.Percent ? double.IsFinite(result = length.Value * basis / 100)
            : length.TryResolve(lengths, CssPercentBasis.NotSupported, out result);
    }

    private static double ParseDouble(XElement element, string attribute)
    {
        var str = SvgCssStyles.ApplicableGeometry(element, attribute) ? t_styles?.Get(element, attribute) ?? element.Attribute(attribute)?.Value : element.Attribute(attribute)?.Value;
        if (str == null || str == "auto") return 0;
        if (t_styles is not null && TryResolveSvgLength(element, attribute, str, out var resolved)) return resolved;

        str = str.Trim();

        // Percentage lengths resolve against the viewport axis the attribute measures
        // along (SVG 1.1 §7.10): horizontal ones against its width, vertical against
        // its height, undirected (r) against the normalized diagonal. Rejecting the
        // percent form made <rect width="100%" height="100%"> — the standard SVG
        // background idiom — parse as 0×0 and vanish.
        if (str.EndsWith('%'))
        {
            if (!double.TryParse(str.AsSpan(0, str.Length - 1), CultureInfo.InvariantCulture, out var pct) && double.IsFinite(pct))
                return 0;
            var basis = attribute switch
            {
                "x" or "cx" or "x1" or "x2" or "width" or "rx" or "dx" => t_viewportWidth,
                "y" or "cy" or "y1" or "y2" or "height" or "ry" or "dy" => t_viewportHeight,
                _ => ViewportDiagonal(),
            };
            return pct / 100.0 * basis;
        }

        if (str.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            str = str.Substring(0, str.Length - 2);

        if (double.TryParse(str, CultureInfo.InvariantCulture, out var val))
            return val;
        return 0;
    }

    private static double ParseDoubleAttribute(XElement element, string attribute, double defaultValue)
    {
        var str = GetResolvedAttribute(element, attribute);
        if (str is not null && t_styles is not null && TryResolveSvgLength(element, attribute, str, out var resolved)) return resolved;
        if (str != null)
        {
            str = str.Trim();
            // Remove px suffix
            if (str.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                str = str.Substring(0, str.Length - 2);
            if (double.TryParse(str, CultureInfo.InvariantCulture, out var val))
                return val;
        }
        return defaultValue;
    }

    private static double ParseLengthAttribute(XElement element, string attribute, double defaultValue)
    {
        var str = element.Attribute(attribute)?.Value;
        if (string.IsNullOrWhiteSpace(str)) return defaultValue;

        str = str.Trim();

        // Handle percentage (treat as the value itself for now)
        if (str.EndsWith('%'))
        {
            if (double.TryParse(str.AsSpan(0, str.Length - 1), CultureInfo.InvariantCulture, out var pct) && double.IsFinite(pct))
                return pct; // Percentage needs context; return raw value
            return defaultValue;
        }

        // Remove unit suffixes
        string[] units = ["px", "pt", "em", "ex", "cm", "mm", "in"];
        foreach (var unit in units)
        {
            if (str.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                str = str.Substring(0, str.Length - unit.Length);
                break;
            }
        }

        if (double.TryParse(str, CultureInfo.InvariantCulture, out var val))
            return val;

        return defaultValue;
    }

    private static List<double> ParseNumberList(string str)
    {
        var result = new List<double>();
        var span = str.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            // Skip separators
            while (pos < span.Length && (span[pos] == ' ' || span[pos] == ',' || span[pos] == '\t' || span[pos] == '\r' || span[pos] == '\n'))
                pos++;
            if (pos >= span.Length) break;

            var start = pos;

            // Handle sign
            if (pos < span.Length && (span[pos] == '-' || span[pos] == '+'))
                pos++;

            // Integer part
            while (pos < span.Length && char.IsDigit(span[pos]))
                pos++;

            // Decimal part
            if (pos < span.Length && span[pos] == '.')
            {
                pos++;
                while (pos < span.Length && char.IsDigit(span[pos]))
                    pos++;
            }

            // Exponent
            if (pos < span.Length && (span[pos] == 'e' || span[pos] == 'E'))
            {
                pos++;
                if (pos < span.Length && (span[pos] == '-' || span[pos] == '+'))
                    pos++;
                while (pos < span.Length && char.IsDigit(span[pos]))
                    pos++;
            }

            if (pos > start)
            {
                if (double.TryParse(span[start..pos], CultureInfo.InvariantCulture, out var val))
                    result.Add(val);
            }
            else
            {
                pos++; // Skip unrecognized character
            }
        }

        return result;
    }

    private static string? ExtractUrlId(string urlStr)
    {
        // url(#id) or url('#id') or url("#id")
        var start = urlStr.IndexOf('#');
        if (start < 0) return null;
        start++;

        var end = urlStr.IndexOf(')', start);
        if (end < 0) end = urlStr.Length;

        var id = urlStr.Substring(start, end - start).Trim().Trim('\'', '"');
        return string.IsNullOrEmpty(id) ? null : id;
    }

    #endregion

    #region Named SVG Colors

    private static readonly Dictionary<string, Color> s_svgColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = Color.FromRgb(240, 248, 255),
        ["antiquewhite"] = Color.FromRgb(250, 235, 215),
        ["aqua"] = Color.FromRgb(0, 255, 255),
        ["aquamarine"] = Color.FromRgb(127, 255, 212),
        ["azure"] = Color.FromRgb(240, 255, 255),
        ["beige"] = Color.FromRgb(245, 245, 220),
        ["bisque"] = Color.FromRgb(255, 228, 196),
        ["black"] = Color.FromRgb(0, 0, 0),
        ["blanchedalmond"] = Color.FromRgb(255, 235, 205),
        ["blue"] = Color.FromRgb(0, 0, 255),
        ["blueviolet"] = Color.FromRgb(138, 43, 226),
        ["brown"] = Color.FromRgb(165, 42, 42),
        ["burlywood"] = Color.FromRgb(222, 184, 135),
        ["cadetblue"] = Color.FromRgb(95, 158, 160),
        ["chartreuse"] = Color.FromRgb(127, 255, 0),
        ["chocolate"] = Color.FromRgb(210, 105, 30),
        ["coral"] = Color.FromRgb(255, 127, 80),
        ["cornflowerblue"] = Color.FromRgb(100, 149, 237),
        ["cornsilk"] = Color.FromRgb(255, 248, 220),
        ["crimson"] = Color.FromRgb(220, 20, 60),
        ["cyan"] = Color.FromRgb(0, 255, 255),
        ["darkblue"] = Color.FromRgb(0, 0, 139),
        ["darkcyan"] = Color.FromRgb(0, 139, 139),
        ["darkgoldenrod"] = Color.FromRgb(184, 134, 11),
        ["darkgray"] = Color.FromRgb(169, 169, 169),
        ["darkgreen"] = Color.FromRgb(0, 100, 0),
        ["darkgrey"] = Color.FromRgb(169, 169, 169),
        ["darkkhaki"] = Color.FromRgb(189, 183, 107),
        ["darkmagenta"] = Color.FromRgb(139, 0, 139),
        ["darkolivegreen"] = Color.FromRgb(85, 107, 47),
        ["darkorange"] = Color.FromRgb(255, 140, 0),
        ["darkorchid"] = Color.FromRgb(153, 50, 204),
        ["darkred"] = Color.FromRgb(139, 0, 0),
        ["darksalmon"] = Color.FromRgb(233, 150, 122),
        ["darkseagreen"] = Color.FromRgb(143, 188, 143),
        ["darkslateblue"] = Color.FromRgb(72, 61, 139),
        ["darkslategray"] = Color.FromRgb(47, 79, 79),
        ["darkslategrey"] = Color.FromRgb(47, 79, 79),
        ["darkturquoise"] = Color.FromRgb(0, 206, 209),
        ["darkviolet"] = Color.FromRgb(148, 0, 211),
        ["deeppink"] = Color.FromRgb(255, 20, 147),
        ["deepskyblue"] = Color.FromRgb(0, 191, 255),
        ["dimgray"] = Color.FromRgb(105, 105, 105),
        ["dimgrey"] = Color.FromRgb(105, 105, 105),
        ["dodgerblue"] = Color.FromRgb(30, 144, 255),
        ["firebrick"] = Color.FromRgb(178, 34, 34),
        ["floralwhite"] = Color.FromRgb(255, 250, 240),
        ["forestgreen"] = Color.FromRgb(34, 139, 34),
        ["fuchsia"] = Color.FromRgb(255, 0, 255),
        ["gainsboro"] = Color.FromRgb(220, 220, 220),
        ["ghostwhite"] = Color.FromRgb(248, 248, 255),
        ["gold"] = Color.FromRgb(255, 215, 0),
        ["goldenrod"] = Color.FromRgb(218, 165, 32),
        ["gray"] = Color.FromRgb(128, 128, 128),
        ["green"] = Color.FromRgb(0, 128, 0),
        ["greenyellow"] = Color.FromRgb(173, 255, 47),
        ["grey"] = Color.FromRgb(128, 128, 128),
        ["honeydew"] = Color.FromRgb(240, 255, 240),
        ["hotpink"] = Color.FromRgb(255, 105, 180),
        ["indianred"] = Color.FromRgb(205, 92, 92),
        ["indigo"] = Color.FromRgb(75, 0, 130),
        ["ivory"] = Color.FromRgb(255, 255, 240),
        ["khaki"] = Color.FromRgb(240, 230, 140),
        ["lavender"] = Color.FromRgb(230, 230, 250),
        ["lavenderblush"] = Color.FromRgb(255, 240, 245),
        ["lawngreen"] = Color.FromRgb(124, 252, 0),
        ["lemonchiffon"] = Color.FromRgb(255, 250, 205),
        ["lightblue"] = Color.FromRgb(173, 216, 230),
        ["lightcoral"] = Color.FromRgb(240, 128, 128),
        ["lightcyan"] = Color.FromRgb(224, 255, 255),
        ["lightgoldenrodyellow"] = Color.FromRgb(250, 250, 210),
        ["lightgray"] = Color.FromRgb(211, 211, 211),
        ["lightgreen"] = Color.FromRgb(144, 238, 144),
        ["lightgrey"] = Color.FromRgb(211, 211, 211),
        ["lightpink"] = Color.FromRgb(255, 182, 193),
        ["lightsalmon"] = Color.FromRgb(255, 160, 122),
        ["lightseagreen"] = Color.FromRgb(32, 178, 170),
        ["lightskyblue"] = Color.FromRgb(135, 206, 250),
        ["lightslategray"] = Color.FromRgb(119, 136, 153),
        ["lightslategrey"] = Color.FromRgb(119, 136, 153),
        ["lightsteelblue"] = Color.FromRgb(176, 196, 222),
        ["lightyellow"] = Color.FromRgb(255, 255, 224),
        ["lime"] = Color.FromRgb(0, 255, 0),
        ["limegreen"] = Color.FromRgb(50, 205, 50),
        ["linen"] = Color.FromRgb(250, 240, 230),
        ["magenta"] = Color.FromRgb(255, 0, 255),
        ["maroon"] = Color.FromRgb(128, 0, 0),
        ["mediumaquamarine"] = Color.FromRgb(102, 205, 170),
        ["mediumblue"] = Color.FromRgb(0, 0, 205),
        ["mediumorchid"] = Color.FromRgb(186, 85, 211),
        ["mediumpurple"] = Color.FromRgb(147, 111, 219),
        ["mediumseagreen"] = Color.FromRgb(60, 179, 113),
        ["mediumslateblue"] = Color.FromRgb(123, 104, 238),
        ["mediumspringgreen"] = Color.FromRgb(0, 250, 154),
        ["mediumturquoise"] = Color.FromRgb(72, 209, 204),
        ["mediumvioletred"] = Color.FromRgb(199, 21, 133),
        ["midnightblue"] = Color.FromRgb(25, 25, 112),
        ["mintcream"] = Color.FromRgb(245, 255, 250),
        ["mistyrose"] = Color.FromRgb(255, 228, 225),
        ["moccasin"] = Color.FromRgb(255, 228, 181),
        ["navajowhite"] = Color.FromRgb(255, 222, 173),
        ["navy"] = Color.FromRgb(0, 0, 128),
        ["oldlace"] = Color.FromRgb(253, 245, 230),
        ["olive"] = Color.FromRgb(128, 128, 0),
        ["olivedrab"] = Color.FromRgb(107, 142, 35),
        ["orange"] = Color.FromRgb(255, 165, 0),
        ["orangered"] = Color.FromRgb(255, 69, 0),
        ["orchid"] = Color.FromRgb(218, 112, 214),
        ["palegoldenrod"] = Color.FromRgb(238, 232, 170),
        ["palegreen"] = Color.FromRgb(152, 251, 152),
        ["paleturquoise"] = Color.FromRgb(175, 238, 238),
        ["palevioletred"] = Color.FromRgb(219, 112, 147),
        ["papayawhip"] = Color.FromRgb(255, 239, 213),
        ["peachpuff"] = Color.FromRgb(255, 218, 185),
        ["peru"] = Color.FromRgb(205, 133, 63),
        ["pink"] = Color.FromRgb(255, 192, 203),
        ["plum"] = Color.FromRgb(221, 160, 221),
        ["powderblue"] = Color.FromRgb(176, 224, 230),
        ["purple"] = Color.FromRgb(128, 0, 128),
        ["rebeccapurple"] = Color.FromRgb(102, 51, 153),
        ["red"] = Color.FromRgb(255, 0, 0),
        ["rosybrown"] = Color.FromRgb(188, 143, 143),
        ["royalblue"] = Color.FromRgb(65, 105, 225),
        ["saddlebrown"] = Color.FromRgb(139, 69, 19),
        ["salmon"] = Color.FromRgb(250, 128, 114),
        ["sandybrown"] = Color.FromRgb(244, 164, 96),
        ["seagreen"] = Color.FromRgb(46, 139, 87),
        ["seashell"] = Color.FromRgb(255, 245, 238),
        ["sienna"] = Color.FromRgb(160, 82, 45),
        ["silver"] = Color.FromRgb(192, 192, 192),
        ["skyblue"] = Color.FromRgb(135, 206, 235),
        ["slateblue"] = Color.FromRgb(106, 90, 205),
        ["slategray"] = Color.FromRgb(112, 128, 144),
        ["slategrey"] = Color.FromRgb(112, 128, 144),
        ["snow"] = Color.FromRgb(255, 250, 250),
        ["springgreen"] = Color.FromRgb(0, 255, 127),
        ["steelblue"] = Color.FromRgb(70, 130, 180),
        ["tan"] = Color.FromRgb(210, 180, 140),
        ["teal"] = Color.FromRgb(0, 128, 128),
        ["thistle"] = Color.FromRgb(216, 191, 216),
        ["tomato"] = Color.FromRgb(255, 99, 71),
        ["turquoise"] = Color.FromRgb(64, 224, 208),
        ["violet"] = Color.FromRgb(238, 130, 238),
        ["wheat"] = Color.FromRgb(245, 222, 179),
        ["white"] = Color.FromRgb(255, 255, 255),
        ["whitesmoke"] = Color.FromRgb(245, 245, 245),
        ["yellow"] = Color.FromRgb(255, 255, 0),
        ["yellowgreen"] = Color.FromRgb(154, 205, 50),
    };

    #endregion
}
