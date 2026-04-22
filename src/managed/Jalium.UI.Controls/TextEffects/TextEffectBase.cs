using System;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Convenience base class for <see cref="ITextEffect"/> implementations. Provides
/// sensible default durations, a linear stagger curve, and a small library of
/// easing helpers so subclasses can focus on per-phase visual math.
/// </summary>
public abstract class TextEffectBase : ITextEffect
{
    /// <inheritdoc />
    public virtual double EnterDurationMs => 520.0;

    /// <inheritdoc />
    public virtual double ShiftDurationMs => 280.0;

    /// <inheritdoc />
    public virtual double ExitDurationMs => 380.0;

    /// <summary>
    /// Per-cell delay step for the default stagger curve, in milliseconds.
    /// </summary>
    protected virtual double StaggerStepMs => 18.0;

    /// <summary>
    /// Upper bound on stagger delay, in milliseconds. Longer batches compress
    /// their tail into this ceiling so very long lines don't have a visibly slow
    /// last character.
    /// </summary>
    protected virtual double StaggerMaxMs => 260.0;

    /// <inheritdoc />
    public virtual double GetStaggerDelayMs(int indexInBatch, int batchSize)
    {
        if (indexInBatch <= 0 || batchSize <= 1)
        {
            return 0;
        }

        var linear = indexInBatch * StaggerStepMs;
        return Math.Min(linear, StaggerMaxMs);
    }

    /// <inheritdoc />
    public abstract void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload);

    /// <summary>
    /// Cubic ease-out: fast start, soft landing. t in [0, 1].
    /// </summary>
    protected static double EaseOutCubic(double t)
    {
        var u = 1.0 - Math.Clamp(t, 0.0, 1.0);
        return 1.0 - (u * u * u);
    }

    /// <summary>
    /// Cubic ease-in: soft start, fast exit. t in [0, 1].
    /// </summary>
    protected static double EaseInCubic(double t)
    {
        var u = Math.Clamp(t, 0.0, 1.0);
        return u * u * u;
    }

    /// <summary>
    /// Quadratic ease-out. Cheaper than cubic, slightly softer landing.
    /// </summary>
    protected static double EaseOutQuad(double t)
    {
        var u = 1.0 - Math.Clamp(t, 0.0, 1.0);
        return 1.0 - (u * u);
    }

    /// <summary>
    /// Back ease-out: overshoots the target by a tension-controlled amount, then
    /// settles. <paramref name="tension"/> of 1.70158 matches WPF <c>BackEase</c>;
    /// higher values exaggerate the overshoot.
    /// </summary>
    /// <param name="t">Linear progress in [0, 1].</param>
    /// <param name="tension">Overshoot amount (1.70158 ≈ 10% past target).</param>
    protected static double BackEaseOut(double t, double tension)
    {
        var u = Math.Clamp(t, 0.0, 1.0) - 1.0;
        return (u * u * ((tension + 1) * u + tension)) + 1.0;
    }

    /// <summary>
    /// Linear interpolation. Kept as a named helper so call sites read intent
    /// ("lerp from a to b by t") instead of arithmetic noise.
    /// </summary>
    protected static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * Math.Clamp(t, 0.0, 1.0));
    }
}
