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

        var currentHeight = panel.ActualHeight;

        // Measure natural height
        panel.Height = double.NaN;
        panel.MaxHeight = double.PositiveInfinity;
        panel.Measure(new Size(
            panel.ActualWidth > 0 ? panel.ActualWidth : double.PositiveInfinity,
            double.PositiveInfinity));
        var targetHeight = panel.DesiredSize.Height;

        if (targetHeight <= 0)
        {
            panel.ClipToBounds = false;
            return null;
        }

        var startHeight = Math.Min(currentHeight, targetHeight);
        panel.Height = startHeight;

        // Arrow setup
        RotateTransform? rotateTransform = null;
        double startAngle = 0;
        double lastAngle = double.NaN;
        if (arrow != null)
        {
            rotateTransform = EnsureRotateTransform(arrow);
            startAngle = rotateTransform.Angle;
            lastAngle = startAngle;
        }

        // Cloth setup
        var clothChildren = CollectClothChildren(panel, targetHeight);

        var startTime = Environment.TickCount64;
        var remainingRatio = targetHeight > 0 ? 1.0 - startHeight / targetHeight : 1.0;
        var duration = ExpandDurationMs * Math.Max(remainingRatio, 0.3);

        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = duration > 0 ? Math.Min(1.0, elapsed / duration) : 1.0;

            // --- Height: BackEase elastic ---
            panel.Height = startHeight + (targetHeight - startHeight) * ExpandEase.Ease(progress);

            // --- Arrow: CubicEase smooth ---
            if (rotateTransform != null)
            {
                var newAngle = startAngle + (90.0 - startAngle) * ArrowEase.Ease(progress);
                if (Math.Abs(newAngle - lastAngle) >= 0.5)
                {
                    rotateTransform.Angle = newAngle;
                    lastAngle = newAngle;
                    arrow!.InvalidateVisual();
                }
            }

            // --- Cloth: staggered by progress offset ---
            for (int i = 0; i < clothChildren.Length; i++)
            {
                ref var c = ref clothChildren[i];
                // Child's local progress: delayed by its stagger, then scaled to fill remaining range
                var delay = c.ProgressDelay;
                var childProgress = delay < 1.0
                    ? Math.Clamp((progress - delay) / (1.0 - delay), 0.0, 1.0)
                    : 1.0;
                var childEased = ClothEase.Ease(childProgress);
                c.Element.RenderOffset = new Point(0, c.InitialY * (1.0 - childEased));
            }

            if (progress >= 1.0)
            {
                timer.Stop();
                panel.Height = double.NaN;
                panel.MaxHeight = double.PositiveInfinity;
                panel.ClipToBounds = false;

                for (int i = 0; i < clothChildren.Length; i++)
                    clothChildren[i].Element.RenderOffset = default;

                if (rotateTransform != null)
                {
                    rotateTransform.Angle = 90;
                    arrow!.InvalidateVisual();
                }
            }
        };
        timer.Start();
        return timer;
    }

    /// <summary>
    /// Animates a panel collapsing smoothly.
    /// </summary>
    internal static DispatcherTimer? AnimateCollapse(FrameworkElement panel, DispatcherTimer? activeTimer, Shapes.Path? arrow = null)
    {
        activeTimer?.Stop();
        ClearChildOffsets(panel);

        var startHeight = panel.ActualHeight;
        if (startHeight <= 0)
        {
            panel.Visibility = Visibility.Collapsed;
            panel.Height = double.NaN;
            panel.MaxHeight = double.PositiveInfinity;
            if (arrow != null)
            {
                EnsureRotateTransform(arrow).Angle = 0;
                arrow.InvalidateVisual();
            }
            return null;
        }

        panel.ClipToBounds = true;
        panel.Height = double.NaN;
        panel.MaxHeight = startHeight;

        RotateTransform? rotateTransform = null;
        double startAngle = 0;
        double lastAngle = double.NaN;
        if (arrow != null)
        {
            rotateTransform = EnsureRotateTransform(arrow);
            startAngle = rotateTransform.Angle;
            lastAngle = startAngle;
        }

        var startTime = Environment.TickCount64;
        var duration = CollapseDurationMs;

        var timer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        timer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = duration > 0 ? Math.Min(1.0, elapsed / duration) : 1.0;
            var easedProgress = CollapseEase.Ease(progress);

            panel.MaxHeight = startHeight * (1.0 - easedProgress);

            if (rotateTransform != null)
            {
                var newAngle = startAngle * (1.0 - easedProgress);
                if (Math.Abs(newAngle - lastAngle) >= 0.5)
                {
                    rotateTransform.Angle = newAngle;
                    lastAngle = newAngle;
                    arrow!.InvalidateVisual();
                }
            }

            if (progress >= 1.0)
            {
                timer.Stop();
                panel.Visibility = Visibility.Collapsed;
                panel.Height = double.NaN;
                panel.MaxHeight = double.PositiveInfinity;
                panel.ClipToBounds = false;

                if (rotateTransform != null)
                {
                    rotateTransform.Angle = 0;
                    arrow!.InvalidateVisual();
                }
            }
        };
        timer.Start();
        return timer;
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
