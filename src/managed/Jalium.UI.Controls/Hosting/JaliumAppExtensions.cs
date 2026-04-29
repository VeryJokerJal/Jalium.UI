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

    /// <summary>
    /// Opts in to the idle-resource reclaimer. Once enabled, every visual that
    /// has stayed off-screen — collapsed, hidden, scrolled out of the viewport,
    /// or in a window that is no longer being painted — for longer than
    /// <see cref="ResourceReclamationOptions.IdleTimeoutMs"/> has its retained
    /// drawing-cache slot evicted, and any element that implements
    /// <see cref="IReclaimableResource"/> has its
    /// <see cref="IReclaimableResource.ReclaimIdleResources"/> method invoked
    /// so it can release decoded pixels, GPU uploads, decoder state, and other
    /// re-acquirable resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tunables live on the singleton <see cref="ResourceReclamationOptions"/>
    /// resolved from the application's service provider. Defaults: idle window
    /// 2 s, scan once every 60 frames (~once per second at 60 Hz), both the
    /// drawing-cache eviction and the <see cref="IReclaimableResource"/>
    /// callback are on. Mutate the options at any time to retune at runtime.
    /// </para>
    /// <para>
    /// The reclaimer is stopped and disposed automatically when the host
    /// shuts down (it is registered as a DI singleton, so the service-provider
    /// dispose chain handles teardown). Calling this method more than once is
    /// a no-op after the first call.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = AppBuilder.CreateBuilder(args);
    /// using var app = builder.Build();
    /// app.UseIdleResourceReclamation();   // default: 2 s idle window
    /// app.Run();
    /// </code>
    /// To tune the idle window:
    /// <code>
    /// app.Services.GetRequiredService&lt;ResourceReclamationOptions&gt;().IdleTimeoutMs = 5000;
    /// app.UseIdleResourceReclamation();
    /// </code>
    /// </example>
    public static JaliumApp UseIdleResourceReclamation(this JaliumApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var reclaimer = app.Services.GetRequiredService<ResourceReclaimer>();
        reclaimer.Start();
        return app;
    }
}
