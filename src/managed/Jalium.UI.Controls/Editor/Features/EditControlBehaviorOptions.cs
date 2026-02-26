namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Lightweight behavior switches for EditControl.
/// Defaults preserve existing behavior unless explicitly changed.
/// </summary>
public sealed class EditControlBehaviorOptions
{
    private int _undoMergeWindowMs = 250;

    /// <summary>
    /// Suppresses command shortcuts while IME is composing text.
    /// </summary>
    public bool SuppressShortcutsDuringIme { get; set; } = true;

    /// <summary>
    /// Preserves document line ending style when copying selected text.
    /// </summary>
    public bool PreserveLineEndingsOnCopy { get; set; }

    /// <summary>
    /// Coalesces repeated visual invalidation requests during rapid input.
    /// </summary>
    public bool CoalesceInvalidateVisual { get; set; }

    /// <summary>
    /// Keeps legacy Ctrl+L behavior (delete current line/selection).
    /// If false, Ctrl+L selects current line.
    /// </summary>
    public bool UseLegacyCtrlLDeleteLineShortcut { get; set; } = true;

    /// <summary>
    /// Enables Ctrl+D "select next occurrence" behavior instead of line duplication.
    /// </summary>
    public bool CtrlDSelectNextOccurrence { get; set; }

    /// <summary>
    /// Merges consecutive typing edits that happen within this time window.
    /// </summary>
    public int UndoMergeWindowMs
    {
        get => _undoMergeWindowMs;
        set => _undoMergeWindowMs = Math.Clamp(value, 0, 2_000);
    }

    /// <summary>
    /// Uses a higher contrast fallback for selection rendering.
    /// </summary>
    public bool HighContrastSelectionFallback { get; set; }
}
