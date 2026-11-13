using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI;

internal interface ICssTextInkContext
{
    int InkDepth { get; set; }
}

/// <summary>Captures platform glyph ink without painting the live tree's backgrounds or editor chrome.</summary>
internal static class CssBackgroundTextClip
{
    [ThreadStatic] private static Visual? s_root;
    [ThreadStatic] private static bool s_rootEntryPending;
    internal static bool Active => s_root is not null;
    internal static bool IsRoot(Visual visual) => ReferenceEquals(s_root, visual);
    internal static bool TryEnterRoot(Visual visual)
    {
        if (!s_rootEntryPending || !IsRoot(visual)) return false;
        s_rootEntryPending = false;
        return true;
    }
    internal sealed record Raster(Rect Bounds, int Width, int Height, byte[] Alpha)
    {
        private Rect? _inkBounds;
        internal Rect InkBounds => _inkBounds ??= ComputeInkBounds(Bounds, Width, Height, Alpha);
    }

    private static Rect ComputeInkBounds(Rect bounds, int width, int height, byte[] alpha)
    {
        if (width <= 0 || height <= 0 || alpha.Length < width * height)
            return Rect.Empty;
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (alpha[row + x] == 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < minX || maxY < minY)
            return Rect.Empty;
        double sx = bounds.Width / width, sy = bounds.Height / height;
        return new Rect(
            bounds.X + minX * sx,
            bounds.Y + minY * sy,
            (maxX - minX + 1) * sx,
            (maxY - minY + 1) * sy);
    }

    internal static Rect? MeasureInkBounds(FrameworkElement owner, Rect extent)
    {
        if (Active || extent.IsEmpty) return null;
        var measure = new InkBoundsContext(extent);
        Traverse(owner, new InkContext(measure, default) { Offset = default });
        return measure.Bounds;
    }

    private static readonly Brush White = CreateWhite();
    private static Brush CreateWhite() { var brush = new SolidColorBrush(Colors.White); brush.Freeze(); return brush; }

    internal static Raster? Capture(FrameworkElement owner, Rect bounds, double scale, Geometry? additionalInk = null)
    {
        if (Active || bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || !double.IsFinite(scale) || scale <= 0) return null;
        bounds = Align(bounds, scale, out double rasterWidth, out double rasterHeight);
        if (rasterWidth > 16384 || rasterHeight > 16384 ||
            rasterWidth * rasterHeight > 16_777_216)
        {
            // A tall empty box is not a tall glyph bitmap. Measure only the
            // actual text/decorations before allocating a bounded raster.
            var measure = new InkBoundsContext(bounds);
            Traverse(owner, new InkContext(measure, default) { Offset = default });
            var ink = measure.Bounds ?? Rect.Empty;
            if (additionalInk is not null) ink.Union(additionalInk.Bounds);
            if (ink.IsEmpty) return null;
            bounds.Intersect(ink);
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
            double requested = scale;
            scale = Math.Min(scale, Math.Min(16382 / bounds.Width, Math.Min(16382 / bounds.Height,
                Math.Sqrt(16_700_000 / bounds.Width / bounds.Height))));
            if (scale < requested) Report("Text clip resolution is constrained by the raster budget");
            bounds = Align(bounds, scale, out rasterWidth, out rasterHeight);
        }
        if (rasterWidth < 1 || rasterHeight < 1 || rasterWidth > 16384 || rasterHeight > 16384 ||
            rasterWidth * rasterHeight > 16_777_216) return null;
        int width = (int)rasterWidth, height = (int)rasterHeight;
        RenderContext context;
        RenderTarget target;
        try
        {
            context = RenderContext.CreateRasterizerContext();
            try { target = context.CreateOffscreenRenderTarget(width, height); }
            catch { context.Dispose(); throw; }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            NotSupportedException or InvalidOperationException)
        {
            Report("Platform glyph rasterizer is unavailable: " + error.Message);
            return null;
        }
        using (context)
        using (target)
        {
            target.SetDpi((float)(96 * scale), (float)(96 * scale));
            if (target.RequestReadback() != JaliumResult.Ok) { Report("Glyph readback is unavailable"); return null; }
            var drawing = new RenderTargetDrawingContext(target, context);
            try
            {
                target.BeginDraw();
                try
                {
                    target.Clear(0, 0, 0, 0);
                    var ink = new InkContext(drawing, bounds.Location) { Offset = default };
                    Traverse(owner, ink);
                    if (additionalInk is not null)
                    {
                        drawing.Offset = new Point(-bounds.X, -bounds.Y);
                        drawing.DrawGeometry(White, null, additionalInk);
                    }
                }
                finally { drawing.Close(); target.EndDraw(); }
                byte[] pixels = new byte[checked(width * height * 4)];
                if (target.FetchReadback(pixels, (uint)(width * 4), out int actualWidth, out int actualHeight) != JaliumResult.Ok ||
                    actualWidth != width || actualHeight != height)
                { Report("Glyph readback failed"); return null; }
                byte[] alpha = new byte[width * height];
                for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
                // The rounded-up pixel extent must keep the original DIP-to-pixel mapping.
                return new(new Rect(bounds.X, bounds.Y, width / scale, height / scale), width, height, alpha);
            }
            finally { drawing.Close(); drawing.ClearCache(); }
        }
    }

    private static Rect Align(Rect bounds, double scale, out double pixelWidth, out double pixelHeight)
    {
        // Keep the glyph raster on its original pixel grid. Cropping at a
        // fractional inline-box edge and resampling the mask would blur ink twice.
        double pixelLeft = Math.Floor(bounds.Left * scale - 1e-9);
        double pixelTop = Math.Floor(bounds.Top * scale - 1e-9);
        double pixelRight = Math.Ceiling(bounds.Right * scale + 1e-9);
        double pixelBottom = Math.Ceiling(bounds.Bottom * scale + 1e-9);
        pixelWidth = Math.Max(0, pixelRight - pixelLeft);
        pixelHeight = Math.Max(0, pixelBottom - pixelTop);
        return new Rect(pixelLeft / scale, pixelTop / scale, pixelWidth / scale, pixelHeight / scale);
    }

    private static void Traverse(Visual owner, DrawingContext context)
    {
        s_root = owner;
        s_rootEntryPending = true;
        try { owner.Render(context); }
        finally { s_root = null; s_rootEntryPending = false; }
    }

    // Conservative bounds only; glyph coverage always comes from the native
    // renderer. Highlight/advance rectangles are never used as the mask.
    private sealed class InkBoundsContext(Rect extent) : DrawingContextAdapter,
        IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext, ITransformDrawingContext
    {
        private readonly BoundsAccumulator _bounds = new();
        private readonly Stack<Point?> _offsetStack = new();
        internal Rect? Bounds => _bounds.GetBounds();
        public Point Offset { get; set; }
        public Rect? CurrentClipBounds => extent;
        private Rect Place(Rect rect) { rect.Offset(Offset.X, Offset.Y); return rect; }
        public override void DrawText(FormattedText text, Point origin)
        {
            if (!text.IsMeasured && !TextMeasurement.MeasureText(text)) { _bounds.AccumulateRect(extent); return; }
            var rect = new Rect(origin.X, origin.Y, Math.Max(1, text.WidthIncludingTrailingWhitespace), Math.Max(1, text.Height));
            rect.Inflate(text.FontSize * 2, text.FontSize * 2);
            _bounds.AccumulateRect(Place(rect));
        }
        public override void DrawLine(Pen pen, Point a, Point b) => _bounds.AccumulateRect(Place(new Rect(
            Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y))), pen.Thickness/2+1);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => _bounds.AccumulateRect(Place(rect), (pen?.Thickness ?? 0)/2+1);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => DrawRectangle(brush,pen,rect);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) => DrawRectangle(brush,pen,new Rect(center.X-x,center.Y-y,2*x,2*y));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => DrawRectangle(brush,pen,geometry.Bounds);
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void PushClip(Geometry geometry)
        {
            _offsetStack.Push(null);
            _bounds.PushClip(new RectangleGeometry(Place(geometry.Bounds)));
        }
        public override void PushTransform(Transform transform)
        {
            if (transform is TranslateTransform translate)
            {
                _offsetStack.Push(Offset);
                Offset = new Point(Offset.X + translate.X, Offset.Y + translate.Y);
                _bounds.PushOpacity();
                return;
            }
            _offsetStack.Push(null);
            var m = Matrix.Multiply(
                Matrix.Multiply(new Matrix(1, 0, 0, 1, -Offset.X, -Offset.Y), transform.Value),
                new Matrix(1, 0, 0, 1, Offset.X, Offset.Y));
            _bounds.PushTransform(new MatrixTransform(m));
        }
        public void PushTransform(Transform transform, double x, double y)
        {
            _offsetStack.Push(null);
            double cx = Offset.X + x, cy = Offset.Y + y;
            var m = Matrix.Multiply(
                Matrix.Multiply(new Matrix(1, 0, 0, 1, -cx, -cy), transform.Value),
                new Matrix(1, 0, 0, 1, cx, cy));
            _bounds.PushTransform(new MatrixTransform(m));
        }
        public override void PushOpacity(double value) { _offsetStack.Push(null); _bounds.PushOpacity(); }
        public override void PushOpacityMask(Brush mask) { _offsetStack.Push(null); _bounds.PushOpacity(); }
        public override void PushGuidelineSet(GuidelineSet guidelines) { _offsetStack.Push(null); _bounds.PushOpacity(); }
        public override void Pop()
        {
            if (_offsetStack.TryPop(out var saved) && saved is { } prev)
                Offset = prev;
            _bounds.Pop();
        }
        public void PopOpacity() => Pop();
        public void PopTransform() => Pop();
        public override void Close() { }
    }

    internal static void Report(string message) =>
        CssDiagnostics.Report("background-clip", CssDiagnosticReason.LossyConversion, null, message);

    private sealed class InkContext(DrawingContext inner, Point crop) : DrawingContextAdapter,
        IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext, ITransformDrawingContext, ICssTextInkContext
    {
        public int InkDepth { get; set; }
        private bool Ink => InkDepth > 0;
        public Point Offset
        {
            get
            {
                var innerOffset = ((IOffsetDrawingContext)inner).Offset;
                return new(innerOffset.X + crop.X, innerOffset.Y + crop.Y);
            }
            set => ((IOffsetDrawingContext)inner).Offset = new(value.X - crop.X, value.Y - crop.Y);
        }
        public Rect? CurrentClipBounds
        {
            get
            {
                var bounds = (inner as IClipBoundsDrawingContext)?.CurrentClipBounds;
                if (bounds is { } rect) { rect.Offset(crop.X, crop.Y); return rect; }
                return null;
            }
        }
        private static Pen? Paint(Pen? pen) => pen is null ? null : new Pen(White, pen.Thickness)
        {
            StartLineCap = pen.StartLineCap, EndLineCap = pen.EndLineCap, DashCap = pen.DashCap,
            LineJoin = pen.LineJoin, MiterLimit = pen.MiterLimit, DashStyle = pen.DashStyle,
        };
        public override void DrawText(FormattedText text, Point origin) => inner.DrawText(text.CreatePaintSnapshot(White), origin);
        // GlyphRun.BuildGeometry currently returns advance rectangles. Never substitute those for glyph ink.
        public override void DrawGlyphRun(Brush? brush, GlyphRun run) => Report("GlyphRun outline capture is unavailable");
        public override void DrawLine(Pen pen, Point a, Point b) { if (Ink) inner.DrawLine(Paint(pen)!, a, b); }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { if (Ink) inner.DrawRectangle(brush is null ? null : White, Paint(pen), rect); }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { if (Ink) inner.DrawRoundedRectangle(brush is null ? null : White, Paint(pen), rect, x, y); }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { if (Ink) inner.DrawEllipse(brush is null ? null : White, Paint(pen), center, x, y); }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { if (Ink) inner.DrawGeometry(brush is null ? null : White, Paint(pen), geometry); }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void SetShapeType(int type, float exponent) => inner.SetShapeType(type, exponent);
        public override void PushTransform(Transform transform) => inner.PushTransform(transform);
        public override void PushClip(Geometry geometry) => inner.PushClip(geometry);
        public void PushRoundedRectClip(Rect bounds, CornerRadius radii) => ((IClipDrawingContext)inner).PushRoundedRectClip(bounds, radii);
        public void PushPerCornerRoundedRectClip(Rect bounds, CornerRadius radii) => ((IClipDrawingContext)inner).PushPerCornerRoundedRectClip(bounds, radii);
        public override void PushOpacity(double opacity) => inner.PushOpacity(1);
        public override void PushOpacityMask(Brush mask) => inner.PushOpacity(1);
        public override void PushGuidelineSet(GuidelineSet guidelines) => inner.PushGuidelineSet(guidelines);
        public override void Pop() => inner.Pop();
        public void PopOpacity() => ((IOpacityDrawingContext)inner).PopOpacity();
        public void PushTransform(Transform transform, double x, double y) => ((ITransformDrawingContext)inner).PushTransform(transform, x, y);
        public void PopTransform() => ((ITransformDrawingContext)inner).PopTransform();
        public override void Close() { }
    }
}
