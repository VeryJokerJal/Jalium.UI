using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Jalium.Extensions.Configuration;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Diagnostics.Metrics;
using Jalium.Extensions.FileProviders;
using Jalium.Extensions.Logging;
using Jalium.Extensions.Logging.Debug;
using Jalium.Extensions.Options;

namespace Jalium.Extensions.Hosting;

/// <summary>
/// Jalium-flavored generic-host builder. API surface tracks
/// <c>Microsoft.Extensions.Hosting.HostApplicationBuilder</c>; defaults wire up:
/// <list type="bullet">
///   <item><see cref="ConfigurationManager"/> backed by environment variables + appsettings.json + appsettings.{Env}.json</item>
///   <item>Logging with Debug provider only (no console — by design)</item>
///   <item>Options + Metrics core registrations</item>
/// </list>
/// </summary>
public sealed class HostApplicationBuilder : IHostApplicationBuilder
{
    private readonly IConfigurationManager _configuration;
    private readonly ServiceCollection _services = new();
    private readonly HostBuilderContext _hostContext;
    private readonly HostingEnvironment _environment;
    private readonly LoggingBuilder _logging;
    private readonly MetricsBuilder _metrics;
    private Func<IServiceProvider> _createServiceProvider;
    private Action<object> _configureContainer = _ => { };
    private bool _built;

    public HostApplicationBuilder() : this(new HostApplicationBuilderSettings()) { }
    public HostApplicationBuilder(string[]? args) : this(new HostApplicationBuilderSettings { Args = args }) { }

    public HostApplicationBuilder(HostApplicationBuilderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _configuration = settings.Configuration ?? new ConfigurationManager();

        // ── 1. Compute env name + app name + content root ──
        var envName = settings.EnvironmentName
            ?? System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;
        var appName = settings.ApplicationName
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? string.Empty;
        var contentRoot = settings.ContentRootPath ?? AppContext.BaseDirectory;

        _environment = new HostingEnvironment
        {
            EnvironmentName = envName,
            ApplicationName = appName,
            ContentRootPath = contentRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
        };

        // ── 2. Seed configuration ──
        ((IConfigurationBuilder)_configuration).SetBasePath(contentRoot);

        if (!settings.DisableDefaults)
        {
            ((IConfigurationBuilder)_configuration)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{envName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "DOTNET_")
                .AddEnvironmentVariables();
        }

        _hostContext = new HostBuilderContext(new Dictionary<object, object>())
        {
            HostingEnvironment = _environment,
            Configuration = _configuration,
        };

        // ── 3. Core service registrations ──
        _services.AddSingleton<IHostEnvironment>(_environment);
        _services.AddSingleton<IConfiguration>(_configuration);
        _services.AddSingleton<HostBuilderContext>(_hostContext);
        _services.AddSingleton<IHostApplicationLifetime, ApplicationLifetime>();
        _services.AddSingleton<IHostLifetime, NoOpHostLifetime>();
        _services.AddSingleton<HostOptions>(_ => new HostOptions());
        _services.AddSingleton<IHost, JaliumHost>();
        _services.AddOptions();
        _services.AddLogging();

        if (!settings.DisableDefaults)
        {
            _logging = new LoggingBuilder(_services);
            ((ILoggingBuilder)_logging).AddConfiguration(_configuration.GetSection("Logging")).AddDebug();
        }
        else
        {
            _logging = new LoggingBuilder(_services);
        }

        _services.AddMetrics();
        _metrics = new MetricsBuilder(_services);

        _createServiceProvider = () => _services.BuildServiceProvider();
    }

    public IConfigurationManager Configuration => _configuration;
    public IHostEnvironment Environment => _environment;
    public ILoggingBuilder Logging => _logging;
    public IMetricsBuilder Metrics => _metrics;
    public IServiceCollection Services => _services;
    IDictionary<object, object> IHostApplicationBuilder.Properties => _hostContext.Properties;

    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder>? configure = null) where TContainerBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(factory);
        _createServiceProvider = () =>
        {
            var builder = factory.CreateBuilder(_services);
            configure?.Invoke(builder);
            _configureContainer?.Invoke(builder);
            return factory.CreateServiceProvider(builder);
        };
    }

    public IHost Build()
    {
        if (_built) throw new InvalidOperationException("Build can only be called once.");
        _built = true;
        var provider = _createServiceProvider();
        return provider.GetRequiredService<IHost>();
    }
}

/// <summary>Convenience entry point: <c>Host.CreateApplicationBuilder()</c>.</summary>
public static partial class Host
{
    public static HostApplicationBuilder CreateApplicationBuilder() => new();
    public static HostApplicationBuilder CreateApplicationBuilder(string[]? args) => new(args);
    public static HostApplicationBuilder CreateApplicationBuilder(HostApplicationBuilderSettings settings) => new(settings);

    public static HostApplicationBuilder CreateEmptyApplicationBuilder(HostApplicationBuilderSettings? settings)
    {
        settings ??= new HostApplicationBuilderSettings();
        settings.DisableDefaults = true;
        return new HostApplicationBuilder(settings);
    }
}

/// <summary>Convenience extensions for adding hosted services.</summary>
public static class ServiceCollectionHostedServiceExtensions
{
    public static IServiceCollection AddHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THostedService>(this IServiceCollection services)
        where THostedService : class, IHostedService
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, THostedService>());
        return services;
    }

    public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services, Func<IServiceProvider, THostedService> factory)
        where THostedService : class, IHostedService
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(sp => factory(sp)));
        return services;
    }
}

/// <summary>RunAsync / WaitForShutdownAsync helpers — mirror MS HostingAbstractionsHostExtensions.</summary>
public static class HostingAbstractionsHostExtensions
{
    public static void Start(this IHost host) => host.StartAsync().GetAwaiter().GetResult();
    public static void Stop(this IHost host) => host.StopAsync().GetAwaiter().GetResult();
    public static void Run(this IHost host) => host.RunAsync().GetAwaiter().GetResult();

    public static async Task RunAsync(this IHost host, CancellationToken token = default)
    {
        await host.StartAsync(token).ConfigureAwait(false);
        await host.WaitForShutdownAsync(token).ConfigureAwait(false);
        host.Dispose();
    }

    public static async Task WaitForShutdownAsync(this IHost host, CancellationToken token = default)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        token.Register(static s => ((IHostApplicationLifetime)s!).StopApplication(), lifetime);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStopping.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs);
        await tcs.Task.ConfigureAwait(false);
        await host.StopAsync().ConfigureAwait(false);
    }
}
