using Jalium.UI;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Per-cell, per-frame context handed to <see cref="ITextEffect.Apply"/>. A fresh
/// value is constructed for each cell every frame, but it holds no heap-allocated
/// state beyond the <see cref="Cell"/> reference, so the cost is a handful of
/// struct copies.
/// </summary>
public readonly struct TextEffectFrameContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextEffectFrameContext"/> struct.
    /// </summary>
    public TextEffectFrameContext(
        TextEffectCell cell,
        TextEffectCellPhase phase,
        double phaseProgressLinear,
        double timeInPhaseMs,
        double totalTimeMs,
        Size presenterSize)
    {
        Cell = cell;
        Phase = phase;
        PhaseProgressLinear = phaseProgressLinear;
        TimeInPhaseMs = timeInPhaseMs;
        TotalTimeMs = totalTimeMs;
        PresenterSize = presenterSize;
    }

    /// <summary>
    /// The cell being rendered.
    /// </summary>
    public TextEffectCell Cell { get; }

    /// <summary>
    /// Snapshot of the cell's phase at the start of this frame. Equal to
    /// <see cref="TextEffectCell.Phase"/> but captured for thread-safe reading.
    /// </summary>
    public TextEffectCellPhase Phase { get; }

    /// <summary>
    /// Linear progress through the current phase, in [0, 1]. Effects typically
    /// apply their own easing on top of this value.
    /// </summary>
    public double PhaseProgressLinear { get; }

    /// <summary>
    /// Elapsed time since the current phase started (excluding stagger delay), in ms.
    /// </summary>
    public double TimeInPhaseMs { get; }

    /// <summary>
    /// Total time since the cell was created, in ms. Useful for time-varying effects
    /// like sparkle or shimmer that don't restart with each phase change.
    /// </summary>
    public double TotalTimeMs { get; }

    /// <summary>
    /// Current render size of the owning presenter, in pixels.
    /// </summary>
    public Size PresenterSize { get; }
}
