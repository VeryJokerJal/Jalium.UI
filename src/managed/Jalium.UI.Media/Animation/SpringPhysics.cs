namespace Jalium.UI.Media.Animation;

/// <summary>
/// Single-axis damped harmonic oscillator for spring physics animation.
/// Maintains position and velocity state; call <see cref="Step"/> each frame to advance the simulation.
/// </summary>
public struct SpringAxis
{
    public double Position;
    public double Velocity;
    public double Target;

    /// <summary>
    /// Advances the spring simulation by one time step using sub-stepping for stability.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="stiffness">Spring stiffness (higher = snappier). Typical: 800-1500.</param>
    /// <param name="dampingRatio">Damping ratio. 0=undamped, &lt;1=underdamped (bouncy), 1=critical, &gt;1=overdamped.</param>
    /// <param name="maxDisplacement">Maximum allowed displacement from target. 0 = unlimited.</param>
    /// <returns>True if the spring has settled (converged to target).</returns>
    public bool Step(double dt, double stiffness, double dampingRatio, double maxDisplacement = 0)
    {
        // Sub-step to ensure stability: dt_sub < 2/sqrt(stiffness) with safety margin.
        // For stiffness=1200, limit is ~0.058s; we use 0.004s sub-steps for large margin.
        const double maxSubStep = 0.004;
        int steps = Math.Max(1, (int)Math.Ceiling(dt / maxSubStep));
        double subDt = dt / steps;

        double damping = 2.0 * dampingRatio * Math.Sqrt(stiffness);

        for (int i = 0; i < steps; i++)
        {
            double displacement = Position - Target;
            double acceleration = -stiffness * displacement - damping * Velocity;
            Velocity += acceleration * subDt;
            Position += Velocity * subDt;
        }

        // Clamp position to prevent runaway divergence
        if (maxDisplacement > 0)
        {
            double displacement = Position - Target;
            if (Math.Abs(displacement) > maxDisplacement)
            {
                Position = Target + Math.Sign(displacement) * maxDisplacement;
                Velocity = 0;
            }
        }

        const double positionThreshold = 0.0001;
        const double velocityThreshold = 0.01;

        double finalDisp = Position - Target;
        if (Math.Abs(finalDisp) < positionThreshold &&
            Math.Abs(Velocity) < velocityThreshold)
        {
            Position = Target;
            Velocity = 0;
            return true;
        }
        return false;
    }
}
