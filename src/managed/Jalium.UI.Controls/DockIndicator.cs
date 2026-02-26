using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides rendering and hit-testing for modern dock indicator buttons.
/// Uses a dark semi-transparent theme consistent with Fluent / WinUI 3 style.
/// Renders a center cross (5 buttons) over a target panel and edge buttons on the root layout.
/// </summary>
internal static class DockIndicator
{
    private const double ButtonSize = 32;
    private const double ButtonGap = 4;
    private const double CrossArmLength = ButtonSize + ButtonGap;
    private const double EdgeButtonSize = 32;
    private const double CornerRadius = 6;

    // Dark theme colors for indicator chrome
    private static readonly Color ChromeBg = Color.FromArgb(230, 32, 32, 32);
    private static readonly Color ChromeBorder = Color.FromArgb(180, 70, 70, 70);
    private static readonly Color ButtonBg = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color ButtonBorder = Color.FromArgb(160, 80, 80, 80);
    private static readonly Color ButtonHoverBg = Color.FromArgb(255, 0, 120, 212); // Accent
    private static readonly Color IconNormal = Color.FromArgb(220, 200, 200, 200);
    private static readonly Color IconHover = Color.FromRgb(255, 255, 255);

    #region Hit-Testing

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

    #endregion

    #region Rendering — Center Cross

    internal static void RenderCenterCross(DrawingContext dc, double panelWidth, double panelHeight, DockPosition hoveredPosition)
    {
        DrawCrossBackground(dc, panelWidth, panelHeight);

        DockPosition[] positions = [DockPosition.Top, DockPosition.Left, DockPosition.Center, DockPosition.Right, DockPosition.Bottom];
        foreach (var pos in positions)
        {
            var rect = GetCenterButtonRect(panelWidth, panelHeight, pos);
            DrawButton(dc, rect, pos, pos == hoveredPosition);
        }
    }

    private static void DrawCrossBackground(DrawingContext dc, double panelWidth, double panelHeight)
    {
        var cx = panelWidth / 2;
        var cy = panelHeight / 2;
        var half = ButtonSize / 2;
        var pad = 5.0;

        var bgBrush = new SolidColorBrush(ChromeBg);
        var borderPen = new Pen(new SolidColorBrush(ChromeBorder), 1);

        // Horizontal bar: Left + Center + Right
        var hRect = new Rect(
            cx - half - CrossArmLength - pad,
            cy - half - pad,
            ButtonSize * 3 + ButtonGap * 2 + pad * 2,
            ButtonSize + pad * 2);
        dc.DrawRoundedRectangle(bgBrush, borderPen, hRect, CornerRadius, CornerRadius);

        // Vertical bar: Top + Center + Bottom
        var vRect = new Rect(
            cx - half - pad,
            cy - half - CrossArmLength - pad,
            ButtonSize + pad * 2,
            ButtonSize * 3 + ButtonGap * 2 + pad * 2);
        dc.DrawRoundedRectangle(bgBrush, borderPen, vRect, CornerRadius, CornerRadius);
    }

    #endregion

    #region Rendering — Edge Buttons

    internal static void RenderEdgeButtons(DrawingContext dc, double layoutWidth, double layoutHeight, DockPosition hoveredPosition)
    {
        DockPosition[] positions = [DockPosition.EdgeTop, DockPosition.EdgeBottom, DockPosition.EdgeLeft, DockPosition.EdgeRight];
        foreach (var pos in positions)
        {
            var rect = GetEdgeButtonRect(layoutWidth, layoutHeight, pos);

            // Pill-shaped background
            var bgBrush = new SolidColorBrush(ChromeBg);
            var borderPen = new Pen(new SolidColorBrush(ChromeBorder), 1);
            var bgRect = new Rect(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
            dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, CornerRadius, CornerRadius);

            DrawButton(dc, rect, pos, pos == hoveredPosition);
        }
    }

    #endregion

    #region Rendering — Button

    private static void DrawButton(DrawingContext dc, Rect rect, DockPosition position, bool isHovered)
    {
        var bgBrush = isHovered
            ? new SolidColorBrush(ButtonHoverBg)
            : new SolidColorBrush(ButtonBg);
        var borderPen = new Pen(new SolidColorBrush(isHovered ? ButtonHoverBg : ButtonBorder), 1);

        dc.DrawRoundedRectangle(bgBrush, borderPen, rect, 4, 4);

        var iconColor = isHovered ? IconHover : IconNormal;
        DrawDockIcon(dc, rect, position, iconColor);
    }

    private static void DrawDockIcon(DrawingContext dc, Rect rect, DockPosition position, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.2);
        var fillBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));
        var m = 7.0;
        var iconRect = new Rect(rect.X + m, rect.Y + m, rect.Width - m * 2, rect.Height - m * 2);

        // Window frame outline
        dc.DrawRoundedRectangle(null, pen, iconRect, 1.5, 1.5);

        // Filled dock region
        var canonicalPos = ToCanonicalPosition(position);
        switch (canonicalPos)
        {
            case DockPosition.Left:
            {
                var w = iconRect.Width * 0.4;
                var fill = new Rect(iconRect.X, iconRect.Y, w, iconRect.Height);
                dc.DrawRectangle(fillBrush, null, fill);
                dc.DrawLine(pen, new Point(iconRect.X + w, iconRect.Y), new Point(iconRect.X + w, iconRect.Bottom));
                break;
            }
            case DockPosition.Right:
            {
                var w = iconRect.Width * 0.4;
                var fill = new Rect(iconRect.Right - w, iconRect.Y, w, iconRect.Height);
                dc.DrawRectangle(fillBrush, null, fill);
                dc.DrawLine(pen, new Point(iconRect.Right - w, iconRect.Y), new Point(iconRect.Right - w, iconRect.Bottom));
                break;
            }
            case DockPosition.Top:
            {
                var h = iconRect.Height * 0.4;
                var fill = new Rect(iconRect.X, iconRect.Y, iconRect.Width, h);
                dc.DrawRectangle(fillBrush, null, fill);
                dc.DrawLine(pen, new Point(iconRect.X, iconRect.Y + h), new Point(iconRect.Right, iconRect.Y + h));
                break;
            }
            case DockPosition.Bottom:
            {
                var h = iconRect.Height * 0.4;
                var fill = new Rect(iconRect.X, iconRect.Bottom - h, iconRect.Width, h);
                dc.DrawRectangle(fillBrush, null, fill);
                dc.DrawLine(pen, new Point(iconRect.X, iconRect.Bottom - h), new Point(iconRect.Right, iconRect.Bottom - h));
                break;
            }
            case DockPosition.Center:
            {
                dc.DrawRectangle(fillBrush, null, iconRect);
                break;
            }
        }
    }

    private static DockPosition ToCanonicalPosition(DockPosition position)
    {
        return position switch
        {
            DockPosition.EdgeLeft => DockPosition.Left,
            DockPosition.EdgeRight => DockPosition.Right,
            DockPosition.EdgeTop => DockPosition.Top,
            DockPosition.EdgeBottom => DockPosition.Bottom,
            _ => position,
        };
    }

    #endregion

    #region Rendering — Dock Preview

    internal static void RenderPreview(DrawingContext dc, double width, double height, DockPosition position)
    {
        if (position == DockPosition.None) return;

        var previewBrush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 212));
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 120, 212)), 2);

        var canonicalPos = ToCanonicalPosition(position);
        Rect previewRect = canonicalPos switch
        {
            DockPosition.Left => new Rect(0, 0, width / 2, height),
            DockPosition.Right => new Rect(width / 2, 0, width / 2, height),
            DockPosition.Top => new Rect(0, 0, width, height / 2),
            DockPosition.Bottom => new Rect(0, height / 2, width, height / 2),
            DockPosition.Center => new Rect(0, 0, width, height),
            _ => default,
        };

        if (previewRect.Width > 0 && previewRect.Height > 0)
        {
            dc.DrawRoundedRectangle(previewBrush, borderPen, previewRect, 4, 4);
        }
    }

    #endregion
}
