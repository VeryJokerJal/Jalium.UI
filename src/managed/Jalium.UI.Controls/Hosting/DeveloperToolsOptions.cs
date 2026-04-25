using Microsoft.Extensions.DependencyInjection;

namespace Jalium.UI.Hosting;

/// <summary>
/// Opt-in switches for Jalium.UI's built-in developer surfaces.
/// Default: both features are <see langword="false"/> so they never show up in
/// shipped applications. Opt in from the built <see cref="JaliumApp"/> (ASP.NET
/// Core-style — <c>Use*</c> lives on the app, not the builder):
/// <code>
/// var builder = AppBuilder.CreateBuilder(args);
/// using var app = builder.Build();
/// app.UseDevTools();    // F12 + Ctrl+Shift+C picker
/// app.UseDebugHud();    // F3 overlay
/// </code>
/// Without these calls the key handlers silently ignore F3/F12 — no DevToolsWindow
/// is ever constructed, no HUD overlay is ever toggled on.
/// </summary>
/// <remarks>
/// Registered as a singleton on the DI container by <see cref="AppBuilder"/>, so
/// every <c>Use*</c> extension and every framework consumer sees the same
/// mutable instance. Framework code should never construct it directly — resolve
/// via DI or read through <see cref="DeveloperToolsResolver"/>.
/// </remarks>
public sealed class DeveloperToolsOptions
{
    /// <summary>
    /// When <see langword="true"/>, F12 opens the DevTools inspector window and
    /// Ctrl+Shift+C activates the element-picker gesture. Otherwise those keys
    /// are inert.
    /// </summary>
    public bool EnableDevTools { get; set; } = false;

    /// <summary>
    /// When <see langword="true"/>, F3 toggles the on-screen debug HUD overlay
    /// (frame timings, dirty regions, backend info). Otherwise F3 is a no-op.
    /// </summary>
    public bool EnableDebugHud { get; set; } = false;
}

/// <summary>
/// Static resolver used by low-level framework hooks (Window, input dispatcher)
/// that need to check the developer-tools switches without forcing a reference
/// through <see cref="Application.Services"/> at every call site. Returns
/// safe defaults when the application was not built via <see cref="AppBuilder"/>
/// or when no <c>app.UseDevTools()</c>/<c>app.UseDebugHud()</c> call has been made.
/// </summary>
internal static class DeveloperToolsResolver
{
    public static DeveloperToolsOptions Current
    {
        get
        {
            var services = Application.Current?.Services;
            if (services == null) return DefaultsInstance;

            try
            {
                return services.GetService<DeveloperToolsOptions>() ?? DefaultsInstance;
            }
            catch
            {
                // ObjectDisposedException during shutdown, missing registration,
                // etc. — treat as "not enabled".
                return DefaultsInstance;
            }
        }
    }

    public static bool IsDevToolsEnabled => Current.EnableDevTools;
    public static bool IsDebugHudEnabled => Current.EnableDebugHud;

    // Shared read-only defaults so the hot path doesn't allocate.
    private static readonly DeveloperToolsOptions DefaultsInstance = new();
}
