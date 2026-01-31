namespace Jalium.UI;

/// <summary>
/// Represents a mouse cursor type.
/// </summary>
public enum CursorType
{
    /// <summary>
    /// The default arrow cursor.
    /// </summary>
    Arrow = 0,

    /// <summary>
    /// A crosshair cursor.
    /// </summary>
    Cross = 1,

    /// <summary>
    /// A hand cursor, typically used for hyperlinks.
    /// </summary>
    Hand = 2,

    /// <summary>
    /// A help cursor (arrow with question mark).
    /// </summary>
    Help = 3,

    /// <summary>
    /// An I-beam cursor for text selection.
    /// </summary>
    IBeam = 4,

    /// <summary>
    /// No cursor (hidden).
    /// </summary>
    None = 5,

    /// <summary>
    /// A pen cursor for ink input.
    /// </summary>
    Pen = 6,

    /// <summary>
    /// A scroll all directions cursor.
    /// </summary>
    ScrollAll = 7,

    /// <summary>
    /// A scroll east cursor.
    /// </summary>
    ScrollE = 8,

    /// <summary>
    /// A scroll north cursor.
    /// </summary>
    ScrollN = 9,

    /// <summary>
    /// A scroll northeast cursor.
    /// </summary>
    ScrollNE = 10,

    /// <summary>
    /// A scroll northwest cursor.
    /// </summary>
    ScrollNW = 11,

    /// <summary>
    /// A scroll south cursor.
    /// </summary>
    ScrollS = 12,

    /// <summary>
    /// A scroll southeast cursor.
    /// </summary>
    ScrollSE = 13,

    /// <summary>
    /// A scroll southwest cursor.
    /// </summary>
    ScrollSW = 14,

    /// <summary>
    /// A scroll west cursor.
    /// </summary>
    ScrollW = 15,

    /// <summary>
    /// A horizontal resize cursor.
    /// </summary>
    SizeWE = 16,

    /// <summary>
    /// A vertical resize cursor.
    /// </summary>
    SizeNS = 17,

    /// <summary>
    /// A diagonal resize cursor (northwest-southeast).
    /// </summary>
    SizeNWSE = 18,

    /// <summary>
    /// A diagonal resize cursor (northeast-southwest).
    /// </summary>
    SizeNESW = 19,

    /// <summary>
    /// A move cursor (four-way arrow).
    /// </summary>
    SizeAll = 20,

    /// <summary>
    /// A not allowed cursor.
    /// </summary>
    No = 21,

    /// <summary>
    /// A wait cursor (hourglass or spinning).
    /// </summary>
    Wait = 22,

    /// <summary>
    /// An arrow with hourglass cursor.
    /// </summary>
    AppStarting = 23,

    /// <summary>
    /// An up arrow cursor.
    /// </summary>
    UpArrow = 24
}

/// <summary>
/// Represents a cursor that can be displayed for a UI element.
/// </summary>
public sealed class Cursor
{
    private readonly CursorType _cursorType;

    /// <summary>
    /// Gets the cursor type.
    /// </summary>
    public CursorType CursorType => _cursorType;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cursor"/> class.
    /// </summary>
    /// <param name="cursorType">The cursor type.</param>
    public Cursor(CursorType cursorType)
    {
        _cursorType = cursorType;
    }

    /// <inheritdoc />
    public override string ToString() => _cursorType.ToString();

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Cursor other && _cursorType == other._cursorType;

    /// <inheritdoc />
    public override int GetHashCode() => _cursorType.GetHashCode();

    /// <summary>
    /// Compares two Cursor instances for equality.
    /// </summary>
    public static bool operator ==(Cursor? left, Cursor? right)
    {
        if (left is null) return right is null;
        if (right is null) return false;
        return left._cursorType == right._cursorType;
    }

    /// <summary>
    /// Compares two Cursor instances for inequality.
    /// </summary>
    public static bool operator !=(Cursor? left, Cursor? right) => !(left == right);
}

/// <summary>
/// Provides a set of predefined <see cref="Cursor"/> values.
/// </summary>
public static class Cursors
{
    /// <summary>
    /// Gets the default arrow cursor.
    /// </summary>
    public static Cursor Arrow { get; } = new(CursorType.Arrow);

    /// <summary>
    /// Gets a crosshair cursor.
    /// </summary>
    public static Cursor Cross { get; } = new(CursorType.Cross);

    /// <summary>
    /// Gets a hand cursor (typically for hyperlinks).
    /// </summary>
    public static Cursor Hand { get; } = new(CursorType.Hand);

    /// <summary>
    /// Gets a help cursor.
    /// </summary>
    public static Cursor Help { get; } = new(CursorType.Help);

    /// <summary>
    /// Gets an I-beam cursor for text selection.
    /// </summary>
    public static Cursor IBeam { get; } = new(CursorType.IBeam);

    /// <summary>
    /// Gets a hidden cursor.
    /// </summary>
    public static Cursor None { get; } = new(CursorType.None);

    /// <summary>
    /// Gets a pen cursor.
    /// </summary>
    public static Cursor Pen { get; } = new(CursorType.Pen);

    /// <summary>
    /// Gets a scroll all directions cursor.
    /// </summary>
    public static Cursor ScrollAll { get; } = new(CursorType.ScrollAll);

    /// <summary>
    /// Gets a scroll east cursor.
    /// </summary>
    public static Cursor ScrollE { get; } = new(CursorType.ScrollE);

    /// <summary>
    /// Gets a scroll north cursor.
    /// </summary>
    public static Cursor ScrollN { get; } = new(CursorType.ScrollN);

    /// <summary>
    /// Gets a scroll northeast cursor.
    /// </summary>
    public static Cursor ScrollNE { get; } = new(CursorType.ScrollNE);

    /// <summary>
    /// Gets a scroll northwest cursor.
    /// </summary>
    public static Cursor ScrollNW { get; } = new(CursorType.ScrollNW);

    /// <summary>
    /// Gets a scroll south cursor.
    /// </summary>
    public static Cursor ScrollS { get; } = new(CursorType.ScrollS);

    /// <summary>
    /// Gets a scroll southeast cursor.
    /// </summary>
    public static Cursor ScrollSE { get; } = new(CursorType.ScrollSE);

    /// <summary>
    /// Gets a scroll southwest cursor.
    /// </summary>
    public static Cursor ScrollSW { get; } = new(CursorType.ScrollSW);

    /// <summary>
    /// Gets a scroll west cursor.
    /// </summary>
    public static Cursor ScrollW { get; } = new(CursorType.ScrollW);

    /// <summary>
    /// Gets a horizontal resize cursor.
    /// </summary>
    public static Cursor SizeWE { get; } = new(CursorType.SizeWE);

    /// <summary>
    /// Gets a vertical resize cursor.
    /// </summary>
    public static Cursor SizeNS { get; } = new(CursorType.SizeNS);

    /// <summary>
    /// Gets a diagonal resize cursor (northwest-southeast).
    /// </summary>
    public static Cursor SizeNWSE { get; } = new(CursorType.SizeNWSE);

    /// <summary>
    /// Gets a diagonal resize cursor (northeast-southwest).
    /// </summary>
    public static Cursor SizeNESW { get; } = new(CursorType.SizeNESW);

    /// <summary>
    /// Gets a move cursor.
    /// </summary>
    public static Cursor SizeAll { get; } = new(CursorType.SizeAll);

    /// <summary>
    /// Gets a not allowed cursor.
    /// </summary>
    public static Cursor No { get; } = new(CursorType.No);

    /// <summary>
    /// Gets a wait cursor.
    /// </summary>
    public static Cursor Wait { get; } = new(CursorType.Wait);

    /// <summary>
    /// Gets an app starting cursor.
    /// </summary>
    public static Cursor AppStarting { get; } = new(CursorType.AppStarting);

    /// <summary>
    /// Gets an up arrow cursor.
    /// </summary>
    public static Cursor UpArrow { get; } = new(CursorType.UpArrow);
}
