namespace Jalium.UI.Media;

/// <summary>
/// Implements a set of predefined DashStyle objects.
/// </summary>
public static class DashStyles
{
    private static DashStyle? _solid;
    private static DashStyle? _dash;
    private static DashStyle? _dot;
    private static DashStyle? _dashDot;
    private static DashStyle? _dashDotDot;

    /// <summary>
    /// Gets a DashStyle that represents a solid line (no dashes).
    /// </summary>
    public static DashStyle Solid => _solid ??= new DashStyle();

    /// <summary>
    /// Gets a DashStyle that represents a dashed line.
    /// </summary>
    public static DashStyle Dash => _dash ??= new DashStyle(new[] { 2.0, 2.0 }, 0);

    /// <summary>
    /// Gets a DashStyle that represents a dotted line.
    /// </summary>
    public static DashStyle Dot => _dot ??= new DashStyle(new[] { 0.0, 2.0 }, 0);

    /// <summary>
    /// Gets a DashStyle that represents an alternating dash-dot line.
    /// </summary>
    public static DashStyle DashDot => _dashDot ??= new DashStyle(new[] { 2.0, 2.0, 0.0, 2.0 }, 0);

    /// <summary>
    /// Gets a DashStyle that represents an alternating dash-dot-dot line.
    /// </summary>
    public static DashStyle DashDotDot => _dashDotDot ??= new DashStyle(new[] { 2.0, 2.0, 0.0, 2.0, 0.0, 2.0 }, 0);
}
