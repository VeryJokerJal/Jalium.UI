// Manages the lifecycle of a language server process (spawn, restart, shutdown).

using System.Diagnostics;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Configuration for launching a language server process.
/// </summary>
public sealed class LanguageServerConfig
{
    /// <summary>
    /// The executable path (e.g., "clangd", "rust-analyzer", "omnisharp").
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Command-line arguments.
    /// </summary>
    public string[] Arguments { get; set; } = [];

    /// <summary>
    /// Additional environment variables.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Working directory for the server process.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Language identifiers this server handles (e.g., "csharp", "rust").
    /// </summary>
    public string[] Languages { get; set; } = [];

    /// <summary>
    /// Initialization options to pass in the initialize request.
    /// </summary>
    public object? InitializationOptions { get; set; }

    /// <summary>
    /// Whether to auto-restart if the server crashes.
    /// </summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>
    /// Max auto-restart attempts before giving up.
    /// </summary>
    public int MaxRestartAttempts { get; set; } = 5;

    /// <summary>
    /// Time window (in seconds) for counting restart attempts.
    /// </summary>
    public int RestartWindowSeconds { get; set; } = 60;
}

/// <summary>
/// Manages the lifecycle of a language server child process.
/// </summary>
internal sealed class LanguageServerProcess : IAsyncDisposable
{
    private readonly LanguageServerConfig _config;
    private Process? _process;
    private JsonRpcConnection? _connection;
    private bool _disposed;
    private readonly List<DateTime> _restartTimes = [];
    private readonly object _syncRoot = new();

    public event Action<Exception>? ProcessError;
    public event Action? ProcessExited;

    public bool IsRunning => _process is { HasExited: false };
    public JsonRpcConnection? Connection => _connection;

    public LanguageServerProcess(LanguageServerConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Starts the language server process and returns a JSON-RPC connection to it.
    /// </summary>
    public JsonRpcConnection Start()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LanguageServerProcess));

            if (_process is { HasExited: false })
                throw new InvalidOperationException("Process is already running.");

            var psi = new ProcessStartInfo
            {
                FileName = _config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var arg in _config.Arguments)
                psi.ArgumentList.Add(arg);

            if (_config.WorkingDirectory != null)
                psi.WorkingDirectory = _config.WorkingDirectory;

            if (_config.Environment != null)
            {
                foreach (var (key, value) in _config.Environment)
                    psi.Environment[key] = value;
            }

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start language server: {_config.Command}");

            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            // Read stderr in background for diagnostics
            _ = Task.Run(async () =>
            {
                try
                {
                    while (_process?.StandardError != null && !_process.HasExited)
                    {
                        var line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        System.Diagnostics.Debug.WriteLine($"[LSP stderr] {line}");
                    }
                }
                catch { /* process exited */ }
            });

            _connection = new JsonRpcConnection(
                _process.StandardOutput.BaseStream,
                _process.StandardInput.BaseStream);

            return _connection;
        }
    }

    /// <summary>
    /// Attempts to restart the process if auto-restart is enabled and limits allow.
    /// </summary>
    public bool TryRestart(out JsonRpcConnection? connection)
    {
        connection = null;

        if (!_config.AutoRestart)
            return false;

        lock (_syncRoot)
        {
            if (_disposed) return false;

            // Prune old restart timestamps
            var cutoff = DateTime.UtcNow.AddSeconds(-_config.RestartWindowSeconds);
            _restartTimes.RemoveAll(t => t < cutoff);

            if (_restartTimes.Count >= _config.MaxRestartAttempts)
                return false;

            _restartTimes.Add(DateTime.UtcNow);
        }

        CleanupProcess();

        try
        {
            connection = Start();
            return true;
        }
        catch (Exception ex)
        {
            ProcessError?.Invoke(ex);
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        ProcessExited?.Invoke();
    }

    private void CleanupProcess()
    {
        lock (_syncRoot)
        {
            if (_connection != null)
            {
                _ = _connection.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
                _connection = null;
            }

            if (_process != null)
            {
                _process.Exited -= OnProcessExited;
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(3000);
                    }
                }
                catch { /* best effort */ }
                _process.Dispose();
                _process = null;
            }
        }
    }

    /// <summary>
    /// Attempts a graceful shutdown: sends shutdown request, then exit notification, then kills if needed.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        JsonRpcConnection? conn;
        lock (_syncRoot)
        {
            conn = _connection;
        }

        if (conn != null)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(5000);
                await conn.SendRequestAsync("shutdown", null, timeoutCts.Token).ConfigureAwait(false);
                await conn.SendNotificationAsync("exit", null, timeoutCts.Token).ConfigureAwait(false);
            }
            catch { /* timeout or already dead */ }
        }

        // Wait briefly for graceful exit
        var proc = _process;
        if (proc != null && !proc.HasExited)
        {
            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                delayCts.CancelAfter(3000);
                await proc.WaitForExitAsync(delayCts.Token).ConfigureAwait(false);
            }
            catch { /* timeout */ }
        }

        CleanupProcess();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await ShutdownAsync().ConfigureAwait(false);
    }
}
