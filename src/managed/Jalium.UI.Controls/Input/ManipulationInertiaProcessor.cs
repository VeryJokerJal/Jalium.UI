using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Drives manipulation inertia after all contacts have lifted.
/// Each tick integrates the selected stopping curve and
/// raises a paired ManipulationDelta (IsInertial = true). When all stopping
/// curves finish (or the target's handler cancels),
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
    private const double AngularStopDegPerMs = 0.018;       // ~18 deg/sec
    private const double ExpansionStopDipsPerMs = 0.005;

    private readonly UIElement _target;
    private readonly Point _origin;
    private readonly Func<bool>? _lifetime;
    private readonly Action? _faulted;
    private readonly Func<(IInputElement Container, Point Origin)>? _coordinates;
    private readonly Action<ManipulationDelta, ManipulationVelocities>? _completion;
    private readonly DispatcherTimer _timer;

    private Vector _linearVelocity;
    private double _angularVelocity;       // deg / ms
    private Vector _expansionVelocity;     // DIP / ms (x,y)

    private InertiaMotion _translation;
    private InertiaMotion _rotation;
    private InertiaMotion _expansionX;
    private InertiaMotion _expansionY;
    private Vector _translationDirection;
    private double _radiusX;
    private double _radiusY;

    private ManipulationDelta _cumulative;
    private long _lastTickTicks;
    private bool _running;

    public ManipulationInertiaProcessor(
        UIElement target,
        Point origin,
        ManipulationDelta cumulativeBefore,
        Dispatcher dispatcher,
        Func<bool>? lifetime = null,
        Func<(IInputElement Container, Point Origin)>? coordinates = null,
        Action<ManipulationDelta, ManipulationVelocities>? completion = null,
        Action? faulted = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _target = target;
        _origin = origin;
        _lifetime = lifetime; _coordinates = coordinates; _completion = completion; _faulted = faulted;
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
    internal ManipulationDelta Cumulative => _cumulative;
    internal ManipulationVelocities CurrentVelocities => new()
    {
        LinearVelocity = _linearVelocity, AngularVelocity = _angularVelocity, ExpansionVelocity = _expansionVelocity
    };
    private bool IsCurrent => _lifetime?.Invoke() != false;
    private (IInputElement Container, Point Origin) Coordinates => _coordinates?.Invoke() ?? (_target, _origin);
    private bool Dispatch(InputEventArgs args)
    {
        if (!_running || !IsCurrent) return false;
        InputRoute.Raise(_target, args, () => _running && IsCurrent);
        return _running && IsCurrent;
    }

    /// <summary>
    /// Begins integrating velocity. Returns false (and raises nothing) if the
    /// initial velocity is zero or below the default curve's stop threshold.
    /// </summary>
    public bool Start(
        Vector linearVelocity,
        double angularVelocity,
        Vector expansionVelocity,
        InertiaTranslationBehavior? translationBehavior,
        InertiaRotationBehavior? rotationBehavior,
        InertiaExpansionBehavior? expansionBehavior)
        => Start(linearVelocity, angularVelocity, expansionVelocity, translationBehavior,
            rotationBehavior, expansionBehavior, ManipulationModes.All, 1);

    internal bool Start(
        Vector linearVelocity, double angularVelocity, Vector expansionVelocity,
        InertiaTranslationBehavior? translationBehavior, InertiaRotationBehavior? rotationBehavior,
        InertiaExpansionBehavior? expansionBehavior, ManipulationModes modes, double initialExpansionRadius = 1)
    {
        Cancel();
        // Honour caller-provided initial velocity overrides (WPF behavior).
        // Note: `behavior?.InitialVelocity != default` would target-type `default`
        // to Vector? (null), collapsing the test to a mere null check and never
        // comparing the velocity value. Mirror the rotation branch below: null-check
        // the behavior, then compare InitialVelocity against the zero vector.
        if (translationBehavior?.IsInitialVelocitySet == true)
            linearVelocity = translationBehavior.InitialVelocity;
        if (rotationBehavior?.IsInitialVelocitySet == true)
            angularVelocity = rotationBehavior.InitialVelocity;
        if (expansionBehavior?.IsInitialVelocitySet == true)
            expansionVelocity = expansionBehavior.InitialVelocity;

        if (!Finite(linearVelocity) || !double.IsFinite(linearVelocity.Length))
            throw new ArgumentOutOfRangeException(nameof(linearVelocity));
        if (!double.IsFinite(angularVelocity)) throw new ArgumentOutOfRangeException(nameof(angularVelocity));
        if (!Finite(expansionVelocity) || !double.IsFinite(expansionVelocity.Length))
            throw new ArgumentOutOfRangeException(nameof(expansionVelocity));
        if ((modes & ManipulationModes.TranslateX) == 0) linearVelocity = new(0, linearVelocity.Y);
        if ((modes & ManipulationModes.TranslateY) == 0) linearVelocity = new(linearVelocity.X, 0);
        if ((modes & ManipulationModes.Rotate) == 0) angularVelocity = 0;
        if ((modes & ManipulationModes.Scale) == 0) expansionVelocity = Vector.Zero;
        double radius = expansionBehavior?.IsInitialRadiusSet == true ? expansionBehavior.InitialRadius : initialExpansionRadius;
        if (!double.IsFinite(radius) || radius < 1) throw new ArgumentOutOfRangeException(nameof(initialExpansionRadius));
        _radiusX = _radiusY = radius;
        if (radius == 1)
            expansionVelocity = new(Math.Max(0, expansionVelocity.X), Math.Max(0, expansionVelocity.Y));

        double speed = linearVelocity.Length;
        _translationDirection = speed > 0 ? MultiplyVector(linearVelocity, 1 / speed) : Vector.Zero;
        _translation = InertiaMotion.Create(speed, LinearStopDipsPerMs,
            translationBehavior?.IsDesiredDecelerationSet == true ? translationBehavior.DesiredDeceleration : null,
            translationBehavior?.IsDesiredDisplacementSet == true ? translationBehavior.DesiredDisplacement : null);
        _rotation = InertiaMotion.Create(angularVelocity, AngularStopDegPerMs,
            rotationBehavior?.IsDesiredDecelerationSet == true ? rotationBehavior.DesiredDeceleration : null,
            rotationBehavior?.IsDesiredRotationSet == true ? rotationBehavior.DesiredRotation : null);
        // Keep the default expansion's vector-magnitude threshold and common
        // stopping time, including small diagonal movement.
        double expansionSpeed = expansionVelocity.Length;
        double expansionThresholdX = expansionSpeed > 0
            ? Math.Abs(expansionVelocity.X) / expansionSpeed * ExpansionStopDipsPerMs : ExpansionStopDipsPerMs;
        double expansionThresholdY = expansionSpeed > 0
            ? Math.Abs(expansionVelocity.Y) / expansionSpeed * ExpansionStopDipsPerMs : ExpansionStopDipsPerMs;
        _expansionX = InertiaMotion.Create(expansionVelocity.X, expansionThresholdX,
            expansionBehavior?.IsDesiredDecelerationSet == true ? expansionBehavior.DesiredDeceleration : null,
            expansionBehavior?.IsDesiredExpansionSet == true ? expansionBehavior.DesiredExpansion.X : null);
        _expansionY = InertiaMotion.Create(expansionVelocity.Y, expansionThresholdY,
            expansionBehavior?.IsDesiredDecelerationSet == true ? expansionBehavior.DesiredDeceleration : null,
            expansionBehavior?.IsDesiredExpansionSet == true ? expansionBehavior.DesiredExpansion.Y : null);
        UpdateVelocities();
        if (MotionComplete) return false;

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
        try { AdvanceTickCore(nowTicks); }
        catch { Cancel(); _faulted?.Invoke(); throw; }
    }

    private void AdvanceTickCore(long nowTicks)
    {
        if (!IsCurrent) { Cancel(); _completion?.Invoke(_cumulative, CurrentVelocities); return; }
        double dt = Math.Max(1.0, nowTicks - _lastTickTicks);
        _lastTickTicks = nowTicks;

        Vector deltaTranslation = MultiplyVector(_translationDirection, _translation.Advance(dt));
        double deltaRotation = _rotation.Advance(dt);
        double oldRadiusX = _radiusX, oldRadiusY = _radiusY;
        double expansionX = _expansionX.Advance(dt), expansionY = _expansionY.Advance(dt);
        _radiusX = Math.Max(1, oldRadiusX + expansionX);
        _radiusY = Math.Max(1, oldRadiusY + expansionY);
        if (_radiusX == 1 && expansionX < 0) _expansionX.Stop();
        if (_radiusY == 1 && expansionY < 0) _expansionY.Stop();
        Vector deltaExpansion = new(_radiusX - oldRadiusX, _radiusY - oldRadiusY);
        Vector deltaScale = new(_radiusX / oldRadiusX, _radiusY / oldRadiusY);
        UpdateVelocities();

        // Expansion carries radius displacement in DIPs; scale is the matching
        // multiplicative change relative to the radius before this frame.
        Vector newCumTranslation = _cumulative.Translation + deltaTranslation;
        double newCumRotation = _cumulative.Rotation + deltaRotation;
        Vector newCumExpansion = _cumulative.Expansion + deltaExpansion;
        Vector newCumScale = new(_cumulative.Scale.X * deltaScale.X, _cumulative.Scale.Y * deltaScale.Y);
        if (!Finite(newCumTranslation) || !double.IsFinite(newCumRotation) || !Finite(newCumExpansion) || !Finite(newCumScale))
            throw new InvalidOperationException("The inertia operation exceeded the finite coordinate range.");

        ManipulationDelta deltaThisTick = new()
        {
            Translation = deltaTranslation,
            Rotation = deltaRotation,
            Expansion = deltaExpansion,
            Scale = deltaScale
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
        if (!_running || !IsCurrent) { Cancel(); _completion?.Invoke(_cumulative, CurrentVelocities); return; }

        // Explicit distances finish at their endpoint, including sub-threshold motion.
        if (handlerHaltsInertia || MotionComplete)
        {
            Cancel();
            if (_completion is { } completion) completion(_cumulative, CurrentVelocities);
            else RaiseCompleted();
        }
    }

    /// <returns>True if the handler asked the engine to stop (Complete/Cancel).</returns>
    private bool RaiseDeltaTick(ManipulationDelta deltaThisTick, ManipulationVelocities velocities)
    {
        var coordinates = Coordinates;
        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationDeltaEvent,
            ManipulationContainer = coordinates.Container,
            ManipulationOrigin = coordinates.Origin,
            DeltaManipulation = deltaThisTick,
            CumulativeManipulation = _cumulative,
            Velocities = velocities,
            IsInertial = true
        };
        if (!Dispatch(previewArgs)) return true;

        ManipulationDeltaEventArgs bubbleArgs = previewArgs;
        if (!previewArgs.Handled && !previewArgs.CompleteRequested && !previewArgs.CancelRequested)
        {
            bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationDeltaEvent,
                ManipulationContainer = coordinates.Container,
                ManipulationOrigin = coordinates.Origin,
                DeltaManipulation = deltaThisTick,
                CumulativeManipulation = _cumulative,
                Velocities = velocities,
                IsInertial = true
            };
            if (!Dispatch(bubbleArgs)) return true;
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
        var coordinates = Coordinates;
        ManipulationBoundaryFeedbackEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = coordinates.Container,
            BoundaryFeedback = unused
        };
        if (!Dispatch(previewArgs)) return;
        if (!previewArgs.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = coordinates.Container,
                BoundaryFeedback = unused
            };
            _ = Dispatch(bubbleArgs);
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

    private bool MotionComplete => _translation.IsComplete && _rotation.IsComplete &&
        _expansionX.IsComplete && _expansionY.IsComplete;

    private void UpdateVelocities()
    {
        _linearVelocity = MultiplyVector(_translationDirection, _translation.Velocity);
        _angularVelocity = _rotation.Velocity;
        _expansionVelocity = new(_expansionX.Velocity, _expansionY.Velocity);
    }

    private static bool Finite(Vector value) => double.IsFinite(value.X) && double.IsFinite(value.Y);

    private static Vector MultiplyVector(Vector v, double s) => new(v.X * s, v.Y * s);

    private static bool HasAny(ManipulationDelta delta) =>
        delta.Translation.Length > 0.0001
        || Math.Abs(delta.Rotation) > 0.0001
        || delta.Expansion.Length > 0.0001
        || delta.Scale.X != 1.0 || delta.Scale.Y != 1.0;
}
