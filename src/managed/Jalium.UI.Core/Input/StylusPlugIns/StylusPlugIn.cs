namespace Jalium.UI.Input.StylusPlugIns;

/// <summary>
/// Base class for stylus packet interception and processing.
/// </summary>
public abstract class StylusPlugIn
{
    private UIElement? _element;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, this plug-in's <c>OnStylusXxx</c> input
    /// hooks execute on the <see cref="RealTimeStylus"/> background thread —
    /// giving real-time, low-latency packet handling at the cost of being
    /// unable to touch UI-thread state directly (use
    /// <c>NotifyWhenProcessed</c> + <c>AddCustomData</c> to hand a result
    /// back to the UI thread).
    /// Default: <see langword="false"/> (UI-thread execution, identical to
    /// the previous behaviour). Set to <see langword="true"/> on rendering
    /// plug-ins such as <c>DynamicRenderer</c> where ink-stroke preview
    /// latency is visible.
    /// </summary>
    public bool IsRealTimeCapable { get; protected set; }

    public UIElement? Element => _element;

    public Rect ElementBounds =>
        _element is FrameworkElement frameworkElement
            ? frameworkElement.VisualBounds
            : Rect.Empty;

    protected virtual bool IsActiveForInput(RawStylusInput rawStylusInput) => true;

    protected virtual void OnAdded() { }
    protected virtual void OnRemoved() { }

    protected virtual void OnStylusDown(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusMove(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusUp(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusInAirMove(RawStylusInput rawStylusInput) { }

    protected virtual void OnStylusDownProcessed(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusMoveProcessed(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusUpProcessed(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusInAirMoveProcessed(RawStylusInput rawStylusInput) { }

    internal bool ShouldProcess(RawStylusInput rawStylusInput)
    {
        return Enabled && IsActiveForInput(rawStylusInput);
    }

    internal void InvokeInput(RawStylusInput rawStylusInput)
    {
        switch (rawStylusInput.Action)
        {
            case StylusInputAction.Down:
                OnStylusDown(rawStylusInput);
                break;
            case StylusInputAction.Move:
                OnStylusMove(rawStylusInput);
                break;
            case StylusInputAction.Up:
                OnStylusUp(rawStylusInput);
                break;
            case StylusInputAction.InAirMove:
                OnStylusInAirMove(rawStylusInput);
                break;
        }
    }

    internal void InvokeProcessed(RawStylusInput rawStylusInput)
    {
        switch (rawStylusInput.Action)
        {
            case StylusInputAction.Down:
                OnStylusDownProcessed(rawStylusInput);
                break;
            case StylusInputAction.Move:
                OnStylusMoveProcessed(rawStylusInput);
                break;
            case StylusInputAction.Up:
                OnStylusUpProcessed(rawStylusInput);
                break;
            case StylusInputAction.InAirMove:
                OnStylusInAirMoveProcessed(rawStylusInput);
                break;
        }
    }

    internal void Attach(UIElement element)
    {
        if (_element != null && !ReferenceEquals(_element, element))
        {
            throw new InvalidOperationException("StylusPlugIn is already attached to another element.");
        }

        _element = element;
        OnAdded();
    }

    internal void Detach()
    {
        if (_element == null)
        {
            return;
        }

        OnRemoved();
        _element = null;
    }
}
