namespace Jalium.UI.Media;

/// <summary>
/// Retains immutable platform shaping for a drawing independently of the
/// layout owner, which may be disposed before the drawing is replayed.
/// </summary>
internal interface IPlatformTextLine
{
    object CreateRenderSnapshot();
}

/// <summary>Repaints retained shaping without changing glyphs, positions, or source ownership.</summary>
internal interface IPlatformTextPaintLine
{
    object CreatePaintSnapshot(Brush? foreground);
}
