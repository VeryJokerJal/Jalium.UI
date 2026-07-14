using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Combines machine-level logind shutdown handling with desktop-session XSMP
/// handling. The two protocols can announce the same shutdown, so this class
/// also guarantees that managed SessionEnding handlers run only once.
/// </summary>
internal sealed class LinuxSessionLifecycleMonitor : IDisposable
{
    private readonly List<IDisposable> _monitors = [];
    private readonly object _gate = new();
    private readonly Func<ReasonSessionEnding, bool> _sessionEnding;
    private bool _eventRaised;
    private bool _lastAllowed = true;

    private LinuxSessionLifecycleMonitor(Func<ReasonSessionEnding, bool> sessionEnding)
    {
        _sessionEnding = sessionEnding;
    }

    internal static LinuxSessionLifecycleMonitor? TryCreate(
        Func<ReasonSessionEnding, bool> sessionEnding,
        Action sessionDie)
    {
        ArgumentNullException.ThrowIfNull(sessionEnding);
        ArgumentNullException.ThrowIfNull(sessionDie);
        if (!OperatingSystem.IsLinux())
            return null;

        var lifecycle = new LinuxSessionLifecycleMonitor(sessionEnding);
        LinuxSessionMonitor? logind = LinuxSessionMonitor.TryCreate(
            lifecycle.RaiseSessionEnding,
            lifecycle.ResetSessionEnding);
        if (logind != null)
            lifecycle._monitors.Add(logind);

        LinuxXsmpSessionMonitor? xsmp = LinuxXsmpSessionMonitor.TryCreate(
            lifecycle.RaiseSessionEnding,
            lifecycle.ResetSessionEnding,
            sessionDie);
        if (xsmp != null)
            lifecycle._monitors.Add(xsmp);

        if (lifecycle._monitors.Count == 0)
        {
            lifecycle.Dispose();
            return null;
        }

        return lifecycle;
    }

    private bool RaiseSessionEnding(ReasonSessionEnding reason)
    {
        lock (_gate)
        {
            if (_eventRaised)
                return _lastAllowed;

            _eventRaised = true;
            try
            {
                _lastAllowed = _sessionEnding(reason);
            }
            catch
            {
                _lastAllowed = true;
            }
            return _lastAllowed;
        }
    }

    private void ResetSessionEnding()
    {
        lock (_gate)
        {
            _eventRaised = false;
            _lastAllowed = true;
        }
    }

    public void Dispose()
    {
        for (int index = _monitors.Count - 1; index >= 0; index--)
            _monitors[index].Dispose();
        _monitors.Clear();
    }
}

/// <summary>
/// Bridges the X Session Management Protocol through libSM/libICE. XSMP is
/// still exported by the major X11 and Wayland desktop sessions and supplies
/// the logout notification/cancellation path that logind intentionally lacks.
/// </summary>
internal sealed class LinuxXsmpSessionMonitor : IDisposable
{
    private const int SmInteractStyleNone = 0;
    private const short PollIn = 0x0001;

    private static readonly Native.SaveYourselfCallback s_saveYourself = OnSaveYourself;
    private static readonly Native.DieCallback s_die = OnDie;
    private static readonly Native.SaveCompleteCallback s_saveComplete = OnSaveComplete;
    private static readonly Native.ShutdownCancelledCallback s_shutdownCancelled = OnShutdownCancelled;
    private static readonly Native.InteractCallback s_interact = OnInteract;

    private readonly Func<ReasonSessionEnding, bool> _sessionEnding;
    private readonly Action _sessionEndingCancelled;
    private readonly Action _sessionDie;
    private GCHandle _selfHandle;
    private nint _connection;
    private nint _iceConnection;
    private Thread? _pumpThread;
    private volatile bool _stopPump;
    private int _disposed;

    private LinuxXsmpSessionMonitor(
        Func<ReasonSessionEnding, bool> sessionEnding,
        Action sessionEndingCancelled,
        Action sessionDie)
    {
        _sessionEnding = sessionEnding;
        _sessionEndingCancelled = sessionEndingCancelled;
        _sessionDie = sessionDie;
    }

    internal static LinuxXsmpSessionMonitor? TryCreate(
        Func<ReasonSessionEnding, bool> sessionEnding,
        Action sessionEndingCancelled,
        Action sessionDie)
    {
        ArgumentNullException.ThrowIfNull(sessionEnding);
        ArgumentNullException.ThrowIfNull(sessionEndingCancelled);
        ArgumentNullException.ThrowIfNull(sessionDie);
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SESSION_MANAGER")) ||
            !Native.IsAvailable)
        {
            return null;
        }

        var monitor = new LinuxXsmpSessionMonitor(
            sessionEnding, sessionEndingCancelled, sessionDie);
        return monitor.TryStart() ? monitor : null;
    }

    private bool TryStart()
    {
        try
        {
            _selfHandle = GCHandle.Alloc(this);
            nint clientData = GCHandle.ToIntPtr(_selfHandle);
            Native.Callbacks callbacks = Native.CreateCallbacks(
                clientData,
                s_saveYourself,
                s_die,
                s_saveComplete,
                s_shutdownCancelled);

            _connection = Native.OpenConnection(
                ref callbacks, out nint clientId, out string connectionError);
            Native.Free(ref clientId);
            if (_connection == 0)
            {
                if (Environment.GetEnvironmentVariable("JALIUM_XSMP_DEBUG") == "1")
                    Console.Error.WriteLine("[XSMP] " + connectionError);
                return false;
            }

            _iceConnection = Native.GetIceConnection(_connection);
            if (_iceConnection == 0)
                return false;

            Native.SetRequiredProperties(_connection);

            _pumpThread = new Thread(Pump)
            {
                IsBackground = true,
                Name = "Jalium.XsmpSessionPump",
            };
            _pumpThread.Start();
            return true;
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or EntryPointNotFoundException or
                  BadImageFormatException)
        {
            return false;
        }
        finally
        {
            if (_connection == 0)
                Dispose();
        }
    }

    private void Pump()
    {
        int descriptor = Native.GetConnectionNumber(_iceConnection);
        if (descriptor < 0)
            return;

        while (!_stopPump)
        {
            var pollDescriptor = new Native.PollDescriptor
            {
                FileDescriptor = descriptor,
                Events = PollIn,
            };
            int ready = Native.Poll(ref pollDescriptor, 100);
            if (_stopPump)
                return;
            if (ready <= 0 || (pollDescriptor.ReturnedEvents & PollIn) == 0)
                continue;

            // IceProcessMessagesSuccess is zero. Other values mean I/O error
            // or an orderly session-manager disconnect.
            if (Native.ProcessMessages(_iceConnection) != 0)
                return;
        }
    }

    private void HandleSaveYourself(nint connection, bool shutdown, int interactStyle)
    {
        if (!shutdown)
        {
            // Non-shutdown SaveYourself is a checkpoint, not SessionEnding.
            Native.SaveYourselfDone(connection, success: true);
            return;
        }

        bool allowed;
        try
        {
            allowed = _sessionEnding(ReasonSessionEnding.Logoff);
        }
        catch
        {
            allowed = true;
        }

        if (allowed)
        {
            Native.SaveYourselfDone(connection, success: true);
            return;
        }

        if (interactStyle != SmInteractStyleNone &&
            Native.RequestInteraction(connection, s_interact, GCHandle.ToIntPtr(_selfHandle)))
        {
            return;
        }

        // With interaction forbidden, XSMP provides no cancellation channel.
        // Report the save failure rather than claiming Cancel was accepted.
        Native.SaveYourselfDone(connection, success: false);
    }

    private static LinuxXsmpSessionMonitor? FromUserData(nint userData)
    {
        if (userData == 0)
            return null;
        try
        {
            return GCHandle.FromIntPtr(userData).Target as LinuxXsmpSessionMonitor;
        }
        catch
        {
            return null;
        }
    }

    private static void OnSaveYourself(
        nint connection,
        nint userData,
        int saveType,
        int shutdown,
        int interactStyle,
        int fast)
    {
        try
        {
            FromUserData(userData)?.HandleSaveYourself(
                connection, shutdown != 0, interactStyle);
        }
        catch
        {
            Native.SaveYourselfDone(connection, success: true);
        }
    }

    private static void OnInteract(nint connection, nint userData)
    {
        try
        {
            Native.InteractDone(connection, cancelShutdown: true);
            Native.SaveYourselfDone(connection, success: true);
            FromUserData(userData)?._sessionEndingCancelled();
        }
        catch
        {
            // Never let managed exceptions cross libSM callbacks.
        }
    }

    private static void OnDie(nint connection, nint userData)
    {
        try
        {
            FromUserData(userData)?._sessionDie();
        }
        catch
        {
        }
    }

    private static void OnSaveComplete(nint connection, nint userData)
    {
    }

    private static void OnShutdownCancelled(nint connection, nint userData)
    {
        try
        {
            FromUserData(userData)?._sessionEndingCancelled();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stopPump = true;
        Thread? pump = _pumpThread;
        if (pump != null && pump != Thread.CurrentThread)
            _ = pump.Join(TimeSpan.FromSeconds(1));
        _pumpThread = null;

        if (_connection != 0)
            Native.CloseConnection(ref _connection);
        _iceConnection = 0;
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private static class Native
    {
        private const string LibSm = "libSM.so.6";
        private const string LibIce = "libICE.so.6";
        private const string LibC = "libc";
        private const nuint CallbackMask = 0x0f;

        private static readonly Lazy<bool> s_available = new(() =>
        {
            nint sm = 0;
            nint ice = 0;
            try
            {
                return NativeLibrary.TryLoad(LibSm, out sm) &&
                       NativeLibrary.TryLoad(LibIce, out ice);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (sm != 0)
                    NativeLibrary.Free(sm);
                if (ice != 0)
                    NativeLibrary.Free(ice);
            }
        });

        internal static bool IsAvailable => s_available.Value;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void SaveYourselfCallback(
            nint connection, nint userData, int saveType, int shutdown,
            int interactStyle, int fast);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DieCallback(nint connection, nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void SaveCompleteCallback(nint connection, nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ShutdownCancelledCallback(nint connection, nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void InteractCallback(nint connection, nint userData);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PollDescriptor
        {
            internal int FileDescriptor;
            internal short Events;
            internal short ReturnedEvents;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CallbackEntry
        {
            internal nint Callback;
            internal nint ClientData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Callbacks
        {
            internal CallbackEntry SaveYourself;
            internal CallbackEntry Die;
            internal CallbackEntry SaveComplete;
            internal CallbackEntry ShutdownCancelled;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SmPropertyValue
        {
            internal int Length;
            internal nint Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SmProperty
        {
            internal nint Name;
            internal nint Type;
            internal int ValueCount;
            internal nint Values;
        }

        internal static Callbacks CreateCallbacks(
            nint clientData,
            SaveYourselfCallback saveYourself,
            DieCallback die,
            SaveCompleteCallback saveComplete,
            ShutdownCancelledCallback shutdownCancelled) => new()
        {
            SaveYourself = new CallbackEntry
            {
                Callback = Marshal.GetFunctionPointerForDelegate(saveYourself),
                ClientData = clientData,
            },
            Die = new CallbackEntry
            {
                Callback = Marshal.GetFunctionPointerForDelegate(die),
                ClientData = clientData,
            },
            SaveComplete = new CallbackEntry
            {
                Callback = Marshal.GetFunctionPointerForDelegate(saveComplete),
                ClientData = clientData,
            },
            ShutdownCancelled = new CallbackEntry
            {
                Callback = Marshal.GetFunctionPointerForDelegate(shutdownCancelled),
                ClientData = clientData,
            },
        };

        internal static nint OpenConnection(
            ref Callbacks callbacks,
            out nint clientId,
            out string errorMessage)
        {
            byte[] error = new byte[256];
            nint connection = SmcOpenConnection(
                null, 0, 1, 0, CallbackMask, ref callbacks, null,
                out clientId, error.Length, error);
            int terminator = Array.IndexOf(error, (byte)0);
            if (terminator < 0)
                terminator = error.Length;
            errorMessage = System.Text.Encoding.UTF8.GetString(error, 0, terminator);
            return connection;
        }

        internal static nint GetIceConnection(nint connection) =>
            SmcGetIceConnection(connection);

        internal static int GetConnectionNumber(nint connection) =>
            IceConnectionNumber(connection);

        internal static int ProcessMessages(nint connection) =>
            IceProcessMessages(connection, 0, 0);

        internal static int Poll(ref PollDescriptor descriptor, int timeoutMilliseconds) =>
            poll(ref descriptor, 1, timeoutMilliseconds);

        internal static bool RequestInteraction(
            nint connection, InteractCallback callback, nint userData) =>
            SmcInteractRequest(
                connection,
                0,
                Marshal.GetFunctionPointerForDelegate(callback),
                userData) != 0;

        internal static void InteractDone(nint connection, bool cancelShutdown) =>
            SmcInteractDone(connection, cancelShutdown ? 1 : 0);

        internal static void SaveYourselfDone(nint connection, bool success) =>
            SmcSaveYourselfDone(connection, success ? 1 : 0);

        internal static void SetRequiredProperties(nint connection)
        {
            string[] managedArguments = Environment.GetCommandLineArgs();
            string processPath = Environment.ProcessPath ??
                (managedArguments.Length > 0 ? managedArguments[0] : "dotnet");
            var restartArguments = new List<string>();

            // Framework-dependent apps need the dotnet host followed by the
            // managed entry assembly. Apphost/self-contained processes already
            // expose the executable as argv[0], so do not duplicate it.
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase) &&
                (managedArguments.Length == 0 ||
                 !string.Equals(
                     processPath,
                     managedArguments[0],
                     StringComparison.Ordinal)))
            {
                restartArguments.Add(processPath);
            }
            restartArguments.AddRange(managedArguments);
            if (restartArguments.Count == 0)
                restartArguments.Add(processPath);

            string program = managedArguments.Length > 0
                ? managedArguments[0]
                : processPath;
            byte[] restartNever = [3]; // SmRestartNever

            var allocations = new List<nint>();
            try
            {
                nint[] properties =
                [
                    CreateProperty("Program", "ARRAY8", [Utf8(program)], allocations),
                    CreateProperty("UserID", "ARRAY8", [Utf8(Environment.UserName)], allocations),
                    CreateProperty("RestartCommand", "LISTofARRAY8",
                        restartArguments.Select(Utf8).ToArray(), allocations),
                    CreateProperty("CloneCommand", "LISTofARRAY8",
                        restartArguments.Select(Utf8).ToArray(), allocations),
                    CreateProperty("RestartStyleHint", "CARD8", [restartNever], allocations),
                ];

                nint propertyArray = Marshal.AllocHGlobal(properties.Length * nint.Size);
                allocations.Add(propertyArray);
                for (int index = 0; index < properties.Length; index++)
                    Marshal.WriteIntPtr(propertyArray, index * nint.Size, properties[index]);

                SmcSetProperties(connection, properties.Length, propertyArray);
            }
            finally
            {
                for (int index = allocations.Count - 1; index >= 0; index--)
                    Marshal.FreeHGlobal(allocations[index]);
            }
        }

        private static nint CreateProperty(
            string name,
            string type,
            IReadOnlyList<byte[]> values,
            List<nint> allocations)
        {
            int valueSize = Marshal.SizeOf<SmPropertyValue>();
            nint valueArray = Marshal.AllocHGlobal(valueSize * values.Count);
            allocations.Add(valueArray);
            for (int index = 0; index < values.Count; index++)
            {
                byte[] bytes = values[index];
                nint value = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
                allocations.Add(value);
                if (bytes.Length > 0)
                    Marshal.Copy(bytes, 0, value, bytes.Length);
                Marshal.StructureToPtr(
                    new SmPropertyValue { Length = bytes.Length, Value = value },
                    valueArray + index * valueSize,
                    false);
            }

            nint property = Marshal.AllocHGlobal(Marshal.SizeOf<SmProperty>());
            allocations.Add(property);
            Marshal.StructureToPtr(new SmProperty
            {
                Name = AllocateUtf8Z(name, allocations),
                Type = AllocateUtf8Z(type, allocations),
                ValueCount = values.Count,
                Values = valueArray,
            }, property, false);
            return property;
        }

        private static nint AllocateUtf8Z(string value, List<nint> allocations)
        {
            byte[] bytes = Utf8(value);
            nint pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            allocations.Add(pointer);
            if (bytes.Length > 0)
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return pointer;
        }

        private static byte[] Utf8(string value) =>
            System.Text.Encoding.UTF8.GetBytes(value);

        internal static void CloseConnection(ref nint connection)
        {
            nint value = connection;
            connection = 0;
            _ = SmcCloseConnection(value, 0, 0);
        }

        internal static void Free(ref nint value)
        {
            nint pointer = value;
            value = 0;
            if (pointer != 0)
                free(pointer);
        }

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint SmcOpenConnection(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? networkIdsList,
            nint context,
            int xsmpMajorRevision,
            int xsmpMinorRevision,
            nuint mask,
            ref Callbacks callbacks,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? previousId,
            out nint clientId,
            int errorLength,
            [Out] byte[] errorString);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmcCloseConnection(
            nint connection, int count, nint reasonMessages);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint SmcGetIceConnection(nint connection);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmcInteractRequest(
            nint connection, int dialogType, nint callback, nint userData);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmcInteractDone(nint connection, int cancelShutdown);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmcSaveYourselfDone(nint connection, int success);

        [DllImport(LibSm, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmcSetProperties(
            nint connection, int propertyCount, nint properties);

        [DllImport(LibIce, CallingConvention = CallingConvention.Cdecl)]
        private static extern int IceConnectionNumber(nint connection);

        [DllImport(LibIce, CallingConvention = CallingConvention.Cdecl)]
        private static extern int IceProcessMessages(
            nint connection, nint replyWait, nint replyReadyRet);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl)]
        private static extern int poll(
            ref PollDescriptor fileDescriptors,
            nuint descriptorCount,
            int timeoutMilliseconds);

        [DllImport(LibC, CallingConvention = CallingConvention.Cdecl)]
        private static extern void free(nint value);
    }
}
