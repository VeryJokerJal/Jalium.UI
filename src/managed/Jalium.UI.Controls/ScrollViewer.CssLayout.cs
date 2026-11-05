using Jalium.UI.Input;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class ScrollViewer
{
    private bool _cssScrollHandlersAttached;
    private bool _isArrangingPhysicalContent;

    internal void EnsureCssScrollHandlersAttached()
    {
        if (_cssScrollHandlersAttached)
            return;
        _cssScrollHandlersAttached = true;
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(HandleCssPostMouseWheel), handledEventsToo: true);
    }

    private void MeasurePhysicalContent(Size available, Size viewport)
    {
        EnsureCssScrollHandlersAttached();
        CssScrollProperties.CaptureAnchor(this);
        if (ScrollLayoutContent is not FrameworkElement child) { ScrollLayoutContent!.Measure(available); return; }
        var previous = child.CssNativeBox;
        var fill = GetFillViewport(ScrollSemanticContent ?? child);
        child.CssNativeBox = new(viewport, fill is FillViewportMode.Vertical or FillViewportMode.Both);
        try
        {
            // A scrollable measure constraint can stay infinite while its real
            // viewport changes. Percentages still need the new containing block.
            if (child.CssLayout is { UsesPercent: true } layout && layout.NativeMeasureContainingBlock != viewport)
                child.InvalidateCssAllocation();
            child.Measure(available);
        }
        finally { child.CssNativeBox = previous; }
    }

    private void ArrangePhysicalContent(Rect slot, Size viewport)
    {
        EnsureCssScrollHandlersAttached();
        if (ScrollLayoutContent is not FrameworkElement child) { ScrollLayoutContent!.Arrange(slot); return; }
        var previous = child.CssNativeBox;
        var fill = GetFillViewport(ScrollSemanticContent ?? child);
        child.CssNativeBox = new(viewport, fill is FillViewportMode.Vertical or FillViewportMode.Both);
        if (child.LastCssNativeArrangeBox != child.CssNativeBox) child.InvalidateArrange();
        _isArrangingPhysicalContent = true;
        try
        {
            var oldH = _horizontalOffset;
            var oldV = _verticalOffset;
            child.Arrange(slot);
            if (CssScrollProperties.RestoreAnchor(this))
            {
                var adjustedSlot = new Rect(
                    slot.X + (oldH - _horizontalOffset),
                    slot.Y + (oldV - _verticalOffset),
                    slot.Width,
                    slot.Height);
                child.Arrange(adjustedSlot);
            }
        }
        finally
        {
            _isArrangingPhysicalContent = false;
            child.CssNativeBox = previous;
        }
    }

    internal void SetCssScrollOffsetImmediate(double horizontalOffset, double verticalOffset)
    {
        CancelSmoothScroll();
        ResetMacOSWheelElasticity();
        ClearEndAnchor(isVertical: true);
        ClearEndAnchor(isVertical: false);

        if (_scrollInfo != null)
        {
            if (HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                _scrollInfo.SetHorizontalOffset(Math.Max(0, horizontalOffset));
            if (VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                _scrollInfo.SetVerticalOffset(Math.Max(0, verticalOffset));
            _horizontalOffset = _scrollInfo.HorizontalOffset;
            _verticalOffset = _scrollInfo.VerticalOffset;
            UpdateRequestedOffsetsFromContent();
            if (!_isArrangingPhysicalContent)
            {
                InvalidateScrollArrange();
                RaiseScrollChanged();
                UpdateScrollBarMetrics();
            }
            return;
        }

        var maxH = HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? 0
            : Math.Max(0, _extentWidth - _viewportWidth);
        var maxV = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? 0
            : Math.Max(0, _extentHeight - _viewportWidth > 0 ? _extentHeight - _viewportHeight : 0);
        maxV = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? 0
            : Math.Max(0, _extentHeight - _viewportHeight);

        var newH = double.IsFinite(horizontalOffset) ? Math.Clamp(horizontalOffset, 0, maxH) : 0;
        var newV = double.IsFinite(verticalOffset) ? Math.Clamp(verticalOffset, 0, maxV) : 0;

        var changed = !AreClose(_horizontalOffset, newH) || !AreClose(_verticalOffset, newV);
        _horizontalOffset = newH;
        _verticalOffset = newV;
        _smoothTargetX = newH;
        _smoothTargetY = newV;
        UpdateRequestedOffsetsFromContent();

        if (changed && !_isArrangingPhysicalContent)
        {
            if (!TryTranslatePhysicalContent())
            {
                InvalidateScrollArrange();
            }
            RaiseScrollChanged();
            UpdateScrollBarMetrics();
        }
    }

    private void HandleCssPostMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var overscrollX = CssScrollProperties.GetEffectiveOverscrollBehaviorX(this);
        var overscrollY = CssScrollProperties.GetEffectiveOverscrollBehaviorY(this);
        var isHorizontalGesture = (e.KeyboardModifiers & ModifierKeys.Shift) != 0 ||
                                  (Math.Abs(e.HorizontalDelta) > Math.Abs(e.VerticalDelta) && e.HorizontalDelta != 0) ||
                                  (!CanScrollVertically && CanScrollHorizontally);

        if (overscrollX == CssOverscrollBehavior.None && Math.Abs(_overscrollX) > 0)
        {
            _overscrollX = 0;
            InvalidateScrollArrange();
            UpdateScrollBarMetrics();
        }

        if (overscrollY == CssOverscrollBehavior.None && Math.Abs(_overscrollY) > 0)
        {
            _overscrollY = 0;
            InvalidateScrollArrange();
            UpdateScrollBarMetrics();
        }

        // Stop boundary scroll chaining when overscroll-behavior is contain or none.
        if (!e.Handled)
        {
            if (!isHorizontalGesture &&
                overscrollY is CssOverscrollBehavior.Contain or CssOverscrollBehavior.None &&
                VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
            {
                e.Handled = true;
            }
            else if (isHorizontalGesture &&
                     overscrollX is CssOverscrollBehavior.Contain or CssOverscrollBehavior.None &&
                     HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
            {
                e.Handled = true;
            }
        }

        var snapType = CssScrollProperties.GetEffectiveScrollSnapType(this);
        if (snapType.IsEnabled && e.Delta != 0)
        {
            var rawTarget = new Point(
                _isSmoothScrolling ? _smoothTargetX : _horizontalOffset,
                _isSmoothScrolling ? _smoothTargetY : _verticalOffset);
            var velocity = isHorizontalGesture
                ? new Vector(-e.Delta, 0)
                : new Vector(0, -e.Delta);
            var snapped = CssScrollProperties.ResolveSnapOffset(this, rawTarget, velocity);
            if (_isSmoothScrolling &&
                CssScrollProperties.GetScrollBehavior(this) != CssScrollBehavior.Auto)
            {
                _smoothTargetX = snapped.X;
                _smoothTargetY = snapped.Y;
            }
            else
            {
                SetCssScrollOffsetImmediate(snapped.X, snapped.Y);
            }
            e.Handled = true;
        }
    }
}
