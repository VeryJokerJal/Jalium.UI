using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Makes platform system resources available without retaining an Application instance.
/// </summary>
internal static class SystemResourceIntegration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "System keys must resolve while parsing resources before an Application or Window is constructed.")]
    internal static void Register()
        => ResourceLookup.SystemResourceLookup = Lookup;

    private static object? Lookup(object key)
    {
        // Defer reading native preferences until a system key is actually requested.
        return SystemColors.TryGetResource(key, out var value)
            || SystemFonts.TryGetResource(key, out value)
            || SystemParameters.TryGetResource(key, out value)
                ? value
                : null;
    }
}
