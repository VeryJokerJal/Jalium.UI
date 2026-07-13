using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// POSIX pseudo-terminal backend for the Terminal control (Linux; also valid on
/// other Unix-likes with the same libc surface).
///
/// The pty pair comes from posix_openpt/grantpt/unlockpt, and the child is
/// launched with posix_spawn using POSIX_SPAWN_SETSID plus an addopen file
/// action on the slave path: opening the first tty after setsid makes it the
/// controlling terminal, so shells get full job control without any fork/exec
/// in managed code. The master fd is opened O_CLOEXEC so the child never holds
/// it open.
/// </summary>
internal static partial class UnixPty
{
    private const string Libc = "libc";

    // open(2) flags (asm-generic, valid on x86_64/arm64/riscv Linux).
    private const int O_RDWR = 0x2;
    private const int O_NOCTTY = 0x100;
    private const int O_CLOEXEC = 0x80000;

    // spawn(3) — identical value in glibc and musl.
    private const short POSIX_SPAWN_SETSID = 0x80;

    // ioctl(2)
    private const uint TIOCSWINSZ = 0x5414;

    // waitpid(2)
    private const int WNOHANG = 1;

    // signal numbers
    private const int SIGKILL = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort XPixels;
        public ushort YPixels;
    }

    /// <summary>
    /// Creates a pty and spawns the given command on it. Returns null when the
    /// pty or spawn machinery is unavailable.
    /// </summary>
    public static UnixPtySession? Create(
        string shell,
        string? arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment,
        int columns,
        int rows)
    {
        var masterFd = posix_openpt(O_RDWR | O_NOCTTY | O_CLOEXEC);
        if (masterFd < 0)
            return null;

        try
        {
            if (grantpt(masterFd) != 0 || unlockpt(masterFd) != 0)
            {
                close(masterFd);
                return null;
            }

            var slaveNameBuffer = new byte[256];
            if (ptsname_r(masterFd, slaveNameBuffer, (nuint)slaveNameBuffer.Length) != 0)
            {
                close(masterFd);
                return null;
            }

            var terminator = Array.IndexOf(slaveNameBuffer, (byte)0);
            var slavePath = Encoding.UTF8.GetString(
                slaveNameBuffer, 0, terminator >= 0 ? terminator : slaveNameBuffer.Length);

            // Apply the initial size to the master before the child starts so
            // the shell's first prompt lays out at the right width.
            SetSize(masterFd, columns, rows);

            var pid = Spawn(shell, arguments, workingDirectory, extraEnvironment, slavePath);
            if (pid <= 0)
            {
                close(masterFd);
                return null;
            }

            return new UnixPtySession(masterFd, pid);
        }
        catch (DllNotFoundException)
        {
            close(masterFd);
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            close(masterFd);
            return null;
        }
    }

    private static int Spawn(
        string shell,
        string? arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment,
        string slavePath)
    {
        // Opaque libc structs (posix_spawn_file_actions_t / posix_spawnattr_t):
        // 336 bytes covers the largest (glibc x86_64 attr); zeroed so *_init
        // sees clean memory on any libc.
        var fileActions = Marshal.AllocHGlobal(512);
        var attr = Marshal.AllocHGlobal(512);
        var argvHandles = new List<nint>();
        var envHandles = new List<nint>();
        var argvArray = nint.Zero;
        var envArray = nint.Zero;

        static nint MarshalUtf8(string value, List<nint> owner)
        {
            var ptr = Marshal.StringToCoTaskMemUTF8(value);
            owner.Add(ptr);
            return ptr;
        }

        static nint BuildPointerArray(List<nint> pointers)
        {
            var array = Marshal.AllocHGlobal(nint.Size * (pointers.Count + 1));
            for (var i = 0; i < pointers.Count; i++)
                Marshal.WriteIntPtr(array, i * nint.Size, pointers[i]);
            Marshal.WriteIntPtr(array, pointers.Count * nint.Size, nint.Zero);
            return array;
        }

        try
        {
            unsafe
            {
                new Span<byte>((void*)fileActions, 512).Clear();
                new Span<byte>((void*)attr, 512).Clear();
            }

            if (posix_spawn_file_actions_init(fileActions) != 0)
                return -1;
            if (posix_spawnattr_init(attr) != 0)
            {
                posix_spawn_file_actions_destroy(fileActions);
                return -1;
            }

            try
            {
                // New session; opening the slave (fd 0) then makes it the
                // controlling terminal. stdout/stderr dup from fd 0.
                _ = posix_spawnattr_setflags(attr, POSIX_SPAWN_SETSID);

                var slavePathUtf8 = MarshalUtf8(slavePath, argvHandles);
                if (posix_spawn_file_actions_addopen(fileActions, 0, slavePathUtf8, O_RDWR, 0) != 0)
                    return -1;
                if (posix_spawn_file_actions_adddup2(fileActions, 0, 1) != 0)
                    return -1;
                if (posix_spawn_file_actions_adddup2(fileActions, 0, 2) != 0)
                    return -1;

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    try
                    {
                        // glibc >= 2.29 and musl both ship the _np variant.
                        var dirUtf8 = MarshalUtf8(workingDirectory, argvHandles);
                        _ = posix_spawn_file_actions_addchdir_np(fileActions, dirUtf8);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // Ancient libc: start in the inherited directory instead.
                    }
                }

                // argv: [shell, args...] — arguments are passed as a single
                // string and split on spaces only when simple; complex quoting
                // should be handled by the caller via the shell itself.
                var argvPointers = new List<nint> { MarshalUtf8(shell, argvHandles) };
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    foreach (var part in SplitArguments(arguments))
                        argvPointers.Add(MarshalUtf8(part, argvHandles));
                }

                argvArray = BuildPointerArray(argvPointers);

                // Environment: inherit, overlay caller-specified variables, and
                // guarantee a sane TERM for full-screen/color programs.
                var environment = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    if (entry.Key is string key && entry.Value is string value)
                        environment[key] = value;
                }

                if (extraEnvironment != null)
                {
                    foreach (var pair in extraEnvironment)
                        environment[pair.Key] = pair.Value;
                }

                if (!environment.ContainsKey("TERM"))
                    environment["TERM"] = "xterm-256color";

                var envPointers = new List<nint>(environment.Count);
                foreach (var pair in environment)
                    envPointers.Add(MarshalUtf8($"{pair.Key}={pair.Value}", envHandles));

                envArray = BuildPointerArray(envPointers);

                var shellUtf8 = argvPointers[0];
                var spawnResult = posix_spawn(out var pid, shellUtf8, fileActions, attr, argvArray, envArray);
                return spawnResult == 0 ? pid : -1;
            }
            finally
            {
                posix_spawn_file_actions_destroy(fileActions);
                posix_spawnattr_destroy(attr);
            }
        }
        finally
        {
            foreach (var handle in argvHandles)
                Marshal.FreeCoTaskMem(handle);
            foreach (var handle in envHandles)
                Marshal.FreeCoTaskMem(handle);
            if (argvArray != nint.Zero)
                Marshal.FreeHGlobal(argvArray);
            if (envArray != nint.Zero)
                Marshal.FreeHGlobal(envArray);
            Marshal.FreeHGlobal(fileActions);
            Marshal.FreeHGlobal(attr);
        }
    }

    private static IEnumerable<string> SplitArguments(string arguments)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    internal static void SetSize(int masterFd, int columns, int rows)
    {
        var size = new WinSize
        {
            Rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue),
            Columns = (ushort)Math.Clamp(columns, 1, ushort.MaxValue),
        };
        _ = ioctl(masterFd, TIOCSWINSZ, ref size);
    }

    internal static int ReadMaster(int masterFd, byte[] buffer)
    {
        return (int)read(masterFd, buffer, (nuint)buffer.Length);
    }

    internal static int WriteMaster(int masterFd, byte[] buffer, int count)
    {
        return (int)write(masterFd, buffer, (nuint)count);
    }

    internal static void CloseFd(int fd) => _ = close(fd);

    internal static bool TryReap(int pid, bool block, out int exitCode)
    {
        exitCode = -1;
        var result = waitpid(pid, out var status, block ? 0 : WNOHANG);
        if (result != pid)
            return false;

        // WIFEXITED → WEXITSTATUS; signalled children report 128+signo like shells do.
        if ((status & 0x7F) == 0)
            exitCode = (status >> 8) & 0xFF;
        else
            exitCode = 128 + (status & 0x7F);
        return true;
    }

    internal static void KillProcess(int pid) => _ = kill(pid, SIGKILL);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_openpt(int flags);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int grantpt(int fd);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int unlockpt(int fd);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int ptsname_r(int fd, [Out] byte[] buffer, nuint bufferLength);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial nint read(int fd, [Out] byte[] buffer, nuint count);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial nint write(int fd, [In] byte[] buffer, nuint count);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int close(int fd);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int ioctl(int fd, nuint request, ref WinSize size);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int kill(int pid, int signal);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int waitpid(int pid, out int status, int options);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn_file_actions_init(nint fileActions);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn_file_actions_destroy(nint fileActions);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn_file_actions_addopen(
        nint fileActions, int fd, nint pathUtf8, int oflag, int mode);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn_file_actions_adddup2(nint fileActions, int fd, int newFd);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn_file_actions_addchdir_np(nint fileActions, nint pathUtf8);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawnattr_init(nint attr);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawnattr_destroy(nint attr);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawnattr_setflags(nint attr, short flags);

    [LibraryImport(Libc, SetLastError = true)]
    private static partial int posix_spawn(
        out int pid, nint pathUtf8, nint fileActions, nint attr, nint argv, nint envp);
}

/// <summary>
/// A live POSIX pty session: owns the master fd and the child pid.
/// </summary>
internal sealed class UnixPtySession : IPtySession
{
    private readonly object _reapLock = new();
    private int _masterFd;
    private readonly int _pid;
    private bool _exited;
    private int _exitCode = -1;
    private bool _disposed;

    internal UnixPtySession(int masterFd, int pid)
    {
        _masterFd = masterFd;
        _pid = pid;
    }

    public bool HasExited
    {
        get
        {
            lock (_reapLock)
            {
                if (_exited)
                    return true;

                if (UnixPty.TryReap(_pid, block: false, out var exitCode))
                {
                    _exited = true;
                    _exitCode = exitCode;
                }

                return _exited;
            }
        }
    }

    public int GetExitCode()
    {
        lock (_reapLock)
        {
            return _exitCode;
        }
    }

    public void Kill()
    {
        if (!HasExited)
            UnixPty.KillProcess(_pid);
    }

    public void Write(byte[] data)
    {
        var fd = _masterFd;
        if (fd < 0)
            return;

        var written = 0;
        while (written < data.Length)
        {
            var chunk = written == 0
                ? data
                : data.AsSpan(written).ToArray();
            var result = UnixPty.WriteMaster(fd, chunk, chunk.Length);
            if (result <= 0)
                return;
            written += result;
        }
    }

    public byte[]? Read()
    {
        var fd = _masterFd;
        if (fd < 0)
            return null;

        var buffer = new byte[4096];
        var count = UnixPty.ReadMaster(fd, buffer);
        if (count <= 0)
            return null;

        if (count == buffer.Length)
            return buffer;

        var result = new byte[count];
        Array.Copy(buffer, result, count);
        return result;
    }

    public void Resize(int columns, int rows)
    {
        var fd = _masterFd;
        if (fd >= 0)
            UnixPty.SetSize(fd, columns, rows);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var fd = _masterFd;
        _masterFd = -1;
        if (fd >= 0)
            UnixPty.CloseFd(fd);

        // Collect the child so it never lingers as a zombie: kill if still
        // running (closing the master already sent it SIGHUP), then reap.
        lock (_reapLock)
        {
            if (!_exited)
            {
                if (!UnixPty.TryReap(_pid, block: false, out var exitCode))
                {
                    UnixPty.KillProcess(_pid);
                    if (UnixPty.TryReap(_pid, block: true, out exitCode))
                    {
                        _exited = true;
                        _exitCode = exitCode;
                    }
                }
                else
                {
                    _exited = true;
                    _exitCode = exitCode;
                }
            }
        }
    }
}
