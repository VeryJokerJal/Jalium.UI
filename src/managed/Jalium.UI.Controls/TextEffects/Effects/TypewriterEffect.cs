using System;

namespace Jalium.UI.Controls.TextEffects.Effects;

/// <summary>
/// Classic typewriter: cells appear one by one with no fade — each glyph is
/// either fully hidden or fully visible, and the reveal sweeps across the batch
/// at a fixed per-character cadence. Unlike <see cref="FadeInEffect"/> this
/// effect relies entirely on stagger for its rhythm; the per-cell enter
/// animation is essentially instantaneous.
/// </summary>
/// <remarks>
/// Because the visible/hidden flip is sharp, the per-cell enter duration is
/// short on purpose: you want characters to "land" decisively, not fade.
/// Adjust <see cref="CharacterDelayMs"/> to change typing speed.
/// </remarks>
public sealed class TypewriterEffect : TextEffectBase
{
    /// <summary>
    /// Time each following character waits before it starts appearing, in
    /// milliseconds. 40 ms ≈ fast typist, 80 ms ≈ unhurried narrator.
    /// </summary>
    public double CharacterDelayMs { get; set; } = 55.0;

    /// <summary>
    /// Opacity cutoff. Below this the cell is treated as hidden for the frame;
    /// at or above it the cell renders fully opaque. The sharp cutoff is what
    /// gives the typewriter its mechanical feel.
    /// </summary>
    public double Cutoff { get; set; } = 0.5;

    /// <inheritdoc />
    public override double EnterDurationMs => 60.0;

    /// <inheritdoc />
    public override double ShiftDurationMs => 180.0;

    /// <inheritdoc />
    public override double ExitDurationMs => 120.0;

    /// <inheritdoc />
    public override double GetStaggerDelayMs(int indexInBatch, int batchSize)
    {
        if (indexInBatch <= 0)
        {
            return 0;
        }

        // No upper cap — the signature typewriter rhythm means a long line
        // should take proportionally long. If callers want a ceiling they can
        // set MaxCells on the presenter or swap for a different effect.
        return indexInBatch * Math.Max(1.0, CharacterDelayMs);
    }

    /// <inheritdoc />
    public override void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload)
    {
        var t = context.PhaseProgressLinear;

        switch (context.Phase)
        {
            case TextEffectCellPhase.Entering:
                payload.Opacity = t >= Cutoff ? 1.0 : 0.0;
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
                // Backspace feel — cells wink out in reverse stagger order.
                payload.Opacity = t < Cutoff ? 1.0 : 0.0;
                break;

            case TextEffectCellPhase.Visible:
            case TextEffectCellPhase.Hidden:
            default:
                break;
        }
    }
}
