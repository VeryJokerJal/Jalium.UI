namespace Jalium.UI;

/// <summary>
/// Interface for elements that support IME (Input Method Editor) input.
/// </summary>
public interface IImeSupport
{
    /// <summary>
    /// Gets the caret position for IME composition window positioning.
    /// </summary>
    /// <returns>The caret position in element-local DIPs.</returns>
    Point GetImeCaretPosition();

    /// <summary>
    /// Called when IME composition starts.
    /// </summary>
    void OnImeCompositionStart();

    /// <summary>
    /// Called when the IME composition string is updated.
    /// </summary>
    /// <param name="compositionString">The current composition string.</param>
    /// <param name="cursorPosition">The cursor position within the composition string.</param>
    void OnImeCompositionUpdate(string compositionString, int cursorPosition);

    /// <summary>
    /// Called when IME composition ends.
    /// </summary>
    /// <param name="resultString">The final committed string, or null if cancelled.</param>
    void OnImeCompositionEnd(string? resultString);
}
