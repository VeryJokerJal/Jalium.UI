namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// Platform-neutral pseudo-terminal session consumed by <see cref="TerminalProcess"/>:
/// ConPTY on Windows, a POSIX pty (posix_openpt + posix_spawn) on Linux/Unix.
/// </summary>
internal interface IPtySession : IDisposable
{
    /// <summary>Whether the child process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Exit code of the child once it has exited (-1 while running/unknown).</summary>
    int GetExitCode();

    /// <summary>Forcefully terminates the child process.</summary>
    void Kill();

    /// <summary>Writes raw bytes to the terminal input.</summary>
    void Write(byte[] data);

    /// <summary>
    /// Blocking read of the next output chunk. Returns null on EOF/closed,
    /// an empty array on a transient empty read.
    /// </summary>
    byte[]? Read();

    /// <summary>Updates the terminal window size (columns x rows).</summary>
    void Resize(int columns, int rows);
}

/// <summary>
/// Adapts the Windows ConPTY session to <see cref="IPtySession"/>.
/// </summary>
internal sealed class ConPtySessionAdapter : IPtySession
{
    private readonly ConPty.PseudoConsoleSession _session;

    public ConPtySessionAdapter(ConPty.PseudoConsoleSession session)
    {
        _session = session;
    }

    public bool HasExited => _session.HasExited;

    public int GetExitCode() => _session.GetExitCode() ?? -1;

    public void Kill() => _session.Kill();

    public void Write(byte[] data) => ConPty.WriteInput(_session.InputWriteHandle, data);

    public byte[]? Read() => ConPty.ReadOutput(_session.OutputReadHandle);

    public void Resize(int columns, int rows)
    {
        if (_session.ConsoleHandle != nint.Zero)
            ConPty.Resize(_session.ConsoleHandle, (short)columns, (short)rows);
    }

    public void Dispose() => _session.Dispose();
}
