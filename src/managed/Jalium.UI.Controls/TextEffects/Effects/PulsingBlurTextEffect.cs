using System;
using Jalium.UI;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Demonstration <see cref="ShaderTextEffect"/> that pipes the presenter surface
/// through a native <see cref="BlurEffect"/> whose radius pulses on a sine
/// wave. Serves as a worked example of a time-varying GPU pass: exactly the
/// same plumbing a custom <c>ShaderEffect</c> subclass (with user DXBC) uses
/// to refresh constants each frame.
/// </summary>
/// <remarks>
/// Intended as a ready-to-use effect and as a template — users writing a
/// proper shader subclass should follow the same <see cref="UpdateForFrame"/>
/// / <see cref="CurrentEffect"/> pattern with their own <c>ShaderEffect</c>
/// instance in place of <see cref="BlurEffect"/>.
/// </remarks>
public sealed class PulsingBlurTextEffect : ShaderTextEffect
{
    // Shared instance — the presenter pushes this every frame, and we merely
    // mutate its Radius in UpdateForFrame. Native sided caching keys on the
    // managed Effect reference, so keeping it stable avoids PSO churn.
    private readonly BlurEffect _blur = new(1.0);

    /// <summary>
    /// Peak extra blur added on top of <see cref="BaselinePx"/>, in pixels.
    /// At the pulse peak, radius = <see cref="BaselinePx"/> + <see cref="AmplitudePx"/>.
    /// </summary>
    public double AmplitudePx { get; set; } = 6.0;

    /// <summary>
    /// Minimum blur radius, in pixels. At the pulse trough the radius equals
    /// this value. Must be at least 1 for the native shader to run.
    /// </summary>
    public double BaselinePx { get; set; } = 1.0;

    /// <summary>
    /// Full period of one pulse cycle, in milliseconds.
    /// </summary>
    public double PeriodMs { get; set; } = 2400.0;

    /// <summary>
    /// Additional phase offset in [0, 1) — useful when several pulsing effects
    /// are visible simultaneously and should breathe out of sync.
    /// </summary>
    public double PhaseOffset { get; set; } = 0.0;

    /// <inheritdoc />
    public override IEffect? CurrentEffect => _blur.Radius >= 0.5 ? _blur : null;

    /// <inheritdoc />
    protected internal override void UpdateForFrame(Size presenterSize, double totalElapsedMs)
    {
        _ = presenterSize;
        var period = Math.Max(1.0, PeriodMs);
        var phase = ((totalElapsedMs / period) + PhaseOffset) % 1.0;
        // Sine in [-1, 1] → [0, 1] triangle-ish smooth wave.
        var wave = 0.5 * (1.0 + Math.Sin(phase * Math.PI * 2.0));
        _blur.Radius = Math.Max(0.0, BaselinePx + AmplitudePx * wave);
    }
}
