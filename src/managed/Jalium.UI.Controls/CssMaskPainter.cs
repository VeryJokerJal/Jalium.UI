using System.Runtime.CompilerServices;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Builds an isolated alpha mask and applies it once to the complete visual subtree.</summary>
internal static class CssMaskPainter
{
    private const int MaximumPixels = 16_777_216;
    internal sealed record Raster(Rect Bounds, int Width, int Height, byte[] Alpha);
    private sealed record Key(Rect Bounds, Size Size, Thickness Border, Thickness Padding, CssUsedBorderRadii Radii,
        double Dpi, BitmapScalingMode Scaling, FlowDirection Direction, CssMaskImages Images, CssMaskBorderSource MaskBorder,
        bool CloneDecoration, int FragmentHash);
    private sealed class Cache { internal Key? Key; internal long[] Revisions = []; internal Raster? Raster; }
    private static readonly ConditionalWeakTable<DependencyObject, Cache> Caches = new();

    internal static void Invalidate(DependencyObject owner)
    {
        Caches.Remove(owner);
        if (owner is ContentElement)
        {
            var node = CssNode.Get(owner);
            if (CssMaskProperties.Active(owner))
                node.CssLayout ??= new();
            if (node.VisualOwner is UIElement visual)
            {
                visual.InvalidateMeasure();
                visual.InvalidateVisual();
            }
        }
    }

    internal static IReadOnlyList<CssInlineBoxFragment>? InlineFragments(FrameworkElement owner)
    {
        if (owner.IsCssInlineFrame)
        {
            var list = CssInlineBoxPainter.Fragments(owner);
            return list.Count > 0 ? list : null;
        }
        if (owner is TextBlock { IsCssInlineBox: true } text && text.CssDecorationFragments is { Length: > 0 } fragments)
            return fragments;
        return null;
    }

    internal static CssMaskImages ResolveImages(FrameworkElement owner)
    {
        var images = (CssMaskImages)owner.GetValue(CssMaskProperties.ImagesProperty)!;
        if (images.Active && images.Definitions.Length > 0 && images.Brushes.All(static b => b is null))
        {
            var node = CssNode.Get(owner);
            var context = new CssApplyContext(node, CssEngine.BuildLengthContext(node), new());
            images = CssMaskImages.Materialize(images.Definitions, context);
            owner.SetValue(CssMaskProperties.ImagesProperty, images);
        }
        return images;
    }

    internal sealed class Frame(IAlphaMaskDrawingContext context, Raster raster, Rect capture, Point offset) : IDisposable
    {
        private bool _open = true;
        internal void Complete()
        {
            _open = false;
            context.EndAlphaMaskCapture();
            context.ApplyAlphaMask(raster.Alpha, raster.Width, raster.Height,
                new Rect(raster.Bounds.X + offset.X, raster.Bounds.Y + offset.Y, raster.Bounds.Width, raster.Bounds.Height), capture);
        }
        public void Dispose()
        {
            if (!_open) return;
            _open = false;
            context.EndAlphaMaskCapture(); // Abort: restore the target without painting partial user content.
        }
    }

    internal static Frame? Begin(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement { Visibility: Visibility.Visible } owner ||
            CssContentsLayout.IsContents(owner) || CssContentsLayout.Suppressed(owner) || !CssMaskProperties.Active(owner)) return null;
        if (context is not IAlphaMaskDrawingContext { SupportsAlphaMasks: true } target || context is not IOffsetDrawingContext offset)
        {
            CssDiagnostics.Report("mask-image", CssDiagnosticReason.LossyConversion, null, "Drawing context does not support grouped alpha masks");
            return null;
        }
        var capture = InkBounds(owner);
        if (!(capture.Width > 0 && capture.Height > 0)) return null;
        var images = ResolveImages(owner);
        var clips = (CssMaskList<CssMaskBox>)owner.GetValue(CssMaskProperties.ClipsProperty)!;
        var fragments = InlineFragments(owner);
        Rect defaultBox;
        if (fragments is { Count: > 0 })
        {
            defaultBox = fragments[0].Rect;
            for (var i = 1; i < fragments.Count; i++)
                defaultBox.Union(fragments[i].Rect);
        }
        else
        {
            defaultBox = new Rect(owner.RenderSize);
        }
        var bounds = images.Brushes.Any(brush => brush is ImageBrush { ImageSource: SvgMaskImage }) ||
            Enumerable.Range(0, images.Brushes.Length).Any(i => clips.At(i) == CssMaskBox.NoClip)
            ? capture : defaultBox;
        if (CssMaskBorderProperties.Active(owner))
        {
            var border = CssBoxMetrics.BackgroundInsets(owner, owner.RenderSize.Width).Border;
            var area = CssMaskBorderPainter.Area(owner, border);
            // A mask border is confined to its image area, even when captured
            // descendants or filters extend further. Keep that finite boundary
            // as the raster boundary so inverse bilinear sampling does not fade
            // the last interior pixels into unrelated overflow space.
            if (images.Active) bounds.Intersect(area); else bounds = area;
        }
        var raster = Build(owner, images, bounds, target.AlphaMaskRasterScale, target.AlphaMaskTransformScale);
        var absolute = new Rect(capture.X + offset.Offset.X, capture.Y + offset.Offset.Y, capture.Width, capture.Height);
        target.BeginAlphaMaskCapture(absolute);
        return new(target, raster, absolute, offset.Offset);
    }

    private sealed record FragmentLayer(
        Rect PhysicalRect,
        CssRoundedRectangleGeometry Shape,
        Func<double, double, (double R, double G, double B, double A)>? Sample);

    private static Raster Build(FrameworkElement owner, CssMaskImages images, Rect bounds, double contextScale, double transformScale)
    {
        var (border, padding) = CssBoxMetrics.BackgroundInsets(owner, owner.RenderSize.Width);
        var radii = CssBorderRadiusProperties.Get(owner)?.Resolve(owner.RenderSize) ?? CssBackgroundPainter.CircularRadii(Visual.GetCornerRadius(owner));
        var dpi = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(owner) as Visual ?? owner).DpiScaleY;
        if (!(dpi > 0 && double.IsFinite(dpi))) dpi = 1;
        if (transformScale > 0 && double.IsFinite(transformScale)) dpi *= Math.Max(1, transformScale);
        if (contextScale > 0 && double.IsFinite(contextScale)) dpi = Math.Max(dpi, contextScale);
        if (images.Brushes.Any(brush => brush is ImageBrush { ImageSource: SvgMaskImage }) &&
            bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) &&
            double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom))
        {
            // A referenced SVG mask has its own geometry and may end at the
            // captured ink edge. Include transparent texels outside that edge
            // so transformed bilinear sampling does not extend its last pixel.
            var left = (Math.Floor(bounds.Left * dpi) - 1) / dpi;
            var top = (Math.Floor(bounds.Top * dpi) - 1) / dpi;
            var right = (Math.Ceiling(bounds.Right * dpi) + 1) / dpi;
            var bottom = (Math.Ceiling(bounds.Bottom * dpi) + 1) / dpi;
            bounds = new Rect(left, top, right - left, bottom - top);
        }
        var scaling = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
        var maskBorder = CssMaskBorderPainter.ResolveSource(owner);
        var fragments = InlineFragments(owner);
        var cloneDecoration = CssBoxDecorationBreakProperties.Value(owner) == CssBoxDecorationBreak.Clone;
        var fragmentHash = 0;
        if (fragments is not null)
        {
            foreach (var fragment in fragments)
                fragmentHash = HashCode.Combine(fragmentHash, fragment.Rect, fragment.DecorationRect, fragment.LeftEdge, fragment.RightEdge);
        }
        var key = new Key(bounds, owner.RenderSize, border, padding, radii, dpi, scaling, owner.FlowDirection, images, maskBorder, cloneDecoration, fragmentHash);
        var cache = Caches.GetOrCreateValue(owner); var revisions = images.Resources.Concat(maskBorder.Resources).Select(resource => resource.Revision).ToArray();
        if (cache.Key == key && cache.Revisions.AsSpan().SequenceEqual(revisions) && cache.Raster is { } previous) return previous;
        Raster result;
        if (!(bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)))
            result = new(new Rect(0, 0, 1, 1), 1, 1, [0]);
        else
        {
            // Limit allocation without truncating the painting area or the number of repeated tiles.
            var scale = Math.Min(dpi, Math.Sqrt(MaximumPixels / bounds.Width / bounds.Height));
            var width = Math.Max(1, (int)Math.Min(MaximumPixels, Math.Ceiling(bounds.Width * scale)));
            var height = Math.Max(1, (int)Math.Min(MaximumPixels / width, Math.Ceiling(bounds.Height * scale)));
            var alpha = new float[checked(width * height)]; var outer = new Rect(owner.RenderSize);
            if (!images.Active) Array.Fill(alpha, 1);
            var sizes = (CssMaskList<CssBackgroundSize>)owner.GetValue(CssMaskProperties.SizesProperty)!;
            var repeats = (CssMaskList<CssBackgroundRepeat>)owner.GetValue(CssMaskProperties.RepeatsProperty)!;
            var positions = (CssMaskList<CssBackgroundPosition>)owner.GetValue(CssMaskProperties.PositionsProperty)!;
            var origins = (CssMaskList<CssMaskBox>)owner.GetValue(CssMaskProperties.OriginsProperty)!;
            var clips = (CssMaskList<CssMaskBox>)owner.GetValue(CssMaskProperties.ClipsProperty)!;
            var modes = (CssMaskList<CssMaskMode>)owner.GetValue(CssMaskProperties.ModesProperty)!;
            var composites = (CssMaskList<CssMaskComposite>)owner.GetValue(CssMaskProperties.CompositesProperty)!;
            for (var layer = images.Brushes.Length - 1; layer >= 0; layer--)
            {
                if (!images.Active) break;
                var clip = clips.At(layer);
                var mode = modes.At(layer);
                var composite = composites.At(layer);
                var svg = (images.Brushes[layer] as ImageBrush)?.ImageSource as SvgMaskImage;
                var naturalResolution = (images.Brushes[layer] as ImageBrush)?.CssBackgroundLayout?.NaturalResolution ?? 1;

                FragmentLayer[]? fragmentLayers = null;
                Func<double, double, (double R, double G, double B, double A)>? sample = null;
                CssRoundedRectangleGeometry? shape = null;
                if (fragments is { Count: > 0 } && svg is null)
                {
                    fragmentLayers = new FragmentLayer[fragments.Count];
                    for (var f = 0; f < fragments.Count; f++)
                    {
                        var fragment = fragments[f];
                        var fragBorder = fragment.Edges(border);
                        var fragPadding = fragment.Edges(padding);
                        var fragInset = Insets(origins.At(layer), fragBorder, fragPadding);
                        var fragLayout = new CssBackgroundImageLayout(sizes.At(layer), repeats.At(layer), positions.At(layer), fragInset, naturalResolution);
                        var fragSample = Sampler(images.Brushes[layer], fragLayout, fragment.DecorationRect, fragInset, scaling, scale);
                        var fragClipInset = Insets(clip, fragBorder, fragPadding);
                        var fragShape = new CssRoundedRectangleGeometry(Inner(fragment.Rect, fragClipInset), fragment.Radii(owner).Inset(fragClipInset));
                        fragmentLayers[f] = new(fragment.Rect, fragShape, fragSample);
                    }
                }
                else
                {
                    var inset = Insets(origins.At(layer), border, padding);
                    var layout = new CssBackgroundImageLayout(sizes.At(layer), repeats.At(layer), positions.At(layer), inset, naturalResolution);
                    sample = Sampler(images.Brushes[layer], layout, outer, inset, scaling, scale);
                    var clipInset = Insets(clip, border, padding);
                    shape = new CssRoundedRectangleGeometry(Inner(outer, clipInset), radii.Inset(clipInset));
                }

                var binding = svg is null ? null : new SvgMaskBinding(svg.Definition, outer, outer.Size, mode);
                var dependencies = new HashSet<ImageSource>(ReferenceEqualityComparer.Instance);
                var svgPixels = binding?.Rasterize(bounds, width, height, dependencies);
                svg?.Observe(dependencies);
                var halfX = bounds.Width / width / 4; var halfY = bounds.Height / height / 4;
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var px = bounds.X + (x + .5) * bounds.Width / width; var py = bounds.Y + (y + .5) * bounds.Height / height;
                    double source;
                    if (fragmentLayers is not null)
                    {
                        source = 0;
                        for (var f = 0; f < fragmentLayers.Length; f++)
                        {
                            var fl = fragmentLayers[f];
                            if (px + halfX < fl.PhysicalRect.Left || px - halfX > fl.PhysicalRect.Right ||
                                py + halfY < fl.PhysicalRect.Top || py - halfY > fl.PhysicalRect.Bottom)
                                continue;
                            var fragColor = fl.Sample?.Invoke(px, py) ?? default;
                            var fragSource = fragColor.A;
                            if (mode == CssMaskMode.Luminance) fragSource *= .2125 * fragColor.R + .7154 * fragColor.G + .0721 * fragColor.B;
                            if (clip != CssMaskBox.NoClip)
                            {
                                var hits = 0;
                                if (fl.Shape.FillContains(new Point(px - halfX, py - halfY))) hits++;
                                if (fl.Shape.FillContains(new Point(px + halfX, py - halfY))) hits++;
                                if (fl.Shape.FillContains(new Point(px - halfX, py + halfY))) hits++;
                                if (fl.Shape.FillContains(new Point(px + halfX, py + halfY))) hits++;
                                fragSource *= hits / 4d;
                            }
                            if (fragSource > source) source = fragSource;
                        }
                    }
                    else
                    {
                        var color = sample?.Invoke(px, py) ?? default;
                        source = color.A;
                        if (mode == CssMaskMode.Luminance) source *= .2125 * color.R + .7154 * color.G + .0721 * color.B;
                        if (svg is not null)
                        {
                            var at = (y * width + x) * 4;
                            source = svgPixels is null ? 0 : binding!.Alpha(svgPixels[at + 2], svgPixels[at + 1], svgPixels[at], svgPixels[at + 3]);
                        }
                        if (svg is null && clip != CssMaskBox.NoClip && shape is not null)
                        {
                            var hits = 0;
                            if (shape.FillContains(new Point(px - halfX, py - halfY))) hits++;
                            if (shape.FillContains(new Point(px + halfX, py - halfY))) hits++;
                            if (shape.FillContains(new Point(px - halfX, py + halfY))) hits++;
                            if (shape.FillContains(new Point(px + halfX, py + halfY))) hits++;
                            source *= hits / 4d;
                        }
                    }
                    var index = y * width + x; var destination = alpha[index];
                    alpha[index] = (float)Math.Clamp(layer == images.Brushes.Length - 1 ? source : composite switch
                    {
                        CssMaskComposite.Subtract => source * (1 - destination),
                        CssMaskComposite.Intersect => source * destination,
                        CssMaskComposite.Exclude => source * (1 - destination) + destination * (1 - source),
                        _ => source + destination * (1 - source),
                    }, 0, 1);
                }
            }
            if (maskBorder.Active)
            {
                var sample = CssMaskBorderPainter.Sampler(owner, border, scale);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    alpha[y * width + x] *= (float)sample(bounds.X + (x + .5) * bounds.Width / width,
                        bounds.Y + (y + .5) * bounds.Height / height);
            }
            result = new(bounds, width, height, alpha.Select(value => (byte)Math.Clamp(Math.Round(value * 255), 0, 255)).ToArray());
        }
        cache.Key = key; cache.Revisions = revisions; cache.Raster = result; return result;
    }

    private static Func<double, double, (double R, double G, double B, double A)>? Sampler(Brush? brush,
        CssBackgroundImageLayout layout, Rect outer, Thickness inset, BitmapScalingMode scaling, double scale)
    {
        if (brush is null) return null;
        if (brush.CssImageLayout is { } generated)
        {
            if (!generated.IsValid) return null;
            var area = Inner(outer, inset);
            var pattern = layout.WithPositioningInsets(default).TilePattern(area, generated.IntrinsicSize(area.Size));
            var tile = pattern.ImageRect;
            if (!(tile.Width > 0 && tile.Height > 0) || generated.Resolve(tile.Width, tile.Height) is not { } resolved) return null;
            var sample = SoftwareDrawingContext.CssMaskSampler(resolved, new Rect(tile.Size), scale);
            if (sample is null) return null;
            return (x, y) => pattern.X.TryMap(x, out var sx) && pattern.Y.TryMap(y, out var sy) ? sample(sx, sy) : default;
        }
        if (brush is not ImageBrush image) return SoftwareDrawingContext.CssMaskSampler(brush, outer, scale);
        if (image.ImageSource is null) return null;
        var copy = new ImageBrush(image.ImageSource)
        {
            IsTransientPaintCopy = true, Stretch = Stretch.Fill, CssBackgroundLayout = layout,
            ScalingMode = image.ScalingMode == BitmapScalingMode.Unspecified ? scaling : image.ScalingMode,
            Opacity = image.Opacity,
        };
        return SoftwareDrawingContext.CssMaskSampler(copy, outer, scale);
    }

    private static Thickness Insets(CssMaskBox box, Thickness border, Thickness padding) => box switch
    {
        CssMaskBox.Padding => border,
        CssMaskBox.Content or CssMaskBox.Fill => new(border.Left + padding.Left, border.Top + padding.Top,
            border.Right + padding.Right, border.Bottom + padding.Bottom),
        _ => default,
    };
    private static Rect Inner(Rect outer, Thickness inset) => new(outer.X + inset.Left, outer.Y + inset.Top,
        Math.Max(0, outer.Width - inset.Left - inset.Right), Math.Max(0, outer.Height - inset.Top - inset.Bottom));

    internal static Rect InkBounds(Visual visual)
    {
        // Postorder traversal keeps overflow at any depth and avoids recursion
        // on a deeply nested authored tree. Transforms stay in each parent's
        // coordinate system; applying a final world-space union would inflate it.
        var pending = new Stack<(Visual Visual, bool Visited)>();
        var boundsByVisual = new Dictionary<Visual, Rect>();
        var scheduled = new HashSet<Visual> { visual };
        pending.Push((visual, false));
        while (pending.TryPop(out var item))
        {
            if (!item.Visited)
            {
                pending.Push((item.Visual, true));
                for (var i = item.Visual.InternalVisualChildrenCount - 1; i >= 0; i--)
                    if (item.Visual.InternalGetVisualChild(i) is { } child &&
                        child is not UIElement { Visibility: not Visibility.Visible } && scheduled.Add(child))
                        pending.Push((child, false));
                continue;
            }
            var bounds = item.Visual is UIElement element ? new Rect(element.RenderSize) : Rect.Empty;
            if (item.Visual is FrameworkElement frame)
            {
                var padding = frame.Effect?.EffectPadding ?? default;
                var extra = Math.Max(0, frame.GetExtraDirtyPadding());
                bounds = new Rect(-padding.Left - extra, -padding.Top - extra,
                    frame.RenderSize.Width + padding.Left + padding.Right + extra * 2,
                    frame.RenderSize.Height + padding.Top + padding.Bottom + extra * 2);
                if (InlineFragments(frame) is { } fragments)
                {
                    foreach (var fragment in fragments)
                        bounds.Union(fragment.Rect);
                }
                if (CssMaskBorderProperties.Active(frame))
                {
                    var border = CssBoxMetrics.BackgroundInsets(frame, frame.RenderSize.Width).Border;
                    bounds.Union(CssMaskBorderPainter.Area(frame, border));
                }
            }
            for (var i = 0; i < item.Visual.InternalVisualChildrenCount; i++)
            {
                if (item.Visual.InternalGetVisualChild(i) is not { } child ||
                    !boundsByVisual.Remove(child, out var content)) continue;
                if (child is UIElement control)
                {
                    if (control.RenderTransform is { } transform)
                    {
                        var origin = new Point(control.RenderSize.Width * control.RenderTransformOrigin.X,
                            control.RenderSize.Height * control.RenderTransformOrigin.Y);
                        var matrix = Matrix.Multiply(new Matrix(1, 0, 0, 1, -origin.X, -origin.Y), transform.Value);
                        matrix = Matrix.Multiply(matrix, new Matrix(1, 0, 0, 1, origin.X, origin.Y));
                        content = new MatrixTransform(matrix).TransformBounds(content);
                    }
                    var offset = VisualTreeHelper.GetOffset(control);
                    content.Offset(offset.X, offset.Y);
                }
                bounds.Union(content);
            }
            boundsByVisual[item.Visual] = bounds;
        }
        return boundsByVisual[visual];
    }
}
