using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.Configuration;
using Jalium.Extensions.DependencyInjection;

namespace Jalium.Extensions.Options;

/// <summary>
/// Service-collection extensions for the Options framework. Surface mirrors MS:
/// <c>AddOptions</c>, <c>Configure</c>, <c>PostConfigure</c>, <c>OptionsBuilder.Bind</c>.
/// </summary>
public static class OptionsServiceCollectionExtensions
{
    /// <summary>Adds the core Options services (factory + monitor + cache + manager).</summary>
    public static IServiceCollection AddOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAdd(ServiceDescriptor.Singleton(typeof(IOptions<>), typeof(UnnamedOptionsManager<>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IOptionsSnapshot<>), typeof(OptionsManager<>)));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(IOptionsMonitor<>), typeof(OptionsMonitor<>)));
        services.TryAdd(ServiceDescriptor.Transient(typeof(IOptionsFactory<>), typeof(OptionsFactory<>)));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(IOptionsMonitorCache<>), typeof(OptionsCache<>)));
        return services;
    }

    public static OptionsBuilder<TOptions> AddOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services)
        where TOptions : class => services.AddOptions<TOptions>(global::Jalium.Extensions.Options.Options.DefaultName);

    public static OptionsBuilder<TOptions> AddOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, string? name)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        return new OptionsBuilder<TOptions>(services, name ?? global::Jalium.Extensions.Options.Options.DefaultName);
    }

    public static IServiceCollection Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, Action<TOptions> configure)
        where TOptions : class => services.Configure(global::Jalium.Extensions.Options.Options.DefaultName, configure);

    public static IServiceCollection Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, string? name, Action<TOptions> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<TOptions>>(new ConfigureNamedOptions<TOptions>(name, configure));
        return services;
    }

    public static IServiceCollection PostConfigure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, Action<TOptions> configure)
        where TOptions : class => services.PostConfigure(global::Jalium.Extensions.Options.Options.DefaultName, configure);

    public static IServiceCollection PostConfigure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, string? name, Action<TOptions> configure)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions();
        services.AddSingleton<IPostConfigureOptions<TOptions>>(new PostConfigureOptions<TOptions>(name, configure));
        return services;
    }

    public static IServiceCollection Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, IConfiguration config)
        where TOptions : class => services.Configure<TOptions>(global::Jalium.Extensions.Options.Options.DefaultName, config);

    public static IServiceCollection Configure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions>(this IServiceCollection services, string? name, IConfiguration config)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        services.AddOptions();
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(name, config));
        services.AddSingleton<IConfigureOptions<TOptions>>(new ConfigureNamedOptions<TOptions>(name, o => ConfigurationBinder.Bind(config, o)));
        return services;
    }
}

[RequiresUnreferencedCode("OptionsBuilder.Bind uses ConfigurationBinder.")]
public sealed class OptionsBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> where TOptions : class
{
    public string Name { get; }
    public IServiceCollection Services { get; }
    public OptionsBuilder(IServiceCollection services, string? name)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Name = name ?? global::Jalium.Extensions.Options.Options.DefaultName;
    }

    public OptionsBuilder<TOptions> Configure(Action<TOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        Services.AddSingleton<IConfigureOptions<TOptions>>(new ConfigureNamedOptions<TOptions>(Name, configureOptions));
        return this;
    }

    public OptionsBuilder<TOptions> PostConfigure(Action<TOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        Services.AddSingleton<IPostConfigureOptions<TOptions>>(new PostConfigureOptions<TOptions>(Name, configureOptions));
        return this;
    }

    public OptionsBuilder<TOptions> Bind(IConfiguration config) => Bind(config, _ => { });

    public OptionsBuilder<TOptions> Bind(IConfiguration config, Action<BinderOptions> configureBinder)
    {
        ArgumentNullException.ThrowIfNull(config);
        Services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(Name, config));
        Services.AddSingleton<IConfigureOptions<TOptions>>(new ConfigureNamedOptions<TOptions>(Name, o => ConfigurationBinder.Bind(config, o, configureBinder)));
        return this;
    }

    public OptionsBuilder<TOptions> BindConfiguration(string configSectionPath)
    {
        Services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(sp =>
        {
            var root = sp.GetRequiredService<IConfiguration>();
            var section = string.IsNullOrEmpty(configSectionPath) ? root : root.GetSection(configSectionPath);
            return new ConfigurationChangeTokenSource<TOptions>(Name, section);
        });
        Services.AddSingleton<IConfigureOptions<TOptions>>(sp =>
        {
            var root = sp.GetRequiredService<IConfiguration>();
            var section = string.IsNullOrEmpty(configSectionPath) ? root : root.GetSection(configSectionPath);
            return new ConfigureNamedOptions<TOptions>(Name, o => ConfigurationBinder.Bind(section, o));
        });
        return this;
    }

    public OptionsBuilder<TOptions> Validate(Func<TOptions, bool> validation, string failureMessage = "An options validation failure has occurred.")
    {
        ArgumentNullException.ThrowIfNull(validation);
        Services.AddSingleton<IValidateOptions<TOptions>>(new ValidateOptions<TOptions>(Name, validation, failureMessage));
        return this;
    }
}

/// <summary>Binds an <see cref="IConfiguration"/> reload token to options invalidation.</summary>
public sealed class ConfigurationChangeTokenSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptionsChangeTokenSource<TOptions>
{
    private readonly IConfiguration _config;
    public ConfigurationChangeTokenSource(string? name, IConfiguration config)
    {
        Name = name ?? global::Jalium.Extensions.Options.Options.DefaultName;
        _config = config;
    }
    public string? Name { get; }
    public Jalium.Extensions.Primitives.IChangeToken GetChangeToken() => _config.GetReloadToken();
}

/// <summary>Resolves <see cref="IOptions{T}"/> without per-scope semantics — always returns the default name.</summary>
[RequiresUnreferencedCode("Depends on OptionsFactory.")]
public sealed class UnnamedOptionsManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TOptions> : IOptions<TOptions>
    where TOptions : class, new()
{
    private readonly IOptionsFactory<TOptions> _factory;
    private TOptions? _value;
    private readonly object _sync = new();
    public UnnamedOptionsManager(IOptionsFactory<TOptions> factory) { _factory = factory; }
    public TOptions Value
    {
        get
        {
            if (_value != null) return _value;
            lock (_sync) return _value ??= _factory.Create(global::Jalium.Extensions.Options.Options.DefaultName);
        }
    }
}
