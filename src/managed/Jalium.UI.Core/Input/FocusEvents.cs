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

    internal ManipulationStartedEventArgs()
    {
    }

    public IInputElement ManipulationContainer { get; internal init; } = null!;
    public Point ManipulationOrigin { get; internal init; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    public void Complete()
    {
        CompleteRequested = true;
        _cancelRequested = false;
    }

    public bool Cancel()
    {
        _cancelRequested = true;
        CompleteRequested = false;
        return true;
    }

    internal bool CancelRequested => _cancelRequested;
    internal bool CompleteRequested { get; private set; }

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
    internal ManipulationDeltaEventArgs()
    {
    }

    public IInputElement ManipulationContainer { get; internal init; } = null!;
    public Point ManipulationOrigin { get; internal init; }
    public ManipulationDelta DeltaManipulation { get; internal init; } = null!;
    public ManipulationDelta CumulativeManipulation { get; internal init; } = null!;
    public ManipulationVelocities Velocities { get; internal init; } = null!;
    public bool IsInertial { get; internal init; }
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
    public void Complete()
    {
        CompleteRequested = true;
        StartInertiaRequested = false;
        CancelRequested = false;
    }

    /// <summary>
    /// Cancels the active manipulation. Any subsequent input is treated as fresh pointer input.
    /// </summary>
    public bool Cancel()
    {
        if (IsInertial)
            return false;

        CancelRequested = true;
        CompleteRequested = false;
        StartInertiaRequested = false;
        return true;
    }

    /// <summary>
    /// Hints the engine that inertia should begin immediately, even while contacts remain.
    /// </summary>
    public void StartInertia()
    {
        CompleteRequested = true;
        StartInertiaRequested = true;
        CancelRequested = false;
    }

    /// <summary>
    /// Reports the portion of the delta the handler could not apply (e.g. clamped to a scrollable boundary).
    /// The engine raises a <c>ManipulationBoundaryFeedback</c> event so containers can show overscroll feedback.
    /// </summary>
    public void ReportBoundaryFeedback(ManipulationDelta unusedManipulation)
    {
        ArgumentNullException.ThrowIfNull(unusedManipulation);
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

    internal ManipulationCompletedEventArgs()
    {
    }

    public IInputElement ManipulationContainer { get; internal init; } = null!;
    public Point ManipulationOrigin { get; internal init; }
    public ManipulationDelta TotalManipulation { get; internal init; } = null!;
    public ManipulationVelocities FinalVelocities { get; internal init; } = null!;
    public bool IsInertial { get; internal init; }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    public bool Cancel()
    {
        if (IsInertial)
            return false;

        _cancelRequested = true;
        return true;
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
    private readonly List<Manipulations.InertiaParameters2D> _inertiaParameters = [];
    private InertiaTranslationBehavior? _translationBehavior;
    private InertiaRotationBehavior? _rotationBehavior;
    private InertiaExpansionBehavior? _expansionBehavior;

    internal ManipulationInertiaStartingEventArgs()
    {
    }

    public IInputElement ManipulationContainer { get; internal init; } = null!;
    public Point ManipulationOrigin { get; set; }
    public ManipulationVelocities InitialVelocities { get; internal init; } = null!;
    public InertiaTranslationBehavior TranslationBehavior
    {
        get => _translationBehavior ??= new InertiaTranslationBehavior(InitialVelocities.LinearVelocity);
        set => _translationBehavior = value;
    }
    public InertiaRotationBehavior RotationBehavior
    {
        get => _rotationBehavior ??= new InertiaRotationBehavior(InitialVelocities.AngularVelocity);
        set => _rotationBehavior = value;
    }
    public InertiaExpansionBehavior ExpansionBehavior
    {
        get => _expansionBehavior ??= new InertiaExpansionBehavior(InitialVelocities.ExpansionVelocity);
        set => _expansionBehavior = value;
    }
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    internal bool CancelRequested { get; private set; }
    internal bool IsInInertia { get; init; }

    [System.ComponentModel.Browsable(false)]
    public void SetInertiaParameter(Manipulations.InertiaParameters2D parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _inertiaParameters.Add(parameter);
    }

    internal IReadOnlyList<Manipulations.InertiaParameters2D> InertiaParameters => _inertiaParameters;

    public bool Cancel()
    {
        if (IsInInertia)
            return false;

        CancelRequested = true;
        return true;
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
public class ManipulationVelocities
{
    internal ManipulationVelocities()
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

    public Vector LinearVelocity { get; internal init; }
    public double AngularVelocity { get; internal init; }
    public Vector ExpansionVelocity { get; internal init; }
}

public class InertiaTranslationBehavior
{
    private Vector _initialVelocity = new(double.NaN, double.NaN);
    private double _desiredDisplacement = double.NaN;
    private double _desiredDeceleration = double.NaN;

    public InertiaTranslationBehavior()
    {
    }

    internal InertiaTranslationBehavior(Vector initialVelocity)
    {
        _initialVelocity = initialVelocity;
    }

    public Vector InitialVelocity
    {
        get => _initialVelocity;
        set
        {
            IsInitialVelocitySet = true;
            _initialVelocity = value;
        }
    }

    public double DesiredDisplacement
    {
        get => _desiredDisplacement;
        set
        {
            ThrowIfInvalidFinite(value);
            IsDesiredDisplacementSet = true;
            _desiredDisplacement = value;
            IsDesiredDecelerationSet = false;
            _desiredDeceleration = double.NaN;
        }
    }

    public double DesiredDeceleration
    {
        get => _desiredDeceleration;
        set
        {
            ThrowIfInvalidFinite(value);
            IsDesiredDecelerationSet = true;
            _desiredDeceleration = value;
            IsDesiredDisplacementSet = false;
            _desiredDisplacement = double.NaN;
        }
    }

    internal bool IsInitialVelocitySet { get; private set; }
    internal bool IsDesiredDisplacementSet { get; private set; }
    internal bool IsDesiredDecelerationSet { get; private set; }
    internal bool CanUseForInertia => IsInitialVelocitySet || IsDesiredDisplacementSet || IsDesiredDecelerationSet;

    private static void ThrowIfInvalidFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public class InertiaRotationBehavior
{
    private double _initialVelocity = double.NaN;
    private double _desiredRotation = double.NaN;
    private double _desiredDeceleration = double.NaN;

    public InertiaRotationBehavior()
    {
    }

    internal InertiaRotationBehavior(double initialVelocity)
    {
        _initialVelocity = initialVelocity;
    }

    public double InitialVelocity
    {
        get => _initialVelocity;
        set
        {
            IsInitialVelocitySet = true;
            _initialVelocity = value;
        }
    }

    public double DesiredRotation
    {
        get => _desiredRotation;
        set
        {
            ThrowIfInvalidFinite(value);
            IsDesiredRotationSet = true;
            _desiredRotation = value;
            IsDesiredDecelerationSet = false;
            _desiredDeceleration = double.NaN;
        }
    }

    public double DesiredDeceleration
    {
        get => _desiredDeceleration;
        set
        {
            ThrowIfInvalidFinite(value);
            IsDesiredDecelerationSet = true;
            _desiredDeceleration = value;
            IsDesiredRotationSet = false;
            _desiredRotation = double.NaN;
        }
    }

    internal bool IsInitialVelocitySet { get; private set; }
    internal bool IsDesiredRotationSet { get; private set; }
    internal bool IsDesiredDecelerationSet { get; private set; }
    internal bool CanUseForInertia => IsInitialVelocitySet || IsDesiredRotationSet || IsDesiredDecelerationSet;

    private static void ThrowIfInvalidFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public class InertiaExpansionBehavior
{
    private Vector _initialVelocity = new(double.NaN, double.NaN);
    private Vector _desiredExpansion = new(double.NaN, double.NaN);
    private double _desiredDeceleration = double.NaN;
    private double _initialRadius = 1.0;

    public InertiaExpansionBehavior()
    {
    }

    internal InertiaExpansionBehavior(Vector initialVelocity)
    {
        _initialVelocity = initialVelocity;
    }

    public Vector InitialVelocity
    {
        get => _initialVelocity;
        set
        {
            IsInitialVelocitySet = true;
            _initialVelocity = value;
        }
    }

    public Vector DesiredExpansion
    {
        get => _desiredExpansion;
        set
        {
            IsDesiredExpansionSet = true;
            _desiredExpansion = value;
            IsDesiredDecelerationSet = false;
            _desiredDeceleration = double.NaN;
        }
    }

    public double DesiredDeceleration
    {
        get => _desiredDeceleration;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            IsDesiredDecelerationSet = true;
            _desiredDeceleration = value;
            IsDesiredExpansionSet = false;
            _desiredExpansion = new Vector(double.NaN, double.NaN);
        }
    }

    public double InitialRadius
    {
        get => _initialRadius;
        set
        {
            IsInitialRadiusSet = true;
            _initialRadius = value;
        }
    }

    internal bool IsInitialVelocitySet { get; private set; }
    internal bool IsDesiredExpansionSet { get; private set; }
    internal bool IsDesiredDecelerationSet { get; private set; }
    internal bool IsInitialRadiusSet { get; private set; }
    internal bool CanUseForInertia =>
        IsInitialVelocitySet || IsDesiredExpansionSet || IsDesiredDecelerationSet || IsInitialRadiusSet;
}

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
