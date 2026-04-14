using System.Globalization;
using System.Xml.Linq;

namespace Jalium.UI.Media;

/// <summary>
/// Parses SVG (Scalable Vector Graphics) documents into Jalium.UI Drawing objects.
/// Supports core SVG shape elements, gradients, transforms, clip paths, and styling.
/// </summary>
internal static class SvgParser
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// Parses an SVG document string into a DrawingGroup with optional viewport dimensions.
    /// </summary>
    public static (DrawingGroup Drawing, double Width, double Height) Parse(string svgContent)
    {
        var doc = XDocument.Parse(svgContent);
        var svgElement = doc.Root;
        if (svgElement == null)
            return (new DrawingGroup(), 0, 0);

        var defs = new Dictionary<string, XElement>(StringComparer.Ordinal);
        CollectDefs(svgElement, defs);

        // Parse viewBox and dimensions
        ParseViewBoxAndDimensions(svgElement, out var width, out var height, out var viewBox);

        var group = new DrawingGroup();
        ParseChildren(svgElement, group, defs);

        // Apply viewBox transform if needed
        if (viewBox != null && width > 0 && height > 0)
        {
            var vb = viewBox.Value;
            if (vb.Width > 0 && vb.Height > 0 &&
                (vb.X != 0 || vb.Y != 0 ||
                 Math.Abs(vb.Width - width) > 0.001 || Math.Abs(vb.Height - height) > 0.001))
            {
                var scaleX = width / vb.Width;
                var scaleY = height / vb.Height;
                var scale = Math.Min(scaleX, scaleY);

                var translateX = (width - vb.Width * scale) / 2 - vb.X * scale;
                var translateY = (height - vb.Height * scale) / 2 - vb.Y * scale;

                var transforms = new TransformGroup();
                transforms.Add(new ScaleTransform { ScaleX = scale, ScaleY = scale });
                transforms.Add(new TranslateTransform { X = translateX, Y = translateY });
                group.Transform = transforms;
            }
        }
        else if (viewBox != null && (width <= 0 || height <= 0))
        {
            // No explicit width/height; use viewBox dimensions
            width = viewBox.Value.Width;
            height = viewBox.Value.Height;
        }

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
            if (localName == "defs" || localName == "metadata" || localName == "title" || localName == "desc")
                continue;

            ParseElement(child, group, defs);
        }
    }

    private static void ParseElement(XElement element, DrawingGroup parentGroup, Dictionary<string, XElement> defs)
    {
        var localName = element.Name.LocalName;

        // Check display:none or visibility:hidden
        var style = GetAttribute(element, "style");
        if (style != null && (style.Contains("display:none") || style.Contains("display: none")))
            return;
        var display = GetAttribute(element, "display");
        if (display == "none")
            return;

        switch (localName)
        {
            case "g":
            case "svg":
                ParseGroup(element, parentGroup, defs);
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
                // Basic text support - render as path if possible
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

    private static void ParseGroup(XElement element, DrawingGroup parentGroup, Dictionary<string, XElement> defs)
    {
        var childGroup = new DrawingGroup();

        // Apply group-level attributes
        ApplyTransform(element, childGroup);
        ApplyOpacity(element, childGroup);
        ApplyClipPath(element, childGroup, defs);

        ParseChildren(element, childGroup, defs);

        if (childGroup.Children.Count > 0)
            parentGroup.Children.Add(childGroup);
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
        if (numbers.Count < 4) return;

        var figure = new PathFigure
        {
            StartPoint = new Point(numbers[0], numbers[1]),
            IsClosed = closed,
            IsFilled = closed
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

    private static void ParseUse(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var href = element.Attribute(XlinkNs + "href")?.Value
                   ?? element.Attribute("href")?.Value;
        if (string.IsNullOrEmpty(href)) return;

        // Remove leading '#'
        if (href.StartsWith('#'))
            href = href.Substring(1);

        if (!defs.TryGetValue(href, out var referenced)) return;

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

        // Parse the referenced element
        if (referenced.Name.LocalName == "symbol")
        {
            ParseChildren(referenced, useGroup, defs);
        }
        else
        {
            ParseElement(referenced, useGroup, defs);
        }

        if (useGroup.Children.Count > 0)
            group.Children.Add(useGroup);
    }

    #endregion

    #region Shape Drawing Helper

    private static void AddShapeDrawing(XElement element, Geometry geometry, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var fill = ResolveFillBrush(element, defs);
        var pen = ResolveStrokePen(element, defs);

        // Default SVG behavior: if no fill specified and no fill="none", default is black
        if (fill == null && pen == null)
        {
            var fillAttr = GetResolvedAttribute(element, "fill");
            if (fillAttr == null) // No fill attribute at all = default black
                fill = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }

        var drawing = new GeometryDrawing(fill, pen, geometry);

        // Wrap in group if element has transform or opacity
        var transform = ParseTransform(element);
        var opacity = ParseDoubleAttribute(element, "opacity", 1.0);

        if (transform != null || opacity < 1.0)
        {
            var wrapper = new DrawingGroup();
            if (transform != null)
                wrapper.Transform = transform;
            if (opacity < 1.0)
                wrapper.Opacity = opacity;
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

    private static Brush? ResolveFillBrush(XElement element, Dictionary<string, XElement> defs)
    {
        var fillStr = GetResolvedAttribute(element, "fill");
        if (fillStr == null) return null;
        if (fillStr == "none") return null;

        var brush = ResolveBrush(fillStr, defs);
        if (brush != null)
        {
            var fillOpacity = ParseDoubleAttribute(element, "fill-opacity", 1.0);
            if (fillOpacity < 1.0)
                brush.Opacity = fillOpacity;
        }
        return brush;
    }

    private static Pen? ResolveStrokePen(XElement element, Dictionary<string, XElement> defs)
    {
        var strokeStr = GetResolvedAttribute(element, "stroke");
        if (string.IsNullOrEmpty(strokeStr) || strokeStr == "none") return null;

        var strokeBrush = ResolveBrush(strokeStr, defs);
        if (strokeBrush == null) return null;

        var strokeWidth = ParseDoubleAttribute(element, "stroke-width", 1.0);
        var strokeOpacity = ParseDoubleAttribute(element, "stroke-opacity", 1.0);
        if (strokeOpacity < 1.0)
            strokeBrush.Opacity = strokeOpacity;

        var pen = new Pen
        {
            Brush = strokeBrush,
            Thickness = strokeWidth
        };

        // Stroke line cap
        var lineCap = GetResolvedAttribute(element, "stroke-linecap");
        if (lineCap != null)
        {
            pen.StartLineCap = pen.EndLineCap = lineCap switch
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

        // Dash array
        var dashArray = GetResolvedAttribute(element, "stroke-dasharray");
        if (!string.IsNullOrEmpty(dashArray) && dashArray != "none")
        {
            var dashes = ParseNumberList(dashArray);
            if (dashes.Count > 0)
            {
                var dashStyle = new DashStyle();
                // Normalize dashes by stroke width
                foreach (var d in dashes)
                    dashStyle.Dashes.Add(strokeWidth > 0 ? d / strokeWidth : d);

                var dashOffset = ParseDoubleAttribute(element, "stroke-dashoffset", 0);
                if (dashOffset != 0 && strokeWidth > 0)
                    dashStyle.Offset = dashOffset / strokeWidth;

                pen.DashStyle = dashStyle;
            }
        }

        return pen;
    }

    private static Brush? ResolveBrush(string value, Dictionary<string, XElement> defs)
    {
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
        var localName = element.Name.LocalName;

        // Handle xlink:href inheritance
        var href = element.Attribute(XlinkNs + "href")?.Value ?? element.Attribute("href")?.Value;
        XElement? parent = null;
        if (!string.IsNullOrEmpty(href))
        {
            var parentId = href.TrimStart('#');
            defs.TryGetValue(parentId, out parent);
        }

        if (localName == "linearGradient")
            return ParseLinearGradient(element, parent, defs);
        if (localName == "radialGradient")
            return ParseRadialGradient(element, parent, defs);

        return null;
    }

    private static LinearGradientBrush ParseLinearGradient(XElement element, XElement? parent, Dictionary<string, XElement> defs)
    {
        var x1 = ParseDoubleAttribute(element, "x1", parent != null ? ParseDoubleAttribute(parent, "x1", 0) : 0);
        var y1 = ParseDoubleAttribute(element, "y1", parent != null ? ParseDoubleAttribute(parent, "y1", 0) : 0);
        var x2 = ParseDoubleAttribute(element, "x2", parent != null ? ParseDoubleAttribute(parent, "x2", 1) : 1);
        var y2 = ParseDoubleAttribute(element, "y2", parent != null ? ParseDoubleAttribute(parent, "y2", 0) : 0);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2)
        };

        // gradientUnits
        var units = element.Attribute("gradientUnits")?.Value ?? parent?.Attribute("gradientUnits")?.Value;
        if (units == "userSpaceOnUse")
            brush.MappingMode = BrushMappingMode.Absolute;

        // Spread method
        var spread = element.Attribute("spreadMethod")?.Value ?? parent?.Attribute("spreadMethod")?.Value;
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
        var stops = CollectGradientStops(element, parent);
        foreach (var stop in stops)
            brush.GradientStops.Add(stop);

        // Gradient transform
        var transformStr = element.Attribute("gradientTransform")?.Value ?? parent?.Attribute("gradientTransform")?.Value;
        if (!string.IsNullOrEmpty(transformStr))
            brush.Transform = ParseTransformString(transformStr);

        return brush;
    }

    private static RadialGradientBrush ParseRadialGradient(XElement element, XElement? parent, Dictionary<string, XElement> defs)
    {
        var cx = ParseDoubleAttribute(element, "cx", parent != null ? ParseDoubleAttribute(parent, "cx", 0.5) : 0.5);
        var cy = ParseDoubleAttribute(element, "cy", parent != null ? ParseDoubleAttribute(parent, "cy", 0.5) : 0.5);
        var r = ParseDoubleAttribute(element, "r", parent != null ? ParseDoubleAttribute(parent, "r", 0.5) : 0.5);
        var fx = ParseDoubleAttribute(element, "fx", parent != null ? ParseDoubleAttribute(parent, "fx", cx) : cx);
        var fy = ParseDoubleAttribute(element, "fy", parent != null ? ParseDoubleAttribute(parent, "fy", cy) : cy);

        var brush = new RadialGradientBrush
        {
            Center = new Point(cx, cy),
            GradientOrigin = new Point(fx, fy),
            RadiusX = r,
            RadiusY = r
        };

        // gradientUnits
        var units = element.Attribute("gradientUnits")?.Value ?? parent?.Attribute("gradientUnits")?.Value;
        if (units == "userSpaceOnUse")
            brush.MappingMode = BrushMappingMode.Absolute;

        // Spread method
        var spread = element.Attribute("spreadMethod")?.Value ?? parent?.Attribute("spreadMethod")?.Value;
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
        var stops = CollectGradientStops(element, parent);
        foreach (var stop in stops)
            brush.GradientStops.Add(stop);

        // Gradient transform
        var transformStr = element.Attribute("gradientTransform")?.Value ?? parent?.Attribute("gradientTransform")?.Value;
        if (!string.IsNullOrEmpty(transformStr))
            brush.Transform = ParseTransformString(transformStr);

        return brush;
    }

    private static List<GradientStop> CollectGradientStops(XElement element, XElement? parent)
    {
        var stops = new List<GradientStop>();

        // Collect stops from this element first; if none, inherit from parent
        var stopElements = element.Elements().Where(e => e.Name.LocalName == "stop").ToList();
        if (stopElements.Count == 0 && parent != null)
            stopElements = parent.Elements().Where(e => e.Name.LocalName == "stop").ToList();

        foreach (var stopEl in stopElements)
        {
            var offset = ParseStopOffset(stopEl);
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
            if (double.TryParse(offsetStr.AsSpan(0, offsetStr.Length - 1), CultureInfo.InvariantCulture, out var pct))
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

        // Check style attribute first for stop-color and stop-opacity
        var style = stopElement.Attribute("style")?.Value;
        string? stopColorStr = null;
        string? stopOpacityStr = null;

        if (style != null)
        {
            var properties = ParseStyleProperties(style);
            properties.TryGetValue("stop-color", out stopColorStr);
            properties.TryGetValue("stop-opacity", out stopOpacityStr);
        }

        stopColorStr ??= stopElement.Attribute("stop-color")?.Value;
        stopOpacityStr ??= stopElement.Attribute("stop-opacity")?.Value;

        if (stopColorStr != null)
        {
            var parsed = ParseSvgColor(stopColorStr);
            if (parsed != null) color = parsed.Value;
        }

        if (stopOpacityStr != null && double.TryParse(stopOpacityStr, CultureInfo.InvariantCulture, out var opacity))
        {
            color = Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);
        }

        return color;
    }

    #endregion

    #region Transform Parsing

    private static void ApplyTransform(XElement element, DrawingGroup group)
    {
        var transform = ParseTransform(element);
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
        var opacity = ParseDoubleAttribute(element, "opacity", 1.0);
        if (opacity < 1.0)
            group.Opacity = opacity;
    }

    private static void ApplyClipPath(XElement element, DrawingGroup group, Dictionary<string, XElement> defs)
    {
        var clipPathStr = GetResolvedAttribute(element, "clip-path");
        if (string.IsNullOrEmpty(clipPathStr)) return;

        var id = ExtractUrlId(clipPathStr);
        if (id == null || !defs.TryGetValue(id, out var clipPathElement)) return;

        var geometryGroup = new GeometryGroup();
        foreach (var child in clipPathElement.Elements())
        {
            var clipGeometry = ParseClipGeometry(child);
            if (clipGeometry != null)
                geometryGroup.Children.Add(clipGeometry);
        }

        if (geometryGroup.Children.Count > 0)
            group.ClipGeometry = geometryGroup.Children.Count == 1 ? geometryGroup.Children[0] : geometryGroup;
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
        try { return PathMarkupParser.Parse(d); }
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
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    private static Transform? ParseTransform(XElement element)
    {
        var transformStr = element.Attribute("transform")?.Value;
        if (string.IsNullOrWhiteSpace(transformStr)) return null;
        return ParseTransformString(transformStr);
    }

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

        var group = new TransformGroup();
        foreach (var t in transforms)
            group.Add(t);
        return group;
    }

    #endregion

    #region Color Parsing

    private static Color? ParseSvgColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value == "none" || value == "transparent")
            return Color.FromArgb(0, 0, 0, 0);

        if (value == "currentColor")
            return Color.FromRgb(0, 0, 0); // Default to black

        // Hex colors
        if (value.StartsWith('#'))
        {
            var hex = value.AsSpan(1);
            if (hex.Length == 3)
            {
                var r = ParseHexChar(hex[0]);
                var g = ParseHexChar(hex[1]);
                var b = ParseHexChar(hex[2]);
                return Color.FromRgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            }
            if (hex.Length == 4)
            {
                var a = ParseHexChar(hex[0]);
                var r = ParseHexChar(hex[1]);
                var g = ParseHexChar(hex[2]);
                var b = ParseHexChar(hex[3]);
                return Color.FromArgb((byte)(a * 17), (byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            }
            if (hex.Length == 6)
            {
                var r = byte.Parse(hex.Slice(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber);
                return Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                var r = byte.Parse(hex.Slice(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber);
                var a = byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }

        // rgb() / rgba()
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var parenStart = value.IndexOf('(');
            var parenEnd = value.IndexOf(')');
            if (parenStart >= 0 && parenEnd > parenStart)
            {
                var inner = value.Substring(parenStart + 1, parenEnd - parenStart - 1);
                var parts = inner.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3)
                {
                    var r = ParseColorComponent(parts[0]);
                    var g = ParseColorComponent(parts[1]);
                    var b = ParseColorComponent(parts[2]);
                    byte a = 255;
                    if (parts.Length >= 4)
                    {
                        var aStr = parts[3].Trim();
                        if (aStr.EndsWith('%'))
                        {
                            if (double.TryParse(aStr.AsSpan(0, aStr.Length - 1), CultureInfo.InvariantCulture, out var pct))
                                a = (byte)Math.Clamp(pct * 2.55, 0, 255);
                        }
                        else if (double.TryParse(aStr, CultureInfo.InvariantCulture, out var aVal))
                        {
                            a = (byte)Math.Clamp(aVal <= 1.0 ? aVal * 255 : aVal, 0, 255);
                        }
                    }
                    return Color.FromArgb(a, r, g, b);
                }
            }
        }

        // Named SVG colors
        if (s_svgColors.TryGetValue(value, out var named))
            return named;

        // Try the framework's ColorConverter as fallback
        var frameworkColor = ColorConverter.ConvertFromString(value);
        if (frameworkColor is Color c)
            return c;

        return null;
    }

    private static byte ParseColorComponent(string part)
    {
        part = part.Trim();
        if (part.EndsWith('%'))
        {
            if (double.TryParse(part.AsSpan(0, part.Length - 1), CultureInfo.InvariantCulture, out var pct))
                return (byte)Math.Clamp(pct * 2.55, 0, 255);
        }
        else if (int.TryParse(part, CultureInfo.InvariantCulture, out var val))
        {
            return (byte)Math.Clamp(val, 0, 255);
        }
        return 0;
    }

    private static int ParseHexChar(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0
    };

    #endregion

    #region Attribute Helpers

    private static string? GetAttribute(XElement element, string name)
    {
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
        // Check style attribute first (highest priority)
        var style = element.Attribute("style")?.Value;
        if (style != null)
        {
            var properties = ParseStyleProperties(style);
            if (properties.TryGetValue(name, out var styleVal))
                return styleVal;
        }

        // Then direct attribute
        var attr = element.Attribute(name)?.Value;
        if (attr != null) return attr;

        // Inherit from parent (for inheritable properties like fill, stroke, etc.)
        var parent = element.Parent;
        if (parent != null && parent.Name.LocalName != "svg" &&
            IsInheritableProperty(name))
        {
            return GetResolvedAttribute(parent, name);
        }

        return null;
    }

    private static bool IsInheritableProperty(string name) => name switch
    {
        "fill" or "stroke" or "stroke-width" or "stroke-linecap" or "stroke-linejoin" or
        "stroke-miterlimit" or "stroke-dasharray" or "stroke-dashoffset" or
        "fill-rule" or "fill-opacity" or "stroke-opacity" or "opacity" or
        "font-size" or "font-family" or "font-weight" or "font-style" or
        "text-anchor" or "color" => true,
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

    private static double ParseDouble(XElement element, string attribute)
    {
        var str = element.Attribute(attribute)?.Value;
        if (str != null && double.TryParse(str, CultureInfo.InvariantCulture, out var val))
            return val;
        return 0;
    }

    private static double ParseDoubleAttribute(XElement element, string attribute, double defaultValue)
    {
        var str = GetResolvedAttribute(element, attribute);
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
            if (double.TryParse(str.AsSpan(0, str.Length - 1), CultureInfo.InvariantCulture, out var pct))
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
