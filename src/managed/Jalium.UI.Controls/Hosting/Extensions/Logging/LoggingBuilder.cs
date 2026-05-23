using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.Configuration;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Options;

namespace Jalium.Extensions.Logging;

/// <summary>
/// Service-collection extensions that wire the Logging core (factory + filters + ILogger&lt;T&gt;).
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddLogging(this IServiceCollection services) => services.AddLogging(_ => { });

    public static IServiceCollection AddLogging(this IServiceCollection services, Action<ILoggingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        services.TryAddSingleton<ILoggerFactory, LoggerFactory>();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));

        configure?.Invoke(new LoggingBuilder(services));
        return services;
    }

    internal static IServiceCollection AddLoggerProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services) where T : class, ILoggerProvider
    {
        services.AddSingleton<ILoggerProvider, T>();
        return services;
    }
}

internal sealed class LoggingBuilder : ILoggingBuilder
{
    public LoggingBuilder(IServiceCollection services) { Services = services; }
    public IServiceCollection Services { get; }
}

public static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddProvider(this ILoggingBuilder builder, ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        builder.Services.AddSingleton(provider);
        return builder;
    }

    public static ILoggingBuilder ClearProviders(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<ILoggerProvider>();
        return builder;
    }

    public static ILoggingBuilder SetMinimumLevel(this ILoggingBuilder builder, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<LoggerFilterOptions>(o => o.MinLevel = level);
        return builder;
    }

    public static ILoggingBuilder AddFilter(this ILoggingBuilder builder, string? category, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<LoggerFilterOptions>(o => o.Rules.Add(new LoggerFilterRule(null, category, level, null)));
        return builder;
    }

    public static ILoggingBuilder AddFilter(this ILoggingBuilder builder, Func<string?, string?, LogLevel, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(filter);
        builder.Services.Configure<LoggerFilterOptions>(o => o.Rules.Add(new LoggerFilterRule(null, null, null, filter)));
        return builder;
    }

    public static ILoggingBuilder AddFilter<TProvider>(this ILoggingBuilder builder, string? category, LogLevel level) where TProvider : ILoggerProvider
    {
        builder.Services.Configure<LoggerFilterOptions>(o => o.Rules.Add(new LoggerFilterRule(typeof(TProvider).FullName, category, level, null)));
        return builder;
    }

    public static ILoggingBuilder AddConfiguration(this ILoggingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        builder.Services.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(new ConfigureOptions<LoggerFilterOptions>(o => ApplyConfiguration(o, configuration)));
        builder.Services.AddSingleton<IOptionsChangeTokenSource<LoggerFilterOptions>>(new ConfigurationChangeTokenSource<LoggerFilterOptions>(null, configuration));
        return builder;
    }

    private static void ApplyConfiguration(LoggerFilterOptions options, IConfiguration configuration)
    {
        // Honor "Logging:LogLevel:Default" / "Logging:LogLevel:<Category>" style configuration —
        // when the section was passed as the root Configuration directly, fall back to scanning
        // its "LogLevel" child if a "Logging" section is not present.
        var loggingSection = configuration.GetSection("Logging");
        var logLevelSection = loggingSection.Exists() ? loggingSection.GetSection("LogLevel") : configuration.GetSection("LogLevel");
        foreach (var entry in logLevelSection.GetChildren())
        {
            if (!Enum.TryParse<LogLevel>(entry.Value, ignoreCase: true, out var lvl)) continue;
            if (string.Equals(entry.Key, "Default", StringComparison.OrdinalIgnoreCase))
                options.MinLevel = lvl;
            else
                options.Rules.Add(new LoggerFilterRule(null, entry.Key, lvl, null));
        }
    }
}
