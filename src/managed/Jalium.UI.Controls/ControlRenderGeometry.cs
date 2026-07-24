namespace Jalium.UI.Controls;

internal static class ControlRenderGeometry
{
    /// <summary>
    /// Clamps a requested render length to the finite, non-negative portion of the
    /// available axis. Layout can legitimately allocate less space than a control's
    /// chrome while a window is being resized; drawing geometry must collapse in that
    /// case instead of constructing a negative <see cref="Rect"/>.
    /// </summary>
    public static double GetAvailableLength(double requestedLength, double availableLength)
    {
        if (!double.IsFinite(requestedLength) || requestedLength <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(availableLength))
        {
            return requestedLength;
        }

        if (!double.IsFinite(availableLength) || availableLength <= 0)
        {
            return 0;
        }

        return Math.Min(requestedLength, availableLength);
    }

    /// <summary>
    /// Returns the portion of <paramref name="bounds"/> left after reserving control
    /// chrome on each edge. Insets are consumed from the leading edge first and the
    /// result deterministically collapses to zero when the chrome is larger than the
    /// allocated surface.
    /// </summary>
    public static Rect GetContentRect(Rect bounds, Thickness insets)
    {
        if (bounds.IsEmpty)
        {
            return Rect.Empty;
        }

        var width = NormalizeLength(bounds.Width);
        var height = NormalizeLength(bounds.Height);
        var left = GetAvailableLength(NormalizeInset(insets.Left), width);
        var top = GetAvailableLength(NormalizeInset(insets.Top), height);
        var right = GetAvailableLength(NormalizeInset(insets.Right), width - left);
        var bottom = GetAvailableLength(NormalizeInset(insets.Bottom), height - top);

        return new Rect(
            bounds.X + left,
            bounds.Y + top,
            width - left - right,
            height - top - bottom);
    }

    /// <summary>
    /// Returns a trailing horizontal slice whose width never exceeds its bounds.
    /// </summary>
    public static Rect GetTrailingRect(Rect bounds, double requestedWidth)
    {
        if (bounds.IsEmpty)
        {
            return Rect.Empty;
        }

        var width = NormalizeLength(bounds.Width);
        var height = NormalizeLength(bounds.Height);
        var sliceWidth = GetAvailableLength(requestedWidth, width);
        return new Rect(bounds.X + width - sliceWidth, bounds.Y, sliceWidth, height);
    }

    /// <summary>
    /// Gets the usable axis length between the centers of two end thumbs.
    /// </summary>
    public static double GetTrackLength(double axisLength, double thumbLength)
        => Math.Max(0, NormalizeLength(axisLength) - NormalizeLength(thumbLength));

    /// <summary>
    /// Creates a centered horizontal or vertical track that remains valid even when
    /// the surface is smaller than its thumb or requested thickness.
    /// </summary>
    public static Rect GetCenteredTrackRect(
        Rect bounds,
        Orientation orientation,
        double thumbLength,
        double requestedThickness)
    {
        if (bounds.IsEmpty)
        {
            return Rect.Empty;
        }

        var width = NormalizeLength(bounds.Width);
        var height = NormalizeLength(bounds.Height);

        if (orientation == Orientation.Horizontal)
        {
            var trackLength = GetTrackLength(width, thumbLength);
            var thickness = GetAvailableLength(requestedThickness, height);
            return new Rect(
                bounds.X + (width - trackLength) / 2.0,
                bounds.Y + (height - thickness) / 2.0,
                trackLength,
                thickness);
        }

        var verticalTrackLength = GetTrackLength(height, thumbLength);
        var verticalThickness = GetAvailableLength(requestedThickness, width);
        return new Rect(
            bounds.X + (width - verticalThickness) / 2.0,
            bounds.Y + (height - verticalTrackLength) / 2.0,
            verticalThickness,
            verticalTrackLength);
    }

    public static Rect GetStrokeAlignedRect(Rect bounds, double strokeThickness)
    {
        if (!double.IsFinite(strokeThickness) || strokeThickness <= 0)
        {
            return bounds;
        }

        var inset = strokeThickness / 2.0;
        return GetContentRect(bounds, new Thickness(inset));
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

    private static double NormalizeLength(double value)
        => double.IsFinite(value) && value > 0 ? value : 0;

    private static double NormalizeInset(double value)
        => double.IsFinite(value) && value > 0 ? value : 0;
}
