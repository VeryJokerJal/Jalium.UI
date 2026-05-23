using System.Diagnostics.CodeAnalysis;
using Jalium.Extensions.Configuration;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Diagnostics.Metrics;
using Jalium.Extensions.FileProviders;
using Jalium.Extensions.Logging;

namespace Jalium.Extensions.Hosting;

/// <summary>Describes the runtime hosting environment (env name, app name, content root).</summary>
public interface IHostEnvironment
{
    string EnvironmentName { get; set; }
    string ApplicationName { get; set; }
    string ContentRootPath { get; set; }
    IFileProvider ContentRootFileProvider { get; set; }
}

public sealed class HostingEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = string.Empty;
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public static class Environments
{
    public const string Development = "Development";
    public const string Staging = "Staging";
    public const string Production = "Production";
}

public static class HostEnvironmentEnvExtensions
{
    public static bool IsDevelopment(this IHostEnvironment env) => env.IsEnvironment(Environments.Development);
    public static bool IsStaging(this IHostEnvironment env) => env.IsEnvironment(Environments.Staging);
    public static bool IsProduction(this IHostEnvironment env) => env.IsEnvironment(Environments.Production);
    public static bool IsEnvironment(this IHostEnvironment env, string environmentName)
        => string.Equals(env.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase);
}

public interface IHostApplicationLifetime
{
    CancellationToken ApplicationStarted { get; }
    CancellationToken ApplicationStopping { get; }
    CancellationToken ApplicationStopped { get; }
    void StopApplication();
}

public interface IHostLifetime
{
    Task WaitForStartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IHost : IDisposable
{
    IServiceProvider Services { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IHostBuilder
{
    IDictionary<object, object> Properties { get; }
    IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate);
    IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate);
    IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate);
    IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory) where TContainerBuilder : notnull;
    IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory) where TContainerBuilder : notnull;
    IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate);
    IHost Build();
}

public sealed class HostBuilderContext
{
    public HostBuilderContext(IDictionary<object, object> properties) { Properties = properties; }
    public IHostEnvironment HostingEnvironment { get; set; } = null!;
    public IConfiguration Configuration { get; set; } = null!;
    public IDictionary<object, object> Properties { get; }
}

public interface IHostApplicationBuilder
{
    IConfigurationManager Configuration { get; }
    IHostEnvironment Environment { get; }
    ILoggingBuilder Logging { get; }
    IMetricsBuilder Metrics { get; }
    IDictionary<object, object> Properties { get; }
    IServiceCollection Services { get; }
    void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder>? configure = null) where TContainerBuilder : notnull;
}

public sealed class HostApplicationBuilderSettings
{
    public string[]? Args { get; set; }
    public string? ApplicationName { get; set; }
    public string? EnvironmentName { get; set; }
    public string? ContentRootPath { get; set; }
    public ConfigurationManager? Configuration { get; set; }
    public bool DisableDefaults { get; set; }
}

/// <summary>Base class that runs an <see cref="IHostedService.ExecuteAsync"/> loop until cancelled.</summary>
public abstract class BackgroundService : IHostedService, IDisposable
{
    private Task? _executeTask;
    private CancellationTokenSource? _stoppingCts;

    public virtual Task? ExecuteTask => _executeTask;

    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executeTask = ExecuteAsync(_stoppingCts.Token);
        return _executeTask.IsCompleted ? _executeTask : Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executeTask == null) return;
        try { _stoppingCts?.Cancel(); }
        finally
        {
            var done = await Task.WhenAny(_executeTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (done != _executeTask) { /* timeout */ }
        }
    }

    public virtual void Dispose()
    {
        _stoppingCts?.Cancel();
        _stoppingCts?.Dispose();
    }
}
