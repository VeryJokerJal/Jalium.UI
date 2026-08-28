using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Internal element that renders text content for TextBoxBase controls.
/// This element is inserted into the PART_ContentHost of the control's template.
/// Similar to WPF's TextBoxView.
/// </summary>
internal sealed class TextBoxContentHost : FrameworkElement
{
    private readonly TextBoxBase _owner;

    // Track the width used during MeasureOverride so ArrangeOverride can spot
    // the Infinity-measure / finite-arrange mismatch that otherwise leaves
    // the reported DesiredSize based on unwrapped text while the renderer
    // wraps to the (narrower) arrange width.
    private double _lastMeasureWidth = double.NaN;

    // Convergence state for the corrective re-measure below. The arrange width the
    // last request was made for, and how many requests have been made without the
    // layout ever settling.
    private double _remeasureRequestedForArrangeWidth = double.NaN;
    private int _unsettledRemeasureRequests;

    private const int MaxUnsettledRemeasureRequests = 3;

    /// <summary>
    /// Initializes a new instance of the TextBoxContentHost class.
    /// </summary>
    /// <param name="owner">The owning TextBoxBase control.</param>
    public TextBoxContentHost(TextBoxBase owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        // This element should be hit-testable so clicks are routed through it
        IsHitTestVisible = true;
    }

    /// <summary>
    /// Gets the owning TextBoxBase control.
    /// </summary>
    public TextBoxBase Owner => _owner;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // A different constraint from the parent is new information: whatever we
        // concluded about the previous constraint no longer applies, so allow the
        // corrective request below to fire again.
        if (!WidthsMatch(_lastMeasureWidth, availableSize.Width))
        {
            _remeasureRequestedForArrangeWidth = double.NaN;
        }

        _lastMeasureWidth = availableSize.Width;
        return _owner.MeasureTextContent(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // If the width we were arranged with differs from the width we were
        // last measured with, the DesiredSize we reported is based on the
        // wrong wrap width — the renderer will actually wrap to finalSize.Width
        // and may produce more (or fewer) rows than we reported. Ask for
        // another measure pass so the enclosing ScrollViewer/parent picks up
        // the correct height and the user can scroll to the end of wrapped
        // content.
        //
        // That request MUST be self-limiting. Asking unconditionally is only safe
        // when the parent then measures us with the width it arranged us at — and a
        // parent that measures with Infinity (any ScrollViewer that allows
        // horizontal scrolling, which is what a TextBox template wraps
        // PART_ContentHost in) never will. It re-measures with Infinity, arranges at
        // the same finite width, and the mismatch is back: arrange invalidates
        // measure, the frame's layout pass re-measures and re-arranges, arrange
        // invalidates measure again — a layout loop that never settles and pins the
        // UI thread at 100% of a core for the lifetime of the window. (Observed as
        // "DevTools' first page is very laggy": the Inspector's filter box hits this
        // the moment the tab is re-shown, and every window on that thread — the
        // inspected app included — then competes with a spinning layout pass.)
        //
        // So: at most one request per arrange width, and at most a few before giving
        // up entirely. A parent that honours the request settles on the first pass;
        // one that does not simply stops being asked. Either way layout terminates.
        bool mismatched = !double.IsNaN(_lastMeasureWidth) && !WidthsMatch(_lastMeasureWidth, finalSize.Width);

        if (mismatched)
        {
            bool alreadyRequestedForThisWidth = WidthsMatch(_remeasureRequestedForArrangeWidth, finalSize.Width);
            if (!alreadyRequestedForThisWidth && _unsettledRemeasureRequests < MaxUnsettledRemeasureRequests)
            {
                _remeasureRequestedForArrangeWidth = finalSize.Width;
                _unsettledRemeasureRequests++;
                InvalidateMeasure();
            }
        }
        else
        {
            // Measure and arrange agree — the layout settled, so the budget resets and
            // a genuine later change gets its corrective pass again.
            _remeasureRequestedForArrangeWidth = double.NaN;
            _unsettledRemeasureRequests = 0;
        }

        _owner.ArrangeTextContent(finalSize);
        return finalSize;
    }

    /// <summary>
    /// Width comparison that treats Infinity as its own distinct value: an infinite
    /// measure and a finite arrange never "match", and NaN (no pass yet) matches nothing.
    /// </summary>
    private static bool WidthsMatch(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return false;
        if (double.IsInfinity(a) || double.IsInfinity(b)) return double.IsInfinity(a) && double.IsInfinity(b);
        return Math.Abs(a - b) <= 0.5;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        // Delegate rendering to the owner
        _owner.RenderTextContent(drawingContext);
    }

    /// <summary>
    /// Opts out of the retained-mode drawing cache.
    /// </summary>
    /// <remarks>
    /// <see cref="OnRender"/> is a pure delegator to <see cref="TextBoxBase.RenderTextContent"/>;
    /// all rendering state (caret index, selection span, scroll offset,
    /// spell-check squiggles, syntax colours, …) lives on the owner, not on
    /// this visual. The retained-mode cache keyed off this visual's own
    /// <c>_isRenderDirty</c> flag therefore cannot track the real dirty
    /// state — an <c>InvalidateVisual</c> on the owner flips only its own
    /// flag, and this proxy keeps replaying last frame's command list (e.g.
    /// the old selection rectangle). Rendering in immediate mode every
    /// frame is both correct and cheap here: the owner's draw routine is
    /// already a small fixed set of text/selection/caret primitives.
    /// </remarks>
    protected override bool ParticipatesInRenderCache => false;
}
