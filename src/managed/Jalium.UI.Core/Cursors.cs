namespace Jalium.UI;

/// <summary>
/// Provides a set of predefined <see cref="Cursor"/> values.
/// </summary>
public static class Cursors
{
    /// <summary>
    /// Gets the default arrow cursor.
    /// </summary>
    public static Cursor Arrow { get; } = new Cursor(CursorType.Arrow);

    /// <summary>
    /// Gets the hand (pointer) cursor.
    /// </summary>
    public static Cursor Hand { get; } = new Cursor(CursorType.Hand);

    /// <summary>
    /// Gets the I-beam text cursor.
    /// </summary>
    public static Cursor IBeam { get; } = new Cursor(CursorType.IBeam);

    /// <summary>
    /// Gets the wait/busy cursor.
    /// </summary>
    public static Cursor Wait { get; } = new Cursor(CursorType.Wait);

    /// <summary>
    /// Gets the crosshair cursor.
    /// </summary>
    public static Cursor Cross { get; } = new Cursor(CursorType.Cross);

    /// <summary>
    /// Gets the resize horizontal cursor.
    /// </summary>
    public static Cursor SizeWE { get; } = new Cursor(CursorType.SizeWE);

    /// <summary>
    /// Gets the resize vertical cursor.
    /// </summary>
    public static Cursor SizeNS { get; } = new Cursor(CursorType.SizeNS);

    /// <summary>
    /// Gets a help cursor.
    /// </summary>
    public static Cursor Help { get; } = new Cursor(CursorType.Help);

    /// <summary>
    /// Gets a hidden cursor.
    /// </summary>
    public static Cursor None { get; } = new Cursor(CursorType.None);

    /// <summary>
    /// Gets a pen cursor.
    /// </summary>
    public static Cursor Pen { get; } = new Cursor(CursorType.Pen);

    /// <summary>
    /// Gets a scroll all directions cursor.
    /// </summary>
    public static Cursor ScrollAll { get; } = new Cursor(CursorType.ScrollAll);

    /// <summary>
    /// Gets a scroll east cursor.
    /// </summary>
    public static Cursor ScrollE { get; } = new Cursor(CursorType.ScrollE);

    /// <summary>
    /// Gets a scroll north cursor.
    /// </summary>
    public static Cursor ScrollN { get; } = new Cursor(CursorType.ScrollN);

    /// <summary>
    /// Gets a scroll northeast cursor.
    /// </summary>
    public static Cursor ScrollNE { get; } = new Cursor(CursorType.ScrollNE);

    /// <summary>
    /// Gets a scroll northwest cursor.
    /// </summary>
    public static Cursor ScrollNW { get; } = new Cursor(CursorType.ScrollNW);

    /// <summary>
    /// Gets a scroll south cursor.
    /// </summary>
    public static Cursor ScrollS { get; } = new Cursor(CursorType.ScrollS);

    /// <summary>
    /// Gets a scroll southeast cursor.
    /// </summary>
    public static Cursor ScrollSE { get; } = new Cursor(CursorType.ScrollSE);

    /// <summary>
    /// Gets a scroll southwest cursor.
    /// </summary>
    public static Cursor ScrollSW { get; } = new Cursor(CursorType.ScrollSW);

    /// <summary>
    /// Gets a scroll west cursor.
    /// </summary>
    public static Cursor ScrollW { get; } = new Cursor(CursorType.ScrollW);

    /// <summary>
    /// Gets a diagonal resize cursor (northwest-southeast).
    /// </summary>
    public static Cursor SizeNWSE { get; } = new Cursor(CursorType.SizeNWSE);

    /// <summary>
    /// Gets a diagonal resize cursor (northeast-southwest).
    /// </summary>
    public static Cursor SizeNESW { get; } = new Cursor(CursorType.SizeNESW);

    /// <summary>
    /// Gets a move cursor.
    /// </summary>
    public static Cursor SizeAll { get; } = new Cursor(CursorType.SizeAll);

    /// <summary>
    /// Gets a not allowed cursor.
    /// </summary>
    public static Cursor No { get; } = new Cursor(CursorType.No);

    /// <summary>
    /// Gets an app starting cursor.
    /// </summary>
    public static Cursor AppStarting { get; } = new Cursor(CursorType.AppStarting);

    /// <summary>
    /// Gets an up arrow cursor.
    /// </summary>
    public static Cursor UpArrow { get; } = new Cursor(CursorType.UpArrow);
}
