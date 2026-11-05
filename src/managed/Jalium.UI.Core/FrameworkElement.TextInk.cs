namespace Jalium.UI;

public partial class FrameworkElement
{
    private double _descendantTextInkPadding;
    internal virtual double CssTextInkPadding => System.Threading.Volatile.Read(ref _descendantTextInkPadding);

    // Compositor-only movement does not run ArrangeCore. Update the mirrored
    // ancestor bounds on that UI-thread path too, without invalidating layout.
    internal void RefreshCssTextInkAncestors()
    {
        if (CssTextInkPadding <= 0) return;
        for (var current = VisualParent; current is not null; current = current.VisualParent)
        {
            if (current is not FrameworkElement parent) continue;
            var previous = parent.CssTextInkPadding;
            parent.UpdateCssTextInkBounds();
            if (previous == parent.CssTextInkPadding) break;
        }
    }

    // Children are already arranged. Forward only text ink that actually leaves
    // this box, so a zero-height inline ancestor cannot cull visible glyphs.
    // This is one pass over immediate children at layout time; background dirty
    // registration and the rendering walk read the mirrored scalar only.
    private void UpdateCssTextInkBounds()
    {
        var padding = 0d;
        for (var index = 0; index < VisualChildrenCount; index++)
        {
            if (GetVisualChild(index) is not FrameworkElement child || child.Visibility != Visibility.Visible) continue;
            var extra = child.CssTextInkPadding;
            if (extra <= 0) continue;
            var transform = child.GetRenderMatrixTo(this);
            var size = child.RenderSize;
            var a = transform.Transform(new Point(-extra, -extra));
            var b = transform.Transform(new Point(size.Width + extra, -extra));
            var c = transform.Transform(new Point(-extra, size.Height + extra));
            var d = transform.Transform(new Point(size.Width + extra, size.Height + extra));
            var offset = child.RenderOffset;
            var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) + offset.X;
            var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + offset.X;
            var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) + offset.Y;
            var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + offset.Y;
            if (!double.IsFinite(left + right + top + bottom)) continue;
            padding = Math.Max(padding, Math.Max(Math.Max(-left, right - RenderSize.Width), Math.Max(-top, bottom - RenderSize.Height)));
        }
        System.Threading.Volatile.Write(ref _descendantTextInkPadding, Math.Max(0, padding));
    }
}
