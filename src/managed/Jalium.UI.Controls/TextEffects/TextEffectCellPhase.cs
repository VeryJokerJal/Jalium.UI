namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// Identifies the lifecycle phase of a <see cref="TextEffectCell"/>.
/// </summary>
public enum TextEffectCellPhase
{
    /// <summary>
    /// The cell has just been created and has not yet started its enter animation.
    /// No rendering output.
    /// </summary>
    Hidden,

    /// <summary>
    /// The cell is running its entry animation towards fully-visible state.
    /// </summary>
    Entering,

    /// <summary>
    /// Steady state. The cell sits at its laid-out position with no active animation.
    /// </summary>
    Visible,

    /// <summary>
    /// The cell is interpolating from a previous laid-out position to a new one,
    /// because neighbouring cells were inserted or removed.
    /// </summary>
    Shifting,

    /// <summary>
    /// The cell is running its exit animation. It still renders but does not
    /// participate in layout (its advance is excluded so neighbours can collapse
    /// the space).
    /// </summary>
    Exiting,
}
