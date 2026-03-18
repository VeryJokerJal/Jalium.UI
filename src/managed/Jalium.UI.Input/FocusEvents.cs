using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for keyboard focus change events.
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
/// Provides data for QueryCursor events.
/// </summary>
public sealed class QueryCursorEventArgs : InputEventArgs
{
    public Cursor? Cursor { get; set; }
}

public delegate void QueryCursorEventHandler(object sender, QueryCursorEventArgs e);

/// <summary>
/// Provides data for manipulation started events.
/// </summary>
public sealed class ManipulationStartedEventArgs : InputEventArgs
{
    public UIElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public bool Cancel { get; set; }

    public void Complete() { }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationStartedEventHandler typedHandler)
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
    public UIElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public ManipulationDelta? DeltaManipulation { get; init; }
    public ManipulationDelta? CumulativeManipulation { get; init; }
    public ManipulationVelocities? Velocities { get; init; }
    public bool IsInertial { get; init; }

    public void Complete() { }
    public void Cancel() { }
    public void StartInertia() { }
    public void ReportBoundaryFeedback(ManipulationDelta unusedManipulation) { }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationDeltaEventHandler typedHandler)
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
    public UIElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public ManipulationDelta? TotalManipulation { get; init; }
    public ManipulationVelocities? FinalVelocities { get; init; }
    public bool IsInertial { get; init; }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationCompletedEventHandler typedHandler)
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
    public UIElement? ManipulationContainer { get; init; }
    public Point ManipulationOrigin { get; init; }
    public ManipulationVelocities? InitialVelocities { get; init; }
    public InertiaTranslationBehavior? TranslationBehavior { get; set; }
    public InertiaRotationBehavior? RotationBehavior { get; set; }
    public InertiaExpansionBehavior? ExpansionBehavior { get; set; }

    public void SetInertiaParameter(InertiaParameters2D parameter) { }
    public void Complete() { }
    public void Cancel() { }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationInertiaStartingEventHandler typedHandler)
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
    public int Timestamp { get; init; }
    public TouchPointCollection GetTouchPoints(UIElement? relativeTo) => new();
    public TouchPoint? GetPrimaryTouchPoint(UIElement? relativeTo) => null;
    public void SuspendMousePromotionUntilTouchUp() { }
}

public delegate void TouchFrameEventHandler(object sender, TouchFrameEventArgs e);
