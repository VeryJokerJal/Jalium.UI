using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal static class ControlFocusVisual
{
    private static readonly SolidColorBrush s_defaultOuterBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultInnerBrush = new(Color.FromArgb(0xB3, 0x00, 0x00, 0x00));

    public static void Draw(
        DrawingContext dc,
        FrameworkElement owner,
        Rect bounds,
        CornerRadius cornerRadius,
        double outerThickness = 1.0,
        double innerThickness = 1.0,
        double innerInset = 2.0)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var outerBrush = ResolveOuterBrush(owner);
        DrawOutline(dc, outerBrush, outerThickness, bounds, cornerRadius);

        if (innerInset <= 0)
        {
            return;
        }

        var innerBounds = new Rect(
            bounds.X + innerInset,
            bounds.Y + innerInset,
            Math.Max(0, bounds.Width - (innerInset * 2)),
            Math.Max(0, bounds.Height - (innerInset * 2)));
        if (innerBounds.Width <= 0 || innerBounds.Height <= 0)
        {
            return;
        }

        var innerCornerRadius = ControlRenderGeometry.InsetCornerRadius(cornerRadius, innerInset);
        var innerBrush = ResolveInnerBrush(owner);
        DrawOutline(dc, innerBrush, innerThickness, innerBounds, innerCornerRadius);
    }

    private static void DrawOutline(
        DrawingContext dc,
        Brush brush,
        double thickness,
        Rect bounds,
        CornerRadius cornerRadius)
    {
        if (thickness <= 0)
        {
            return;
        }

        var pen = new Pen(brush, thickness);
        var rect = ControlRenderGeometry.GetStrokeAlignedRect(bounds, thickness);
        var radius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, thickness);

        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomRight > 0 || radius.BottomLeft > 0)
        {
            dc.DrawRoundedRectangle(null, pen, rect, radius);
        }
        else
        {
            dc.DrawRectangle(null, pen, rect);
        }
    }

    private static Brush ResolveOuterBrush(FrameworkElement owner)
    {
        return owner.TryFindResource("FocusStrokeColorOuterBrush") as Brush
            ?? owner.TryFindResource("ControlBorderFocused") as Brush
            ?? s_defaultOuterBrush;
    }

    private static Brush ResolveInnerBrush(FrameworkElement owner)
    {
        return owner.TryFindResource("FocusStrokeColorInnerBrush") as Brush
            ?? owner.TryFindResource("ControlBorderFocused") as Brush
            ?? s_defaultInnerBrush;
    }
}
