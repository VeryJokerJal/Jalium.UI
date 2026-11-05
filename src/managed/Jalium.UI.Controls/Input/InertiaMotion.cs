namespace Jalium.UI.Controls;

/// <summary>Integrates one signed inertia component without depending on frame boundaries.</summary>
internal struct InertiaMotion
{
    private const double DefaultDecayPerMs = .002;
    private double _initialVelocity;
    private double _duration;
    private double _elapsed;
    private double _distance;
    private double _position;
    private bool _exponential;

    internal double Velocity { get; private set; }
    internal bool IsComplete => Velocity == 0;

    internal static InertiaMotion Create(double velocity, double stopThreshold,
        double? deceleration, double? distance)
    {
        if (!double.IsFinite(velocity)) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (deceleration is { } dec && (!double.IsFinite(dec) || dec < 0))
            throw new ArgumentOutOfRangeException(nameof(deceleration));
        if (distance is { } length && (!double.IsFinite(length) || length < 0))
            throw new ArgumentOutOfRangeException(nameof(distance));
        double speed = Math.Abs(velocity);
        if (speed == 0 || distance == 0 ||
            (distance == null && deceleration == null && speed <= stopThreshold)) return default;

        var result = new InertiaMotion { Velocity = velocity, _initialVelocity = velocity };
        if (distance is { } target)
        {
            result._duration = target / speed * 2;
            result._distance = Math.CopySign(target, velocity);
        }
        else if (deceleration is { } acceleration)
        {
            if (acceleration == 0)
            {
                // Zero is an explicit request for constant velocity. The owner
                // can still complete/cancel the operation or hit a boundary.
                result._duration = double.PositiveInfinity;
                return result;
            }
            result._duration = speed / acceleration;
            result._distance = velocity * .5 * result._duration;
        }
        else
        {
            result._exponential = true;
            result._duration = (Math.Log(speed) - Math.Log(stopThreshold)) / DefaultDecayPerMs;
            return result;
        }
        if (!(result._duration > 0) || !double.IsFinite(result._duration) || !double.IsFinite(result._distance))
            throw new ArgumentOutOfRangeException(nameof(distance), "The inertia stopping time and distance must be finite.");
        return result;
    }

    internal double Advance(double milliseconds)
    {
        if (IsComplete) return 0;
        double previous = _position;
        if (_exponential)
        {
            double elapsed = Math.Min(milliseconds, Math.Max(0, _duration - _elapsed));
            double decay = Math.Exp(-DefaultDecayPerMs * elapsed);
            _position += Velocity * ((1 - decay) / DefaultDecayPerMs);
            _elapsed += elapsed;
            Velocity = _elapsed >= _duration ? 0 : Velocity * decay;
        }
        else if (double.IsPositiveInfinity(_duration))
        {
            _position += Velocity * milliseconds;
        }
        else
        {
            _elapsed = Math.Min(_duration, _elapsed + milliseconds);
            double progress = _elapsed / _duration;
            _position = _distance * progress * (2 - progress);
            Velocity = _elapsed >= _duration ? 0 : _initialVelocity * (1 - progress);
        }
        if (!double.IsFinite(_position) || !double.IsFinite(Velocity))
            throw new InvalidOperationException("The inertia component exceeded the finite coordinate range.");
        return _position - previous;
    }

    internal void Stop() => Velocity = 0;
}
