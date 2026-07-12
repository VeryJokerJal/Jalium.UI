using Jalium.UI.Controls.Automation.AtSpi;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Runtime diagnostics for the Linux AT-SPI2 accessibility bridge.
/// </summary>
public static class LinuxAccessibility
{
    /// <summary>Gets whether the process is registered on the AT-SPI2 accessibility bus.</summary>
    public static bool IsAtSpiActive => AtSpiAccessibilityBridge.IsActive;

    /// <summary>Gets a stable, human-readable bridge state.</summary>
    public static string AtSpiStatus => AtSpiAccessibilityBridge.Status;

    /// <summary>Gets the last startup or transport error, if any.</summary>
    public static string? AtSpiLastError => AtSpiAccessibilityBridge.LastError;
}
