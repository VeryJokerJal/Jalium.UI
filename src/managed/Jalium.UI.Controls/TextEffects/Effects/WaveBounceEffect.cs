using System;
using Jalium.UI;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Playful entrance: each cell drops in from above with a vertical sinusoidal
/// wobble, scaled slightly bigger at the start so it "pops" as it settles.
/// The stagger is strong enough that an appending string looks like a wave
/// running across the line — fits "动感" / lively scenarios (notifications,
/// game-style feedback, playful labels).
/// </summary>
public sealed class WaveBounceEffect : TextEffectBase
{
    /// <summary>
    /// Vertical drop distance at the start of the enter animation, expressed
    /// in multiples of the cell's line height. Positive values mean "drops
    /// down from above"; negative values give "lifts up from below".
    /// </summary>
    public double DropDistance { get; set; } = -0.6;

    /// <summary>
    /// Amplitude of the sinusoidal wobble as a fraction of line height.
    /// 0 disables the wobble entirely.
    /// </summary>
    public double WobbleAmplitude { get; set; } = 0.25;

    /// <summary>
    /// Number of wobble oscillations during the enter phase.
    /// </summary>
    public double WobbleCycles { get; set; } = 1.5;

    /// <summary>
    /// Scale factor at the start of the enter animation; settles to 1.0 by the end.
    /// Values around 1.2–1.4 give a lively pop.
    /// </summary>
    public double PopScale { get; set; } = 1.25;

    /// <inheritdoc />
    public override double EnterDurationMs => 480.0;

    /// <inheritdoc />
    public override double ShiftDurationMs => 280.0;

    /// <inheritdoc />
    public override double ExitDurationMs => 320.0;

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
                {
                    // Base descent eases out (decelerates into position).
                    var descent = EaseOutCubic(t);
                    var basePos = (1.0 - descent) * DropDistance * lineHeight;

                    // Wobble: a damped sine that dies off as the cell settles.
                    var damping = 1.0 - t;
                    var wobble = Math.Sin(t * Math.PI * 2 * WobbleCycles) * WobbleAmplitude * lineHeight * damping;

                    payload.TranslateY = basePos + wobble;

                    // Pop scale: eases from PopScale down to 1 over the first
                    // half, then settles flat.
                    var scaleT = Math.Min(1.0, t / 0.5);
                    var scale = Lerp(PopScale, 1.0, EaseOutQuad(scaleT));
                    payload.ScaleX = scale;
                    payload.ScaleY = scale;

                    payload.Opacity = Math.Clamp(t / 0.3, 0.0, 1.0);
                    break;
                }

            case TextEffectCellPhase.Shifting:
                {
                    var eased = EaseOutCubic(t);
                    var remaining = 1.0 - eased;
                    payload.TranslateX = (context.Cell.ShiftOriginX - context.Cell.Bounds.X) * remaining;
                    payload.TranslateY = (context.Cell.ShiftOriginY - context.Cell.Bounds.Y) * remaining;
                    break;
                }

            case TextEffectCellPhase.Exiting:
                {
                    // Fall out in the same direction the cell came from,
                    // keeping visual consistency with the entrance.
                    var eased = EaseInCubic(t);
                    payload.TranslateY = eased * DropDistance * 0.8 * lineHeight;
                    payload.Opacity = 1.0 - eased;
                    break;
                }

            case TextEffectCellPhase.Visible:
            case TextEffectCellPhase.Hidden:
            default:
                break;
        }

        payload.TransformOrigin = new Point(context.Cell.Bounds.Width / 2, context.Cell.Bounds.Height / 2);
    }
}
