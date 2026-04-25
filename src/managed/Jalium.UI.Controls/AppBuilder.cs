using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jalium.UI;

/// <summary>
/// Optional configuration passed to <see cref="AppBuilder.CreateBuilder(AppBuilderSettings)"/>.
/// Mirrors the fields of <see cref="HostApplicationBuilderSettings"/> so callers can
/// customize configuration, content root, environment name, and whether default
/// Microsoft.Extensions.Hosting setup (logging, configuration sources) is applied.
/// </summary>
public sealed class AppBuilderSettings
{
    /// <summary>
    /// Command-line arguments forwarded into <see cref="IConfiguration"/> and to
    /// <see cref="Application.Run(string[])"/>.
    /// </summary>
    public string[]? Args { get; set; }

    /// <summary>
    /// Application name used by the hosting environment. When <see langword="null"/>
    /// the entry assembly name is used.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Environment name (<c>Development</c>/<c>Staging</c>/<c>Production</c>).
    /// Falls back to the <c>DOTNET_ENVIRONMENT</c> environment variable when null.
    /// </summary>
    public string? EnvironmentName { get; set; }

    /// <summary>
    /// Explicit content root directory. Defaults to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string? ContentRootPath { get; set; }

    /// <summary>
    /// An existing <see cref="ConfigurationManager"/> to seed the builder's configuration.
    /// </summary>
    public ConfigurationManager? Configuration { get; set; }

    /// <summary>
    /// When <see langword="true"/>, skip the default configuration sources (appsettings.json,
    /// environment variables, command-line), default logging providers, and default service
    /// registrations added by <see cref="HostApplicationBuilder"/>.
    /// </summary>
    public bool DisableDefaults { get; set; }
}

/// <summary>
/// Fluent builder for a Jalium.UI application. Implements
/// <see cref="IHostApplicationBuilder"/> so the standard Microsoft.Extensions
/// configuration, dependency-injection, logging, and metrics patterns work
/// unchanged.
/// </summary>
/// <remarks>
/// <para>
/// API shape follows ASP.NET Core: <c>builder.Services.Add*</c> /
/// <c>builder.Configure*</c> happen <em>before</em> <see cref="Build"/>;
/// every <c>Use*</c> — including <c>UseApplication</c> — lives on the built
/// <see cref="JaliumApp"/> and runs <em>after</em> <see cref="Build"/>.
/// </para>
/// <para>
/// Usage:
/// <code>
/// var builder = AppBuilder.CreateBuilder(args);
/// builder.Services.AddSingleton&lt;IMyService, MyService&gt;();
/// builder.ConfigureApplication(a => a.MainWindow = new MainWindow());
/// using var app = builder.Build();
/// app.UseApplication&lt;App&gt;();     // pick Application subtype
/// app.UseDevTools();              // activate feature opt-ins
/// app.UseJaliumMetrics();
/// return app.Run();
/// </code>
/// </para>
/// <para>
/// Only one <see cref="Application"/> exists per process. <see cref="Build"/>
/// does not construct it — that's deferred until
/// <see cref="JaliumApp.UseApplication{TApp}"/> (or friends), or the first
/// access that requires it (e.g. <see cref="JaliumApp.Run()"/>), at which point
/// a default <see cref="Application"/> is created if no <c>UseApplication</c>
/// has run. The returned <see cref="JaliumApp"/> owns both the
/// <see cref="IHost"/> (lifetime, DI scope root) and, once bound, the
/// <see cref="Jalium.UI.Application"/> (UI message loop).
/// </para>
/// </remarks>
public sealed class AppBuilder : IHostApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private readonly List<Action<Application>> _configureApplication = new();
    private bool _isBuilt;

    private AppBuilder(AppBuilderSettings? settings)
    {
        var hostSettings = new HostApplicationBuilderSettings
        {
            Args = settings?.Args,
            ApplicationName = settings?.ApplicationName,
            EnvironmentName = settings?.EnvironmentName,
            ContentRootPath = settings?.ContentRootPath,
            Configuration = settings?.Configuration,
            DisableDefaults = settings?.DisableDefaults ?? false,
        };
        _hostBuilder = new HostApplicationBuilder(hostSettings);
        Args = settings?.Args;

        // Core Jalium services are always available — keeps consumer code
        // from having to opt-in for basic MVVM and options support. Users can
        // still call ConfigureJalium() to bind appsettings.json, or override
        // individual registrations.
        _hostBuilder.Services.TryAddSingleton<ViewRegistry>();
        _hostBuilder.Services.TryAddSingleton<IViewFactory, ViewFactory>();
        _hostBuilder.Services.TryAddSingleton<DeveloperToolsOptions>();
        _hostBuilder.Services.AddOptions<JaliumRuntimeOptions>()
            .Bind(_hostBuilder.Configuration.GetSection(JaliumRuntimeOptions.SectionName));
    }

    /// <summary>
    /// Creates a new <see cref="AppBuilder"/> with default settings.
    /// </summary>
    public static AppBuilder CreateBuilder() => new(settings: null);

    /// <summary>
    /// Creates a new <see cref="AppBuilder"/> seeded with the supplied command-line arguments.
    /// Arguments are parsed into <see cref="IConfiguration"/> and forwarded to
    /// <see cref="Application.Run(string[])"/>.
    /// </summary>
    public static AppBuilder CreateBuilder(string[] args) => new(new AppBuilderSettings { Args = args });

    /// <summary>
    /// Creates a new <see cref="AppBuilder"/> from explicit <paramref name="settings"/>.
    /// </summary>
    public static AppBuilder CreateBuilder(AppBuilderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new AppBuilder(settings);
    }

    /// <summary>
    /// Command-line arguments captured at builder creation time (may be <see langword="null"/>).
    /// </summary>
    public string[]? Args { get; }

    // ── IHostApplicationBuilder ──────────────────────────────────────────────

    /// <inheritdoc />
    public IConfigurationManager Configuration => _hostBuilder.Configuration;

    /// <inheritdoc />
    public IHostEnvironment Environment => _hostBuilder.Environment;

    /// <inheritdoc />
    public ILoggingBuilder Logging => _hostBuilder.Logging;

    /// <inheritdoc />
    public IMetricsBuilder Metrics => _hostBuilder.Metrics;

    /// <inheritdoc />
    IDictionary<object, object> IHostApplicationBuilder.Properties =>
        ((IHostApplicationBuilder)_hostBuilder).Properties;

    /// <inheritdoc />
    public IServiceCollection Services => _hostBuilder.Services;

    /// <inheritdoc />
    public void ConfigureContainer<TContainerBuilder>(
        IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null)
        where TContainerBuilder : notnull
        => _hostBuilder.ConfigureContainer(factory, configure);

    // ── Application configuration ───────────────────────────────────────────

    /// <summary>
    /// Queues a configuration callback that runs against the live
    /// <see cref="Application"/> after it is constructed (by
    /// <see cref="JaliumApp.UseApplication{TApp}"/> or the default-Application
    /// fallback) but before the host is started. Use this to set
    /// <see cref="Application.MainWindow"/>, <see cref="Application.StartupUri"/>,
    /// <see cref="Application.ShutdownMode"/>, entries in
    /// <see cref="Application.Resources"/>, or to subscribe to the
    /// <see cref="Application.Startup"/>/<see cref="Application.Exit"/> events.
    /// </summary>
    public AppBuilder ConfigureApplication(Action<Application> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureApplication.Add(configure);
        return this;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="IHost"/> and returns a <see cref="JaliumApp"/>
    /// that is not yet bound to an <see cref="Application"/>. Call
    /// <see cref="JaliumApp.UseApplication{TApp}"/> (or friends) to bind a
    /// subclass; otherwise a default <see cref="Application"/> is created on
    /// first need.
    /// </summary>
    /// <returns>
    /// A <see cref="JaliumApp"/> that owns the host. Dispose it (or call
    /// <see cref="JaliumApp.Run()"/>, which disposes on exit) to tear everything
    /// down cleanly.
    /// </returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute",
        Justification = "HostApplicationBuilder default configuration providers are analyzer-safe; consumers opt in to reflective features explicitly.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Generic code may not be available at runtime",
        Justification = "HostApplicationBuilder.Build uses reflection only for optional features not enabled by default.")]
    public JaliumApp Build()
    {
        if (_isBuilt)
        {
            throw new InvalidOperationException("AppBuilder.Build can only be called once per builder instance.");
        }

        _isBuilt = true;

        var host = _hostBuilder.Build();
        return new JaliumApp(host, _configureApplication.ToArray(), Args);
    }
}
