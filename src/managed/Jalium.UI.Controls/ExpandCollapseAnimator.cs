using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides smooth expand/collapse animation for panels with elastic spring easing.
/// All expand animations (height, arrow, cloth) share one unified progress (0→1).
/// Cloth draping staggers per-child via progress offset, not time offset.
/// </summary>
internal static class ExpandCollapseAnimator
{
    private const double ExpandDurationMs = 350;
    private const double CollapseDurationMs = 150;

    // Cloth: each child's animation starts at a progress delay proportional to its index.
    // e.g. 0.08 means child[1] starts at progress=0.08, child[2] at 0.16, etc.
    private const double ClothStaggerProgress = 0.08;

    private static readonly BackEase ExpandEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 1.0 };
    private static readonly BackEase ClothEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 1.2 };
    private static readonly CubicEase ArrowEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase CollapseEase = new() { EasingMode = EasingMode.EaseInOut };

    /// <summary>
    /// Animates a panel expanding with elastic overshoot and cloth draping effect.
    /// Height, arrow, and cloth all driven by a single progress 0→1.
    /// </summary>
    internal static DispatcherTimer? AnimateExpand(FrameworkElement panel, DispatcherTimer? activeTimer, Shapes.Path? arrow = null)
    {
        activeTimer?.Stop();
        ClearChildOffsets(panel);

        panel.Visibility = Visibility.Visible;
        panel.ClipToBounds = true;
        panel.Height = double.NaN;
        panel.MaxHeight = double.PositiveInfinity;
        if (arrow != null)
        {
            EnsureRotateTransform(arrow).Angle = 90;
            arrow.InvalidateVisual();
        }

        panel.ClipToBounds = false;
        return null;
    }

    /// <summary>
    /// Animates a panel collapsing smoothly.
    /// </summary>
    internal static DispatcherTimer? AnimateCollapse(FrameworkElement panel, DispatcherTimer? activeTimer, Shapes.Path? arrow = null)
    {
        activeTimer?.Stop();
        ClearChildOffsets(panel);
        if (arrow != null)
        {
            EnsureRotateTransform(arrow).Angle = 0;
            arrow.InvalidateVisual();
        }

        panel.Visibility = Visibility.Collapsed;
        panel.Height = double.NaN;
        panel.MaxHeight = double.PositiveInfinity;
        panel.ClipToBounds = false;
        return null;
    }

    #region Helpers

    private struct ClothChild
    {
        public UIElement Element;
        public double InitialY;
        public double ProgressDelay; // 0.0 for first child, increases for later children
    }

    private static ClothChild[] CollectClothChildren(FrameworkElement panel, double targetHeight)
    {
        if (panel is not Panel p || p.Children.Count == 0)
            return [];

        var count = 0;
        for (int i = 0; i < p.Children.Count; i++)
        {
            if (p.Children[i] is UIElement child && child.Visibility != Visibility.Collapsed)
                count++;
        }

        if (count == 0)
            return [];

        var baseOffset = Math.Min(targetHeight * 0.3, 40.0);
        var result = new ClothChild[count];
        var idx = 0;

        for (int i = 0; i < p.Children.Count; i++)
        {
            if (p.Children[i] is UIElement child && child.Visibility != Visibility.Collapsed)
            {
                var progressDelay = idx * ClothStaggerProgress;
                result[idx] = new ClothChild
                {
                    Element = child,
                    InitialY = -baseOffset * (idx + 1.0) / count,
                    ProgressDelay = Math.Min(progressDelay, 0.5) // cap at 50% so child still has room to animate
                };
                child.RenderOffset = new Point(0, result[idx].InitialY);
                idx++;
            }
        }

        return result;
    }

    private static void ClearChildOffsets(FrameworkElement panel)
    {
        if (panel is not Panel p) return;
        for (int i = 0; i < p.Children.Count; i++)
        {
            if (p.Children[i] is UIElement child)
                child.RenderOffset = default;
        }
    }

    private static RotateTransform EnsureRotateTransform(Shapes.Path arrow)
    {
        if (arrow.RenderTransform is RotateTransform existing)
            return existing;

        var rt = new RotateTransform();
        arrow.RenderTransform = rt;
        return rt;
    }

    #endregion
}
