using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;

namespace Jalium.UI.StartupAllocationProbe;

internal static partial class Program
{
    private const int ExitUsage = 2;

    private const string NativeShellOnlyArgument = "--native-shell-only";
    private const string ApplicationNativeShellArgument = "--application-native-shell";
    private const string NativeWindowClassName = "Jalium.UI.StartupAllocationProbe.NativeShell";
    private const string NativeWindowTitle = "Jalium.UI Startup Allocation Native Shell";

    private const uint ClassRedrawHorizontal = 0x0002;
    private const uint ClassRedrawVertical = 0x0001;
    private const uint WindowStyleOverlappedWindow = 0x00CF0000;
    private const int UseDefaultWindowPosition = unchecked((int)0x80000000);
    private const int ShowWindowNormal = 1;
    private const int DefaultArrowCursor = 32512;
    private const int ColorWindow = 5;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageTimer = 0x0113;
    private const uint NativeStableMilliseconds = 10_000;
    private const nuint NativeTimerRequestedId = 1;
    private const int ErrorGeneralFailure = 31;
    private const int ManagedCallbackFailure = unchecked((int)0xE0434352);

    private static nint s_nativeWindowHandle;
    private static nuint s_nativeTimerId;
    private static bool s_nativeTimerArmed;
    private static bool s_nativeWindowDestroyed;
    private static bool s_nativeStableReached;
    private static GcSnapshot s_nativeStable;
    private static int s_nativeCallbackErrorCode;
    private static string? s_nativeCallbackErrorOperation;

    private static NativeShellMode GetNativeShellMode(string[] args)
    {
        bool nativeShellOnly = false;
        bool applicationNativeShell = false;

        foreach (string argument in args)
        {
            if (string.Equals(argument, NativeShellOnlyArgument, StringComparison.OrdinalIgnoreCase))
            {
                nativeShellOnly = true;
            }
            else if (string.Equals(argument, ApplicationNativeShellArgument, StringComparison.OrdinalIgnoreCase))
            {
                applicationNativeShell = true;
            }
        }

        if (nativeShellOnly && applicationNativeShell)
        {
            return NativeShellMode.Conflict;
        }

        if (applicationNativeShell)
        {
            return NativeShellMode.ApplicationNativeShell;
        }

        return nativeShellOnly
            ? NativeShellMode.NativeShellOnly
            : NativeShellMode.None;
    }

    private static int RunNativeShellMode(NativeShellMode mode)
    {
        string modeName = GetNativeShellModeName(mode);
        ResetNativeShellState();
        GcSnapshot entry = Capture();

        try
        {
            GcSnapshot applicationBefore = default;
            GcSnapshot applicationAfter = default;
            GcSnapshot themeVerified = default;
            int themeDictionaryCount = 0;
            bool hasButtonResource = false;

            if (mode == NativeShellMode.ApplicationNativeShell)
            {
                applicationBefore = Capture();
                s_phase = "application_construct";
                var application = new Application();
                s_application = application;
                applicationAfter = Capture();

                s_phase = "theme_verify";
                themeDictionaryCount = application.Resources.MergedDictionaries.Count;
                hasButtonResource = application.Resources.Contains(typeof(Button));
                if (themeDictionaryCount < 3 || !hasButtonResource)
                {
                    throw new InvalidOperationException(
                        "The complete desktop theme was not loaded, including the Button resource.");
                }

                themeVerified = Capture();
            }

            WriteNativeEntry(modeName, entry);
            if (mode == NativeShellMode.ApplicationNativeShell)
            {
                WriteNativeStage(modeName, "application_before", applicationBefore, entry);
                WriteNativeStage(modeName, "application_after", applicationAfter, applicationBefore);
                WriteNativeThemeStage(
                    modeName,
                    themeVerified,
                    applicationAfter,
                    themeDictionaryCount,
                    hasButtonResource);
            }

            // Capture this after the pre-window markers so their formatting cost is
            // not attributed to native class registration or CreateWindowExW.
            s_phase = "native_before";
            GcSnapshot nativeBefore = Capture();
            NativeWindowRunResult result = RunNativeWindow(modeName, nativeBefore);

            if (s_nativeStableReached)
            {
                WriteNativeStage(modeName, "stable_10s", s_nativeStable, result.NativeShown);
            }

            WriteNativeRunReturnStage(
                modeName,
                result.RunReturned,
                s_nativeStableReached ? s_nativeStable : result.NativeShown,
                result.MessageExitCode);

            if (s_nativeCallbackErrorCode != 0)
            {
                WriteNativeWin32Error(
                    modeName,
                    s_nativeCallbackErrorOperation ?? "native_window_callback",
                    s_nativeCallbackErrorCode);
                return ExitFailure;
            }

            if (!s_nativeStableReached)
            {
                WriteNativeError(
                    modeName,
                    "early_exit",
                    "The native message loop ended before the stable_10s timer stage completed.");
                return ExitFailure;
            }

            if (result.MessageExitCode != 0)
            {
                WriteNativeError(
                    modeName,
                    "message_loop_exit",
                    $"WM_QUIT carried nonzero exit code {result.MessageExitCode}.");
                return ExitFailure;
            }

            return 0;
        }
        catch (Win32Exception ex)
        {
            WriteNativeFailureStage(modeName, Capture(), s_phase);
            WriteNativeWin32Error(modeName, ex.Message, ex.NativeErrorCode);
            return ExitFailure;
        }
        catch (Exception ex)
        {
            WriteNativeFailureStage(modeName, Capture(), s_phase);
            WriteNativeError(modeName, "unhandled", ex.ToString());
            return ExitFailure;
        }
    }

    private static unsafe NativeWindowRunResult RunNativeWindow(
        string modeName,
        GcSnapshot nativeBefore)
    {
        nint moduleHandle = 0;
        bool classRegistered = false;
        int cleanupErrorCode = 0;
        string? cleanupErrorOperation = null;
        int messageExitCode = ExitFailure;
        GcSnapshot nativeShown = default;

        try
        {
            s_phase = "native_get_module";
            Marshal.SetLastPInvokeError(0);
            moduleHandle = GetModuleHandleW(0);
            if (moduleHandle == 0)
            {
                ThrowLastWin32Error("GetModuleHandleW");
            }

            s_phase = "native_load_cursor";
            Marshal.SetLastPInvokeError(0);
            nint cursor = LoadCursorW(0, DefaultArrowCursor);
            if (cursor == 0)
            {
                ThrowLastWin32Error("LoadCursorW");
            }

            s_phase = "native_register_class";
            fixed (char* className = NativeWindowClassName)
            {
                var windowClass = new NativeWindowClass
                {
                    Size = (uint)sizeof(NativeWindowClass),
                    Style = ClassRedrawHorizontal | ClassRedrawVertical,
                    WindowProcedure = &NativeWindowProcedure,
                    Instance = moduleHandle,
                    Cursor = cursor,
                    BackgroundBrush = ColorWindow + 1,
                    ClassName = className,
                };

                Marshal.SetLastPInvokeError(0);
                if (RegisterClassExW(in windowClass) == 0)
                {
                    ThrowLastWin32Error("RegisterClassExW");
                }
            }

            classRegistered = true;

            s_phase = "native_create_window";
            Marshal.SetLastPInvokeError(0);
            s_nativeWindowHandle = CreateWindowExW(
                0,
                NativeWindowClassName,
                NativeWindowTitle,
                WindowStyleOverlappedWindow,
                UseDefaultWindowPosition,
                UseDefaultWindowPosition,
                800,
                600,
                0,
                0,
                moduleHandle,
                0);
            if (s_nativeWindowHandle == 0)
            {
                ThrowLastWin32Error("CreateWindowExW");
            }

            nint createdWindowHandle = s_nativeWindowHandle;
            GcSnapshot nativeCreated = Capture();

            s_phase = "native_show_window";
            _ = ShowWindow(s_nativeWindowHandle, ShowWindowNormal);

            s_phase = "native_set_timer";
            Marshal.SetLastPInvokeError(0);
            s_nativeTimerId = SetTimer(
                s_nativeWindowHandle,
                NativeTimerRequestedId,
                NativeStableMilliseconds,
                0);
            if (s_nativeTimerId == 0)
            {
                ThrowLastWin32Error("SetTimer");
            }

            s_nativeTimerArmed = true;
            nativeShown = Capture();

            // These snapshots precede all three marker strings, isolating native
            // class/window creation and first show from the diagnostic formatting.
            WriteNativeStage(modeName, "native_before", nativeBefore, previous: null);
            WriteNativeStage(modeName, "native_created", nativeCreated, nativeBefore);
            WriteNativeShownStage(
                modeName,
                nativeShown,
                nativeCreated,
                createdWindowHandle,
                s_nativeTimerId);

            s_phase = "native_message_loop";
            messageExitCode = RunNativeMessageLoop();
            s_phase = "native_loop_return";
        }
        finally
        {
            if (s_nativeTimerArmed && s_nativeWindowHandle != 0)
            {
                Marshal.SetLastPInvokeError(0);
                if (!KillTimer(s_nativeWindowHandle, s_nativeTimerId))
                {
                    RememberCleanupError(
                        ref cleanupErrorCode,
                        ref cleanupErrorOperation,
                        "KillTimer");
                }

                s_nativeTimerArmed = false;
            }

            if (!s_nativeWindowDestroyed && s_nativeWindowHandle != 0)
            {
                Marshal.SetLastPInvokeError(0);
                if (!DestroyWindow(s_nativeWindowHandle))
                {
                    RememberCleanupError(
                        ref cleanupErrorCode,
                        ref cleanupErrorOperation,
                        "DestroyWindow");
                }
            }

            if (classRegistered)
            {
                Marshal.SetLastPInvokeError(0);
                if (!UnregisterClassW(NativeWindowClassName, moduleHandle))
                {
                    RememberCleanupError(
                        ref cleanupErrorCode,
                        ref cleanupErrorOperation,
                        "UnregisterClassW");
                }
            }
        }

        if (cleanupErrorCode != 0)
        {
            throw new Win32Exception(
                cleanupErrorCode,
                $"{cleanupErrorOperation} failed with Win32 error {cleanupErrorCode}.");
        }

        return new NativeWindowRunResult(
            nativeShown,
            Capture(),
            messageExitCode);
    }

    private static int RunNativeMessageLoop()
    {
        while (true)
        {
            Marshal.SetLastPInvokeError(0);
            int result = GetMessageW(out NativeMessage message, 0, 0, 0);
            if (result > 0)
            {
                _ = TranslateMessage(in message);
                _ = DispatchMessageW(in message);
                continue;
            }

            if (result == 0)
            {
                return unchecked((int)message.WParam);
            }

            ThrowLastWin32Error("GetMessageW");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint NativeWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WindowMessageTimer when wParam == s_nativeTimerId:
                    s_phase = "stable_10s";
                    s_nativeStable = Capture();
                    s_nativeStableReached = true;

                    Marshal.SetLastPInvokeError(0);
                    if (!KillTimer(window, s_nativeTimerId))
                    {
                        RecordNativeCallbackFailure("KillTimer");
                    }

                    s_nativeTimerArmed = false;
                    s_phase = "native_close";
                    Marshal.SetLastPInvokeError(0);
                    if (!DestroyWindow(window))
                    {
                        RecordNativeCallbackFailure("DestroyWindow");
                        PostQuitMessage(ExitFailure);
                    }

                    return 0;

                case WindowMessageClose:
                    s_phase = "native_close";
                    Marshal.SetLastPInvokeError(0);
                    if (!DestroyWindow(window))
                    {
                        RecordNativeCallbackFailure("DestroyWindow");
                        PostQuitMessage(ExitFailure);
                    }

                    return 0;

                case WindowMessageDestroy:
                    s_nativeWindowDestroyed = true;
                    s_nativeWindowHandle = 0;
                    PostQuitMessage(s_nativeCallbackErrorCode == 0 ? 0 : ExitFailure);
                    return 0;

                default:
                    return DefWindowProcW(window, message, wParam, lParam);
            }
        }
        catch
        {
            if (s_nativeCallbackErrorCode == 0)
            {
                s_nativeCallbackErrorOperation = "NativeWindowProcedure";
                s_nativeCallbackErrorCode = ManagedCallbackFailure;
            }

            PostQuitMessage(ExitFailure);
            return 0;
        }
    }

    private static void ResetNativeShellState()
    {
        s_phase = "entry";
        s_nativeWindowHandle = 0;
        s_nativeTimerId = 0;
        s_nativeTimerArmed = false;
        s_nativeWindowDestroyed = false;
        s_nativeStableReached = false;
        s_nativeStable = default;
        s_nativeCallbackErrorCode = 0;
        s_nativeCallbackErrorOperation = null;
    }

    private static void ThrowLastWin32Error(string operation)
    {
        int errorCode = GetLastWin32ErrorOrFallback();
        throw new Win32Exception(
            errorCode,
            $"{operation} failed with Win32 error {errorCode}.");
    }

    private static int GetLastWin32ErrorOrFallback()
    {
        int errorCode = Marshal.GetLastPInvokeError();
        return errorCode == 0 ? ErrorGeneralFailure : errorCode;
    }

    private static void RecordNativeCallbackFailure(string operation)
    {
        if (s_nativeCallbackErrorCode != 0)
        {
            return;
        }

        s_nativeCallbackErrorOperation = operation;
        s_nativeCallbackErrorCode = GetLastWin32ErrorOrFallback();
    }

    private static void RememberCleanupError(
        ref int errorCode,
        ref string? operation,
        string failedOperation)
    {
        if (errorCode != 0)
        {
            return;
        }

        errorCode = GetLastWin32ErrorOrFallback();
        operation = failedOperation;
    }

    private static string GetNativeShellModeName(NativeShellMode mode)
    {
        return mode switch
        {
            NativeShellMode.NativeShellOnly => "native-shell-only",
            NativeShellMode.ApplicationNativeShell => "application-native-shell",
            _ => throw new InvalidOperationException($"Unsupported native shell mode: {mode}."),
        };
    }

    private static void WriteNativeModeConflict()
    {
        Console.Error.WriteLine(
            $"## STARTUP_ALLOC ERROR category=usage phase=mode_select " +
            $"message=\"{NativeShellOnlyArgument} and {ApplicationNativeShellArgument} cannot be combined.\"");
        Console.Error.Flush();
    }

    private static void WriteNativeEntry(string modeName, GcSnapshot snapshot)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=entry mode={modeName} pid={Environment.ProcessId} " +
            $"outerSize=800x600 stableMs={NativeStableMilliseconds} nativeTitleBar=true " +
            $"frameworkWindow=false diagnosticOnly=true samplingOverhead=true " +
            $"budgetProbe=false acceptanceProbe=false sameExecutable=true " +
            $"allocatedBytes={snapshot.AllocatedBytes} deltaAllocatedBytes=0 " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes=0 " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteNativeThemeStage(
        string modeName,
        GcSnapshot snapshot,
        GcSnapshot previous,
        int dictionaryCount,
        bool hasButtonResource)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=theme_verified mode={modeName} dictionaries={dictionaryCount} " +
            $"buttonResource={hasButtonResource.ToString().ToLowerInvariant()} " +
            $"allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={snapshot.AllocatedBytes - previous.AllocatedBytes} " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes={snapshot.HeapBytes - previous.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteNativeShownStage(
        string modeName,
        GcSnapshot snapshot,
        GcSnapshot previous,
        nint windowHandle,
        nuint timerId)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=native_shown mode={modeName} hwnd=0x{windowHandle:x} " +
            $"timerId={timerId} timerMs={NativeStableMilliseconds} allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={snapshot.AllocatedBytes - previous.AllocatedBytes} " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes={snapshot.HeapBytes - previous.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteNativeRunReturnStage(
        string modeName,
        GcSnapshot snapshot,
        GcSnapshot previous,
        int messageExitCode)
    {
        Console.WriteLine(
            $"## STARTUP_ALLOC stage=run_return mode={modeName} messageExitCode={messageExitCode} " +
            $"stable={s_nativeStableReached.ToString().ToLowerInvariant()} " +
            $"allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={snapshot.AllocatedBytes - previous.AllocatedBytes} " +
            $"heapBytes={snapshot.HeapBytes} deltaHeapBytes={snapshot.HeapBytes - previous.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteNativeFailureStage(
        string modeName,
        GcSnapshot snapshot,
        string phase)
    {
        Console.Error.WriteLine(
            $"## STARTUP_ALLOC stage=failure_exit mode={modeName} phase={phase} " +
            $"allocatedBytes={snapshot.AllocatedBytes} heapBytes={snapshot.HeapBytes} " +
            $"gen0={snapshot.Gen0Collections} gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Error.Flush();
    }

    private static void WriteNativeStage(
        string modeName,
        string stage,
        GcSnapshot snapshot,
        GcSnapshot? previous)
    {
        long deltaAllocatedBytes = previous is { } prior
            ? snapshot.AllocatedBytes - prior.AllocatedBytes
            : 0;
        long deltaHeapBytes = previous is { } priorHeap
            ? snapshot.HeapBytes - priorHeap.HeapBytes
            : 0;

        Console.WriteLine(
            $"## STARTUP_ALLOC stage={stage} mode={modeName} allocatedBytes={snapshot.AllocatedBytes} " +
            $"deltaAllocatedBytes={deltaAllocatedBytes} heapBytes={snapshot.HeapBytes} " +
            $"deltaHeapBytes={deltaHeapBytes} gen0={snapshot.Gen0Collections} " +
            $"gen1={snapshot.Gen1Collections} gen2={snapshot.Gen2Collections}");
        Console.Out.Flush();
    }

    private static void WriteNativeWin32Error(string modeName, string operation, int errorCode)
    {
        Console.Error.WriteLine(
            $"## STARTUP_ALLOC ERROR mode={modeName} category=win32 phase={s_phase} " +
            $"operation=\"{operation}\" win32Error={errorCode}");
        Console.Error.Flush();
    }

    private static void WriteNativeError(string modeName, string category, string message)
    {
        Console.Error.WriteLine(
            $"## STARTUP_ALLOC ERROR mode={modeName} category={category} phase={s_phase} " +
            $"message=\"{message.Replace("\"", "'", StringComparison.Ordinal)}\"");
        Console.Error.Flush();
    }

    private enum NativeShellMode
    {
        None,
        NativeShellOnly,
        ApplicationNativeShell,
        Conflict,
    }

    private readonly record struct NativeWindowRunResult(
        GcSnapshot NativeShown,
        GcSnapshot RunReturned,
        int MessageExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeWindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static partial nint GetModuleHandleW(nint moduleName);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassExW(in NativeWindowClass windowClass);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    private static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "SetTimer", SetLastError = true)]
    private static partial nuint SetTimer(
        nint window,
        nuint eventId,
        uint intervalMilliseconds,
        nint timerProcedure);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint window, nuint eventId);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessageW(
        out NativeMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProcW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "UnregisterClassW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(string className, nint instance);
}
