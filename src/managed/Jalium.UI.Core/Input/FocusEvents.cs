using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for keyboard-focus change events.
/// </summary>
public sealed class FocusChangedEventArgs : EventArgs
{
    public FocusChangedEventArgs(UIElement? oldFocus, UIElement? newFocus)
    {
        OldFocus = oldFocus;
        NewFocus = newFocus;
    }
    public UIElement? OldFocus { get; }
    public UIElement? NewFocus { get; }
}

/// <summary>
/// Provides data for manipulation started events.
/// </summary>
public sealed class ManipulationStartedEventArgs : InputEventArgs
{
    private bool _cancelRequested;

    public IInputElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    public void Complete() { }
    public bool Cancel()
    {
        bool wasCanceled = _cancelRequested;
        _cancelRequested = true;
        return !wasCanceled;
    }

    internal bool CancelRequested => _cancelRequested;

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationStartedEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationStartedEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Provides data for manipulation delta events.
/// </summary>
public sealed class ManipulationDeltaEventArgs : InputEventArgs
{
    public IInputElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public ManipulationDelta? DeltaManipulation { get; init; }
    public ManipulationDelta? CumulativeManipulation { get; init; }
    public ManipulationVelocities? Velocities { get; init; }
    public bool IsInertial { get; init; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    /// <summary>
    /// Portion of <see cref="DeltaManipulation"/> the handler did not consume.
    /// Populated by <see cref="ReportBoundaryFeedback"/>; consumed by the inertia
    /// processor to raise a paired ManipulationBoundaryFeedback event.
    /// </summary>
    internal ManipulationDelta? UnusedManipulation { get; private set; }

    /// <summary>
    /// True when the handler asked to short-circuit the rest of the manipulation pipeline
    /// (transition into inertia immediately, or terminate without inertia).
    /// </summary>
    internal bool CompleteRequested { get; private set; }
    internal bool CancelRequested { get; private set; }
    internal bool StartInertiaRequested { get; private set; }

    /// <summary>
    /// Terminates the active manipulation. No inertia is generated.
    /// </summary>
    public void Complete() => CompleteRequested = true;

    /// <summary>
    /// Cancels the active manipulation. Any subsequent input is treated as fresh pointer input.
    /// </summary>
    public bool Cancel()
    {
        bool wasCanceled = CancelRequested;
        CancelRequested = true;
        return !wasCanceled;
    }

    /// <summary>
    /// Hints the engine that inertia should begin immediately, even while contacts remain.
    /// </summary>
    public void StartInertia() => StartInertiaRequested = true;

    /// <summary>
    /// Reports the portion of the delta the handler could not apply (e.g. clamped to a scrollable boundary).
    /// The engine raises a <c>ManipulationBoundaryFeedback</c> event so containers can show overscroll feedback.
    /// </summary>
    public void ReportBoundaryFeedback(ManipulationDelta unusedManipulation)
    {
        UnusedManipulation = unusedManipulation;
    }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationDeltaEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationDeltaEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Provides data for manipulation completed events.
/// </summary>
public sealed class ManipulationCompletedEventArgs : InputEventArgs
{
    private bool _cancelRequested;

    public IInputElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public ManipulationDelta? TotalManipulation { get; init; }
    public ManipulationVelocities? FinalVelocities { get; init; }
    public bool IsInertial { get; init; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    public bool Cancel()
    {
        bool wasCanceled = _cancelRequested;
        _cancelRequested = true;
        return !wasCanceled;
    }

    internal bool CancelRequested => _cancelRequested;

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationCompletedEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationCompletedEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Provides data for manipulation inertia starting events.
/// </summary>
public sealed class ManipulationInertiaStartingEventArgs : InputEventArgs
{
    public IInputElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; set; }
    public ManipulationVelocities? InitialVelocities { get; init; }
    public InertiaTranslationBehavior? TranslationBehavior { get; set; }
    public InertiaRotationBehavior? RotationBehavior { get; set; }
    public InertiaExpansionBehavior? ExpansionBehavior { get; set; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    internal bool CancelRequested { get; private set; }
    internal bool CompleteRequested { get; private set; }

    public void SetInertiaParameter(InertiaParameters2D parameter) { }
    public void Complete() => CompleteRequested = true;
    public bool Cancel()
    {
        bool wasCanceled = CancelRequested;
        CancelRequested = true;
        return !wasCanceled;
    }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationInertiaStartingEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationInertiaStartingEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Specifies the velocities of a manipulation.
/// </summary>
public sealed class ManipulationVelocities
{
    public ManipulationVelocities()
    {
    }

    public ManipulationVelocities(
        Vector linearVelocity,
        double angularVelocity,
        Vector expansionVelocity)
    {
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
        ExpansionVelocity = expansionVelocity;
    }

    public Vector LinearVelocity { get; init; }
    public double AngularVelocity { get; init; }
    public Vector ExpansionVelocity { get; init; }
}

public sealed class InertiaTranslationBehavior
{
    public Vector InitialVelocity { get; set; }
    public double DesiredDisplacement { get; set; } = double.NaN;
    public double DesiredDeceleration { get; set; } = double.NaN;
}

public sealed class InertiaRotationBehavior
{
    public double InitialVelocity { get; set; }
    public double DesiredRotation { get; set; } = double.NaN;
    public double DesiredDeceleration { get; set; } = double.NaN;
}

public sealed class InertiaExpansionBehavior
{
    public Vector InitialVelocity { get; set; }
    public double DesiredExpansion { get; set; } = double.NaN;
    public double DesiredDeceleration { get; set; } = double.NaN;
    public Vector InitialRadius { get; set; }
}

public abstract class InertiaParameters2D { }

/// <summary>
/// Provides data for touch frame events.
/// </summary>
public sealed class TouchFrameEventArgs : EventArgs
{
    internal TouchFrameEventArgs(int timestamp)
    {
        Timestamp = timestamp;
    }

    public int Timestamp { get; }
    public TouchPointCollection GetTouchPoints(IInputElement? relativeTo) => TouchDevice.GetTouchPoints(relativeTo);
    public TouchPoint? GetPrimaryTouchPoint(IInputElement? relativeTo) => TouchDevice.GetPrimaryTouchPoint(relativeTo);
    public void SuspendMousePromotionUntilTouchUp() { }
}

public delegate void TouchFrameEventHandler(object sender, TouchFrameEventArgs e);
