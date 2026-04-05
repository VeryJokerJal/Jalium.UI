using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// P/Invoke declarations and helpers for Windows ConPTY (Pseudo Console) API.
/// Provides a real terminal environment for shell processes, enabling proper
/// handling of arrow keys, command history, cursor positioning, and ANSI sequences.
/// </summary>
internal static class ConPty
{
    #region Constants

    private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const int S_OK = 0;

    #endregion

    #region Native Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    #endregion

    #region Native Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, nint hInput, nint hOutput, uint dwFlags, out nint phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(nint hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(nint hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out nint hReadPipe, out nint hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint lpAttributeList, int dwAttributeCount, int dwFlags, ref nint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute, nint lpValue, nint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(nint hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(nint hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, nint lpOverlapped);

    #endregion

    #region Public API

    /// <summary>
    /// Holds the resources for a ConPTY session.
    /// </summary>
    public sealed class PseudoConsoleSession : IDisposable
    {
        /// <summary>Handle to the pseudo console.</summary>
        public nint ConsoleHandle { get; private set; }

        /// <summary>Pipe handle for writing input to the console.</summary>
        public nint InputWriteHandle { get; private set; }

        /// <summary>Pipe handle for reading output from the console.</summary>
        public nint OutputReadHandle { get; private set; }

        /// <summary>Process handle.</summary>
        public nint ProcessHandle { get; private set; }

        /// <summary>Thread handle.</summary>
        public nint ThreadHandle { get; private set; }

        /// <summary>Process ID.</summary>
        public int ProcessId { get; private set; }

        // Internal pipe ends that must be kept alive
        private nint _inputReadHandle;
        private nint _outputWriteHandle;
        private nint _attrList;
        private bool _disposed;

        internal PseudoConsoleSession(
            nint consoleHandle, nint inputWriteHandle, nint outputReadHandle,
            nint inputReadHandle, nint outputWriteHandle,
            nint processHandle, nint threadHandle, int processId, nint attrList)
        {
            ConsoleHandle = consoleHandle;
            InputWriteHandle = inputWriteHandle;
            OutputReadHandle = outputReadHandle;
            _inputReadHandle = inputReadHandle;
            _outputWriteHandle = outputWriteHandle;
            ProcessHandle = processHandle;
            ThreadHandle = threadHandle;
            ProcessId = processId;
            _attrList = attrList;
        }

        /// <summary>
        /// Gets the exit code of the process, or null if still running.
        /// </summary>
        public int? GetExitCode()
        {
            if (ProcessHandle == nint.Zero) return null;
            uint result = WaitForSingleObject(ProcessHandle, 0);
            if (result != 0) return null; // Still running
            GetExitCodeProcess(ProcessHandle, out uint exitCode);
            return (int)exitCode;
        }

        /// <summary>
        /// Whether the process has exited.
        /// </summary>
        public bool HasExited
        {
            get
            {
                if (ProcessHandle == nint.Zero) return true;
                return WaitForSingleObject(ProcessHandle, 0) == 0;
            }
        }

        /// <summary>
        /// Kills the process.
        /// </summary>
        public void Kill()
        {
            if (ProcessHandle != nint.Zero && !HasExited)
                TerminateProcess(ProcessHandle, 1);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Close pseudo console first (signals the process)
            if (ConsoleHandle != nint.Zero)
            {
                ClosePseudoConsole(ConsoleHandle);
                ConsoleHandle = nint.Zero;
            }

            // Close pipe handles
            if (InputWriteHandle != nint.Zero) { CloseHandle(InputWriteHandle); InputWriteHandle = nint.Zero; }
            if (OutputReadHandle != nint.Zero) { CloseHandle(OutputReadHandle); OutputReadHandle = nint.Zero; }
            if (_inputReadHandle != nint.Zero) { CloseHandle(_inputReadHandle); _inputReadHandle = nint.Zero; }
            if (_outputWriteHandle != nint.Zero) { CloseHandle(_outputWriteHandle); _outputWriteHandle = nint.Zero; }

            // Clean up process
            if (ThreadHandle != nint.Zero) { CloseHandle(ThreadHandle); ThreadHandle = nint.Zero; }
            if (ProcessHandle != nint.Zero) { CloseHandle(ProcessHandle); ProcessHandle = nint.Zero; }

            // Clean up attribute list
            if (_attrList != nint.Zero)
            {
                DeleteProcThreadAttributeList(_attrList);
                Marshal.FreeHGlobal(_attrList);
                _attrList = nint.Zero;
            }
        }
    }

    /// <summary>
    /// Creates a ConPTY session with the given shell command.
    /// </summary>
    public static PseudoConsoleSession? Create(string commandLine, string? workingDirectory, short columns, short rows)
    {
        // Create the pipes for ConPTY
        var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };

        // Pipe: our write → ConPTY reads (input to the shell)
        if (!CreatePipe(out nint inputReadHandle, out nint inputWriteHandle, ref sa, 0))
            return null;

        // Pipe: ConPTY writes → our read (output from the shell)
        if (!CreatePipe(out nint outputReadHandle, out nint outputWriteHandle, ref sa, 0))
        {
            CloseHandle(inputReadHandle);
            CloseHandle(inputWriteHandle);
            return null;
        }

        // Create the pseudo console
        var size = new COORD { X = columns, Y = rows };
        int hr = CreatePseudoConsole(size, inputReadHandle, outputWriteHandle, 0, out nint consoleHandle);
        if (hr != S_OK)
        {
            CloseHandle(inputReadHandle);
            CloseHandle(inputWriteHandle);
            CloseHandle(outputReadHandle);
            CloseHandle(outputWriteHandle);
            return null;
        }

        // Set up the process attribute list with the pseudo console
        nint attrListSize = nint.Zero;
        InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref attrListSize);
        nint attrList = Marshal.AllocHGlobal(attrListSize);

        if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
        {
            Marshal.FreeHGlobal(attrList);
            ClosePseudoConsole(consoleHandle);
            CloseHandle(inputReadHandle);
            CloseHandle(inputWriteHandle);
            CloseHandle(outputReadHandle);
            CloseHandle(outputWriteHandle);
            return null;
        }

        if (!UpdateProcThreadAttribute(attrList, 0, (nint)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                consoleHandle, (nint)nint.Size, nint.Zero, nint.Zero))
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            ClosePseudoConsole(consoleHandle);
            CloseHandle(inputReadHandle);
            CloseHandle(inputWriteHandle);
            CloseHandle(outputReadHandle);
            CloseHandle(outputWriteHandle);
            return null;
        }

        // Create the process
        var startupInfo = new STARTUPINFOEX
        {
            StartupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFOEX>(),
            },
            lpAttributeList = attrList
        };

        if (!CreateProcessW(
                null,
                commandLine,
                nint.Zero,
                nint.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                nint.Zero,
                workingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION processInfo))
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            ClosePseudoConsole(consoleHandle);
            CloseHandle(inputReadHandle);
            CloseHandle(inputWriteHandle);
            CloseHandle(outputReadHandle);
            CloseHandle(outputWriteHandle);
            return null;
        }

        return new PseudoConsoleSession(
            consoleHandle, inputWriteHandle, outputReadHandle,
            inputReadHandle, outputWriteHandle,
            processInfo.hProcess, processInfo.hThread,
            processInfo.dwProcessId, attrList);
    }

    /// <summary>
    /// Resizes the pseudo console.
    /// </summary>
    public static void Resize(nint consoleHandle, short columns, short rows)
    {
        if (consoleHandle == nint.Zero) return;
        var size = new COORD { X = columns, Y = rows };
        ResizePseudoConsole(consoleHandle, size);
    }

    /// <summary>
    /// Reads data from the ConPTY output pipe synchronously.
    /// Returns null when the pipe is closed.
    /// </summary>
    public static byte[]? ReadOutput(nint outputReadHandle, int bufferSize = 4096)
    {
        var buffer = new byte[bufferSize];
        bool success = ReadFile(outputReadHandle, buffer, (uint)bufferSize, out uint bytesRead, nint.Zero);
        if (!success || bytesRead == 0) return null;

        var result = new byte[bytesRead];
        Array.Copy(buffer, result, bytesRead);
        return result;
    }

    /// <summary>
    /// Writes data to the ConPTY input pipe.
    /// </summary>
    public static bool WriteInput(nint inputWriteHandle, byte[] data)
    {
        return WriteFile(inputWriteHandle, data, (uint)data.Length, out _, nint.Zero);
    }

    #endregion
}
