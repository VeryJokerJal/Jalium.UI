using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Logging;

namespace Jalium.Extensions.Hosting;

/// <summary>Default <see cref="IHost"/>. Starts every <see cref="IHostedService"/> in order then disposes the provider on stop.</summary>
internal sealed class JaliumHost : IHost, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ApplicationLifetime _lifetime;
    private readonly IHostLifetime _hostLifetime;
    private readonly ILogger _logger;
    private readonly HostOptions _options;
    private IEnumerable<IHostedService> _hostedServices = Array.Empty<IHostedService>();
    private bool _stopCalled;
    private bool _disposed;

    public JaliumHost(IServiceProvider services, IHostApplicationLifetime applicationLifetime, IHostLifetime hostLifetime, ILoggerFactory loggerFactory, HostOptions options)
    {
        _services = services;
        _lifetime = (ApplicationLifetime)applicationLifetime;
        _hostLifetime = hostLifetime;
        _logger = loggerFactory.CreateLogger("Jalium.Extensions.Hosting");
        _options = options;
    }

    public IServiceProvider Services => _services;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping);
        var token = combined.Token;

        await _hostLifetime.WaitForStartAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        _hostedServices = _services.GetServices<IHostedService>();
        foreach (var svc in _hostedServices)
        {
            await svc.StartAsync(token).ConfigureAwait(false);
            if (svc is BackgroundService bg && bg.ExecuteTask != null) _ = TryExecuteBackgroundServiceAsync(bg);
        }

        _lifetime.NotifyStarted();
    }

    private async Task TryExecuteBackgroundServiceAsync(BackgroundService bg)
    {
        try { await bg.ExecuteTask!.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, 0, "BackgroundService faulted", ex, (s, e) => $"{s}: {e}");
            if (_options.BackgroundServiceExceptionBehavior == BackgroundServiceExceptionBehavior.StopHost)
                _lifetime.StopApplication();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopCalled) return;
        _stopCalled = true;

        using var cts = new CancellationTokenSource(_options.ShutdownTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        var token = linked.Token;

        _lifetime.StopApplication();

        var errors = new List<Exception>();
        foreach (var svc in _hostedServices.Reverse())
        {
            try { await svc.StopAsync(token).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
        }

        try { await _hostLifetime.StopAsync(token).ConfigureAwait(false); }
        catch (Exception ex) { errors.Add(ex); }

        _lifetime.NotifyStopped();
        if (errors.Count > 0) throw new AggregateException("One or more hosted services failed to stop.", errors);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
        else if (_services is IDisposable d) d.Dispose();
    }
}

public sealed class HostOptions
{
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public BackgroundServiceExceptionBehavior BackgroundServiceExceptionBehavior { get; set; } = BackgroundServiceExceptionBehavior.StopHost;
    public bool ServicesStartConcurrently { get; set; }
    public bool ServicesStopConcurrently { get; set; }
}

public enum BackgroundServiceExceptionBehavior
{
    StopHost = 0,
    Ignore = 1,
}

internal sealed class ApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;
    public void StopApplication() { try { _stopping.Cancel(); } catch { } }
    public void NotifyStarted() { try { _started.Cancel(); } catch { } }
    public void NotifyStopped() { try { _stopped.Cancel(); } catch { } }
}

/// <summary>Default <see cref="IHostLifetime"/> — does not attach to console signals, just yields immediately.</summary>
internal sealed class NoOpHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
