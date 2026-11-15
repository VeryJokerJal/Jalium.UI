using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jalium.UI.Threading;

internal sealed partial class DispatcherCore
{
    private nint _windowsCompositionQueueController;
    private bool _windowsCompositionQueueAttempted;
    private bool _windowsCompositionApartmentOwned;

    internal void EnsureWindowsCompositionQueue()
    {
        if (!s_isWindows || _windowsCompositionQueueAttempted || _disposed || _isShutdown)
            return;

        VerifyAccess();
        _windowsCompositionQueueAttempted = true;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
            return;

        int apartment = InitializeCompositionApartment(0 /* RO_INIT_SINGLETHREADED */);
        if (apartment < 0 && apartment != unchecked((int)0x80010106) /* RPC_E_CHANGED_MODE */)
            return;

        bool ownedApartment = apartment >= 0;
        var options = new CompositionQueueOptions { Size = Marshal.SizeOf<CompositionQueueOptions>(), ThreadType = 2 };
        int result = CreateCompositionQueueController(options, out var controller);
        if (result < 0)
        {
            // A host may already own the thread's queue. Its compositor can use
            // that queue, and its lifetime continues to belong to the host.
            if (ownedApartment) UninitializeCompositionApartment();
            return;
        }

        _windowsCompositionQueueController = controller;
        _windowsCompositionApartmentOwned = ownedApartment;
    }

    internal unsafe void ShutdownWindowsCompositionQueue()
    {
        if (!s_isWindows || !CheckAccess())
            return;

        if (_windowsCompositionQueueController == 0)
        {
            _windowsCompositionQueueAttempted = false;
            return;
        }

        nint controller = Interlocked.Exchange(ref _windowsCompositionQueueController, 0);
        nint action = 0, information = 0;
        int? quitCode = null;
        try
        {
            // IDispatcherQueueController inherits IInspectable; ShutdownQueueAsync
            // follows its DispatcherQueue getter. Raw COM calls also work in AOT.
            var shutdown = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)controller)[7];
            if (shutdown(controller, &action) >= 0 && action != 0)
            {
                var asyncInfoId = new Guid("00000036-0000-0000-C000-000000000046");
                if (Marshal.QueryInterface(action, ref asyncInfoId, out information) >= 0)
                {
                    var status = (delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)information)[7];
                    var elapsed = Stopwatch.StartNew();
                    int currentStatus;
                    while (status(information, &currentStatus) >= 0 && currentStatus == 0 && elapsed.ElapsedMilliseconds < 1000)
                    {
                        if (PeekMessageW(out var message, 0, 0, 0, PM_REMOVE))
                        {
                            if (message.message == WM_QUIT)
                            {
                                quitCode = unchecked((int)message.wParam);
                                continue;
                            }
                            _ = TranslateMessageW(ref message);
                            _ = DispatchMessageW(ref message);
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
            }
        }
        finally
        {
            if (information != 0) Marshal.Release(information);
            if (action != 0) Marshal.Release(action);
            Marshal.Release(controller);
            if (_windowsCompositionApartmentOwned)
                UninitializeCompositionApartment();
            _windowsCompositionApartmentOwned = false;
            _windowsCompositionQueueAttempted = false;
            if (quitCode is int exitCode) PostQuitMessage(exitCode);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [LibraryImport("CoreMessaging.dll", EntryPoint = "CreateDispatcherQueueController")]
    private static partial int CreateCompositionQueueController(CompositionQueueOptions options, out nint controller);

    [LibraryImport("combase.dll", EntryPoint = "RoInitialize")]
    private static partial int InitializeCompositionApartment(uint apartmentType);

    [LibraryImport("combase.dll", EntryPoint = "RoUninitialize")]
    private static partial void UninitializeCompositionApartment();
}
