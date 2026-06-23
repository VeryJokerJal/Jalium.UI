using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides smooth expand/collapse animation for panels with non-linear cubic easing.
/// Drives Height + Opacity together, plus an optional chevron arrow rotation, so the
/// content grows/shrinks visually instead of snapping in and out.
/// </summary>
internal static class ExpandCollapseAnimator
{
    private const double ExpandDurationMs = 280.0;
    private const double CollapseDurationMs = 200.0;

    // Per-panel animation state — keyed by the panel so that multiple Expanders
    // running at once don't stomp on each other. ConditionalWeakTable lets the
    // panel (and therefore its state) be garbage collected normally.
    private static readonly ConditionalWeakTable<FrameworkElement, AnimationContext> s_contexts = new();

    private sealed class AnimationContext
    {
        public DispatcherTimer? Timer;
        public readonly Stopwatch Stopwatch = new();
        public FrameworkElement? Panel;
        public Shapes.Path? Arrow;

        public double StartHeight;
        public double TargetHeight;
        public double StartOpacity;
        public double TargetOpacity;
        public double StartArrowAngle;
        public double TargetArrowAngle;

        public bool Expanding;
        public double DurationMs;
    }

    /// <summary>
    /// Animates a panel expanding with a cubic ease-out curve.
    /// </summary>
    internal static DispatcherTimer? AnimateExpand(FrameworkElement panel, DispatcherTimer? activeTimer, Shapes.Path? arrow = null)
    {
        _ = activeTimer; // Preserved for API compatibility; state is owned by the animator.
        return StartAnimation(panel, arrow, expanding: true);
    }

    /// <summary>
    /// Animates a panel collapsing with a cubic ease-in curve.
    /// </summary>
    internal static DispatcherTimer? AnimateCollapse(FrameworkElement panel, DispatcherTimer? activeTimer, Shapes.Path? arrow = null)
    {
        _ = activeTimer;
        return StartAnimation(panel, arrow, expanding: false);
    }

    private static DispatcherTimer? StartAnimation(FrameworkElement panel, Shapes.Path? arrow, bool expanding)
    {
        var ctx = s_contexts.GetValue(panel, _ => new AnimationContext { Panel = panel });
        ctx.Timer?.Stop();
        ctx.Arrow = arrow;

        // Capture current visual state as the animation starting point. This
        // makes interrupted animations (rapid toggles) blend seamlessly.
        var currentHeight = ResolveCurrentHeight(panel);
        ctx.StartHeight = currentHeight;
        ctx.StartOpacity = panel.Opacity;
        ctx.StartArrowAngle = (arrow?.RenderTransform as RotateTransform)?.Angle
                              ?? (expanding ? 0.0 : 90.0);

        if (expanding)
        {
            // Force the panel into the layout pass so we can measure the
            // content's natural height, then start from the current clipped value.
            panel.Visibility = Visibility.Visible;
            panel.ClipToBounds = true;

            ctx.TargetHeight = MeasureNaturalHeight(panel);
            ctx.TargetOpacity = 1.0;
            ctx.TargetArrowAngle = 90.0;
            ctx.DurationMs = ExpandDurationMs;
        }
        else
        {
            panel.ClipToBounds = true;

            ctx.TargetHeight = 0.0;
            ctx.TargetOpacity = 0.0;
            ctx.TargetArrowAngle = 0.0;
            ctx.DurationMs = CollapseDurationMs;
        }

        ctx.Expanding = expanding;

        // Nothing to animate — snap to target immediately.
        if (ctx.TargetHeight <= 0.0 && !expanding && currentHeight <= 0.0)
        {
            ApplyFinalState(ctx);
            return null;
        }

        // Prime the panel at the starting height so the first frame doesn't
        // jump (important when the panel was sized to Auto/NaN before).
        panel.Height = currentHeight;
        panel.Opacity = ctx.StartOpacity;
        if (arrow != null)
        {
            EnsureRotateTransform(arrow).Angle = ctx.StartArrowAngle;
        }

        ctx.Stopwatch.Restart();

        if (ctx.Timer == null)
        {
            ctx.Timer = new DispatcherTimer
            {
                Interval = CompositionTarget.FrameInterval
            };
            ctx.Timer.Tick += (_, _) => OnTick(ctx);
        }
        ctx.Timer.Start();

        return ctx.Timer;
    }

    private static void OnTick(AnimationContext ctx)
    {
        var panel = ctx.Panel;
        if (panel == null)
        {
            ctx.Timer?.Stop();
            return;
        }

        var elapsed = ctx.Stopwatch.Elapsed.TotalMilliseconds;
        var t = Math.Min(1.0, elapsed / Math.Max(1.0, ctx.DurationMs));

        // Non-linear: cubic ease-out on expand (fast-start, soft-land),
        // cubic ease-in on collapse (soft-start, fast-exit).
        var eased = ctx.Expanding ? EaseOutCubic(t) : EaseInCubic(t);

        panel.Height = ctx.StartHeight + (ctx.TargetHeight - ctx.StartHeight) * eased;
        panel.Opacity = ctx.StartOpacity + (ctx.TargetOpacity - ctx.StartOpacity) * eased;

        // Changing Height re-flows this panel's own content (children revealed by
        // the growing clip) AND every SIBLING below it (they shift down/up). Those
        // re-arranged elements are not otherwise invalidated this frame, so under
        // the partial dirty-rect Present they get rendered into the back buffer but
        // their shifted pixels are never Present1'd — the screen keeps the previous
        // front buffer there, leaving stale/blank labels next to the chevron (which
        // IS explicitly invalidated below). Mark the whole re-flowed band — from the
        // panel's top down to the bottom of its hosting container — dirty so it is
        // both rendered AND presented. Walk to the items host (or root) and let the
        // dirty-region aggregator's area check promote to a full frame if needed.
        InvalidateReflowBelow(panel);

        if (ctx.Arrow != null)
        {
            var angle = ctx.StartArrowAngle + (ctx.TargetArrowAngle - ctx.StartArrowAngle) * eased;
            EnsureRotateTransform(ctx.Arrow).Angle = angle;
            ctx.Arrow.InvalidateVisual();
        }

        if (t >= 1.0)
        {
            ApplyFinalState(ctx);
        }
    }

    private static void ApplyFinalState(AnimationContext ctx)
    {
        var panel = ctx.Panel;
        if (panel == null) return;

        ctx.Timer?.Stop();
        ctx.Stopwatch.Stop();

        if (ctx.Expanding)
        {
            // Restore Auto sizing so the panel can grow/shrink with its content.
            panel.Height = double.NaN;
            panel.Opacity = 1.0;
            panel.ClipToBounds = false;
            if (ctx.Arrow != null)
            {
                EnsureRotateTransform(ctx.Arrow).Angle = 90.0;
            }
        }
        else
        {
            panel.Height = 0.0;
            panel.Opacity = 0.0;
            panel.Visibility = Visibility.Collapsed;
            if (ctx.Arrow != null)
            {
                EnsureRotateTransform(ctx.Arrow).Angle = 0.0;
            }
        }
    }

    /// <summary>
    /// Marks the re-flowed region dirty when an animating panel's Height changes.
    /// Setting <c>panel.Height</c> re-arranges the panel's siblings below it (they
    /// shift) and reveals its own children through the growing clip. Those moved
    /// elements are not otherwise invalidated this frame, so under the partial
    /// dirty-rect Present their shifted pixels are rendered into the back buffer
    /// but never Present1'd — the screen keeps the previous front buffer there,
    /// leaving stale/blank labels beside the explicitly-invalidated chevron.
    /// <para>
    /// <see cref="UIElement.InvalidateComposition"/> on the visual root keeps every
    /// element's recorded command list (no re-record / no cache blow-out) but
    /// re-composites the subtree at its CURRENT positions and marks the frame for
    /// present, so the whole re-flowed column is both drawn and shown. (This could
    /// be narrowed to the panel's screen band for efficiency; the whole-root form
    /// is the safe, always-correct version.)
    /// </para>
    /// </summary>
    private static void InvalidateReflowBelow(FrameworkElement panel)
    {
        UIElement node = panel;
        while (node.VisualParent is UIElement parent) node = parent;
        node.InvalidateComposition();
    }

    private static double ResolveCurrentHeight(FrameworkElement panel)
    {
        if (!double.IsNaN(panel.Height) && panel.Height >= 0.0)
        {
            return panel.Height;
        }

        // Collapsed panels don't participate in layout → ActualHeight is 0.
        return panel.Visibility == Visibility.Visible ? panel.ActualHeight : 0.0;
    }

    private static double MeasureNaturalHeight(FrameworkElement panel)
    {
        // Clear any explicit Height so measure reflects the content's own desire.
        panel.Height = double.NaN;
        panel.InvalidateMeasure();

        var availableWidth = panel.ActualWidth > 0
            ? panel.ActualWidth
            : (panel.VisualParent is FrameworkElement parent && parent.ActualWidth > 0
                ? parent.ActualWidth
                : double.PositiveInfinity);

        panel.Measure(new Size(availableWidth, double.PositiveInfinity));
        var desired = panel.DesiredSize.Height;

        // Guard against pathological 0 when measurement can't resolve yet.
        return desired > 0.0 ? desired : panel.ActualHeight;
    }

    private static RotateTransform EnsureRotateTransform(Shapes.Path arrow)
    {
        // 绝对像素轴心：把旋转中心烘进 RotateTransform 的本地坐标，与渲染时的 RenderSize 解耦。
        // 旧实现靠 RenderTransformOrigin(0.5,0.5)×RenderSize 在绘制那一刻现算轴心，
        // 在 Stretch 缩放 / 首帧布局未稳定时会算偏，旋转后箭头偏移甚至漂出槽位
        // （与 TreeView.SetExpanderAngle / ComboBox / NavigationViewItem 同策）。
        // 每次调用都按最新 ActualWidth/Height 刷新中心；RenderTransformOrigin 保持默认 (0,0)。
        var rt = arrow.RenderTransform as RotateTransform;
        if (rt == null)
        {
            rt = new RotateTransform();
            arrow.RenderTransform = rt;
        }

        var w = arrow.ActualWidth > 0 ? arrow.ActualWidth : arrow.Width;
        var h = arrow.ActualHeight > 0 ? arrow.ActualHeight : arrow.Height;
        rt.CenterX = (double.IsNaN(w) || w <= 0) ? 4 : w / 2;
        rt.CenterY = (double.IsNaN(h) || h <= 0) ? 4 : h / 2;
        return rt;
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private static double EaseInCubic(double t) => t * t * t;
}
