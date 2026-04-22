using System;
using Jalium.UI;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Default text effect for <see cref="TextEffectPresenter"/>. Enter animation
/// raises each cell from below with an overshoot-and-settle curve while it comes
/// into focus from a soft blur; exit animation drifts the cell upwards while it
/// defocuses and fades; shift animation slides a cell to a new laid-out position
/// with a plain ease-out (no overshoot, so text collapsing to fill a hole doesn't
/// wobble).
/// </summary>
public sealed class RiseSettleEffect : TextEffectBase
{
    /// <summary>
    /// Vertical distance, expressed in multiples of the cell's line height, that
    /// an entering cell starts below its final position.
    /// </summary>
    public double RiseDistance { get; set; } = 1.2;

    /// <summary>
    /// Back-ease tension for the enter animation. 1.7 ≈ 10% overshoot past the
    /// target; higher values exaggerate the bounce, lower values dampen it.
    /// </summary>
    public double OvershootTension { get; set; } = 1.7;

    /// <summary>
    /// Blur radius at the start of the enter animation, in pixels. Quadratically
    /// decreases to 0 over the enter curve.
    /// </summary>
    public double RiseBlurPx { get; set; } = 12.0;

    /// <summary>
    /// Blur radius at the end of the exit animation, in pixels. Grows from 0
    /// over the course of the exit curve.
    /// </summary>
    public double DissipateBlurPx { get; set; } = 8.0;

    /// <summary>
    /// Fraction of <see cref="RiseDistance"/> used for the upward drift during
    /// exit (the cell floats upwards as it dissipates, echoing the enter direction).
    /// </summary>
    public double DissipateRiseFraction { get; set; } = 0.6;

    /// <summary>
    /// Fraction of the enter phase during which opacity ramps from 0 to 1.
    /// The blur and vertical motion keep running afterwards, but the cell is
    /// fully opaque so it looks "solidified but still settling".
    /// </summary>
    public double OpacityRampFraction { get; set; } = 0.4;

    /// <inheritdoc />
    public override double EnterDurationMs => 520.0;

    /// <inheritdoc />
    public override double ShiftDurationMs => 280.0;

    /// <inheritdoc />
    public override double ExitDurationMs => 380.0;

    // RiseSettle is a "block rise" effect: every cell appended in a single
    // batch enters simultaneously and settles together. No per-char stagger by
    // default — callers who want a typewriter wave should pick TypewriterEffect
    // or subclass this and override GetStaggerDelayMs.
    protected override double StaggerStepMs => 0.0;
    protected override double StaggerMaxMs => 0.0;

    /// <inheritdoc />
    public override void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
    {
        var t = context.PhaseProgressLinear;
        var lineHeight = context.Cell.LineHeight;
        if (lineHeight <= 0)
        {
            lineHeight = context.Cell.Bounds.Height > 0 ? context.Cell.Bounds.Height : 16.0;
        }

        switch (context.Phase)
        {
            case TextEffectCellPhase.Entering:
                ApplyEnter(t, lineHeight, ref payload);
                break;

            case TextEffectCellPhase.Shifting:
                ApplyShift(context.Cell, t, ref payload);
                break;

            case TextEffectCellPhase.Exiting:
                ApplyExit(t, lineHeight, ref payload);
                break;

            case TextEffectCellPhase.Visible:
            case TextEffectCellPhase.Hidden:
            default:
                // Identity payload — nothing to do.
                break;
        }

        payload.TransformOrigin = new Point(context.Cell.Bounds.Width / 2, context.Cell.Bounds.Height / 2);
    }

    private void ApplyEnter(double t, double lineHeight, ref TextCellRenderPayload payload)
    {
        var eased = BackEaseOut(t, OvershootTension);
        payload.TranslateY = (1.0 - eased) * RiseDistance * lineHeight;

        // Blur follows a quadratic ease-in falloff (1 - t²): the radius
        // decreases slowly at first and accelerates toward the end — "从慢到快".
        // Change rate grows linearly with progress, giving a visibly continuous
        // defocus→focus instead of the hold-then-snap feel of a cubic curve.
        //
        // Written to payload.BlurRadius (not PerCellEffect) because GPU per-cell
        // blur does one offscreen capture + compute dispatch per cell per frame;
        // with ~16 cells simultaneously entering, the resulting full-screen
        // blur storm exceeds what the D3D12 direct renderer can schedule in a
        // frame and causes visible state corruption (sibling elements go black).
        // The CPU sampled path (see TextEffectPresenter.DrawWithSampledBlur) is
        // a 24-sample ring pattern with no centre pass — ~24 DrawText calls
        // per cell, trivially batched by the glyph atlas.
        var blurT = 1.0 - t * t;
        payload.BlurRadius = RiseBlurPx * blurT;

        var ramp = OpacityRampFraction > 0 ? OpacityRampFraction : 0.4;
        payload.Opacity = Math.Clamp(t / ramp, 0.0, 1.0);
    }

    private static void ApplyShift(TextEffectCell cell, double t, ref TextCellRenderPayload payload)
    {
        var eased = EaseOutCubic(t);

        // Interpolate from the cell's previous position (captured when it entered
        // Shifting) to its current laid-out position. Bounds already holds the
        // target, so the offset is (origin - target) scaled by (1 - eased).
        var remaining = 1.0 - eased;
        payload.TranslateX = (cell.ShiftOriginX - cell.Bounds.X) * remaining;
        payload.TranslateY = (cell.ShiftOriginY - cell.Bounds.Y) * remaining;
    }

    private void ApplyExit(double t, double lineHeight, ref TextCellRenderPayload payload)
    {
        var eased = EaseInCubic(t);
        payload.TranslateY = -eased * RiseDistance * DissipateRiseFraction * lineHeight;

        // Blur ramps up on a quadratic ease-in curve (t²): starts slow, ends
        // fast — "从慢到快", mirroring the enter phase so the two are
        // visually symmetrical about the blur axis. Uses CPU sampled blur
        // (see ApplyEnter comment for rationale).
        payload.BlurRadius = DissipateBlurPx * (t * t);

        payload.Opacity = 1.0 - eased;
    }
}
