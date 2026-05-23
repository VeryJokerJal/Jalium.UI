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
/// Older-style generic-host builder. Predates <see cref="HostApplicationBuilder"/> and
/// composes configuration / services through a chain of callbacks (<c>ConfigureServices</c>,
/// <c>ConfigureAppConfiguration</c>, etc.). Mirrors <c>Microsoft.Extensions.Hosting.HostBuilder</c>.
/// </summary>
public sealed class HostBuilder : IHostBuilder
{
    private readonly List<Action<IConfigurationBuilder>> _configureHostConfigActions = new();
    private readonly List<Action<HostBuilderContext, IConfigurationBuilder>> _configureAppConfigActions = new();
    private readonly List<Action<HostBuilderContext, IServiceCollection>> _configureServicesActions = new();
    private readonly List<IConfigureContainerAdapter> _configureContainerActions = new();
    private IServiceFactoryAdapter _serviceProviderFactory = new ServiceFactoryAdapter<IServiceCollection>(new DefaultServiceProviderFactory());
    private bool _built;

    public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

    public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
    {
        ArgumentNullException.ThrowIfNull(configureDelegate);
        _configureHostConfigActions.Add(configureDelegate);
        return this;
    }

    public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
    {
        ArgumentNullException.ThrowIfNull(configureDelegate);
        _configureAppConfigActions.Add(configureDelegate);
        return this;
    }

    public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
    {
        ArgumentNullException.ThrowIfNull(configureDelegate);
        _configureServicesActions.Add(configureDelegate);
        return this;
    }

    public IHostBuilder ConfigureServices(Action<IServiceCollection> configureDelegate)
    {
        ArgumentNullException.ThrowIfNull(configureDelegate);
        _configureServicesActions.Add((_, s) => configureDelegate(s));
        return this;
    }

    public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory) where TContainerBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(factory);
        _serviceProviderFactory = new ServiceFactoryAdapter<TContainerBuilder>(factory);
        return this;
    }

    public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory) where TContainerBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(factory);
        _serviceProviderFactory = new ServiceFactoryAdapter<TContainerBuilder>(factory);
        return this;
    }

    public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
    {
        ArgumentNullException.ThrowIfNull(configureDelegate);
        _configureContainerActions.Add(new ConfigureContainerAdapter<TContainerBuilder>(configureDelegate));
        return this;
    }

    public IHost Build()
    {
        if (_built) throw new InvalidOperationException("Build can only be called once.");
        _built = true;

        // ── Phase 1: host configuration (env / app name / content root) ──────
        var hostConfigBuilder = new ConfigurationBuilder();
        hostConfigBuilder.AddInMemoryCollection();
        foreach (var a in _configureHostConfigActions) a(hostConfigBuilder);
        var hostConfig = hostConfigBuilder.Build();

        var envName = hostConfig["environment"]
            ?? System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;
        var appName = hostConfig["applicationName"]
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? string.Empty;
        var contentRoot = hostConfig["contentRoot"] ?? AppContext.BaseDirectory;

        var environment = new HostingEnvironment
        {
            EnvironmentName = envName,
            ApplicationName = appName,
            ContentRootPath = contentRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
        };

        var hostContext = new HostBuilderContext(Properties)
        {
            HostingEnvironment = environment,
            Configuration = hostConfig,
        };

        // ── Phase 2: app configuration ────────────────────────────────────────
        var appConfigBuilder = new ConfigurationBuilder();
        appConfigBuilder.AddConfiguration(hostConfig);
        ((IConfigurationBuilder)appConfigBuilder).SetBasePath(contentRoot);
        foreach (var a in _configureAppConfigActions) a(hostContext, appConfigBuilder);
        var appConfig = appConfigBuilder.Build();
        hostContext.Configuration = appConfig;

        // ── Phase 3: services ─────────────────────────────────────────────────
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddSingleton(hostContext);
        services.AddSingleton<IConfiguration>(appConfig);
        services.AddSingleton<IHostApplicationLifetime, ApplicationLifetime>();
        services.AddSingleton<IHostLifetime, NoOpHostLifetime>();
        services.AddSingleton<HostOptions>(_ => new HostOptions());
        services.AddSingleton<IHost, JaliumHost>();
        services.AddOptions();
        services.AddLogging(b => b.AddConfiguration(appConfig.GetSection("Logging")).AddDebug());
        services.AddMetrics();

        foreach (var a in _configureServicesActions) a(hostContext, services);

        // ── Phase 4: container builder ────────────────────────────────────────
        var containerBuilder = _serviceProviderFactory.CreateBuilder(services);
        foreach (var ca in _configureContainerActions) ca.ConfigureContainer(hostContext, containerBuilder);
        var provider = _serviceProviderFactory.CreateServiceProvider(containerBuilder);

        return provider.GetRequiredService<IHost>();
    }

    private interface IServiceFactoryAdapter
    {
        object CreateBuilder(IServiceCollection services);
        IServiceProvider CreateServiceProvider(object containerBuilder);
    }

    private sealed class ServiceFactoryAdapter<TContainerBuilder> : IServiceFactoryAdapter where TContainerBuilder : notnull
    {
        private readonly IServiceProviderFactory<TContainerBuilder>? _factory;
        private readonly Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>>? _contextFactory;
        public ServiceFactoryAdapter(IServiceProviderFactory<TContainerBuilder> factory) { _factory = factory; }
        public ServiceFactoryAdapter(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory) { _contextFactory = factory; }
        public object CreateBuilder(IServiceCollection services)
            => (_factory ?? throw new InvalidOperationException("Context-bound factory cannot run without HostBuilderContext.")).CreateBuilder(services);
        public IServiceProvider CreateServiceProvider(object containerBuilder)
            => (_factory!).CreateServiceProvider((TContainerBuilder)containerBuilder);
    }

    private interface IConfigureContainerAdapter
    {
        void ConfigureContainer(HostBuilderContext context, object containerBuilder);
    }

    private sealed class ConfigureContainerAdapter<TContainerBuilder> : IConfigureContainerAdapter
    {
        private readonly Action<HostBuilderContext, TContainerBuilder> _action;
        public ConfigureContainerAdapter(Action<HostBuilderContext, TContainerBuilder> action) { _action = action; }
        public void ConfigureContainer(HostBuilderContext context, object containerBuilder) => _action(context, (TContainerBuilder)containerBuilder);
    }
}

public static partial class Host
{
    /// <summary>Old-style generic host builder. <see cref="CreateApplicationBuilder()"/> is the newer recommended entry point.</summary>
    public static IHostBuilder CreateDefaultBuilder() => CreateDefaultBuilder(args: null);

    public static IHostBuilder CreateDefaultBuilder(string[]? args)
    {
        var builder = new HostBuilder();
        builder.ConfigureHostConfiguration(cb =>
        {
            cb.AddEnvironmentVariables(prefix: "DOTNET_");
            if (args != null && args.Length > 0) cb.AddCommandLine(args);
        });
        builder.ConfigureAppConfiguration((ctx, cb) =>
        {
            var env = ctx.HostingEnvironment;
            cb.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
              .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
            if (env.IsDevelopment() && !string.IsNullOrEmpty(env.ApplicationName))
            {
                try
                {
                    var asm = Assembly.Load(new AssemblyName(env.ApplicationName));
                    if (asm.GetCustomAttribute<UserSecretsIdAttribute>() != null)
                        cb.AddUserSecrets(asm, optional: true, reloadOnChange: false);
                }
                catch { /* assembly might not be loadable by name — best-effort */ }
            }
            cb.AddEnvironmentVariables();
            if (args != null && args.Length > 0) cb.AddCommandLine(args);
        });
        builder.ConfigureServices((_, _) => { /* placeholder — defaults are added in HostBuilder.Build itself */ });
        return builder;
    }
}
