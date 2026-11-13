using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssMaskBorderPainter
{
    private sealed record Patch(Rect Region, CssBorderImagePainter.Axis X, CssBorderImagePainter.Axis Y,
        Func<double, double, (double R, double G, double B, double A)> Sample);

    internal static CssMaskBorderSource ResolveSource(FrameworkElement owner)
    {
        var mask = (CssMaskBorderSource)owner.GetValue(CssMaskBorderProperties.SourceProperty)!;
        if (mask.Active && mask.Image is null && mask.Definition is not null)
        {
            var node = CssNode.Get(owner);
            var context = new CssApplyContext(node, CssEngine.BuildLengthContext(node), new());
            mask = CssMaskBorderSource.Materialize(mask.Definition, context);
            owner.SetValue(CssMaskBorderProperties.SourceProperty, mask);
        }
        return mask;
    }

    private static Thickness Outset(FrameworkElement owner, Thickness border)
    {
        var value = (CssBorderImageMeasures)owner.GetValue(CssMaskBorderProperties.OutsetProperty)!;
        return new(value.At(3, 0, border.Left), value.At(0, 0, border.Top),
            value.At(1, 0, border.Right), value.At(2, 0, border.Bottom));
    }

    internal static Rect Area(FrameworkElement owner, Thickness border)
    {
        var edges = Outset(owner, border);
        if (CssMaskPainter.InlineFragments(owner) is { Count: > 0 } fragments)
        {
            Rect union = Rect.Empty;
            foreach (var fragment in fragments)
            {
                var left = fragment.LeftEdge ? edges.Left : 0;
                var right = fragment.RightEdge ? edges.Right : 0;
                var rect = new Rect(
                    fragment.Rect.X - left,
                    fragment.Rect.Y - edges.Top,
                    fragment.Rect.Width + left + right,
                    fragment.Rect.Height + edges.Top + edges.Bottom);
                union.Union(rect);
            }
            return union;
        }
        return new(-edges.Left, -edges.Top, owner.RenderSize.Width + edges.Left + edges.Right,
            owner.RenderSize.Height + edges.Top + edges.Bottom);
    }

    internal static Func<double, double, double> Sampler(FrameworkElement owner, Thickness border, double scale)
    {
        var mask = ResolveSource(owner);
        if (!mask.Active)
            return (_, _) => 1;
        if (mask.ResolveImage(owner) is not { } image) return (_, _) => 0;
        var slices = (CssBorderImageMeasures)owner.GetValue(CssMaskBorderProperties.SliceProperty)!;
        var widths = (CssBorderImageMeasures)owner.GetValue(CssMaskBorderProperties.WidthProperty)!;
        var repeat = (CssBorderImageTiling)owner.GetValue(CssMaskBorderProperties.RepeatProperty)!;
        var edges = Outset(owner, border);
        var scaling = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
        var luminance = (CssMaskMode)owner.GetValue(CssMaskBorderProperties.ModeProperty)! == CssMaskMode.Luminance;

        Func<double, double, double>? BuildPlanSampler(Rect targetRect, Thickness planOutset, Rect? clipRect)
        {
            var plan = CssBorderImagePainter.Plan(owner, targetRect, border, image, slices, widths, planOutset, repeat);
            if (plan is null) return null;
            var patches = new List<Patch>();
            foreach (var part in plan.Regions)
            {
                if (part.Center && !slices.Fill || part.X.Count == 0 || part.Y.Count == 0) continue;
                var pixels = image.Patch(owner, owner, plan.Brush, plan.Image, part.Source);
                var brush = new ImageBrush(pixels) { Stretch = Stretch.Fill, ScalingMode = scaling, IsTransientPaintCopy = true };
                var sample = SoftwareDrawingContext.CssMaskSampler(brush, new Rect(0, 0, part.X.Tile, part.Y.Tile), scale);
                if (sample is not null) patches.Add(new(part.Destination, part.X, part.Y, sample));
            }
            return (x, y) =>
            {
                if (clipRect is { } clip && !Contains(clip, x, y)) return 0;
                if (!slices.Fill && Contains(plan.Middle, x, y)) return 1;
                foreach (var patch in patches)
                {
                    if (!Contains(patch.Region, x, y) || CssBorderImagePainter.Coordinate(x, patch.X) is not { } u ||
                        CssBorderImagePainter.Coordinate(y, patch.Y) is not { } v) continue;
                    var color = patch.Sample(u * patch.X.Tile, v * patch.Y.Tile);
                    return Math.Clamp(color.A * (luminance ? .2125 * color.R + .7154 * color.G + .0721 * color.B : 1), 0, 1);
                }
                return 0;
            };
        }

        if (CssMaskPainter.InlineFragments(owner) is { Count: > 0 } fragments)
        {
            var fragmentSamplers = new List<(Rect Clip, Func<double, double, double> Sample)>(fragments.Count);
            foreach (var fragment in fragments)
            {
                var left = fragment.LeftEdge ? edges.Left : 0;
                var right = fragment.RightEdge ? edges.Right : 0;
                var clip = new Rect(
                    fragment.Rect.X - left,
                    fragment.Rect.Y - edges.Top,
                    fragment.Rect.Width + left + right,
                    fragment.Rect.Height + edges.Top + edges.Bottom);
                var sampler = BuildPlanSampler(fragment.DecorationRect, edges, clip);
                if (sampler is not null)
                    fragmentSamplers.Add((clip, sampler));
            }
            if (fragmentSamplers.Count == 0) return (_, _) => 0;
            return (x, y) =>
            {
                double best = 0;
                foreach (var (clip, sampler) in fragmentSamplers)
                {
                    if (!Contains(clip, x, y)) continue;
                    var val = sampler(x, y);
                    if (val > best) best = val;
                }
                return best;
            };
        }

        return BuildPlanSampler(new Rect(owner.RenderSize), edges, null) ?? ((_, _) => 0);
    }

    private static bool Contains(Rect area, double x, double y) =>
        x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;
}
