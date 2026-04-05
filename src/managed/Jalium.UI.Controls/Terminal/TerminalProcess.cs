using System.Text;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// Manages a shell child process using Windows ConPTY (Pseudo Console),
/// providing a real terminal environment with proper handling of arrow keys,
/// command history, cursor positioning, and ANSI escape sequences.
/// Falls back to simple pipe redirection on non-Windows platforms.
/// </summary>
internal class TerminalProcess : IDisposable
{
    #region Fields

    /// <summary>
    /// ConPTY session (Windows only).
    /// </summary>
    private ConPty.PseudoConsoleSession? _session;

    /// <summary>
    /// Whether the process is currently running.
    /// </summary>
    private bool _isRunning;

    /// <summary>
    /// Cancellation source for the output reading loop.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The output reading task.
    /// </summary>
    private Task? _readTask;

    /// <summary>
    /// The process exit monitor task.
    /// </summary>
    private Task? _exitMonitorTask;

    /// <summary>
    /// Whether this instance has been disposed.
    /// </summary>
    private bool _disposed;

    #endregion

    #region Events

    /// <summary>
    /// Fired when data is received from the process.
    /// The string contains ANSI escape sequences from the pseudo console.
    /// This event is raised on a background thread.
    /// </summary>
    public event Action<string>? OutputReceived;

    /// <summary>
    /// Fired when the process exits.
    /// This event is raised on a background thread.
    /// </summary>
    public event Action<int>? ProcessExited;

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether the process is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    #endregion

    #region Start / Stop

    /// <summary>
    /// Starts a shell process with a ConPTY pseudo console.
    /// </summary>
    /// <param name="shell">The shell executable path.</param>
    /// <param name="arguments">Optional command-line arguments.</param>
    /// <param name="workingDirectory">The initial working directory.</param>
    /// <param name="environment">Optional additional environment variables (not used with ConPTY).</param>
    /// <param name="columns">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    public void Start(string shell, string? arguments = null, string? workingDirectory = null,
                      Dictionary<string, string>? environment = null, int columns = 80, int rows = 24)
    {
        Stop();

        string? workDir = workingDirectory;
        if (string.IsNullOrEmpty(workDir))
            workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Build command line
        string commandLine = string.IsNullOrEmpty(arguments)
            ? shell
            : $"\"{shell}\" {arguments}";

        _session = ConPty.Create(commandLine, workDir, (short)columns, (short)rows);

        if (_session == null)
            throw new InvalidOperationException("Failed to create ConPTY pseudo console. Ensure Windows 10 1809 or later.");

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // Start reading output from ConPTY
        _readTask = Task.Run(() => ReadOutputLoop(_cts.Token));

        // Monitor process exit
        _exitMonitorTask = Task.Run(() => MonitorProcessExit(_cts.Token));
    }

    /// <summary>
    /// Stops the current shell process.
    /// </summary>
    public void Stop()
    {
        if (_session == null) return;

        _isRunning = false;
        _cts?.Cancel();

        try
        {
            if (!_session.HasExited)
            {
                _session.Kill();
            }
        }
        catch
        {
            // Process may already be gone
        }

        _session.Dispose();
        _session = null;
        _cts?.Dispose();
        _cts = null;
    }

    #endregion

    #region I/O

    /// <summary>
    /// Writes a string to the pseudo console input.
    /// </summary>
    public void WriteInput(string data)
    {
        if (_session == null || _session.HasExited) return;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ConPty.WriteInput(_session.InputWriteHandle, bytes);
        }
        catch
        {
            // Pipe may be closed
        }
    }

    /// <summary>
    /// Writes raw bytes to the pseudo console input.
    /// </summary>
    public void WriteInput(byte[] data)
    {
        if (_session == null || _session.HasExited) return;

        try
        {
            ConPty.WriteInput(_session.InputWriteHandle, data);
        }
        catch
        {
            // Pipe may be closed
        }
    }

    /// <summary>
    /// Sends Ctrl+C to the process via the pseudo console.
    /// </summary>
    public void SendBreak()
    {
        WriteInput("\x03"); // ETX
    }

    /// <summary>
    /// Resizes the pseudo console.
    /// </summary>
    public void NotifyResize(int columns, int rows)
    {
        if (_session == null || _session.ConsoleHandle == nint.Zero) return;
        ConPty.Resize(_session.ConsoleHandle, (short)columns, (short)rows);
    }

    #endregion

    #region Output Reading

    private void ReadOutputLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _session != null)
            {
                byte[]? data = ConPty.ReadOutput(_session.OutputReadHandle);
                if (data == null) break; // Pipe closed

                string text = Encoding.UTF8.GetString(data);
                OutputReceived?.Invoke(text);
            }
        }
        catch when (ct.IsCancellationRequested)
        {
            // Expected on cancellation
        }
        catch
        {
            // Pipe closed or other error
        }
    }

    #endregion

    #region Process Exit Monitoring

    private async Task MonitorProcessExit(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _session != null && !_session.HasExited)
            {
                await Task.Delay(200, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _isRunning = false;
        int exitCode = _session?.GetExitCode() ?? -1;
        ProcessExited?.Invoke(exitCode);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the process and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    #endregion
}
