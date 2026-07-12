namespace Jalium.UI.Input.StylusPlugIns;

/// <summary>
/// Base class for stylus packet interception and processing.
/// </summary>
public abstract class StylusPlugIn
{
    private UIElement? _element;
    private bool _enabled = true;
    private bool _lastIsActiveForInput;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            OnEnabledChanged();
            NotifyIsActiveForInputChangedIfNeeded();
        }
    }

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

    /// <summary>Gets whether the plug-in is currently eligible for input.</summary>
    public bool IsActiveForInput =>
        _enabled && _element is { IsEnabled: true, IsHitTestVisible: true, IsVisible: true };

    /// <summary>Allows derived plug-ins to apply packet-specific filtering.</summary>
    protected virtual bool IsActiveForInputCore(RawStylusInput rawStylusInput) => true;

    protected virtual void OnAdded() { }
    protected virtual void OnRemoved() { }
    protected virtual void OnEnabledChanged() { }
    protected virtual void OnIsActiveForInputChanged() { }

    protected virtual void OnStylusDown(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusMove(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusUp(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusInAirMove(RawStylusInput rawStylusInput) { }
    protected virtual void OnStylusEnter(RawStylusInput rawStylusInput, bool confirmed) { }
    protected virtual void OnStylusLeave(RawStylusInput rawStylusInput, bool confirmed) { }

    protected virtual void OnStylusDownProcessed(object callbackData, bool targetVerified) { }
    protected virtual void OnStylusMoveProcessed(object callbackData, bool targetVerified) { }
    protected virtual void OnStylusUpProcessed(object callbackData, bool targetVerified) { }
    protected virtual void OnStylusInAirMoveProcessed(object callbackData, bool targetVerified) { }

    internal bool ShouldProcess(RawStylusInput rawStylusInput)
    {
        return IsActiveForInput && IsActiveForInputCore(rawStylusInput);
    }

    internal void InvokeInput(RawStylusInput rawStylusInput)
    {
        rawStylusInput.BeginPlugInInvocation(this);
        try
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
        finally
        {
            rawStylusInput.EndPlugInInvocation(this);
        }
    }

    internal void InvokeProcessed(
        StylusInputAction action,
        object callbackData,
        bool targetVerified)
    {
        switch (action)
        {
            case StylusInputAction.Down:
                OnStylusDownProcessed(callbackData, targetVerified);
                break;
            case StylusInputAction.Move:
                OnStylusMoveProcessed(callbackData, targetVerified);
                break;
            case StylusInputAction.Up:
                OnStylusUpProcessed(callbackData, targetVerified);
                break;
            case StylusInputAction.InAirMove:
                OnStylusInAirMoveProcessed(callbackData, targetVerified);
                break;
        }
    }

    internal void InvokeStylusEnter(RawStylusInput rawStylusInput, bool confirmed)
    {
        rawStylusInput.BeginPlugInInvocation(this);
        try
        {
            OnStylusEnter(rawStylusInput, confirmed);
        }
        finally
        {
            rawStylusInput.EndPlugInInvocation(this);
        }
    }

    internal void InvokeStylusLeave(RawStylusInput rawStylusInput, bool confirmed)
    {
        rawStylusInput.BeginPlugInInvocation(this);
        try
        {
            OnStylusLeave(rawStylusInput, confirmed);
        }
        finally
        {
            rawStylusInput.EndPlugInInvocation(this);
        }
    }

    internal void Attach(UIElement element)
    {
        if (_element != null && !ReferenceEquals(_element, element))
        {
            throw new InvalidOperationException("StylusPlugIn is already attached to another element.");
        }

        _element = element;
        element.IsEnabledChanged += OnElementInputStateChanged;
        element.IsHitTestVisibleChanged += OnElementInputStateChanged;
        element.IsVisibleChanged += OnElementInputStateChanged;
        OnAdded();
        NotifyIsActiveForInputChangedIfNeeded();
    }

    internal void Detach()
    {
        if (_element == null)
        {
            return;
        }

        OnRemoved();
        _element.IsEnabledChanged -= OnElementInputStateChanged;
        _element.IsHitTestVisibleChanged -= OnElementInputStateChanged;
        _element.IsVisibleChanged -= OnElementInputStateChanged;
        _element = null;
        NotifyIsActiveForInputChangedIfNeeded();
    }

    private void OnElementInputStateChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        NotifyIsActiveForInputChangedIfNeeded();

    private void NotifyIsActiveForInputChangedIfNeeded()
    {
        bool isActive = IsActiveForInput;
        if (_lastIsActiveForInput == isActive)
            return;
        _lastIsActiveForInput = isActive;
        OnIsActiveForInputChanged();
    }
}
