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
    /// Active pty session: ConPTY on Windows, POSIX pty on Linux/Unix.
    /// </summary>
    private IPtySession? _session;

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

    /// <summary>
    /// Character encoding for shell I/O. Defaults to UTF-8.
    /// </summary>
    private Encoding _encoding = Encoding.UTF8;

    /// <summary>
    /// Stateful decoder for shell output — carries a multi-byte sequence that
    /// straddles two ConPTY reads over to the next chunk instead of corrupting it.
    /// </summary>
    private Decoder _decoder = Encoding.UTF8.GetDecoder();

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

    /// <summary>
    /// Gets or sets the character encoding used to decode shell output and
    /// encode input. Defaults to UTF-8. Takes effect on the next <see cref="Start"/>.
    /// </summary>
    public Encoding Encoding
    {
        get => _encoding;
        set => _encoding = value ?? Encoding.UTF8;
    }

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

        if (OperatingSystem.IsWindows())
        {
            // Build command line
            string commandLine = string.IsNullOrEmpty(arguments)
                ? shell
                : $"\"{shell}\" {arguments}";

            var conPty = ConPty.Create(commandLine, workDir, (short)columns, (short)rows);
            if (conPty == null)
                throw new InvalidOperationException("Failed to create ConPTY pseudo console. Ensure Windows 10 1809 or later.");

            _session = new ConPtySessionAdapter(conPty);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            _session = UnixPty.Create(shell, arguments, workDir!, environment, columns, rows)
                ?? throw new InvalidOperationException(
                    $"Failed to create a POSIX pseudo terminal for '{shell}'. " +
                    "Verify the shell exists and /dev/pts is mounted.");
        }
        else
        {
            throw new PlatformNotSupportedException(
                "The Terminal control requires ConPTY (Windows) or a POSIX pty (Linux/Unix).");
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // Fresh decoder per session so leftover bytes from a previous shell can
        // never bleed into the new one.
        _decoder = _encoding.GetDecoder();

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
            byte[] bytes = _encoding.GetBytes(data);
            _session.Write(bytes);
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
            _session.Write(data);
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
        _session?.Resize(columns, rows);
    }

    #endregion

    #region Output Reading

    private void ReadOutputLoop(CancellationToken ct)
    {
        // Capture the session decoder locally; this loop is its only consumer.
        var decoder = _decoder;
        try
        {
            while (!ct.IsCancellationRequested && _session != null)
            {
                byte[]? data = _session.Read();
                if (data == null) break; // Pipe closed
                if (data.Length == 0) continue;

                // Stateful decode: a multi-byte character split across two reads
                // is held by the decoder and completed when the next chunk arrives,
                // so no encoding (UTF-8, GBK, Shift-JIS, …) is ever corrupted.
                int charCount = decoder.GetCharCount(data, 0, data.Length, flush: false);
                if (charCount == 0) continue;
                char[] chars = new char[charCount];
                decoder.GetChars(data, 0, data.Length, chars, 0, flush: false);
                OutputReceived?.Invoke(new string(chars, 0, charCount));
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
