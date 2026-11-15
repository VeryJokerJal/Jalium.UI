namespace Jalium.UI.Controls;

/// <summary>Shared docking target geometry and hit testing. Appearance belongs to DockIndicatorVisual templates.</summary>
internal static class DockIndicator
{
    private const double ButtonSize = 32;
    private const double ButtonGap = 4;
    private const double CrossArmLength = ButtonSize + ButtonGap;
    private const double EdgeButtonSize = 32;

    internal static Rect GetCenterButtonRect(double panelWidth, double panelHeight, DockPosition position)
    {
        var cx = panelWidth / 2;
        var cy = panelHeight / 2;
        var half = ButtonSize / 2;

        return position switch
        {
            DockPosition.Center => new Rect(cx - half, cy - half, ButtonSize, ButtonSize),
            DockPosition.Top => new Rect(cx - half, cy - half - CrossArmLength, ButtonSize, ButtonSize),
            DockPosition.Bottom => new Rect(cx - half, cy + half + ButtonGap, ButtonSize, ButtonSize),
            DockPosition.Left => new Rect(cx - half - CrossArmLength, cy - half, ButtonSize, ButtonSize),
            DockPosition.Right => new Rect(cx + half + ButtonGap, cy - half, ButtonSize, ButtonSize),
            _ => default,
        };
    }

    internal static Rect GetEdgeButtonRect(double layoutWidth, double layoutHeight, DockPosition position)
    {
        var half = EdgeButtonSize / 2;
        return position switch
        {
            DockPosition.EdgeTop => new Rect(layoutWidth / 2 - half, 10, EdgeButtonSize, EdgeButtonSize),
            DockPosition.EdgeBottom => new Rect(layoutWidth / 2 - half, layoutHeight - 10 - EdgeButtonSize, EdgeButtonSize, EdgeButtonSize),
            DockPosition.EdgeLeft => new Rect(10, layoutHeight / 2 - half, EdgeButtonSize, EdgeButtonSize),
            DockPosition.EdgeRight => new Rect(layoutWidth - 10 - EdgeButtonSize, layoutHeight / 2 - half, EdgeButtonSize, EdgeButtonSize),
            _ => default,
        };
    }

    internal static DockPosition HitTestCenter(double panelWidth, double panelHeight, Point localPoint)
    {
        DockPosition[] positions = [DockPosition.Center, DockPosition.Top, DockPosition.Bottom, DockPosition.Left, DockPosition.Right];
        foreach (var pos in positions)
        {
            var rect = GetCenterButtonRect(panelWidth, panelHeight, pos);
            if (rect.Contains(localPoint))
                return pos;
        }
        return DockPosition.None;
    }

    internal static DockPosition HitTestEdge(double layoutWidth, double layoutHeight, Point localPoint)
    {
        DockPosition[] positions = [DockPosition.EdgeTop, DockPosition.EdgeBottom, DockPosition.EdgeLeft, DockPosition.EdgeRight];
        foreach (var pos in positions)
        {
            var rect = GetEdgeButtonRect(layoutWidth, layoutHeight, pos);
            if (rect.Contains(localPoint))
                return pos;
        }
        return DockPosition.None;
    }

}
