using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Nine-slice CSS border-image painting. The center and edge phases preserve content stacking.</summary>
internal static partial class CssBorderImagePainter
{
    internal static bool Active(FrameworkElement owner, Rect bounds, Thickness border)
    {
        var style = StyleOwner(owner);
        return !CssTableLayout.CollapsedStructure(style) &&
            !(style is FrameworkElement element && CssRubyBoxMetrics.IsStructural(element)) &&
            style.GetValue(CssBorderImageProperties.SourceProperty) is CssBorderImageSource source && source.Resolve(owner, bounds.Size) is not null;
    }
    internal readonly record struct Axis(double Start, double Tile, double Step, int Count);
    internal static Axis Tiles(double start, double extent, double tile, CssBorderImageRepeat repeat)
    {
        if (!(extent > 0 && tile > 0 && double.IsFinite(extent) && double.IsFinite(tile))) return default;
        if (repeat == CssBorderImageRepeat.Stretch) return new(start, extent, extent, 1);
        if (repeat == CssBorderImageRepeat.Round)
        {
            var count = Math.Max(1, Math.Floor(extent / tile + .5));
            var size = double.IsFinite(count) ? extent / count : tile;
            return new(start, size, size, (int)Math.Min(int.MaxValue, count));
        }
        if (repeat == CssBorderImageRepeat.Space)
        {
            var count = Math.Floor(extent / tile);
            if (count < 1) return default;
            if (!double.IsFinite(count)) return new(start, tile, tile, int.MaxValue);
            var gap = (extent - count * tile) / (count + 1);
            return new(start + gap, tile, tile + gap, (int)Math.Min(int.MaxValue, count));
        }
        var remainder = ((extent - tile) / 2) % tile;
        var first = start + (remainder > 0 ? remainder - tile : remainder);
        return new(first, tile, tile, (int)Math.Min(int.MaxValue, Math.Ceiling((start + extent - first) / tile)));
    }

    internal static bool Draw(FrameworkElement owner, DrawingContext context, Rect bounds, Thickness border, bool center)
    {
        if (HasTemplateRoute(owner)) return false;
        return Draw(owner, StyleOwner(owner), context, bounds, border, center);
    }

    private static bool Draw(FrameworkElement owner, DependencyObject styleOwner, DrawingContext context,
        Rect bounds, Thickness border, bool center, Rect? paintClip = null, bool collapsedPass = false)
    {
        if (styleOwner is FrameworkElement ruby && CssRubyBoxMetrics.IsStructural(ruby)) return false;
        if (CssTableLayout.CollapsedStructure(styleOwner)) return false;
        if (styleOwner.GetValue(CssBorderImageProperties.SourceProperty) is not CssBorderImageSource definition) return false;
        if (!collapsedPass && styleOwner is FrameworkElement element && CssTableLayout.CollapsedImageBox(element, ref bounds, ref border) && !center) return false;
        var slices = (CssBorderImageMeasures)styleOwner.GetValue(CssBorderImageProperties.SliceProperty)!;
        var plan = Plan(owner, bounds, border, definition, slices,
            (CssBorderImageMeasures)styleOwner.GetValue(CssBorderImageProperties.WidthProperty)!,
            CssBorderImageProperties.Outset(styleOwner, border),
            (CssBorderImageTiling)styleOwner.GetValue(CssBorderImageProperties.RepeatProperty)!);
        if (plan is null) return false;
        if (center && !slices.Fill) return true;
        var scaling = CssImageRenderingProperties.Resolve(styleOwner, BitmapScalingMode.Unspecified);
        foreach (var part in plan.Regions)
        {
            if (part.Center != center) continue;
            var region = part.Destination; var x = part.X; var y = part.Y;
            if (paintClip is { } visible)
            {
                region.Intersect(visible);
                if (region.IsEmpty || region.Width <= 0 || region.Height <= 0) continue;
                x = VisibleTiles(x, region.Left, region.Right);
                y = VisibleTiles(y, region.Top, region.Bottom);
            }
            if (x.Count == 0 || y.Count == 0) continue;
            var patch = definition.Patch(owner, styleOwner, plan.Brush, plan.Image, part.Source);
            if ((double)x.Count * y.Count > 4096)
            {
                Dense(owner, context, patch, region, x, y, styleOwner);
                continue;
            }
            context.PushClip(new RectangleGeometry(region));
            try
            {
                for (var iy = 0; iy < y.Count; iy++)
                for (var ix = 0; ix < x.Count; ix++)
                {
                    var left = x.Start + ix * x.Step; var top = y.Start + iy * y.Step;
                    context.DrawImage(patch, new(left, top, x.Tile, y.Tile), scaling);
                }
            }
            finally { context.Pop(); }
        }
        return true;
    }

    internal readonly record struct Region(Rect Source, Rect Destination, Axis X, Axis Y, bool Center);
    internal sealed record Layout(Brush Brush, Size Image, Rect Area, Rect Middle, Region[] Regions);

    // One nine-slice layout is shared by colored borders and alpha/luminance masks.
    internal static Layout? Plan(FrameworkElement owner, Rect bounds, Thickness border, CssBorderImageSource definition,
        CssBorderImageMeasures slices, CssBorderImageMeasures widths, Thickness outset, CssBorderImageTiling repeat)
    {
        var area = new Rect(bounds.X - outset.Left, bounds.Y - outset.Top,
            bounds.Width + outset.Left + outset.Right, bounds.Height + outset.Top + outset.Bottom);
        if (!(area.Width > 0 && area.Height > 0 && double.IsFinite(area.Width) && double.IsFinite(area.Height))) return null;
        if (definition.Resolve(owner, area.Size) is not { } source) return null;
        var image = source.Pixels;
        if (!(image.Width > 0 && image.Height > 0)) return null;
        double[] s = [Math.Min(image.Height, slices.At(0, image.Height)), Math.Min(image.Width, slices.At(1, image.Width)),
            Math.Min(image.Height, slices.At(2, image.Height)), Math.Min(image.Width, slices.At(3, image.Width))];
        var b = new[] { border.Top, border.Right, border.Bottom, border.Left };
        var d = new double[4];
        for (var side = 0; side < 4; side++)
        {
            var vertical = side % 2 == 0;
            var dimension = vertical ? image.Height : image.Width;
            var naturalLength = vertical ? source.Intrinsic.Height : source.Intrinsic.Width;
            var natural = naturalLength is { } length ? s[side] * length / dimension : b[side];
            d[side] = widths.At(side, vertical ? area.Height : area.Width, b[side], natural);
        }
        var maximum = d.Max();
        if (maximum > 0)
        {
            var horizontal = d[1] / maximum + d[3] / maximum; var vertical = d[0] / maximum + d[2] / maximum;
            var used = Math.Min(maximum, Math.Min(horizontal > 0 ? area.Width / horizontal : maximum, vertical > 0 ? area.Height / vertical : maximum));
            for (var side = 0; side < 4; side++) d[side] = d[side] / maximum * used;
        }
        double[] sx = [0, s[3], image.Width - s[1]], sy = [0, s[0], image.Height - s[2]];
        double[] sw = [s[3], Math.Max(0, image.Width - s[3] - s[1]), s[1]], sh = [s[0], Math.Max(0, image.Height - s[0] - s[2]), s[2]];
        double[] dx = [area.X, area.X + d[3], area.Right - d[1]], dy = [area.Y, area.Y + d[0], area.Bottom - d[2]];
        double[] dw = [d[3], Math.Max(0, area.Width - d[3] - d[1]), d[1]], dh = [d[0], Math.Max(0, area.Height - d[0] - d[2]), d[2]];
        var regions = new List<Region>(9);
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            if (sw[column] <= 0 || sh[row] <= 0 || dw[column] <= 0 || dh[row] <= 0) continue;
            var xRepeat = column == 1 ? repeat.X : CssBorderImageRepeat.Stretch;
            var yRepeat = row == 1 ? repeat.Y : CssBorderImageRepeat.Stretch;
            var tileWidth = row != 1 ? sw[column] * dh[row] / sh[row] :
                column == 1 ? sw[column] * Scale(d[0], s[0], d[2], s[2]) : dw[column];
            var tileHeight = column != 1 ? sh[row] * dw[column] / sw[column] :
                row == 1 ? sh[row] * Scale(d[3], s[3], d[1], s[1]) : dh[row];
            var x = Tiles(dx[column], dw[column], tileWidth, xRepeat);
            var y = Tiles(dy[row], dh[row], tileHeight, yRepeat);
            var region = new Rect(dx[column], dy[row], dw[column], dh[row]);
            regions.Add(new(new(sx[column], sy[row], sw[column], sh[row]), region, x, y, row == 1 && column == 1));
        }
        return new(source.Brush, image, area, new(dx[1], dy[1], dw[1], dh[1]), regions.ToArray());
    }

    private static double Scale(double firstWidth, double firstSlice, double secondWidth, double secondSlice)
        => firstWidth > 0 && firstSlice > 0 ? firstWidth / firstSlice : secondWidth > 0 && secondSlice > 0 ? secondWidth / secondSlice : 1;

    internal static void Decoration(FrameworkElement owner, DrawingContext context, Rect bounds, Thickness border)
    {
        if (TryInlineFragment(owner, bounds, out var fragment))
        {
            var fullBorder = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Border;
            DecorationFragment(owner, StyleOwner(owner), context, fragment, fullBorder);
            return;
        }
        Draw(owner, context, bounds, border, true);
        Draw(owner, context, bounds, border, false);
    }

    internal static void CollapsedImage(FrameworkElement host, DependencyObject source, DrawingContext context,
        Rect bounds, Thickness border, bool cell, bool center, bool empty = false)
    {
        if (source.GetValue(CssBorderImageProperties.SourceProperty) is null) return;
        if (cell) bounds = new(bounds.X - border.Left, bounds.Y - border.Top, bounds.Width + border.Left + border.Right, bounds.Height + border.Top + border.Bottom);
        var scale = empty ? 1 : 2;
        border = new(border.Left * scale, border.Top * scale, border.Right * scale, border.Bottom * scale);
        Draw(host, source, context, bounds, border, center, collapsedPass: true);
    }

    internal static void EmptyBackground(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement owner || StyleOwner(owner).GetValue(CssBorderImageProperties.SourceProperty) is null) return;
        var property = CssDependencyPropertyLookup.Find(owner.GetType(), "Background");
        if (property is not null && owner.GetValue(property) is Brush) return;
        var border = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Border;
        if (owner.IsCssInlineFrame)
        {
            foreach (var fragment in CssInlineBoxPainter.Fragments(owner)) Decoration(owner, context, fragment.Rect, fragment.Edges(border));
        }
        else if (owner is TextBlock { IsCssInlineBox: true } text)
        {
            foreach (var fragment in text.CssDecorationFragments) DecorationFragment(owner, StyleOwner(owner), context, fragment, border);
        }
        else Decoration(owner, context, new(owner.RenderSize), border);
    }

}
