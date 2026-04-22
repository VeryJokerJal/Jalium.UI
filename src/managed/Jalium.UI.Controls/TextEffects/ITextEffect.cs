namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Extension point for per-cell text animations inside <see cref="TextEffectPresenter"/>.
/// Implementations declare default phase durations, choose how cells within one
/// edit batch are staggered, and write render parameters for each frame.
/// </summary>
/// <remarks>
/// Three built-in phases (<c>Entering</c>, <c>Shifting</c>, <c>Exiting</c>) cover
/// the presenter's editing primitives. Effects never drive the state machine
/// themselves — they only decide what the cell looks like while the presenter
/// runs it.
/// </remarks>
public interface ITextEffect
{
    /// <summary>
    /// Duration of the enter animation, in milliseconds, for a single cell.
    /// Stagger delay from <see cref="GetStaggerDelay"/> is applied on top.
    /// </summary>
    double EnterDurationMs { get; }

    /// <summary>
    /// Duration of the shift animation (cells sliding to a new position because
    /// neighbours were inserted or removed), in milliseconds.
    /// </summary>
    double ShiftDurationMs { get; }

    /// <summary>
    /// Duration of the exit animation, in milliseconds. After this elapses the
    /// cell is destroyed and its memory reclaimed.
    /// </summary>
    double ExitDurationMs { get; }

    /// <summary>
    /// Returns the delay, in milliseconds, that should pass before the cell at
    /// <paramref name="indexInBatch"/> actually starts its enter animation.
    /// A wave of staggered cells gives a flow / typewriter feel.
    /// </summary>
    /// <param name="indexInBatch">0-based position in the batch.</param>
    /// <param name="batchSize">Total number of cells in the batch.</param>
    double GetStaggerDelayMs(int indexInBatch, int batchSize);

    /// <summary>
    /// Writes per-cell render parameters for the current frame.
    /// </summary>
    /// <param name="context">Cell + phase + timing info.</param>
    /// <param name="payload">Out-parameter the effect mutates in place. Starts
    /// at <see cref="TextCellRenderPayload.Identity"/>.</param>
    void Apply(in TextEffectFrameContext context, ref TextCellRenderPayload payload);
}
