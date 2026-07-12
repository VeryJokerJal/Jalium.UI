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
    public static DashStyle Solid => _solid ??= CreateFrozen([]);

    /// <summary>
    /// Gets a DashStyle that represents a dashed line.
    /// </summary>
    public static DashStyle Dash => _dash ??= CreateFrozen([2.0, 2.0]);

    /// <summary>
    /// Gets a DashStyle that represents a dotted line.
    /// </summary>
    public static DashStyle Dot => _dot ??= CreateFrozen([0.0, 2.0]);

    /// <summary>
    /// Gets a DashStyle that represents an alternating dash-dot line.
    /// </summary>
    public static DashStyle DashDot => _dashDot ??= CreateFrozen([2.0, 2.0, 0.0, 2.0]);

    /// <summary>
    /// Gets a DashStyle that represents an alternating dash-dot-dot line.
    /// </summary>
    public static DashStyle DashDotDot => _dashDotDot ??= CreateFrozen([2.0, 2.0, 0.0, 2.0, 0.0, 2.0]);

    private static DashStyle CreateFrozen(IEnumerable<double> dashes)
    {
        var result = new DashStyle(dashes, 0);
        result.Freeze();
        return result;
    }
}
