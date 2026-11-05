using Jalium.UI.Media;

namespace Jalium.UI;

internal static partial class MacOSWritingToolsPresentation
{
    internal readonly struct CssInkScope(ICssTextInkContext? context) : IDisposable
    {
        public void Dispose() { if (context is not null) --context.InkDepth; }
    }

    internal static CssInkScope BeginCssTextInk(DrawingContext context)
    {
        if (context is not ICssTextInkContext ink) return default;
        ++ink.InkDepth;
        return new(ink);
    }

    // A preview walks the actual CSS visual tree, preserving its layout,
    // fragmentation, transforms and clipping. Only text paint is forwarded;
    // box backgrounds, borders and editor selection stay in the live view.
    internal sealed class CssTextContext(DrawingContext inner) : DrawingContextAdapter,
        IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext,
        ITransformDrawingContext, IEffectDrawingContext, IAlphaMaskDrawingContext, IBlendDrawingContext, ICssTextInkContext
    {
        public int InkDepth { get; set; }
        private bool Ink => InkDepth != 0;
        public Point Offset { get => (inner as IOffsetDrawingContext)?.Offset ?? default; set { if (inner is IOffsetDrawingContext offset) offset.Offset = value; } }
        public Rect? CurrentClipBounds => (inner as IClipBoundsDrawingContext)?.CurrentClipBounds;
        public bool IsElementEffectCaptureEnabled => Ink && inner is IEffectDrawingContext { IsElementEffectCaptureEnabled: true };
        public bool SupportsCssTextShadowsOnly => inner is IEffectDrawingContext { SupportsCssTextShadowsOnly: true };
        public bool SupportsAlphaMasks => inner is IAlphaMaskDrawingContext { SupportsAlphaMasks: true };
        public bool SupportsBlendModes=>inner is IBlendDrawingContext{SupportsBlendModes:true};
        public void BeginBlendCapture(Rect bounds)=>((IBlendDrawingContext)inner).BeginBlendCapture(bounds);
        public void EndBlendCapture()=>((IBlendDrawingContext)inner).EndBlendCapture();
        public void CompositeBlendCapture(CssBlendMode mode,Rect bounds)=>((IBlendDrawingContext)inner).CompositeBlendCapture(mode,bounds);
        public double AlphaMaskRasterScale => (inner as IAlphaMaskDrawingContext)?.AlphaMaskRasterScale ?? 1;
        public double AlphaMaskTransformScale => (inner as IAlphaMaskDrawingContext)?.AlphaMaskTransformScale ?? 1;
        public void BeginAlphaMaskCapture(Rect bounds) => ((IAlphaMaskDrawingContext)inner).BeginAlphaMaskCapture(bounds);
        public void EndAlphaMaskCapture() => ((IAlphaMaskDrawingContext)inner).EndAlphaMaskCapture();
        public void ApplyAlphaMask(byte[] coverage, int width, int height, Rect maskBounds, Rect captureBounds) =>
            ((IAlphaMaskDrawingContext)inner).ApplyAlphaMask(coverage, width, height, maskBounds, captureBounds);
        public override void DrawText(FormattedText text, Point origin) { if (Ink) inner.DrawText(text, origin); }
        public override void DrawGlyphRun(Brush? brush, GlyphRun run) { if (Ink) inner.DrawGlyphRun(brush, run); }
        public override void DrawLine(Pen pen, Point a, Point b) { if (Ink) inner.DrawLine(pen, a, b); }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { if (Ink) inner.DrawRectangle(brush, pen, rect); }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { if (Ink) inner.DrawRoundedRectangle(brush, pen, rect, x, y); }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { if (Ink) inner.DrawEllipse(brush, pen, center, x, y); }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { if (Ink) inner.DrawGeometry(brush, pen, geometry); }
        public override void DrawImage(ImageSource image, Rect rect) { if (Ink) inner.DrawImage(image, rect); }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void SetShapeType(int type, float exponent) => inner.SetShapeType(type, exponent);
        public override void PushTransform(Transform transform) => inner.PushTransform(transform);
        public override void PushClip(Geometry geometry) => inner.PushClip(geometry);
        public void PushRoundedRectClip(Rect bounds, CornerRadius radii) => ((IClipDrawingContext)inner).PushRoundedRectClip(bounds, radii);
        public void PushPerCornerRoundedRectClip(Rect bounds, CornerRadius radii) => ((IClipDrawingContext)inner).PushPerCornerRoundedRectClip(bounds, radii);
        public override void PushOpacity(double opacity) => inner.PushOpacity(opacity);
        public override void PushGuidelineSet(GuidelineSet guidelines) => inner.PushGuidelineSet(guidelines);
        public override void PushOpacityMask(Brush mask) => inner.PushOpacityMask(mask);
        public override void Pop() => inner.Pop();
        public void PopOpacity() => ((IOpacityDrawingContext)inner).PopOpacity();
        public void PushTransform(Transform transform, double x, double y) => ((ITransformDrawingContext)inner).PushTransform(transform, x, y);
        public void PopTransform() => ((ITransformDrawingContext)inner).PopTransform();
        public void BeginEffectCapture(float x, float y, float w, float h) => ((IEffectDrawingContext)inner).BeginEffectCapture(x, y, w, h);
        public void EndEffectCapture() => ((IEffectDrawingContext)inner).EndEffectCapture();
        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float originX = 0, float originY = 0, float tl = 0, float tr = 0, float br = 0, float bl = 0) =>
            ((IEffectDrawingContext)inner).ApplyElementEffect(effect, x, y, w, h, originX, originY, tl, tr, br, bl);
        public void ApplyCssTextShadowsOnly(IEffect effect, float x, float y, float w, float h, float originX, float originY) =>
            ((IEffectDrawingContext)inner).ApplyCssTextShadowsOnly(effect, x, y, w, h, originX, originY);
        public override void Close() { }
    }
}
