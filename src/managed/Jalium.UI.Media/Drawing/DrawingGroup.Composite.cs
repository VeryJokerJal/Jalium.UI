using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

public sealed partial class DrawingGroup
{
    internal bool ContainsBlend()
    {
        var pending = new Stack<DrawingGroup>();
        var visited = new HashSet<DrawingGroup>();
        pending.Push(this);
        while (pending.TryPop(out var group))
        {
            if (!visited.Add(group)) continue;
            if (group.BlendMode != CssBlendMode.Normal) return true;
            foreach (var child in group.Children)
                if (child is DrawingGroup nested) pending.Push(nested);
        }
        return false;
    }
    private const int MaximumCompositePixels = 16_777_216;
    internal Rect LocalBounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var child in Children) if (child is not null) bounds.Union(child.Bounds);
            return bounds;
        }
    }
    internal static (int Width, int Height) RasterSize(Rect bounds, double scale)
    {
        if (!(bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))) return default;
        if (!(scale > 0 && double.IsFinite(scale))) scale = 1;
        scale = Math.Min(scale, Math.Sqrt(MaximumCompositePixels / bounds.Width / bounds.Height));
        var width = Math.Max(1, (int)Math.Min(MaximumCompositePixels, Math.Ceiling(bounds.Width * scale)));
        return (width, Math.Max(1, (int)Math.Min(MaximumCompositePixels / width, Math.Ceiling(bounds.Height * scale))));
    }

    private void RenderComposite(DrawingContext context)
    {
        var effect = SvgMask is not null || Opacity < 1 || ClipGeometry is not null;
        // A normal source-over group is invariant when nothing inside blends.
        // Keep ordinary SVG images on their existing direct drawing path.
        var blendGroup = Isolated && ContainsBlend() || BlendMode != CssBlendMode.Normal;
        var alpha = context as IAlphaMaskDrawingContext;
        var offset = context as IOffsetDrawingContext;
        if ((effect || blendGroup) && (alpha?.SupportsAlphaMasks != true || offset is null))
        {
            if (BlendMode != CssBlendMode.Normal)
                CssDiagnostics.Report("mix-blend-mode", CssDiagnosticReason.LossyConversion,
                    context.GetType(), "Drawing context cannot blend this SVG group with its external backdrop");
            var bounds = Bounds; var size = RasterSize(bounds, alpha?.AlphaMaskRasterScale ?? 1);
            if (size.Width == 0 || SoftwareVectorRasterizer.Rasterize(this, size.Width, size.Height, bounds) is not { } pixels) return;
            var image = BitmapSource.Create(size.Width, size.Height, 96 * size.Width / bounds.Width,
                96 * size.Height / bounds.Height, PixelFormats.Bgra32, null, pixels, size.Width * 4);
            context.DrawImage(image, bounds); return;
        }

        var states = 0;
        try
        {
            if (SvgStrokeSpace.TransformFor(this) is { } svgTransform)
            {
                // SVG transforms use matrix composition throughout. The layout
                // TranslateTransform fast path changes Offset instead; recording
                // cannot bake that future Offset into group capture rectangles.
                context.PushTransform(SvgStrokeSpace.IsActive && svgTransform is TranslateTransform
                    ? new MatrixTransform(svgTransform.Value) : svgTransform);
                states++;
            }
            if (ClipGeometry is not null) { context.PushClip(ClipGeometry); states++; }
            if (!effect && !blendGroup) { foreach (var child in Children) child?.RenderTo(context); return; }
            var bounds = LocalBounds;
            if (!bounds.IsEmpty) bounds.Inflate(1 / Math.Max(1, alpha!.AlphaMaskRasterScale), 1 / Math.Max(1, alpha.AlphaMaskRasterScale));
            var size = RasterSize(bounds, alpha!.AlphaMaskRasterScale);
            if (size.Width == 0) return;
            var absolute = new Rect(bounds.X + offset!.Offset.X, bounds.Y + offset.Offset.Y, bounds.Width, bounds.Height);
            using var blend = blendGroup ? CssBlendGroup.Begin(context, absolute, BlendMode) : null;
            if (!effect)
            {
                foreach (var child in Children) child?.RenderTo(context);
                blend?.Complete();
                return;
            }
            byte[] coverage = [255]; int width = 1, height = 1;
            if (SvgMask is { } mask)
            {
                width = size.Width; height = size.Height; coverage = new byte[checked(width * height)];
                if (mask.Rasterize(bounds, width, height) is { } pixels)
                    for (var i = 0; i < coverage.Length; i++) coverage[i] = (byte)Math.Clamp(Math.Round(mask.Alpha(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4], pixels[i * 4 + 3]) * 255), 0, 255);
            }
            alpha.BeginAlphaMaskCapture(absolute);
            var complete = false;
            try { foreach (var child in Children) child?.RenderTo(context); complete = true; }
            finally { alpha.EndAlphaMaskCapture(); }
            if (complete)
            {
                context.PushOpacity(Opacity);
                try { alpha.ApplyAlphaMask(coverage, width, height, absolute, absolute); }
                finally { context.Pop(); }
            }
            blend?.Complete();
        }
        finally { while (states-- > 0) context.Pop(); }
    }
}
