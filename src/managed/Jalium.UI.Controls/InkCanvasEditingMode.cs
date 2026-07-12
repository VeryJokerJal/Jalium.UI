namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the editing modes of an <see cref="InkCanvas"/>.
/// </summary>
public enum InkCanvasEditingMode
{
    /// <summary>
    /// No editing interaction is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The user can draw ink strokes.
    /// </summary>
    Ink = 1,

    /// <summary>
    /// The user can perform recognized gestures without collecting ink.
    /// </summary>
    GestureOnly = 2,

    /// <summary>
    /// The user can collect ink and perform recognized gestures.
    /// </summary>
    InkAndGesture = 3,

    /// <summary>
    /// The user can select strokes by drawing a lasso around them.
    /// </summary>
    Select = 4,

    /// <summary>
    /// The user can erase portions of strokes by touching them.
    /// </summary>
    EraseByPoint = 5,

    /// <summary>
    /// The user can erase entire strokes by touching them.
    /// </summary>
    EraseByStroke = 6,
}
