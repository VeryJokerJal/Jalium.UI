using System;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Minimal text effect: cells fade in on enter, fade out on exit, and shift
/// without overshoot. No motion, no blur, no scale. Suitable for subtitle-style
/// scenarios where attention should stay on the text content rather than the
/// animation itself.
/// </summary>
public sealed class FadeInEffect : TextEffectBase
{
    /// <summary>
    /// Easing exponent applied to opacity. 1.0 = linear; higher values hold at
    /// low opacity longer and pop into view at the end (useful when the effect
    /// runs over a long duration and you want the reveal to feel decisive).
    /// </summary>
    public double OpacityCurve { get; set; } = 1.5;

    /// <inheritdoc />
    public override double EnterDurationMs => 260.0;

    /// <inheritdoc />
    public override double ExitDurationMs => 200.0;

    /// <inheritdoc />
    public override double ShiftDurationMs => 220.0;

    /// <inheritdoc />
    public override void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
    {
        var t = context.PhaseProgressLinear;

        switch (context.Phase)
        {
            case TextEffectCellPhase.Entering:
                payload.Opacity = Math.Pow(Math.Clamp(t, 0.0, 1.0), OpacityCurve);
                break;

            case TextEffectCellPhase.Shifting:
                {
                    var eased = EaseOutCubic(t);
                    var remaining = 1.0 - eased;
                    payload.TranslateX = (context.Cell.ShiftOriginX - context.Cell.Bounds.X) * remaining;
                    payload.TranslateY = (context.Cell.ShiftOriginY - context.Cell.Bounds.Y) * remaining;
                    break;
                }

            case TextEffectCellPhase.Exiting:
                payload.Opacity = Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), OpacityCurve);
                break;

            case TextEffectCellPhase.Visible:
            case TextEffectCellPhase.Hidden:
            default:
                break;
        }
    }
}
