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

    // Fallback colors used when theme resources are unavailable.
    private static readonly Color ChromeBg = Color.FromArgb(230, 32, 32, 32);
    private static readonly Color ChromeBorder = Color.FromArgb(180, 70, 70, 70);
    private static readonly Color ButtonBg = Color.FromArgb(255, 50, 50, 50);
    private static readonly Color ButtonBorder = Color.FromArgb(160, 80, 80, 80);
    private static readonly Color ButtonHoverBg = Color.FromArgb(255, 0x1E, 0x79, 0x3F); // Accent
    private static readonly Color IconNormal = Color.FromArgb(220, 200, 200, 200);
    private static readonly Color IconHover = Color.FromRgb(255, 255, 255);
    private static readonly Color PreviewBg = Color.FromArgb(50, 0x1E, 0x79, 0x3F);
    private static readonly Color PreviewBorder = Color.FromArgb(140, 0x1E, 0x79, 0x3F);
    private static readonly SolidColorBrush s_fallbackChromeBackgroundBrush = new(ChromeBg);
    private static readonly SolidColorBrush s_fallbackChromeBorderBrush = new(ChromeBorder);
    private static readonly SolidColorBrush s_fallbackButtonBackgroundBrush = new(ButtonBg);
    private static readonly SolidColorBrush s_fallbackButtonBorderBrush = new(ButtonBorder);
    private static readonly SolidColorBrush s_fallbackButtonHoverBackgroundBrush = new(ButtonHoverBg);
    private static readonly SolidColorBrush s_fallbackPreviewBackgroundBrush = new(PreviewBg);
    private static readonly SolidColorBrush s_fallbackPreviewBorderBrush = new(PreviewBorder);

    // Cached resolved resources — cleared when theme changes
    private static Brush? s_cachedChromeBackground;
    private static Brush? s_cachedChromeBorder;
    private static Brush? s_cachedButtonBackground;
    private static Brush? s_cachedButtonBorder;
    private static Brush? s_cachedButtonHoverBackground;
    private static Brush? s_cachedPreviewBackground;
    private static Brush? s_cachedPreviewBorder;
    private static Color? s_cachedIconNormal;
    private static Color? s_cachedIconHover;

    // Cached Pen objects to avoid allocation per frame
    private static Pen? s_cachedChromeBorderPen;
    private static Pen? s_cachedButtonBorderPen;
    private static Pen? s_cachedButtonHoverBorderPen;
    private static Pen? s_cachedPreviewBorderPen;

    /// <summary>
    /// Clears cached brushes/pens so they are re-resolved from theme resources on next render.
    /// Call when the drag session starts or theme changes.
    /// </summary>
    internal static void InvalidateResourceCache()
    {
        s_cachedChromeBackground = null;
        s_cachedChromeBorder = null;
        s_cachedButtonBackground = null;
        s_cachedButtonBorder = null;
        s_cachedButtonHoverBackground = null;
        s_cachedPreviewBackground = null;
        s_cachedPreviewBorder = null;
        s_cachedIconNormal = null;
        s_cachedIconHover = null;
        s_cachedChromeBorderPen = null;
        s_cachedButtonBorderPen = null;
        s_cachedButtonHoverBorderPen = null;
        s_cachedPreviewBorderPen = null;
        s_cachedIconPenNormal = null;
        s_cachedIconPenHover = null;
        s_cachedIconFillNormal = null;
        s_cachedIconFillHover = null;
    }

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

        var bgBrush = ResolveChromeBackgroundBrush();
        var borderPen = ResolveChromeBackgroundBorderPen();

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
            var bgBrush = ResolveChromeBackgroundBrush();
            var borderPen = ResolveChromeBackgroundBorderPen();
            var bgRect = new Rect(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
            dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, CornerRadius, CornerRadius);

            DrawButton(dc, rect, pos, pos == hoveredPosition);
        }
    }

    #endregion

    #region Rendering — Button

    private static void DrawButton(DrawingContext dc, Rect rect, DockPosition position, bool isHovered)
    {
        var bgBrush = ResolveButtonBackgroundBrush(isHovered);
        var borderPen = ResolveButtonBorderPen(isHovered);

        dc.DrawRoundedRectangle(bgBrush, borderPen, rect, 4, 4);

        var iconColor = ResolveIconColor(isHovered);
        DrawDockIcon(dc, rect, position, iconColor);
    }

    // Cached icon drawing objects — keyed by hovered state (normal vs hover)
    private static Pen? s_cachedIconPenNormal;
    private static Pen? s_cachedIconPenHover;
    private static SolidColorBrush? s_cachedIconFillNormal;
    private static SolidColorBrush? s_cachedIconFillHover;

    private static void DrawDockIcon(DrawingContext dc, Rect rect, DockPosition position, Color color)
    {
        // Use cached pen/brush per hover state to avoid per-frame allocation
        bool isHover = (color == IconHover || (s_cachedIconHover.HasValue && color == s_cachedIconHover.Value));
        Pen pen;
        SolidColorBrush fillBrush;
        if (isHover)
        {
            pen = s_cachedIconPenHover ??= new Pen(new SolidColorBrush(color), 1.2);
            fillBrush = s_cachedIconFillHover ??= new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));
        }
        else
        {
            pen = s_cachedIconPenNormal ??= new Pen(new SolidColorBrush(color), 1.2);
            fillBrush = s_cachedIconFillNormal ??= new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));
        }
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

        var previewBrush = ResolvePreviewBackgroundBrush();
        var borderPen = ResolvePreviewBorderPen();

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

    private static Brush ResolveChromeBackgroundBrush()
    {
        return s_cachedChromeBackground ??= ResolveBrush("DockIndicatorChromeBackground", s_fallbackChromeBackgroundBrush);
    }

    private static Brush ResolveChromeBorderBrush()
    {
        return s_cachedChromeBorder ??= ResolveBrush("DockIndicatorChromeBorder", s_fallbackChromeBorderBrush);
    }

    private static Pen ResolveChromeBackgroundBorderPen()
    {
        return s_cachedChromeBorderPen ??= new Pen(ResolveChromeBorderBrush(), 1);
    }

    private static Brush ResolveButtonBackgroundBrush(bool isHovered)
    {
        if (isHovered)
            return s_cachedButtonHoverBackground ??= ResolveBrush("DockIndicatorButtonHoverBackground", s_fallbackButtonHoverBackgroundBrush);
        return s_cachedButtonBackground ??= ResolveBrush("DockIndicatorButtonBackground", s_fallbackButtonBackgroundBrush);
    }

    private static Pen ResolveButtonBorderPen(bool isHovered)
    {
        if (isHovered)
            return s_cachedButtonHoverBorderPen ??= new Pen(
                s_cachedButtonHoverBackground ??= ResolveBrush("DockIndicatorButtonHoverBackground", s_fallbackButtonHoverBackgroundBrush), 1);
        return s_cachedButtonBorderPen ??= new Pen(
            s_cachedButtonBorder ??= ResolveBrush("DockIndicatorButtonBorder", s_fallbackButtonBorderBrush), 1);
    }

    private static Color ResolveIconColor(bool isHovered)
    {
        if (isHovered)
            return s_cachedIconHover ??= ResolveColor("DockIndicatorIconHoverForeground", IconHover);
        return s_cachedIconNormal ??= ResolveColor("DockIndicatorIconForeground", IconNormal);
    }

    private static Brush ResolvePreviewBackgroundBrush()
    {
        return s_cachedPreviewBackground ??= ResolveBrush("DockIndicatorPreviewBackground", s_fallbackPreviewBackgroundBrush);
    }

    private static Pen ResolvePreviewBorderPen()
    {
        return s_cachedPreviewBorderPen ??= new Pen(
            s_cachedPreviewBorder ??= ResolveBrush("DockIndicatorPreviewBorder", s_fallbackPreviewBorderBrush), 2);
    }

    private static Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        var appResources = Jalium.UI.Application.Current?.Resources;
        if (appResources != null && appResources.TryGetValue(resourceKey, out var resource) && resource is Brush brush)
            return brush;

        return fallback;
    }

    private static Color ResolveColor(string resourceKey, Color fallback)
    {
        var appResources = Jalium.UI.Application.Current?.Resources;
        if (appResources != null
            && appResources.TryGetValue(resourceKey, out var resource)
            && resource is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return fallback;
    }

    #endregion
}
