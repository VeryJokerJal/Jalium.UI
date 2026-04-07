using System.Runtime.InteropServices;

namespace Jalium.UI.Core.Platform;

/// <summary>
/// Cross-platform dispatcher wake using the jalium.native.platform library.
/// Used on Linux (eventfd) and Android (ALooper eventfd).
/// </summary>
internal sealed partial class NativeDispatcherWake : IDispatcherWake
{
    private const string PlatformLib = "jalium.native.platform";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeDispatcherCallbackDelegate(nint userData);

    private nint _handle;
    private bool _disposed;
    private Action? _callback;
    private NativeDispatcherCallbackDelegate? _nativeDelegate;
    private GCHandle _nativeDelegateHandle;

    public NativeDispatcherWake()
    {
        int result = DispatcherCreate(out _handle);
        if (result != 0 || _handle == nint.Zero)
            throw new InvalidOperationException(
                $"Failed to create native dispatcher (error {result}).");
    }

    public void Wake()
    {
        if (_handle != nint.Zero)
            DispatcherWake(_handle);
    }

    public void SetCallback(Action callback)
    {
        _callback = callback;

        if (_handle == nint.Zero) return;

        // Create a native-callable function pointer that invokes the managed callback.
        // The delegate must be pinned via GCHandle to prevent GC collection.
        _nativeDelegate = _ => _callback?.Invoke();
        if (_nativeDelegateHandle.IsAllocated)
            _nativeDelegateHandle.Free();
        _nativeDelegateHandle = GCHandle.Alloc(_nativeDelegate);

        DispatcherSetCallback(
            _handle,
            Marshal.GetFunctionPointerForDelegate(_nativeDelegate),
            nint.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            DispatcherSetCallback(_handle, nint.Zero, nint.Zero);
            DispatcherDestroy(_handle);
            _handle = nint.Zero;
        }

        if (_nativeDelegateHandle.IsAllocated)
            _nativeDelegateHandle.Free();
    }

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_create")]
    private static partial int DispatcherCreate(out nint dispatcher);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_destroy")]
    private static partial void DispatcherDestroy(nint dispatcher);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_wake")]
    private static partial void DispatcherWake(nint dispatcher);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_set_callback")]
    private static partial void DispatcherSetCallback(nint dispatcher, nint callback, nint userData);
}
