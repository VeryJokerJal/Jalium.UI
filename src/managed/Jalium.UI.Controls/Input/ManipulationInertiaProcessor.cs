using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Drives manipulation inertia after all contacts have lifted.
/// Each tick decays the velocity exponentially, integrates a new delta and
/// raises a paired ManipulationDelta (IsInertial = true). When all three
/// velocity components fall below threshold (or the target's handler cancels),
/// raises a paired ManipulationCompleted (IsInertial = true) and stops.
///
/// Runs on the UI thread via <see cref="DispatcherTimer"/>; no worker thread,
/// no marshalling — the timer is already piggybacked on the render loop when
/// the interval matches a frame (16 ms).
/// </summary>
internal sealed class ManipulationInertiaProcessor
{
    // 60 fps. DispatcherTimer auto-promotes 16-ms intervals onto CompositionTarget.Rendering.
    private const double TickIntervalMs = 16.0;

    // Per-axis "considered stopped" thresholds.
    private const double LinearStopDipsPerMs = 0.005;       // ~5 DIP/sec
    private const double AngularStopDegPerMs = 0.018;       // ~1 deg/sec
    private const double ExpansionStopDipsPerMs = 0.005;

    // Exponential-decay rate when caller did not specify a DesiredDeceleration.
    // k = 0.002/ms ≈ velocity halves every ~350 ms.
    private const double DefaultDecayPerMs = 0.002;

    private readonly UIElement _target;
    private readonly Point _origin;
    private readonly DispatcherTimer _timer;

    private Vector _linearVelocity;
    private double _angularVelocity;       // deg / ms
    private Vector _expansionVelocity;     // DIP / ms (x,y)

    private double _kLinear;
    private double _kAngular;
    private double _kExpansion;

    private ManipulationDelta _cumulative;
    private long _lastTickTicks;
    private bool _running;

    public ManipulationInertiaProcessor(
        UIElement target,
        Point origin,
        ManipulationDelta cumulativeBefore,
        Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _target = target;
        _origin = origin;
        _cumulative = cumulativeBefore;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(TickIntervalMs),
            DispatcherPriority.Render,
            OnTick,
            dispatcher)
        {
            IsEnabled = false
        };
    }

    public bool IsRunning => _running;

    /// <summary>
    /// Begins integrating velocity. Returns false (and raises nothing) if the
    /// initial velocity is already below the stop threshold.
    /// </summary>
    public bool Start(
        Vector linearVelocity,
        double angularVelocity,
        Vector expansionVelocity,
        InertiaTranslationBehavior? translationBehavior,
        InertiaRotationBehavior? rotationBehavior,
        InertiaExpansionBehavior? expansionBehavior)
    {
        // Honour caller-provided initial velocity overrides (WPF behavior).
        // Note: `behavior?.InitialVelocity != default` would target-type `default`
        // to Vector? (null), collapsing the test to a mere null check and never
        // comparing the velocity value. Mirror the rotation branch below: null-check
        // the behavior, then compare InitialVelocity against the zero vector.
        if (translationBehavior != null && translationBehavior.InitialVelocity != default)
            linearVelocity = translationBehavior.InitialVelocity;
        if (rotationBehavior != null && rotationBehavior.InitialVelocity != 0)
            angularVelocity = rotationBehavior.InitialVelocity;
        if (expansionBehavior != null && expansionBehavior.InitialVelocity != default)
            expansionVelocity = expansionBehavior.InitialVelocity;

        _linearVelocity = linearVelocity;
        _angularVelocity = angularVelocity;
        _expansionVelocity = expansionVelocity;

        bool linearStopped = linearVelocity.Length < LinearStopDipsPerMs;
        bool angularStopped = Math.Abs(angularVelocity) < AngularStopDegPerMs;
        bool expansionStopped = expansionVelocity.Length < ExpansionStopDipsPerMs;
        if (linearStopped && angularStopped && expansionStopped)
            return false;

        _kLinear = ResolveDecay(translationBehavior?.DesiredDeceleration, linearVelocity.Length);
        _kAngular = ResolveDecay(rotationBehavior?.DesiredDeceleration, Math.Abs(angularVelocity));
        _kExpansion = ResolveDecay(expansionBehavior?.DesiredDeceleration, expansionVelocity.Length);

        _lastTickTicks = Environment.TickCount64;
        _running = true;
        _timer.IsEnabled = true;
        return true;
    }

    /// <summary>
    /// Stops the inertia integrator without raising additional events.
    /// </summary>
    public void Cancel()
    {
        if (!_running) return;
        _running = false;
        _timer.IsEnabled = false;
    }

    /// <summary>
    /// Manually advances the integrator one tick (or by <paramref name="dtOverrideMs"/> ms when given).
    /// Used by tests to drive the inertia loop deterministically without a real timer wait.
    /// </summary>
    internal void TickForTesting(double dtOverrideMs = TickIntervalMs)
    {
        if (!_running) return;
        long nowTicks = _lastTickTicks + (long)Math.Max(1.0, dtOverrideMs);
        AdvanceTick(nowTicks);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_running) return;
        AdvanceTick(Environment.TickCount64);
    }

    private void AdvanceTick(long nowTicks)
    {
        double dt = Math.Max(1.0, nowTicks - _lastTickTicks);
        _lastTickTicks = nowTicks;

        // Decay each velocity component independently.
        _linearVelocity = MultiplyVector(_linearVelocity, Math.Exp(-_kLinear * dt));
        _angularVelocity *= Math.Exp(-_kAngular * dt);
        _expansionVelocity = MultiplyVector(_expansionVelocity, Math.Exp(-_kExpansion * dt));

        // Integrate displacement for this tick.
        Vector deltaTranslation = MultiplyVector(_linearVelocity, dt);
        double deltaRotation = _angularVelocity * dt;
        Vector deltaExpansion = MultiplyVector(_expansionVelocity, dt);

        // Accumulate. Scale stays unitless multiplicative; Expansion carries the DIP delta.
        Vector newCumTranslation = _cumulative.Translation + deltaTranslation;
        double newCumRotation = _cumulative.Rotation + deltaRotation;
        Vector newCumExpansion = _cumulative.Expansion + deltaExpansion;
        Vector newCumScale = _cumulative.Scale; // expansion doesn't multiplicatively change scale

        ManipulationDelta deltaThisTick = new()
        {
            Translation = deltaTranslation,
            Rotation = deltaRotation,
            Expansion = deltaExpansion,
            Scale = new Vector(1, 1)
        };

        _cumulative = new ManipulationDelta
        {
            Translation = newCumTranslation,
            Rotation = newCumRotation,
            Expansion = newCumExpansion,
            Scale = newCumScale
        };

        ManipulationVelocities velocities = new()
        {
            LinearVelocity = _linearVelocity,
            AngularVelocity = _angularVelocity,
            ExpansionVelocity = _expansionVelocity
        };

        bool handlerHaltsInertia = RaiseDeltaTick(deltaThisTick, velocities);

        // Stop conditions: handler asked to stop, or all three axes below their threshold.
        bool linearStopped = _linearVelocity.Length < LinearStopDipsPerMs;
        bool angularStopped = Math.Abs(_angularVelocity) < AngularStopDegPerMs;
        bool expansionStopped = _expansionVelocity.Length < ExpansionStopDipsPerMs;
        if (handlerHaltsInertia || (linearStopped && angularStopped && expansionStopped))
        {
            Cancel();
            RaiseCompleted();
        }
    }

    /// <returns>True if the handler asked the engine to stop (Complete/Cancel).</returns>
    private bool RaiseDeltaTick(ManipulationDelta deltaThisTick, ManipulationVelocities velocities)
    {
        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationDeltaEvent,
            ManipulationContainer = _target,
            ManipulationOrigin = _origin,
            DeltaManipulation = deltaThisTick,
            CumulativeManipulation = _cumulative,
            Velocities = velocities,
            IsInertial = true
        };
        _target.RaiseEvent(previewArgs);

        ManipulationDeltaEventArgs bubbleArgs = previewArgs;
        if (!previewArgs.Handled)
        {
            bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationDeltaEvent,
                ManipulationContainer = _target,
                ManipulationOrigin = _origin,
                DeltaManipulation = deltaThisTick,
                CumulativeManipulation = _cumulative,
                Velocities = velocities,
                IsInertial = true
            };
            _target.RaiseEvent(bubbleArgs);
        }

        // Boundary feedback: if either handler reported an unused portion of the
        // delta (handler ran into a scroll boundary), surface it as a paired
        // ManipulationBoundaryFeedback event.
        ManipulationDelta? unused = previewArgs.UnusedManipulation ?? bubbleArgs.UnusedManipulation;
        if (unused != null && HasAny(unused))
            RaiseBoundaryFeedback(unused);

        return previewArgs.CompleteRequested || previewArgs.CancelRequested
            || bubbleArgs.CompleteRequested || bubbleArgs.CancelRequested;
    }

    private void RaiseBoundaryFeedback(ManipulationDelta unused)
    {
        ManipulationBoundaryFeedbackEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = _target,
            BoundaryFeedback = unused
        };
        _target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = _target,
                BoundaryFeedback = unused
            };
            _target.RaiseEvent(bubbleArgs);
        }
    }

    private void RaiseCompleted()
    {
        ManipulationVelocities finalVelocities = new()
        {
            LinearVelocity = _linearVelocity,
            AngularVelocity = _angularVelocity,
            ExpansionVelocity = _expansionVelocity
        };
        ManipulationCompletedEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationCompletedEvent,
            ManipulationContainer = _target,
            ManipulationOrigin = _origin,
            TotalManipulation = _cumulative,
            FinalVelocities = finalVelocities,
            IsInertial = true
        };
        _target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            ManipulationCompletedEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationCompletedEvent,
                ManipulationContainer = _target,
                ManipulationOrigin = _origin,
                TotalManipulation = _cumulative,
                FinalVelocities = finalVelocities,
                IsInertial = true
            };
            _target.RaiseEvent(bubbleArgs);
        }
    }

    private static double ResolveDecay(double? desiredDeceleration, double initialSpeed)
    {
        if (desiredDeceleration is double dec && !double.IsNaN(dec) && dec > 0 && initialSpeed > 0)
        {
            // Convert "DIP per ms^2" (linear deceleration) into the equivalent
            // exponential rate evaluated at t=0: dv/dt = -k * v0 ⇒ k = a/|v0|.
            // Higher initial velocities decelerate proportionally faster.
            return dec / initialSpeed;
        }
        return DefaultDecayPerMs;
    }

    private static Vector MultiplyVector(Vector v, double s) => new(v.X * s, v.Y * s);

    private static bool HasAny(ManipulationDelta delta) =>
        delta.Translation.Length > 0.0001
        || Math.Abs(delta.Rotation) > 0.0001
        || delta.Expansion.Length > 0.0001
        || delta.Scale.X != 1.0 || delta.Scale.Y != 1.0;
}
