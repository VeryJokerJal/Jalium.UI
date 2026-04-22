using Jalium.UI;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Represents a single animated unit inside a <see cref="TextEffectPresenter"/>.
/// A cell wraps one grapheme cluster (one user-perceived character, so that emoji
/// / ZWJ sequences / CJK remain atomic) and carries the lifecycle state needed to
/// drive per-character animation.
/// </summary>
/// <remarks>
/// Cell identity is stable across edits: appending or removing other cells never
/// recycles the <see cref="Id"/> of an existing one. Effects can therefore keep
/// their own per-cell state keyed by <see cref="Id"/>.
/// </remarks>
public sealed class TextEffectCell
{
    internal TextEffectCell(long id, string text, int batchId, int indexInBatch, int batchSize)
    {
        Id = id;
        Text = text;
        BatchId = batchId;
        IndexInBatch = indexInBatch;
        BatchSize = batchSize;
        Phase = TextEffectCellPhase.Hidden;
    }

    /// <summary>
    /// Monotonically increasing identifier, unique within the owning presenter.
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// Grapheme text this cell represents. For plain ASCII this is a single char;
    /// for emoji or ZWJ sequences it may be multiple chars.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Identifier of the batch this cell was added in (one <c>AppendText</c> /
    /// <c>InsertText</c> call = one batch). Effects use this to spread a single
    /// edit across time via <see cref="ITextEffect.GetStaggerDelay"/>.
    /// </summary>
    public int BatchId { get; }

    /// <summary>
    /// Position of this cell inside its batch, 0-based.
    /// </summary>
    public int IndexInBatch { get; }

    /// <summary>
    /// Total number of cells in this cell's batch.
    /// </summary>
    public int BatchSize { get; }

    /// <summary>
    /// Current lifecycle phase.
    /// </summary>
    public TextEffectCellPhase Phase { get; internal set; }

    /// <summary>
    /// Laid-out bounds of the cell in the presenter's coordinate space.
    /// For cells in <see cref="TextEffectCellPhase.Shifting"/>, this is the
    /// <i>target</i> bounds — the presenter interpolates from
    /// <see cref="ShiftOriginX"/>/<see cref="ShiftOriginY"/> to here.
    /// </summary>
    public Rect Bounds { get; internal set; }

    /// <summary>
    /// Height of the text line this cell belongs to, in pixels.
    /// </summary>
    public double LineHeight { get; internal set; }

    /// <summary>
    /// The cell's X position at the moment it entered <see cref="TextEffectCellPhase.Shifting"/>.
    /// Undefined outside of that phase.
    /// </summary>
    public double ShiftOriginX { get; internal set; }

    /// <summary>
    /// The cell's Y position at the moment it entered <see cref="TextEffectCellPhase.Shifting"/>.
    /// Undefined outside of that phase.
    /// </summary>
    public double ShiftOriginY { get; internal set; }

    /// <summary>
    /// Frame timestamp (ms since presenter start) when the current phase began.
    /// </summary>
    internal double PhaseStartTimeMs;

    /// <summary>
    /// Duration of the current phase, in milliseconds. Combines the effect's
    /// base duration with any per-cell stagger delay.
    /// </summary>
    internal double PhaseDurationMs;

    /// <summary>
    /// Extra delay before the phase actually starts progressing, in ms. Used for
    /// stagger; during the delay <see cref="TextEffectCellPhase.Entering"/> cells
    /// render with progress = 0 so they stay hidden until their turn.
    /// </summary>
    internal double PhaseDelayMs;
}
