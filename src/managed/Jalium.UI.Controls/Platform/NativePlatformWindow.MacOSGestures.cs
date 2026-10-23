using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacOSGestureSnapshot
{
    internal readonly uint Size;
    internal readonly int Kind, Phase;
    internal readonly uint Flags;
    internal readonly ulong Sequence;
    internal readonly double X, Y, Scale, Rotation, Timestamp;

    internal MacOSGestureSnapshot(ulong sequence, int kind, int phase, double x, double y,
        double scale = 1, double rotation = 0, double timestamp = 1, uint flags = 0)
    {
        Size = 64; Sequence = sequence; Kind = kind; Phase = phase; Flags = flags;
        X = x; Y = y; Scale = scale; Rotation = rotation; Timestamp = timestamp;
    }
}

internal sealed class MacOSGestureRequest(MacOSGestureSnapshot snapshot)
{
    internal MacOSGestureSnapshot Snapshot { get; } = snapshot;
    internal bool Handled { get; set; }
}

internal sealed partial class NativePlatformWindow : IPlatformGestureInput
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MacOSGestureCallback(nint snapshot, nint context);
    // The existing self GCHandle roots this instance and its delegate until
    // both callbacks have been unregistered, including reentrant disposal.
    private readonly MacOSGestureCallback _macOSGestureCallback = OnMacOSGesture;
    private bool _macOSGesturesRegistered;
    public bool SupportsIndirectGestures => _macOSGesturesRegistered && !_disposed;

    private void InitializeMacOSGestures()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            _macOSGesturesRegistered = NativeMethods.AppleWindowSetGestureCallback(_handle,
                Marshal.GetFunctionPointerForDelegate(_macOSGestureCallback), GCHandle.ToIntPtr(_selfHandle)) == 0;
        }
        catch (EntryPointNotFoundException) { } // A matched older native payload remains usable.
    }

    private void DisposeMacOSGestures()
    {
        if (!_macOSGesturesRegistered) return;
        _macOSGesturesRegistered = false;
        _ = NativeMethods.AppleWindowSetGestureCallback(_handle, 0, 0);
    }

    private static int OnMacOSGesture(nint pointer, nint context)
    {
        if (pointer == 0 || context == 0) return 0;
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not NativePlatformWindow window || window._disposed ||
                window._eventHandler is not { } handler) return 0;
            var snapshot = Marshal.PtrToStructure<MacOSGestureSnapshot>(pointer);
            if (snapshot.Size != 64 || snapshot.Sequence == 0 || snapshot.Phase is < 1 or > 4 || snapshot.Kind is < 0 or > 2)
                return 0;
            var request = new MacOSGestureRequest(snapshot);
            handler(new PlatformEvent { Type = PlatformEventType.MacOSGesture, MacOSGesture = request });
            return request.Handled ? 1 : 0;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[NativePlatformWindow] gesture callback failed: {exception}");
            return 0;
        }
    }
}
