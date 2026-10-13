using Jalium.UI.Input;

namespace Jalium.UI.Controls;

public partial class ScrollViewer
{
    private void HandleDirectionalMouseWheel(MouseWheelEventArgs e)
    {
        PrepareMacOSWheelGesture(e);
        double horizontal = double.IsFinite(e.HorizontalDelta) ? e.HorizontalDelta : 0;
        double vertical = double.IsFinite(e.VerticalDelta) ? e.VerticalDelta : 0;
        bool hasHorizontal = horizontal != 0;
        bool hasVertical = vertical != 0;
        if (!hasHorizontal && !hasVertical)
            return;

        bool elastic = CanUseMacOSWheelElasticity(e);
        if (!elastic)
            ResetMacOSWheelElasticity();

        if (_scrollInfo != null)
            SyncFromScrollInfo();

        // AppKit supplies both the direct gesture and its momentum. Animating
        // these packets again adds latency and continues moving after AppKit stops.
        bool smooth = !e.HasPreciseScrollingDeltas && IsScrollInertiaEnabled &&
                      GetEffectiveScrollInertiaDurationMs() > 0;
        if (!smooth)
            CancelSmoothScroll();

        _isApplyingMacOSWheelScroll = elastic;
        try
        {
            if (hasHorizontal && !e.IsHorizontalDeltaHandled)
            {
                // Horizontal wheel deltas use the shared ABI's positive-right convention.
                double delta = -ComputeDirectionalWheelDelta(horizontal, _viewportWidth, e.HasPreciseScrollingDeltas);
                e.IsHorizontalDeltaHandled = elastic
                    ? TryScrollMacOSWheelAxis(delta, isVertical: false, e)
                    : TryScrollMouseWheelAxis(delta, isVertical: false, smooth);
            }

            if (hasVertical && !e.IsVerticalDeltaHandled)
            {
                // Shift remaps only a vertical-only packet. AppKit may have already
                // remapped Shift+wheel to X; don't apply that packet a second time.
                bool scrollHorizontally = !hasHorizontal &&
                    ((e.KeyboardModifiers & ModifierKeys.Shift) != 0 || !CanScrollVertically);
                double viewport = scrollHorizontally ? _viewportWidth : _viewportHeight;
                double delta = ComputeDirectionalWheelDelta(vertical, viewport, e.HasPreciseScrollingDeltas);
                e.IsVerticalDeltaHandled = elastic
                    ? TryScrollMacOSWheelAxis(delta, !scrollHorizontally, e)
                    : TryScrollMouseWheelAxis(delta, !scrollHorizontally, smooth);
            }
        }
        finally
        {
            _isApplyingMacOSWheelScroll = false;
        }

        if (elastic && IsWheelPhaseFinished(e.Phase) && _macOSWheelOwnsOverscroll &&
            (_overscrollX != 0 || _overscrollY != 0))
        {
            // Ended can include a final movement. Capture its resulting visual
            // position after applying the delta, rather than the preview's position.
            StartBounceAnimation();
        }

        // Keep unconsumed axes available to enclosing viewers. The per-axis
        // markers prevent an ancestor from repeating a child's diagonal movement.
        e.Handled = (!hasHorizontal || e.IsHorizontalDeltaHandled) &&
                    (!hasVertical || e.IsVerticalDeltaHandled);
    }

    private static double ComputeDirectionalWheelDelta(double delta, double viewport, bool precise)
    {
        if (precise)
            return -delta * (LineScrollAmount * 3 / 120.0);

        uint lines = GetSystemWheelScrollLines();
        double step = lines == WHEEL_PAGESCROLL ? Math.Max(LineScrollAmount, viewport) : lines * LineScrollAmount;
        return -delta / 120.0 * step;
    }

    private bool TryScrollMouseWheelAxis(double delta, bool isVertical, bool smooth)
    {
        bool enabled = isVertical
            ? VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
            : HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
        double maximum = _scrollInfo != null
            ? Math.Max(0, isVertical
                ? _scrollInfo.ExtentHeight - _scrollInfo.ViewportHeight
                : _scrollInfo.ExtentWidth - _scrollInfo.ViewportWidth)
            : isVertical ? ScrollableHeight : ScrollableWidth;
        double offset = isVertical ? _verticalOffset : _horizontalOffset;
        if (!enabled || maximum <= 0 || delta == 0 || !double.IsFinite(delta))
            return false;

        if (delta < 0)
        {
            ClearEndAnchor(isVertical);
            if (isVertical)
                _smoothVerticalEndAnchorPending = false;
            else
                _smoothHorizontalEndAnchorPending = false;
        }

        double pendingOffset = smooth && _isSmoothScrolling
            ? (isVertical ? _smoothTargetY : _smoothTargetX)
            : offset;
        if ((delta < 0 && offset <= 0 && pendingOffset <= 0) ||
            (delta > 0 && offset >= maximum && pendingOffset >= maximum))
        {
            bool anchored = isVertical
                ? _verticalEndAnchorActive || _smoothVerticalEndAnchorPending
                : _horizontalEndAnchorActive || _smoothHorizontalEndAnchorPending;
            if (delta > 0 && anchored && _scrollInfo != null)
            {
                MaintainEndAnchors(allowCompletion: false);
                return true;
            }
            return false;
        }

        if (smooth)
        {
            InitializeSmoothScrollTargetsIfNeeded();
            double target = (isVertical ? _smoothTargetY : _smoothTargetX) + delta;
            bool anchorEnd = delta > 0 && target >= maximum - 0.01;
            if (isVertical)
            {
                _smoothTargetY = Math.Clamp(target, 0, maximum);
                _smoothVerticalEndAnchorPending = anchorEnd;
            }
            else
            {
                _smoothTargetX = Math.Clamp(target, 0, maximum);
                _smoothHorizontalEndAnchorPending = anchorEnd;
            }
            StartSmoothScroll();
        }
        else
        {
            RunRelativeScroll(() =>
            {
                double target = Math.Clamp(offset + delta, 0, maximum);
                if (isVertical)
                    ScrollToVerticalOffset(target);
                else
                    ScrollToHorizontalOffset(target);

                if (delta > 0 && IsAtScrollEnd(isVertical))
                    ApplyEndAnchorOffset(isVertical);
            });
        }
        return true;
    }
}
