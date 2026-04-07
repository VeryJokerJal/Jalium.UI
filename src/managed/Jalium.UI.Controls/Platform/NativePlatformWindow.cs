using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Platform window implementation using the jalium.native.platform library.
/// Used on Linux and Android (non-Windows platforms).
/// Delegates all windowing operations to the native platform library via P/Invoke.
/// </summary>
internal sealed partial class NativePlatformWindow : IPlatformWindow
{
    private nint _handle;
    private Action<PlatformEvent>? _eventHandler;
    private bool _disposed;

    // Event callback delegate must be pinned to prevent GC collection
    private readonly NativeEventCallbackDelegate _nativeCallback;
    private GCHandle _callbackHandle;

    // The GCHandle to 'this' passed as userData to the native callback
    private GCHandle _selfHandle;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeEventCallbackDelegate(nint eventPtr, nint userData);

    public NativePlatformWindow(string title, int x, int y, int width, int height, uint style, nint parent)
    {
        // Pin the callback delegate
        _nativeCallback = OnNativeEventStatic;
        _callbackHandle = GCHandle.Alloc(_nativeCallback);

        // Pin 'this' so we can recover it from userData
        _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            fixed (char* titlePtr = title)
            {
                var windowParams = new NativePlatformWindowParams
                {
                    Title = (nint)titlePtr,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Style = style,
                    ParentHandle = parent,
                };

                _handle = NativeMethods.WindowCreate(ref windowParams);
            }
        }

        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create platform window.");

        // Register event callback with native window
        WindowSetEventCallback(_handle,
            Marshal.GetFunctionPointerForDelegate(_nativeCallback),
            GCHandle.ToIntPtr(_selfHandle));
    }

    public nint NativeHandle => _handle != nint.Zero
        ? NativeMethods.WindowGetNativeHandle(_handle)
        : nint.Zero;

    public NativeSurfaceDescriptor GetSurface()
    {
        if (_handle == nint.Zero) return default;
        return NativeMethods.WindowGetSurface(_handle);
    }

    public void Show()
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowShow(_handle);
    }

    public void Hide()
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowHide(_handle);
    }

    public void Close()
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowDestroy(_handle);
        _handle = nint.Zero;
    }

    public void SetTitle(string title)
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowSetTitle(_handle, title);
    }

    public void Resize(int width, int height)
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowResize(_handle, width, height);
    }

    public void Move(int x, int y)
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowMove(_handle, x, y);
    }

    public int GetWidth()
    {
        if (_handle == nint.Zero) return 0;
        NativeMethods.WindowGetClientSize(_handle, out int w, out _);
        return w;
    }

    public int GetHeight()
    {
        if (_handle == nint.Zero) return 0;
        NativeMethods.WindowGetClientSize(_handle, out _, out int h);
        return h;
    }

    public void SetState(WindowState state)
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowSetState(_handle, (int)state);
    }

    public WindowState GetState()
    {
        if (_handle == nint.Zero) return WindowState.Normal;
        return (WindowState)NativeMethods.WindowGetState(_handle);
    }

    public void Invalidate()
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowInvalidate(_handle);
    }

    public float GetDpiScale()
    {
        if (_handle == nint.Zero) return 1.0f;
        return NativeMethods.WindowGetDpiScale(_handle);
    }

    public int GetMonitorRefreshRate()
    {
        if (_handle == nint.Zero) return 60;
        return NativeMethods.WindowGetMonitorRefreshRate(_handle);
    }

    public void SetCursor(int cursorShape)
    {
        if (_handle != nint.Zero)
            NativeMethods.WindowSetCursor(_handle, cursorShape);
    }

    public void SetEventHandler(Action<PlatformEvent>? handler)
    {
        _eventHandler = handler;
    }

    // ========================================================================
    // Native Event Marshaling
    // ========================================================================

    /// <summary>
    /// Native event structure matching JaliumPlatformEvent from jalium_platform.h.
    /// Must be kept in sync with the C struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlatformEvent
    {
        public int Type;        // JaliumEventType
        public nint Window;     // JaliumPlatformWindow*

        // Union data - use largest member size with explicit offsets
        // We read the union based on event type, using the flat struct below.
        // Sized to hold the largest union member (pointer event = 44 bytes)
        public float Data0, Data1, Data2, Data3;
        public float Data4, Data5, Data6, Data7;
        public float Data8, Data9;
        public int Data10;
    }

    /// <summary>
    /// Static callback invoked from native code. Recovers the NativePlatformWindow
    /// instance from userData and dispatches the event.
    /// </summary>
    private static void OnNativeEventStatic(nint eventPtr, nint userData)
    {
        if (eventPtr == nint.Zero || userData == nint.Zero)
            return;

        var handle = GCHandle.FromIntPtr(userData);
        if (!handle.IsAllocated)
            return;

        var window = handle.Target as NativePlatformWindow;
        if (window == null || window._eventHandler == null)
            return;

        // Marshal the native event
        var nativeEvt = Marshal.PtrToStructure<NativePlatformEvent>(eventPtr);
        var evt = new PlatformEvent();
        evt.Type = (PlatformEventType)nativeEvt.Type;

        unsafe
        {
            // Read union data based on event type using raw pointer arithmetic.
            // The union starts at offset 8 (after type + window pointer on 64-bit) or
            // at the position of Data0 in NativePlatformEvent.
            float* data = &nativeEvt.Data0;
            int* idata = (int*)data;

            switch (evt.Type)
            {
                case PlatformEventType.Resize:
                    evt.Width = idata[0];
                    evt.Height = idata[1];
                    break;

                case PlatformEventType.Move:
                    evt.X = idata[0];
                    evt.Y = idata[1];
                    break;

                case PlatformEventType.DpiChanged:
                    evt.DpiX = data[0];
                    evt.DpiY = data[1];
                    evt.SuggestedX = idata[2];
                    evt.SuggestedY = idata[3];
                    evt.SuggestedWidth = idata[4];
                    evt.SuggestedHeight = idata[5];
                    break;

                case PlatformEventType.StateChanged:
                    evt.NewState = idata[0];
                    break;

                case PlatformEventType.MouseMove:
                case PlatformEventType.MouseDown:
                case PlatformEventType.MouseUp:
                    evt.MouseX = data[0];
                    evt.MouseY = data[1];
                    evt.Button = idata[2];
                    evt.Modifiers = idata[3];
                    evt.ClickCount = idata[4];
                    break;

                case PlatformEventType.MouseWheel:
                    evt.MouseX = data[0];
                    evt.MouseY = data[1];
                    evt.WheelDeltaX = data[2];
                    evt.WheelDeltaY = data[3];
                    evt.Modifiers = idata[4];
                    break;

                case PlatformEventType.KeyDown:
                case PlatformEventType.KeyUp:
                    evt.KeyCode = idata[0];
                    evt.ScanCode = idata[1];
                    evt.Modifiers = idata[2];
                    evt.IsRepeat = idata[3];
                    break;

                case PlatformEventType.CharInput:
                    evt.Codepoint = (uint)idata[0];
                    break;

                case PlatformEventType.PointerDown:
                case PlatformEventType.PointerUp:
                case PlatformEventType.PointerMove:
                case PlatformEventType.PointerCancel:
                    evt.PointerId = (uint)idata[0];
                    evt.PointerX = data[1];
                    evt.PointerY = data[2];
                    evt.Pressure = data[3];
                    evt.TiltX = data[4];
                    evt.TiltY = data[5];
                    evt.Twist = data[6];
                    evt.PointerType = idata[7];
                    evt.Modifiers = idata[8];
                    break;

                case PlatformEventType.SafeAreaChanged:
                    evt.SafeAreaTop = data[0];
                    evt.SafeAreaBottom = data[1];
                    evt.SafeAreaLeft = data[2];
                    evt.SafeAreaRight = data[3];
                    break;

                case PlatformEventType.KeyboardChanged:
                    evt.KeyboardVisible = idata[0];
                    evt.KeyboardHeightPx = idata[1];
                    break;

                case PlatformEventType.OrientationChanged:
                    evt.Orientation = idata[0];
                    break;
            }
        }

        window._eventHandler(evt);
    }

    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            // Unregister callback before destroying
            WindowSetEventCallback(_handle, nint.Zero, nint.Zero);
            NativeMethods.WindowDestroy(_handle);
            _handle = nint.Zero;
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();
    }

    // P/Invoke for event callback registration
    [LibraryImport("jalium.native.platform", EntryPoint = "jalium_window_set_event_callback")]
    private static partial void WindowSetEventCallback(nint window, nint callback, nint userData);
}
