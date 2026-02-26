namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Optional editor capabilities that can be toggled at runtime.
/// Features are disabled by default unless explicitly enabled.
/// </summary>
public enum EditFeature
{
    MultiCaret,
    ColumnSelection,
    ClipboardHistory,
    PreserveCopyLineEndings,
    ImeShortcutSuppression,
    AccessibilityMode,
    HighContrastMode,
    RenderProfiling,
    AsyncSearch,
    CoalescedInvalidate
}
