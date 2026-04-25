using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jalium.UI.Hosting;

/// <summary>
/// Feature-activation extensions that live on the built <see cref="JaliumApp"/>
/// rather than <see cref="AppBuilder"/>. Mirrors the ASP.NET Core convention:
/// registration (<c>builder.Services.Add*</c>) happens before
/// <see cref="AppBuilder.Build"/>; activation (<c>app.Use*</c>) happens after.
/// </summary>
public static class JaliumAppExtensions
{
    /// <summary>
    /// Opts in to the Jalium.UI DevTools inspector. Without this call F12 /
    /// Ctrl+Shift+C are inert and no <c>DevToolsWindow</c> is ever constructed —
    /// shipping builds should simply not call it.
    /// </summary>
    /// <remarks>
    /// The flag is stored on the singleton <see cref="DeveloperToolsOptions"/>
    /// resolved from the application's service provider.
    /// </remarks>
    public static JaliumApp UseDevTools(this JaliumApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Services.GetRequiredService<DeveloperToolsOptions>().EnableDevTools = true;
        return app;
    }

    /// <summary>
    /// Opts in to the Jalium.UI on-screen debug HUD (frame times, dirty rects,
    /// backend info). Without this call F3 does nothing.
    /// </summary>
    public static JaliumApp UseDebugHud(this JaliumApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Services.GetRequiredService<DeveloperToolsOptions>().EnableDebugHud = true;
        return app;
    }

    /// <summary>
    /// Convenience: opts in to both DevTools and the Debug HUD in one call —
    /// equivalent to <c>app.UseDevTools().UseDebugHud()</c>.
    /// </summary>
    public static JaliumApp UseDeveloperTools(this JaliumApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<DeveloperToolsOptions>();
        options.EnableDevTools = true;
        options.EnableDebugHud = true;
        return app;
    }

    /// <summary>
    /// Enables Jalium.UI frame-time / FPS metric collection (see
    /// <see cref="JaliumMeter"/>). Metrics begin recording as soon as
    /// <see cref="CompositionTarget.Rendering"/> fires, which happens once the
    /// first <see cref="Window"/> is shown. The meter is stopped automatically
    /// when the application exits.
    /// </summary>
    public static JaliumApp UseJaliumMetrics(this JaliumApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The Jalium.UI Meter is registered on creation — any attached
        // IMetricsListener (dotnet-counters, OpenTelemetry MeterProvider, etc.)
        // that opts into the "Jalium.UI" meter name will see the samples.
        var options = app.Services.GetService<IOptions<JaliumRuntimeOptions>>()?.Value;
        var window = options?.Metrics.FpsWindowFrames > 0 ? options.Metrics.FpsWindowFrames : 60;
        JaliumMeter.Start(window);

        app.Application.Exit += (_, _) => JaliumMeter.Stop();

        return app;
    }
}
