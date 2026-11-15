using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Repaints selected and highlighted glyphs without changing layout, source formatting or retained lines.</summary>
internal static class CssSelectionPainter
{
    private static readonly SolidColorBrush s_defaultSpellingBrush = CreateFrozenBrush(Color.FromArgb(235, 239, 68, 68));
    private static readonly SolidColorBrush s_defaultGrammarBrush = CreateFrozenBrush(Color.FromArgb(235, 34, 197, 94));
    private static readonly SolidColorBrush s_defaultSearchBackground = CreateFrozenBrush(Color.FromArgb(88, 250, 204, 21));
    private static readonly SolidColorBrush s_defaultSearchCurrentBackground = CreateFrozenBrush(Color.FromArgb(148, 249, 115, 22));
    private static readonly SolidColorBrush s_defaultTargetBackground = CreateFrozenBrush(Color.FromArgb(84, 168, 85, 247));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    internal sealed record Region(
        Rect Bounds,
        CssSelection.Style? Style,
        Brush? Underlying,
        double? Baseline = null,
        Rect? GlyphBounds = null,
        double? TextTop = null,
        CssPseudoElement Kind = CssPseudoElement.Selection,
        string? HighlightName = null,
        int LayerOrder = 0,
        Matrix? WritingTransform = null)
    {
        internal Rect InkBounds => GlyphBounds ?? Bounds;
    }

    internal static DrawingContext Unwrap(DrawingContext context)
        => context is TextContext text ? Unwrap(text.Inner) : context;

    internal static IReadOnlyList<Region> CollectHighlightRegions(
        DependencyObject source,
        int textLength,
        Func<int, int, IEnumerable<Rect>> boundsResolver,
        (int Start, int Length)? selectionRange = null,
        Brush? underlying = null,
        double? baseline = null,
        double? textTop = null)
    {
        var regions = new List<Region>();
        int layerOrder = 0;

        void AppendRange(int rawStart, int rawEnd, CssSelection.Style? style, CssPseudoElement kind, string? name, int order)
        {
            int start = Math.Clamp(rawStart, 0, textLength);
            int end = Math.Clamp(rawEnd, 0, textLength);
            if (end <= start) return;
            foreach (var rect in boundsResolver(start, end - start))
            {
                if (rect.Width > 0 && rect.Height > 0)
                    regions.Add(new Region(rect, style, underlying, baseline, null, textTop, kind, name, order));
            }
        }

        // 1. Custom highlights (::highlight(<name>)) ordered by priority ascending, then registration order.
        foreach (var entry in CssHighlightRegistry.GetOrderedEntries())
        {
            var customRanges = entry.Highlight.GetRangesForScope(source).ToArray();
            if (customRanges.Length == 0) continue;
            var style = CssSelection.For(source, CssPseudoElement.Highlight, entry.Name);
            var effectiveKind = entry.Highlight.Type switch
            {
                CssHighlightType.SpellingError => CssPseudoElement.SpellingError,
                CssHighlightType.GrammarError => CssPseudoElement.GrammarError,
                _ => CssPseudoElement.Highlight,
            };
            int order = layerOrder++;
            foreach (var range in customRanges)
                AppendRange(range.StartOffset, range.EndOffset, style, effectiveKind, entry.Name, order);
        }

        // 2. ::target-text
        var targetRanges = CssHighlightRegistry.GetBuiltInRanges(CssPseudoElement.TargetText, source);
        if (targetRanges.Count > 0)
        {
            var style = CssSelection.For(source, CssPseudoElement.TargetText);
            int order = layerOrder++;
            foreach (var range in targetRanges)
                AppendRange(range.StartOffset, range.EndOffset, style, CssPseudoElement.TargetText, null, order);
        }

        // 3. ::search-text and ::search-text:current
        var searchRanges = CssHighlightRegistry.GetBuiltInRanges(CssPseudoElement.SearchText, source);
        if (searchRanges.Count > 0)
        {
            var regularStyle = CssSelection.For(source, CssPseudoElement.SearchText, searchCurrent: false);
            var currentStyle = CssSelection.For(source, CssPseudoElement.SearchText, searchCurrent: true);
            int regularOrder = layerOrder++;
            int currentOrder = layerOrder++;
            foreach (var range in searchRanges)
            {
                if (range.IsCurrent)
                    AppendRange(range.StartOffset, range.EndOffset, currentStyle, CssPseudoElement.SearchText, "current", currentOrder);
                else
                    AppendRange(range.StartOffset, range.EndOffset, regularStyle, CssPseudoElement.SearchText, null, regularOrder);
            }
        }

        // 4. ::spelling-error
        var spellingRanges = CssHighlightRegistry.GetBuiltInRanges(CssPseudoElement.SpellingError, source);
        if (spellingRanges.Count > 0)
        {
            var style = CssSelection.For(source, CssPseudoElement.SpellingError);
            int order = layerOrder++;
            foreach (var range in spellingRanges)
                AppendRange(range.StartOffset, range.EndOffset, style, CssPseudoElement.SpellingError, null, order);
        }

        // 5. ::grammar-error
        var grammarRanges = CssHighlightRegistry.GetBuiltInRanges(CssPseudoElement.GrammarError, source);
        if (grammarRanges.Count > 0)
        {
            var style = CssSelection.For(source, CssPseudoElement.GrammarError);
            int order = layerOrder++;
            foreach (var range in grammarRanges)
                AppendRange(range.StartOffset, range.EndOffset, style, CssPseudoElement.GrammarError, null, order);
        }

        // 6. ::selection (top-most highlight layer)
        if (selectionRange is { Length: > 0 } sel)
        {
            var style = CssSelection.For(source, CssPseudoElement.Selection);
            int order = layerOrder++;
            AppendRange(sel.Start, sel.Start + sel.Length, style, CssPseudoElement.Selection, null, order);
        }

        return regions;
    }

    private static Brush? ResolveRegionBackground(Region region, Brush? nativeBackground)
    {
        if (region.Style is { AuthorColors: true } style)
            return CssSelection.Background(style, region.Underlying);
        return region.Kind switch
        {
            CssPseudoElement.Selection => nativeBackground,
            CssPseudoElement.SearchText => string.Equals(region.HighlightName, "current", StringComparison.OrdinalIgnoreCase)
                ? s_defaultSearchCurrentBackground
                : s_defaultSearchBackground,
            CssPseudoElement.TargetText => s_defaultTargetBackground,
            _ => null,
        };
    }

    private static Brush? ResolveRegionForeground(Region region, Brush? nativeForeground)
    {
        if (region.Style is { AuthorColors: true } style)
            return CssSelection.Foreground(style, region.Underlying);
        return region.Kind == CssPseudoElement.Selection ? nativeForeground : null;
    }

    internal static void Paint(DrawingContext context, Geometry clip, IReadOnlyList<Region> regions,
        Brush? nativeBackground, Brush? nativeForeground, Action<DrawingContext> draw,
        Action<DrawingContext, Region>? decorate = null, bool backgrounds = true, Geometry? overlay = null, bool shadows = true, bool lineClips = false, Geometry? wholeInk = null)
    {
        regions = NativeForegroundContext(regions, nativeForeground);
        // Ordinary text shadows remain below every highlight background.
        if (shadows) OriginalShadows(context, draw);
        if (backgrounds)
        {
            foreach (var layerGroup in regions.OrderBy(r => r.LayerOrder).GroupBy(r => r.LayerOrder))
            {
                foreach (var group in layerGroup.GroupBy(region => ResolveRegionBackground(region, nativeBackground), BrushComparer.Instance))
                {
                    if (group.Key is not { } brush || brush is SolidColorBrush { Color.A: 0 }) continue;
                    context.DrawGeometry(brush, null, Area(group.Select(region => region.Bounds)));
                }
            }
        }
        var selected = overlay ?? Area(regions.Select(region => region.InkBounds));
        // Every highlight shadow is above the backgrounds and below all ink.
        if (shadows) HighlightShadows(context, regions, draw, decorate, lineClips, wholeInk, decorationClip: clip);
        // Unselected ink also belongs above every highlight shadow, including
        // a shadow that extends past the selected source boundary.
        var hasMultipleLayers = regions.Count > 1 && regions.Select(r => r.LayerOrder).Distinct().Count() > 1;
        if (!hasMultipleLayers)
        {
            var foregrounds = regions.GroupBy(region => ResolveRegionForeground(region, nativeForeground), BrushComparer.Instance).ToArray();
            if (foregrounds.Length == 1 && (foregrounds[0].Key is null || wholeInk is not null))
            {
                // currentColor preserves the underlying ink, including overhangs
                // and bidi boundaries. Splitting unchanged glyphs adds AA seams.
                draw(new TextContext(context, foregrounds[0].Key));
            }
            else
            {
                if (!lineClips) context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, clip, selected));
                try { draw(new TextContext(context, null, lineRegions: lineClips ? regions : null, lineClip: clip, invertLine: true, overlay: overlay)); }
                finally { if (!lineClips) context.Pop(); }
                // Equal used foreground colors share one clip. Separate source-node
                // styles must not introduce antialiased seams through a retained ligature.
                foreach (var group in foregrounds)
                {
                    if (!lineClips) context.PushClip(overlay ?? Area(group.Select(region => region.InkBounds)));
                    try { draw(new TextContext(context, group.Key, lineRegions: lineClips ? group.ToArray() : null, lineClip: clip, overlay: overlay)); }
                    finally { if (!lineClips) context.Pop(); }
                }
            }
        }
        else
        {
            // Multi-layer highlight cascade: higher LayerOrder regions with an active
            // foreground override win over lower LayerOrder regions in overlapping bounds.
            var activeForegroundRegions = regions
                .Select(r => (Region: r, Foreground: ResolveRegionForeground(r, nativeForeground)))
                .Where(x => x.Foreground is not null)
                .ToArray();

            if (activeForegroundRegions.Length == 0)
            {
                draw(new TextContext(context, null));
            }
            else
            {
                var allOverrideArea = overlay ?? Area(activeForegroundRegions.Select(x => x.Region.InkBounds));
                if (!lineClips) context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, clip, allOverrideArea));
                try { draw(new TextContext(context, null, lineRegions: lineClips ? activeForegroundRegions.Select(x => x.Region).ToArray() : null, lineClip: clip, invertLine: true, overlay: overlay)); }
                finally { if (!lineClips) context.Pop(); }

                var orderedLayers = activeForegroundRegions.OrderBy(x => x.Region.LayerOrder).GroupBy(x => x.Region.LayerOrder).ToArray();
                for (int i = 0; i < orderedLayers.Length; i++)
                {
                    var higherBounds = orderedLayers
                        .Skip(i + 1)
                        .SelectMany(g => g.Select(x => x.Region.InkBounds))
                        .ToArray();
                    Geometry? higherGeometry = higherBounds.Length > 0 ? Area(higherBounds) : null;

                    foreach (var colorGroup in orderedLayers[i].GroupBy(x => x.Foreground, BrushComparer.Instance))
                    {
                        var layerArea = overlay ?? Area(colorGroup.Select(x => x.Region.InkBounds));
                        if (higherGeometry is not null && overlay is null)
                            layerArea = new CombinedGeometry(GeometryCombineMode.Exclude, layerArea, higherGeometry);
                        if (!lineClips) context.PushClip(layerArea);
                        try
                        {
                            draw(new TextContext(context, colorGroup.Key,
                                lineRegions: lineClips ? colorGroup.Select(x => x.Region).ToArray() : null,
                                lineClip: clip,
                                overlay: overlay));
                        }
                        finally { if (!lineClips) context.Pop(); }
                    }
                }
            }
        }
        // Decoration offsets may place strokes below the selected line box.
        // Keep the selected horizontal spans, while honoring the owner's clip.
        context.PushClip(wholeInk ?? DecorationArea(regions, clip.Bounds));
        try
        {
            var decoration = new TextContext(context, null);
            foreach (var region in regions.OrderBy(r => r.LayerOrder))
                decorate?.Invoke(decoration, region);
        }
        finally { context.Pop(); }
    }

    internal static void PaintReplacedOverlay(
        DrawingContext context,
        DependencyObject source,
        Rect bounds,
        Brush? nativeBackground = null,
        Brush? underlying = null,
        CornerRadius cornerRadius = default)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var brush = CssSelection.ReplacedOverlayBrush(source, nativeBackground, underlying);
        if (cornerRadius != default)
            context.DrawRoundedRectangle(brush, null, bounds, cornerRadius.TopLeft, cornerRadius.TopRight);
        else
            context.DrawRectangle(brush, null, bounds);
    }

    internal static void PaintSvgHighlight(
        DrawingContext context,
        DependencyObject source,
        Geometry geometry,
        CssPseudoElement kind = CssPseudoElement.Selection,
        string? highlightName = null,
        Brush? underlying = null)
    {
        var style = CssSelection.For(source, kind, highlightName);
        if (style is null) return;
        var fill = CssSelection.SvgFill(style, underlying);
        var strokeBrush = CssSelection.SvgStroke(style, underlying);
        var strokeWidth = CssSelection.SvgStrokeWidth(style);
        Pen? pen = strokeBrush is not null && strokeWidth > 0 ? new Pen(strokeBrush, strokeWidth) : null;
        if (fill is not null || pen is not null)
            context.DrawGeometry(fill, pen, geometry);
    }

    internal static void OriginalShadows(DrawingContext context, Action<DrawingContext> draw)
        => draw(new TextContext(context, null, shadowsOnly: true));

    private static IReadOnlyList<Region> NativeForegroundContext(IReadOnlyList<Region> regions, Brush? nativeForeground)
    {
        // While the native paired colors apply, currentColor in highlight
        // effects follows their selected foreground. The ordinary draw callback
        // still owns the original brushes and unchanged source text.
        if (nativeForeground is null || !regions.Any(region => region.Kind == CssPseudoElement.Selection && region.Style is { AuthorColors: false }))
            return regions;
        return regions.Select(region => region.Kind == CssPseudoElement.Selection && region.Style is { AuthorColors: false }
            ? region with { Underlying = nativeForeground } : region).ToArray();
    }

    internal static void HighlightShadows(DrawingContext context, IReadOnlyList<Region> regions,
        Action<DrawingContext> draw, Action<DrawingContext, Region>? decorate, bool lineClips = false, Geometry? wholeInk = null, Brush? nativeForeground = null, Geometry? decorationClip = null)
    {
        regions = NativeForegroundContext(regions, nativeForeground);
        var groups = regions.OrderBy(r => r.LayerOrder).GroupBy(region => (region.Style, region.Underlying)).ToArray();
        foreach (var group in groups)
        {
            if (group.Key.Style is not { } style) continue;
            var effect = CssSelection.Shadow(style, group.Key.Underlying);
            if (effect?.HasEffect != true) continue;
            var complete = groups.Length == 1 ? wholeInk : null;
            var bounds = complete?.Bounds ?? group.Select(region => region.InkBounds).Aggregate(Rect.Union);
            var decoration = new DecorationBounds();
            if (decorate is not null) foreach (var region in group) decorate(decoration, region);
            if (decoration.Bounds is { } strokes) bounds = Rect.Union(bounds, strokes);
            var shadow = CssTextShadowPainter.Begin(context, effect, bounds, shadowsOnly: true);
            if (shadow is null) continue;
            // Per-line masks already apply the selected range. Repeating an
            // antialiased clip attenuates glyphs at partial-range boundaries.
            var clipMask = !lineClips || complete is not null;
            try
            {
                if (clipMask) context.PushClip(complete ?? Area(group.Select(region => region.InkBounds)));
                try
                {
                    var mask = new TextContext(context, Brushes.White, lineRegions: lineClips && complete is null ? group.ToArray() : null); draw(mask);
                }
                finally { if (clipMask) context.Pop(); }
                if (decoration.Bounds is not null)
                {
                    context.PushClip(complete ?? DecorationArea(group, decorationClip?.Bounds ?? bounds));
                    try
                    {
                        var mask = new TextContext(context, Brushes.White);
                        foreach (var region in group) decorate?.Invoke(mask, region);
                    }
                    finally { context.Pop(); }
                }
            }
            finally { shadow.Value.End(); }
        }
    }

    internal readonly struct LineScope(DrawingContext? context) : IDisposable
    {
        public void Dispose() => context?.Pop();
    }
    internal static LineScope BeginLine(DrawingContext context, double baseline)
        => context is TextContext text ? text.BeginLine(baseline) : default;

    internal static Geometry Area(IEnumerable<Rect> rectangles)
    {
        var path = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var rect in rectangles.Where(rect => rect.Width > 0 && rect.Height > 0))
            path.Figures.Add(CssRoundedRectangleGeometry.Figure(rect, default));
        path.Freeze(); return path;
    }

    private static Geometry DecorationArea(IEnumerable<Region> regions, Rect clip)
        => Area(regions.Select(region => region.WritingTransform is { }
            ? new Rect(clip.Left, region.InkBounds.Top, clip.Width, region.InkBounds.Height)
            : new Rect(region.InkBounds.X, clip.Top, region.InkBounds.Width, clip.Height)));

    private sealed class DecorationBounds : DrawingContextAdapter
    {
        internal Rect? Bounds;
        private Matrix _matrix = Matrix.Identity;
        private readonly Stack<Matrix> _stack = new();
        private void Add(Rect rect, Pen? pen = null)
        {
            var padding = pen is null ? 0 : Math.Max(1, pen.MiterLimit) * pen.Thickness / 2;
            rect = new(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);
            rect = new MatrixTransform(_matrix).TransformBounds(rect);
            Bounds = Bounds is { } previous ? Rect.Union(previous, rect) : rect;
        }
        public override void DrawLine(Pen pen, Point a, Point b) => Add(new Rect(a, b), pen);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Add(geometry.Bounds, pen);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Add(rect, pen);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => Add(rect, pen);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point point, double x, double y) => Add(new Rect(point.X - x, point.Y - y, x * 2, y * 2), pen);
        public override void DrawText(FormattedText text, Point point) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void PushClip(Geometry geometry) => _stack.Push(_matrix);
        public override void PushTransform(Transform transform)
        {
            _stack.Push(_matrix);
            _matrix = Matrix.Multiply(transform.Value, _matrix);
        }
        public override void PushOpacity(double opacity) => _stack.Push(_matrix);
        public override void Pop() => _matrix = _stack.Pop();
        public override void Close() { }
    }

    internal static void Decorations(DrawingContext context, Region region, FrameworkElement owner)
    {
        if (region.WritingTransform is { } writing)
        {
            var inverse = writing; inverse.Invert();
            context.PushTransform(new MatrixTransform(writing));
            try
            {
                Decorations(context, region with
                {
                    Bounds = new MatrixTransform(inverse).TransformBounds(region.Bounds),
                    WritingTransform = null,
                }, owner);
            }
            finally { context.Pop(); }
            return;
        }
        var effectiveKind = region.Style?.Kind is CssPseudoElement.SpellingError or CssPseudoElement.GrammarError
            ? region.Style.Kind
            : region.Kind;

        if (region.Style is { } style)
        {
            var lines = (CssTextDecorationLine)style.Values.GetValue(CssTextDecorationProperties.LineProperty)!;
            if (lines != CssTextDecorationLine.None)
            {
                var brush = CssSelection.DecorationColor(style, region.Underlying);
                if (brush is null) return;
                var size = style.Values.FontSize;
                var font = TextMeasurement.GetFontMetrics(style.Values.FontFamily.GetRenderingSource(style.Values), size,
                    style.Values.FontWeight.ToOpenTypeWeight(), style.Values.FontStyle.ToOpenTypeStyle());
                var rect = region.Bounds;
                // Native editors draw from the row's top. Rounding a selection row's
                // height must not recenter its glyph baseline or its added decorations.
                var baseline = region.Baseline ?? rect.Top + font.Ascent;
                CssTextDecorationPainter.Draw(context, style.Values, owner, brush, lines, rect.Left, rect.Right,
                    region.TextTop ?? rect.Top, baseline, size, baseline + font.Descent);
                return;
            }

            if (style.SuppressDefaultDecorations)
                return;
        }

        if (effectiveKind is CssPseudoElement.SpellingError or CssPseudoElement.GrammarError)
        {
            Brush waveBrush = effectiveKind == CssPseudoElement.GrammarError
                ? s_defaultGrammarBrush
                : s_defaultSpellingBrush;
            if (region.Style is { } errorStyle &&
                errorStyle.Values.GetValue(CssTextDecorationProperties.ColorProperty) is not null &&
                CssSelection.DecorationColor(errorStyle, region.Underlying) is { } authoredWaveBrush)
            {
                waveBrush = authoredWaveBrush;
            }
            DrawDefaultErrorWave(context, region.Bounds, region.Baseline, waveBrush);
        }
    }

    internal static void DrawDefaultErrorWave(DrawingContext context, Rect bounds, double? baseline, Brush brush, double thickness = 1.35)
    {
        if (bounds.Width <= 1.0 || bounds.Height <= 0) return;
        double yCenter = baseline is { } b ? Math.Min(bounds.Bottom - 1.5, b + 2.5) : bounds.Bottom - 2.0;
        double step = 3.5;
        double amplitude = 1.35;
        var figure = new PathFigure { StartPoint = new Point(bounds.Left, yCenter), IsClosed = false, IsFilled = false };
        double x = bounds.Left;
        bool up = true;
        while (x < bounds.Right)
        {
            double nextX = Math.Min(bounds.Right, x + step);
            double midX = (x + nextX) * 0.5;
            double peakY = yCenter + (up ? -amplitude : amplitude);
            figure.Segments.Add(new QuadraticBezierSegment(new Point(midX, peakY), new Point(nextX, yCenter), true));
            x = nextX;
            up = !up;
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        context.DrawGeometry(null, pen, geometry);
    }

    // Ask the existing selection geometry path for rectangles without painting
    // or changing the source's selection/brush dependency properties.
    internal sealed class GeometryCapture(DrawingContext original) : DrawingContextAdapter, IOffsetDrawingContext
    {
        internal readonly List<Rect> Rectangles = [];
        public Point Offset { get => (original as IOffsetDrawingContext)?.Offset ?? default; set { } }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { if (rect.Width > 0 && rect.Height > 0) Rectangles.Add(rect); }
        public override void DrawText(FormattedText text, Point point) { }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private sealed class BrushComparer : IEqualityComparer<Brush?>
    {
        internal static readonly BrushComparer Instance = new();
        public bool Equals(Brush? a, Brush? b) => ReferenceEquals(a, b) || a is SolidColorBrush x && b is SolidColorBrush y && x.Color == y.Color && x.Opacity == y.Opacity;
        public int GetHashCode(Brush? value) => value is SolidColorBrush brush ? HashCode.Combine(brush.Color, brush.Opacity) : value?.GetHashCode() ?? 0;
    }

    private sealed class TextContext(DrawingContext inner, Brush? foreground, bool shadowsOnly = false,
        IReadOnlyList<Region>? lineRegions = null, Geometry? lineClip = null, bool invertLine = false, Geometry? overlay = null) : DrawingContextAdapter,
        IOffsetDrawingContext, IClipBoundsDrawingContext, IEffectDrawingContext, IOpacityDrawingContext, ITransformDrawingContext
    {
        internal DrawingContext Inner => inner;
        private int _captureDepth;
        internal LineScope BeginLine(double baseline)
        {
            if (lineRegions is null) return default;
            var matching = lineRegions.Where(region => region.Baseline is not { } line || Math.Abs(line - baseline) < .01).ToArray();
            var area = matching.Length > 0 && overlay is not null ? overlay : Area(matching.Select(region => region.InkBounds));
            inner.PushClip(invertLine && lineClip is not null ? new CombinedGeometry(GeometryCombineMode.Exclude, lineClip, area) : area);
            return new(inner);
        }
        private bool Ink => !shadowsOnly || _captureDepth > 0;
        private Brush? Brush(Brush? value) => value is null ? null : shadowsOnly ? Brushes.White : foreground ?? value;
        private Pen? Stroke(Pen? value) => value is null ? null : new Pen(Brush(value.Brush)!, value.Thickness)
        {
            StartLineCap = value.StartLineCap, EndLineCap = value.EndLineCap, DashCap = value.DashCap,
            LineJoin = value.LineJoin, MiterLimit = value.MiterLimit, DashStyle = value.DashStyle, SvgViewportStroke = value.SvgViewportStroke
        };
        public Point Offset { get => (inner as IOffsetDrawingContext)?.Offset ?? default; set { if (inner is IOffsetDrawingContext offset) offset.Offset = value; } }
        public Rect? CurrentClipBounds => (inner as IClipBoundsDrawingContext)?.CurrentClipBounds;
        public bool IsElementEffectCaptureEnabled => shadowsOnly && inner is IEffectDrawingContext { IsElementEffectCaptureEnabled: true, SupportsCssTextShadowsOnly: true };
        public bool SupportsCssTextShadowsOnly => shadowsOnly && inner is IEffectDrawingContext { SupportsCssTextShadowsOnly: true };
        public override void DrawText(FormattedText text, Point point)
        {
            if (!Ink) return;
            var brush = shadowsOnly ? Brushes.White : foreground;
            inner.DrawText(brush is null ? text : text.CreatePaintSnapshot(brush), point);
        }
        public override void DrawGlyphRun(Brush? brush, GlyphRun glyphs) { if (Ink) inner.DrawGlyphRun(Brush(brush), glyphs); }
        public override void DrawLine(Pen pen, Point a, Point b) { if (Ink) inner.DrawLine(Stroke(pen)!, a, b); }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { if (Ink) inner.DrawRectangle(Brush(brush), Stroke(pen), rect); }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { if (Ink) inner.DrawRoundedRectangle(Brush(brush), Stroke(pen), rect, x, y); }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { if (Ink) inner.DrawEllipse(Brush(brush), Stroke(pen), center, x, y); }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { if (Ink) inner.DrawGeometry(Brush(brush), Stroke(pen), geometry); }
        public override void DrawImage(ImageSource source, Rect rect) { if (Ink && !shadowsOnly) inner.DrawImage(source, rect); }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void PushClip(Geometry geometry) => inner.PushClip(geometry);
        public override void PushTransform(Transform transform) => inner.PushTransform(transform);
        public override void PushOpacity(double opacity) => inner.PushOpacity(opacity);
        public override void PushOpacityMask(Brush mask) => inner.PushOpacityMask(mask);
        public override void PushGuidelineSet(GuidelineSet guidelines) => inner.PushGuidelineSet(guidelines);
        public override void Pop() => inner.Pop();
        public void PopOpacity() => ((IOpacityDrawingContext)inner).PopOpacity();
        public void PushTransform(Transform transform, double x, double y) => ((ITransformDrawingContext)inner).PushTransform(transform, x, y);
        public void PopTransform() => ((ITransformDrawingContext)inner).PopTransform();
        public void BeginEffectCapture(float x, float y, float w, float h) { _captureDepth++; ((IEffectDrawingContext)inner).BeginEffectCapture(x, y, w, h); }
        public void EndEffectCapture() { ((IEffectDrawingContext)inner).EndEffectCapture(); _captureDepth--; }
        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float originX = 0, float originY = 0, float tl = 0, float tr = 0, float br = 0, float bl = 0)
            => ((IEffectDrawingContext)inner).ApplyCssTextShadowsOnly(effect, x, y, w, h, originX, originY);
        public void ApplyCssTextShadowsOnly(IEffect effect, float x, float y, float w, float h, float originX, float originY)
            => ((IEffectDrawingContext)inner).ApplyCssTextShadowsOnly(effect, x, y, w, h, originX, originY);
        public override void Close() { }
    }
}
