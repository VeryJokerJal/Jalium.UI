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
/// unchanged. The <see cref="Application"/>'s own properties
/// (<see cref="Application.MainWindow"/>, <see cref="Application.StartupUri"/>,
/// <see cref="Application.ShutdownMode"/>, <see cref="Application.Resources"/>)
/// are configured via <see cref="ConfigureApplication"/> or by subclassing
/// <see cref="Application"/> and registering the subclass with
/// <see cref="UseApplication{TApp}"/>.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var builder = AppBuilder.CreateBuilder(args);
/// builder.Services.AddSingleton&lt;IMyService, MyService&gt;();
/// builder.ConfigureApplication(app => app.MainWindow = new MainWindow());
/// using var app = builder.Build();
/// return app.Run();
/// </code>
/// Only one <see cref="Application"/> exists per process; calling <see cref="Build"/>
/// twice throws. The resulting <see cref="JaliumApp"/> owns both the
/// <see cref="IHost"/> (lifetime, DI scope root) and the <see cref="Jalium.UI.Application"/>
/// (UI message loop).
/// </remarks>
public sealed class AppBuilder : IHostApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private readonly List<Action<Application>> _configureApplication = new();
    private Func<Application>? _applicationFactory;
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

    // ── Application construction / configuration ────────────────────────────

    /// <summary>
    /// Queues a configuration callback that runs against the live
    /// <see cref="Application"/> after it is constructed but before the host is
    /// started. Use this to set <see cref="Application.MainWindow"/>,
    /// <see cref="Application.StartupUri"/>, <see cref="Application.ShutdownMode"/>,
    /// entries in <see cref="Application.Resources"/>, or to subscribe to the
    /// <see cref="Application.Startup"/>/<see cref="Application.Exit"/> events.
    /// </summary>
    public AppBuilder ConfigureApplication(Action<Application> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureApplication.Add(configure);
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="Application"/> subclass to be constructed at
    /// <see cref="Build"/> time via its parameterless constructor. Use this when
    /// you want to keep <c>OnStartup</c>/<c>OnExit</c>/<c>OnSessionEnding</c>
    /// overrides on your own <see cref="Application"/> subclass (the WPF-style
    /// entry point).
    /// </summary>
    /// <typeparam name="TApp">
    /// The <see cref="Application"/> subclass. Must expose a public parameterless
    /// constructor so the framework can instantiate it on the UI thread.
    /// </typeparam>
    /// <remarks>
    /// Calling this multiple times keeps only the last registration. Mutually
    /// exclusive with the other <c>UseApplication</c> overloads.
    /// </remarks>
    public AppBuilder UseApplication<TApp>() where TApp : Application, new()
    {
        _applicationFactory = static () => new TApp();
        return this;
    }

    /// <summary>
    /// Registers a factory that constructs the <see cref="Application"/> instance
    /// at <see cref="Build"/> time. The factory runs on the same thread that
    /// calls <see cref="Build"/>, which must be the UI thread.
    /// </summary>
    public AppBuilder UseApplication(Func<Application> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _applicationFactory = factory;
        return this;
    }

    /// <summary>
    /// Uses a pre-constructed <see cref="Application"/> instance. The instance
    /// must not have been started (no prior <see cref="Application.Run()"/> call).
    /// Useful when you need access to the <see cref="Application"/> before
    /// <see cref="Build"/> — for example to attach handlers early.
    /// </summary>
    public AppBuilder UseApplication(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _applicationFactory = () => application;
        return this;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="IHost"/>, constructs the single <see cref="Application"/>
    /// instance, applies the builder's Jalium.UI-specific settings, and wires the
    /// host <see cref="IServiceProvider"/>, <see cref="IConfiguration"/>, and
    /// <see cref="IHostEnvironment"/> into the <see cref="Application"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="JaliumApp"/> that owns both the host and the application.
    /// Dispose it (or call <see cref="JaliumApp.Run()"/>, which disposes on exit)
    /// to tear everything down cleanly.
    /// </returns>
    public JaliumApp Build()
    {
        if (_isBuilt)
        {
            throw new InvalidOperationException("AppBuilder.Build can only be called once per builder instance.");
        }

        // Only one Application may exist per process. If a factory is registered we trust it
        // (UseApplication(Application instance) passes a pre-constructed instance, so Current
        // is already set); otherwise we create the default Application and its constructor
        // enforces the singleton invariant.
        if (_applicationFactory == null && Application.Current != null)
        {
            throw new InvalidOperationException(
                "An Application instance already exists. AppBuilder creates the process-wide Application; " +
                "call UseApplication(...) to bind an existing instance, or do not construct Application directly.");
        }

        _isBuilt = true;

        var host = _hostBuilder.Build();
        var application = _applicationFactory?.Invoke() ?? new Application();

        if (!ReferenceEquals(application, Application.Current))
        {
            throw new InvalidOperationException(
                "UseApplication factory returned an instance different from Application.Current. " +
                "The Application singleton must be the one produced by the factory.");
        }

        application.AttachHost(
            host.Services,
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<IHostEnvironment>());

        foreach (var configure in _configureApplication)
        {
            configure(application);
        }

        return new JaliumApp(host, application, Args);
    }
}
