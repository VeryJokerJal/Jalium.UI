using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Root container for a docking layout.
/// Hosts a <see cref="DockSplitPanel"/> or <see cref="DockTabPanel"/> as content.
/// Shows an accent border when dock highlighting is active.
/// Dock indicator buttons are rendered in a separate topmost window by <see cref="DockManager"/>.
/// </summary>
public class DockLayout : ContentControl
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(ThemeColors.WindowBackground);
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(ThemeColors.DockTabStripBorder);
    private static readonly SolidColorBrush s_fallbackAccentBrush = new(ThemeColors.Accent);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanFloatProperty =
        DependencyProperty.Register(nameof(CanFloat), typeof(bool), typeof(DockLayout),
            new PropertyMetadata(true));

    private bool _isDockHighlighted;

    // 边框/高亮框的画刷来自主题资源，实例稳定；复用画笔省掉 OnRender 里两个
    // 带 8 个依赖属性的 DependencyObject 的每帧分配。
    private RenderPenCache _borderPen;
    private RenderPenCache _accentPen;

    /// <summary>
    /// Controls whether dock items in this layout can be torn off into standalone windows.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanFloat
    {
        get => (bool)(GetValue(CanFloatProperty) ?? true);
        set => SetValue(CanFloatProperty, value);
    }

    /// <summary>
    /// When true, an accent border is drawn to indicate this layout is a dock target.
    /// Managed by <see cref="DockManager"/>.
    /// </summary>
    internal bool IsDockHighlighted
    {
        get => _isDockHighlighted;
        set => _isDockHighlighted = value;
        // NOTE: no InvalidateVisual() — DockLayout is the root container and invalidating
        // it triggers a full-window re-render. The DockIndicatorWindow provides visual feedback.
    }

    public DockLayout()
    {
        ClipToBounds = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        DockManager.Register(this);
        Loaded += (_, _) => DockManager.Register(this);
        Unloaded += (_, _) => DockManager.Unregister(this);
    }

    protected override void OnRender(DrawingContext drawingContextObj)
    {
        var dc = drawingContextObj;

        var background = ResolveBackgroundBrush();
        dc.DrawRectangle(background, null, new Rect(RenderSize));

        base.OnRender(drawingContextObj);

        var borderPen = _borderPen.Get(ResolveBorderBrush(), 1);
        var half = borderPen.Thickness * 0.5;
        var borderRect = new Rect(
            half,
            half,
            Math.Max(0, RenderSize.Width - borderPen.Thickness),
            Math.Max(0, RenderSize.Height - borderPen.Thickness));
        if (borderRect.Width > 0 && borderRect.Height > 0)
        {
            dc.DrawRectangle(null, borderPen, borderRect);
        }

        if (IsDockHighlighted)
        {
            var accentPen = _accentPen.Get(ResolveAccentBrush(), 2);
            var inset = accentPen.Thickness * 0.5 + 1;
            var accentRect = new Rect(
                inset,
                inset,
                Math.Max(0, RenderSize.Width - inset * 2),
                Math.Max(0, RenderSize.Height - inset * 2));
            if (accentRect.Width > 0 && accentRect.Height > 0)
            {
                dc.DrawRectangle(null, accentPen, accentRect);
            }
        }
    }

    private Brush ResolveBackgroundBrush()
    {
        if (HasLocalValue(Control.BackgroundProperty) && Background != null)
            return Background;

        return ResolveBrush("OneBackgroundPrimary", "WindowBackground", s_fallbackBackgroundBrush);
    }

    private Brush ResolveBorderBrush()
    {
        return ResolveBrush("OneBorderDefault", "DockTabStripBorder", s_fallbackBorderBrush);
    }

    private Brush ResolveAccentBrush()
    {
        return ResolveBrush("OneAccentPrimary", "AccentBrush", s_fallbackAccentBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
