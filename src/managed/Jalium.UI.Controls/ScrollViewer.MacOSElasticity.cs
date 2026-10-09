using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class ScrollViewer
{
    private bool _macOSWheelGestureActive;
    private bool _macOSWheelOwnsOverscroll;
    private bool _macOSWheelSuppressMomentumX;
    private bool _macOSWheelSuppressMomentumY;
    private bool _isApplyingMacOSWheelScroll;

    private void HandleMacOSWheelGesturePhase(object sender, MouseWheelEventArgs e) => PrepareMacOSWheelGesture(e);

    private void HandleMacOSWheelGestureInterrupt(object sender, MouseButtonEventArgs e) => ResetMacOSWheelElasticity();

    private void PrepareMacOSWheelGesture(MouseWheelEventArgs e)
    {
        if (!OperatingSystem.IsMacOS() || !e.HasPreciseScrollingDeltas)
            return;
        if (MacOSScrollBarSettings.PrefersReducedMotion)
        {
            ResetMacOSWheelElasticity();
            return;
        }
        if ((e.Phase & (MouseWheelPhase.Began | MouseWheelPhase.MayBegin | MouseWheelPhase.Changed)) != 0)
        {
            if ((e.Phase & (MouseWheelPhase.Began | MouseWheelPhase.MayBegin)) != 0)
            {
                // Catch a returning edge without discarding its release state.
                // The first inward movement must scroll immediately, while an
                // outward pull can continue from the current visual position.
                bool returning = _bounceTimer is { IsEnabled: true };
                _macOSWheelSuppressMomentumX = _overscrollX != 0 && (_macOSWheelSuppressMomentumX || returning);
                _macOSWheelSuppressMomentumY = _overscrollY != 0 && (_macOSWheelSuppressMomentumY || returning);
                CancelBounceAnimation();
            }
            _macOSWheelGestureActive = true;
        }
        if (IsWheelPhaseFinished(e.Phase) || IsWheelPhaseFinished(e.MomentumPhase))
        {
            _macOSWheelGestureActive = false;
            FinishMacOSWheelElasticity();
        }
    }

    private static bool IsWheelPhaseFinished(MouseWheelPhase phase) =>
        (phase & (MouseWheelPhase.Ended | MouseWheelPhase.Cancelled)) != 0;

    private bool CanUseMacOSWheelElasticity(MouseWheelEventArgs e) =>
        OperatingSystem.IsMacOS() && e.HasPreciseScrollingDeltas &&
        !MacOSScrollBarSettings.PrefersReducedMotion &&
        (_macOSWheelGestureActive || e.Phase != MouseWheelPhase.None || e.MomentumPhase != MouseWheelPhase.None) &&
        ((e.Phase | e.MomentumPhase) & MouseWheelPhase.Cancelled) == 0;

    private double GetWheelAxisMaximum(bool isVertical) => _scrollInfo != null
        ? Math.Max(0, isVertical ? _scrollInfo.ExtentHeight - _scrollInfo.ViewportHeight
            : _scrollInfo.ExtentWidth - _scrollInfo.ViewportWidth)
        : isVertical ? ScrollableHeight : ScrollableWidth;

    private bool IsWheelAxisEnabled(bool isVertical) => (isVertical
        ? VerticalScrollBarVisibility : HorizontalScrollBarVisibility) != ScrollBarVisibility.Disabled;

    private bool TryScrollMacOSWheelAxis(double delta, bool isVertical, MouseWheelEventArgs e)
    {
        double maximum = GetWheelAxisMaximum(isVertical);
        double limit = Math.Min(MaxOverscrollDips, (isVertical ? _viewportHeight : _viewportWidth) / 2);
        if (!IsWheelAxisEnabled(isVertical) || !(maximum > 0) || !(limit > 0) ||
            delta == 0 || !double.IsFinite(delta))
            return false;

        ref double overscroll = ref (isVertical ? ref _overscrollY : ref _overscrollX);
        double oldOverscroll = overscroll;
        double offset = isVertical ? _verticalOffset : _horizontalOffset;
        bool canScroll = delta < 0 ? offset > 0 : offset < maximum;
        bool ancestorCanScroll = !canScroll && HasScrollableAncestorForWheelDelta(delta, isVertical);
        bool momentum = e.MomentumPhase != MouseWheelPhase.None;
        ref bool suppressMomentum = ref (isVertical ? ref _macOSWheelSuppressMomentumY : ref _macOSWheelSuppressMomentumX);
        bool returning = suppressMomentum || (overscroll != 0 && _bounceTimer is { IsEnabled: true });
        bool staleEdge = overscroll > 0 ? offset > 0 : overscroll < 0 && offset < maximum;
        // A final release delta still belongs to the closing gesture.
        bool resumesReturningContent = returning && canScroll && !IsWheelPhaseFinished(e.Phase);

        if (overscroll != 0 && (staleEdge || ancestorCanScroll || resumesReturningContent))
        {
            // A spring is visual feedback for a boundary, not a scroll lock.
            // Drop its axis as soon as input can move content or reach a parent.
            ClearMacOSWheelOverscrollAxis(isVertical);
            suppressMomentum = false;
            returning = false;
        }
        if (momentum && returning && !canScroll && !ancestorCanScroll)
        {
            // Suppress only the remaining outward fling at the same real edge.
            suppressMomentum = true;
            return true;
        }
        suppressMomentum = false;
        if (!momentum)
            CancelBounceAnimation();

        bool handled = false;
        if (overscroll != 0)
        {
            double raw = UnresistedMacOSOverscroll(overscroll, limit);
            if (Math.Sign(raw) == Math.Sign(delta) && Math.Abs(delta) >= Math.Abs(raw))
            {
                // Unwind the stretch before spending the remaining reversal on scrolling.
                delta -= raw;
                overscroll = 0;
            }
            else
            {
                overscroll = ResistMacOSOverscroll(raw - delta, limit);
                delta = 0;
            }
            handled = true;
        }
        if (delta != 0)
        {
            double target = Math.Clamp(offset + delta, 0, maximum);
            handled |= TryScrollMouseWheelAxis(delta, isVertical, smooth: false);
            double remaining = delta - (target - offset);
            if (remaining != 0 && !HasScrollableAncestorForWheelDelta(remaining, isVertical))
            {
                overscroll = ResistMacOSOverscroll(-remaining, limit);
                _macOSWheelOwnsOverscroll = true;
                handled = true;
            }
        }
        if (oldOverscroll != overscroll)
            UpdateOverscrollVisuals();
        if (momentum && overscroll != 0)
        {
            // Remaining native fling packets must not restart the edge's return.
            suppressMomentum = true;
            StartBounceAnimation();
        }
        return handled;
    }

    private void ClearMacOSWheelOverscrollAxis(bool isVertical)
    {
        if (isVertical)
            _overscrollY = _bounceFromY = 0;
        else
            _overscrollX = _bounceFromX = 0;
        if (_overscrollX == 0 && _overscrollY == 0)
            CancelBounceAnimation();
    }

    private bool HasScrollableAncestorForWheelDelta(double delta, bool isVertical)
    {
        for (Visual? current = VisualParent as Visual; current != null; current = current.VisualParent)
        {
            if (current is not ScrollViewer viewer || !viewer.IsWheelAxisEnabled(isVertical))
                continue;
            double maximum = viewer.GetWheelAxisMaximum(isVertical);
            double offset = isVertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
            if (maximum > 0 && (delta < 0 ? offset > 0 : offset < maximum))
                return true;
        }
        return false;
    }

    private static double ResistMacOSOverscroll(double raw, double limit) =>
        Math.CopySign(limit * (1 - 1 / (1 + Math.Abs(raw) * 0.55 / limit)), raw);

    private static double UnresistedMacOSOverscroll(double value, double limit)
    {
        double magnitude = Math.Min(Math.Abs(value), limit * 0.999);
        return Math.CopySign(magnitude * limit / (0.55 * (limit - magnitude)), value);
    }

    private void FinishMacOSWheelElasticity()
    {
        if (!_macOSWheelOwnsOverscroll || (_overscrollX == 0 && _overscrollY == 0))
            return;
        _macOSWheelSuppressMomentumX |= _overscrollX != 0;
        _macOSWheelSuppressMomentumY |= _overscrollY != 0;
        if (_bounceTimer is not { IsEnabled: true })
            StartBounceAnimation();
    }

    private void ResetMacOSWheelElasticity()
    {
        _macOSWheelGestureActive = false;
        _macOSWheelSuppressMomentumX = _macOSWheelSuppressMomentumY = false;
        if (!_macOSWheelOwnsOverscroll)
            return;
        _macOSWheelOwnsOverscroll = false;
        CancelBounceAnimation();
        if (_overscrollX == 0 && _overscrollY == 0)
            return;
        _overscrollX = _overscrollY = 0;
        UpdateOverscrollVisuals();
    }

    private void UpdateOverscrollVisuals()
    {
        if (!TryTranslatePhysicalContent())
            InvalidateArrange();
        UpdateScrollBarMetrics();
        RevealOverlayIndicatorForScrollMovement();
        Jalium.UI.Documents.AdornerLayer.GetAdornerLayer(this)?.InvalidateAdornerPositions(this);
    }
}
