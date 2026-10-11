using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>Converts wheel packets to pixel movement without losing an unconsumed axis.</summary>
internal readonly struct MouseWheelScrollInput
{
    private readonly bool _hasHorizontal;
    private readonly bool _hasVertical;
    private readonly bool _remapVertical;

    internal double Horizontal { get; }
    internal double Vertical { get; }

    internal MouseWheelScrollInput(MouseWheelEventArgs e, double horizontalStep, double verticalStep,
        bool remapShift = true)
    {
        double horizontal = double.IsFinite(e.HorizontalDelta) ? e.HorizontalDelta : 0;
        double vertical = double.IsFinite(e.VerticalDelta) ? e.VerticalDelta : 0;
        _hasHorizontal = horizontal != 0;
        _hasVertical = vertical != 0;
        _remapVertical = remapShift && !_hasHorizontal &&
            (e.KeyboardModifiers & ModifierKeys.Shift) != 0;

        double horizontalScale = e.HasPreciseScrollingDeltas ? 48.0 / 120 : horizontalStep / 120;
        double verticalScale = e.HasPreciseScrollingDeltas ? 48.0 / 120 : verticalStep / 120;
        Horizontal = e.IsHorizontalDeltaHandled ? 0 : horizontal * horizontalScale;
        Vertical = e.IsVerticalDeltaHandled ? 0 : -vertical * verticalScale;
        if (_remapVertical)
        {
            Horizontal = e.IsVerticalDeltaHandled ? 0 : -vertical * horizontalScale;
            Vertical = 0;
        }
        if (!double.IsFinite(Horizontal)) Horizontal = 0;
        if (!double.IsFinite(Vertical)) Vertical = 0;
    }

    internal void MarkHandled(MouseWheelEventArgs e, bool horizontal, bool vertical)
    {
        if (horizontal)
        {
            if (_remapVertical) e.IsVerticalDeltaHandled = true;
            else e.IsHorizontalDeltaHandled = true;
        }
        if (vertical) e.IsVerticalDeltaHandled = true;
        if (_hasHorizontal || _hasVertical)
            e.Handled = (!_hasHorizontal || e.IsHorizontalDeltaHandled) &&
                (!_hasVertical || e.IsVerticalDeltaHandled);
    }
}
