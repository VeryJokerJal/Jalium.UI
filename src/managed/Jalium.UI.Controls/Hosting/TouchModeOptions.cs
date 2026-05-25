namespace Jalium.UI.Hosting;

/// <summary>
/// Process-wide toggle for "touch-friendly" mode. When enabled, the input
/// dispatcher widens the touch/pen hit-test region so finger-sized contacts
/// still land on the intended control.
/// </summary>
/// <remarks>
/// Activated by <c>app.UseTouchMode([minHitTargetSize])</c>. Defaults: disabled.
/// Apps that ship for both touch and mouse can flip <see cref="Enabled"/> at
/// runtime based on the active input source if desired.
/// </remarks>
public sealed class TouchModeOptions
{
    /// <summary>The single process-wide instance.</summary>
    public static TouchModeOptions Current { get; } = new();

    /// <summary>True to widen touch / pen hit-test bounds.</summary>
    public bool Enabled { get; set; }

    /// <summary>Minimum hit-target diameter (DIPs) — defaults to 40, matching Material/WinUI guidelines.</summary>
    public double MinHitTargetSize { get; set; } = 40.0;
}
