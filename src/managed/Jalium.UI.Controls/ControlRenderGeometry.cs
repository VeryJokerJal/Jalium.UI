namespace Jalium.UI.Controls;

internal static class ControlRenderGeometry
{
    public static Rect GetStrokeAlignedRect(Rect bounds, double strokeThickness)
    {
        if (!double.IsFinite(strokeThickness) || strokeThickness <= 0)
        {
            return bounds;
        }

        var inset = strokeThickness / 2.0;
        return new Rect(
            bounds.X + inset,
            bounds.Y + inset,
            Math.Max(0, bounds.Width - strokeThickness),
            Math.Max(0, bounds.Height - strokeThickness));
    }

    public static CornerRadius GetStrokeAlignedCornerRadius(CornerRadius cornerRadius, double strokeThickness)
    {
        if (!double.IsFinite(strokeThickness) || strokeThickness <= 0)
        {
            return cornerRadius;
        }

        var inset = strokeThickness / 2.0;
        return new CornerRadius(
            Math.Max(0, cornerRadius.TopLeft - inset),
            Math.Max(0, cornerRadius.TopRight - inset),
            Math.Max(0, cornerRadius.BottomRight - inset),
            Math.Max(0, cornerRadius.BottomLeft - inset));
    }

    public static CornerRadius InsetCornerRadius(CornerRadius cornerRadius, double inset)
    {
        if (!double.IsFinite(inset) || inset <= 0)
        {
            return cornerRadius;
        }

        return new CornerRadius(
            Math.Max(0, cornerRadius.TopLeft - inset),
            Math.Max(0, cornerRadius.TopRight - inset),
            Math.Max(0, cornerRadius.BottomRight - inset),
            Math.Max(0, cornerRadius.BottomLeft - inset));
    }
}
