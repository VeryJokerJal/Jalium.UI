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

    /// <summary>
    /// Opts in to touch-friendly mode. Once enabled, every touch / pen hit-test
    /// expands its candidate radius to <paramref name="minHitTargetSize"/> DIPs
    /// so finger-sized contacts still land on the intended control even when
    /// the visual hit zone is smaller.
    /// </summary>
    /// <param name="app">The host application.</param>
    /// <param name="minHitTargetSize">Minimum hit-target diameter in DIPs (default 40 — matches Material / WinUI guidelines).</param>
    public static JaliumApp UseTouchMode(this JaliumApp app, double minHitTargetSize = 40.0)
    {
        ArgumentNullException.ThrowIfNull(app);
        TouchModeOptions.Current.Enabled = true;
        TouchModeOptions.Current.MinHitTargetSize = Math.Max(0, minHitTargetSize);
        return app;
    }

    /// <summary>
    /// Selects the rendering strategy the framework uses to turn each frame's damage
    /// into GPU work. See <see cref="RenderingMode"/> for the modes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the first-class replacement for the <c>JALIUM_DAMAGE_SCOPED</c>
    /// environment switch. Default is <see cref="RenderingMode.Adaptive"/> — the
    /// framework picks per GPU adapter, so most apps never need to call this.
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="RenderingMode.Adaptive"/> — integrated / software GPUs get
    ///   <see cref="RenderingMode.Performance"/> (once damage-scoped rendering is
    ///   available), discrete GPUs get <see cref="RenderingMode.FullFrame"/>.</item>
    ///   <item><see cref="RenderingMode.Performance"/> — lowest GPU / power; each
    ///   present only redraws what changed. Best for weak integrated GPUs.</item>
    ///   <item><see cref="RenderingMode.FullFrame"/> — smoothest / highest fidelity;
    ///   every present rasterizes the whole window. Best for discrete GPUs.</item>
    /// </list>
    /// <example>
    /// <code>
    /// using var app = builder.Build();
    /// app.UseRenderingMode(RenderingMode.Performance);   // force low-GPU path
    /// app.Run();
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="app">The host application.</param>
    /// <param name="mode">The rendering mode to select. Defaults to <see cref="RenderingMode.Adaptive"/>.</param>
    public static JaliumApp UseRenderingMode(this JaliumApp app, RenderingMode mode = RenderingMode.Adaptive)
    {
        ArgumentNullException.ThrowIfNull(app);
        RenderingModeOptions.Current.Mode = mode;
        return app;
    }

    /// <summary>
    /// Selects the anti-aliasing quality for filled vector paths. See
    /// <see cref="PathAntiAliasing"/> for the tiers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Path fills are the dominant per-present GPU cost on weak GPUs (8× stencil-
    /// then-cover paths measured ~6–7 ms each on an iGPU). Default is
    /// <see cref="PathAntiAliasing.Msaa8x"/> (unchanged historical behavior).
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="PathAntiAliasing.Analytic"/> — cheapest; analytic coverage
    ///   AA (how WPF / Chromium anti-alias 2D vectors). Best for weak integrated GPUs.</item>
    ///   <item><see cref="PathAntiAliasing.Msaa4x"/> — the common MSAA baseline; roughly
    ///   halves the path cost vs 8× with near-identical quality.</item>
    ///   <item><see cref="PathAntiAliasing.Msaa8x"/> — historical default.</item>
    ///   <item><see cref="PathAntiAliasing.Msaa16x"/> — maximum smoothness (GPU permitting).</item>
    /// </list>
    /// <example>
    /// <code>
    /// using var app = builder.Build();
    /// app.UsePathAntiAliasing(PathAntiAliasing.Msaa4x);   // halve path GPU cost
    /// app.Run();
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="app">The host application.</param>
    /// <param name="mode">The path anti-aliasing quality. Defaults to <see cref="PathAntiAliasing.Msaa8x"/>.</param>
    public static JaliumApp UsePathAntiAliasing(this JaliumApp app, PathAntiAliasing mode = PathAntiAliasing.Msaa8x)
    {
        ArgumentNullException.ThrowIfNull(app);
        RenderQualityOptions.Current.PathAntiAliasing = mode;
        return app;
    }
}
