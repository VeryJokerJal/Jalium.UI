using System.Linq;
using Jalium.UI.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jalium.UI;

/// <summary>
/// The running Jalium.UI application returned by <see cref="AppBuilder.Build"/>.
/// Wraps both the <see cref="IHost"/> (generic-host lifetime, hosted services,
/// service provider) and — once bound via <see cref="UseApplication{TApp}"/> —
/// the Jalium.UI <see cref="Jalium.UI.Application"/> (window tree and native
/// message loop).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppBuilder.Build"/> intentionally does not construct
/// <see cref="Application"/>: type selection happens after Build via
/// <see cref="UseApplication{TApp}"/> so that every <c>Use*</c> shares the same
/// lifecycle phase. If no <c>UseApplication</c> call is made, a default
/// <see cref="Application"/> is created lazily on first need (property access
/// or <see cref="Run()"/>).
/// </para>
/// <para>
/// <see cref="Run()"/> is the normal entry point. It starts the host (invoking
/// any <see cref="IHostedService"/> instances), then enters the UI message loop,
/// and finally stops the host and disposes it. The returned integer is the
/// process exit code.
/// </para>
/// <para>
/// For unit tests or headless scenarios you can call <see cref="StartAsync"/> and
/// <see cref="StopAsync"/> directly without running the UI loop.
/// <see cref="Dispose"/> stops the host if it is still running and tears down
/// all resources.
/// </para>
/// </remarks>
public sealed class JaliumApp : IHost
{
    private readonly IHost _host;
    private readonly Action<Application>[] _configureApplication;
    private readonly string[]? _args;
    private Application? _application;
    private Func<Application>? _applicationFactory;
    private bool _disposed;

    internal JaliumApp(IHost host, Action<Application>[] configureApplication, string[]? args)
    {
        _host = host;
        _configureApplication = configureApplication;
        _args = args;
    }

    /// <summary>
    /// The configured <see cref="Jalium.UI.Application"/> instance. Triggers
    /// construction if no <see cref="UseApplication{TApp}"/> has run yet — the
    /// default <see cref="Jalium.UI.Application"/> is used in that case. Use
    /// this to set <see cref="Jalium.UI.Application.MainWindow"/>, subscribe
    /// to lifecycle events, or read application-level resources.
    /// </summary>
    public Application Application
    {
        get
        {
            EnsureApplication();
            return _application!;
        }
    }

    /// <summary>
    /// Convenience accessor for <see cref="Jalium.UI.Application.MainWindow"/>.
    /// </summary>
    public Window? MainWindow
    {
        get => Application.MainWindow;
        set => Application.MainWindow = value;
    }

    /// <inheritdoc />
    public IServiceProvider Services => _host.Services;

    // ── UseApplication — post-Build type registration ───────────────────────

    /// <summary>
    /// Binds a custom <see cref="Application"/> subclass to this
    /// <see cref="JaliumApp"/>. Must be called before any property access that
    /// forces default <see cref="Application"/> construction and before
    /// <see cref="Run()"/>.
    /// </summary>
    /// <typeparam name="TApp">
    /// The <see cref="Application"/> subclass. Must expose a public parameterless
    /// constructor so the framework can instantiate it on the UI thread.
    /// </typeparam>
    /// <remarks>
    /// Calling this multiple times keeps only the last registration. Mutually
    /// exclusive with the other <c>UseApplication</c> factory/instance
    /// overloads.
    /// </remarks>
    public JaliumApp UseApplication<TApp>() where TApp : Application, new()
    {
        EnsureNotBound();
        _applicationFactory = static () => new TApp();
        return this;
    }

    /// <summary>
    /// Registers a factory that constructs the <see cref="Application"/> instance
    /// on first need. The factory runs on the thread that first touches the
    /// <see cref="Application"/> property (or <see cref="Run()"/>), which must
    /// be the UI thread.
    /// </summary>
    public JaliumApp UseApplication(Func<Application> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureNotBound();
        _applicationFactory = factory;
        return this;
    }

    /// <summary>
    /// Uses a pre-constructed <see cref="Application"/> instance. The instance
    /// must not have been started (no prior <see cref="Application.Run()"/> call).
    /// Useful when you need access to the <see cref="Application"/> before
    /// binding — for example to attach handlers early.
    /// </summary>
    public JaliumApp UseApplication(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        EnsureNotBound();
        _applicationFactory = () => application;
        return this;
    }

    private void EnsureNotBound()
    {
        if (_application != null)
        {
            throw new InvalidOperationException(
                "UseApplication must be called before the Application is constructed. " +
                "An earlier UseApplication call, an Application/MainWindow property access, " +
                "or a Use* extension that touched the Application has already triggered construction.");
        }
    }

    private void EnsureApplication()
    {
        if (_application != null) return;

        // Only one Application may exist per process. If a factory is registered we trust it
        // (UseApplication(Application instance) passes a pre-constructed instance, so Current
        // is already set); otherwise we create the default Application and its constructor
        // enforces the singleton invariant.
        if (_applicationFactory == null && Application.Current != null)
        {
            throw new InvalidOperationException(
                "An Application instance already exists. JaliumApp owns the process-wide Application; " +
                "call app.UseApplication(...) to bind an existing instance, or do not construct Application directly.");
        }

        var application = _applicationFactory?.Invoke() ?? new Application();

        if (!ReferenceEquals(application, Application.Current))
        {
            throw new InvalidOperationException(
                "UseApplication factory returned an instance different from Application.Current. " +
                "The Application singleton must be the one produced by the factory.");
        }

        application.AttachHost(
            _host.Services,
            _host.Services.GetRequiredService<IConfiguration>(),
            _host.Services.GetRequiredService<IHostEnvironment>());

        foreach (var configure in _configureApplication)
        {
            configure(application);
        }

        _application = application;
    }

    // ── Host lifecycle ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _host.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _host.StopAsync(cancellationToken);

    /// <summary>
    /// Runs a host lifecycle operation (<see cref="IHost.StartAsync"/> / <see cref="IHost.StopAsync"/>)
    /// to completion synchronously, with the UI <see cref="SynchronizationContext"/> temporarily
    /// suspended for the duration of the blocking wait.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Application"/> constructor installs a
    /// <see cref="Jalium.UI.Threading.DispatcherSynchronizationContext"/> as the current context so
    /// that <c>async</c>/<c>await</c> continuations resume on the UI dispatcher thread. During
    /// <see cref="Run(string[])"/> the host is started (and later stopped) by blocking on the
    /// returned <see cref="Task"/> <em>before</em> the Win32 message loop is running. If a hosted
    /// service, an <see cref="IHostApplicationLifetime"/> callback, or a <c>BackgroundService</c>
    /// performs real asynchronous work, its captured continuation would be posted back to the
    /// dispatcher and could only run once the message loop drains it — which never happens, because
    /// this thread is blocked waiting for the host operation to finish. The result is a startup
    /// deadlock (process alive, zero windows, no exception).
    /// </para>
    /// <para>
    /// Suspending the context to <see langword="null"/> for the blocking call lets those
    /// continuations resume on the thread pool instead, so the host operation can complete. The
    /// <see cref="Func{Task}"/> shape is essential: the task is created <em>after</em> the context is
    /// suspended, so the operation's synchronous prefix and its first <c>await</c> capture the
    /// suspended (null) context. Passing an already-started task would capture the UI context before
    /// suspension and reintroduce the deadlock.
    /// </para>
    /// <para>
    /// Because the UI message pump is not running during host start/stop, code reached through
    /// <paramref name="operation"/> must not synchronously block on UI-dispatcher work (for example
    /// <c>Dispatcher.Invoke(...)</c> from another thread, or
    /// <c>InvokeAsync(...).GetAwaiter().GetResult()</c> on the UI thread): such a wait still
    /// deadlocks because no pump is draining the queue. Any <c>await</c> that would resume on the UI
    /// thread resumes on the thread pool here; startup/shutdown work that needs UI-thread affinity
    /// must be marshaled explicitly once the application message loop is running.
    /// </para>
    /// </remarks>
    internal static void RunHostOperationBlocking(Func<Task> operation)
    {
        var uiContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            operation().GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
        }
    }

    /// <summary>
    /// Starts the host, runs the Jalium.UI message loop until shutdown, then stops
    /// and disposes the host. Uses the command-line arguments originally supplied
    /// to <see cref="AppBuilder.CreateBuilder(string[])"/> (or
    /// <see cref="System.Environment.GetCommandLineArgs"/> as a fallback).
    /// </summary>
    /// <returns>The process exit code.</returns>
    public int Run()
    {
        var args = _args ?? System.Environment.GetCommandLineArgs().Skip(1).ToArray();
        return Run(args);
    }

    /// <summary>
    /// Starts the host, runs the Jalium.UI message loop with the supplied startup
    /// arguments, then stops and disposes the host.
    /// </summary>
    /// <param name="args">Startup arguments forwarded to <see cref="Application.Run(string[])"/>.</param>
    /// <returns>The process exit code.</returns>
    public int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureApplication();

        RunHostOperationBlocking(() => _host.StartAsync(CancellationToken.None));

        int exitCode;
        try
        {
            exitCode = _application!.Run(args);
        }
        finally
        {
            try
            {
                RunHostOperationBlocking(() => _host.StopAsync(CancellationToken.None));
            }
            catch (Exception)
            {
                // Swallow shutdown-time exceptions — the UI message loop has already
                // exited and the process is on the way out; letting an unrelated host
                // shutdown throw would mask the real exit code.
            }

            Dispose();
        }

        return exitCode;
    }

    /// <summary>
    /// Convenience overload that sets <see cref="MainWindow"/> before running.
    /// </summary>
    public int Run(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        MainWindow = mainWindow;
        return Run();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _host.Dispose();
    }
}
