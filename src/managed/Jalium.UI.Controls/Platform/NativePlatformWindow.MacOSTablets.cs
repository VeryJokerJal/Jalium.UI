using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

// Embeds the unchanged native 72-byte pointer event at offset 8.
[StructLayout(LayoutKind.Explicit, Size = 96)]
internal struct MacOSTabletSnapshot
{
    [FieldOffset(0)] internal uint Size;
    [FieldOffset(8)] internal int Type;
    [FieldOffset(16)] internal nint Window;
    [FieldOffset(24)] internal uint PointerId;
    [FieldOffset(28)] internal float X;
    [FieldOffset(32)] internal float Y;
    [FieldOffset(36)] internal float Pressure;
    [FieldOffset(40)] internal float TiltX;
    [FieldOffset(44)] internal float TiltY;
    [FieldOffset(48)] internal float Twist;
    [FieldOffset(52)] internal int PointerType;
    [FieldOffset(56)] internal int Modifiers;
    [FieldOffset(60)] internal uint Flags;
    [FieldOffset(64)] internal int ToolType;
    [FieldOffset(68)] internal uint Buttons;
    [FieldOffset(72)] internal long TimestampMillis;
    [FieldOffset(80)] internal int MouseType;
    [FieldOffset(84)] internal int MouseButton;
    [FieldOffset(88)] internal int MouseClickCount;
    [FieldOffset(92)] internal uint MouseButtonStates;

    internal readonly PlatformEvent ToPointerEvent() => new()
    {
        Type = (PlatformEventType)Type, PointerId = PointerId, PointerX = X, PointerY = Y,
        Pressure = Pressure, TiltX = TiltX, TiltY = TiltY, Twist = Twist, PointerType = PointerType,
        Modifiers = Modifiers, PointerFlags = Flags, PointerToolType = ToolType,
        PointerButtons = Buttons, PointerTimestampMillis = TimestampMillis
    };

    internal readonly PlatformEvent ToMouseEvent() => new()
    {
        Type = (PlatformEventType)MouseType, MouseX = X, MouseY = Y, Button = MouseButton,
        Modifiers = Modifiers, ClickCount = MouseClickCount,
        HasMouseButtonStates = (MouseButtonStates & 0x80000000u) != 0,
        MouseButtons = MouseButtonStates & 0x1f
    };
}

internal sealed class MacOSTabletRequest(MacOSTabletSnapshot snapshot)
{
    internal MacOSTabletSnapshot Snapshot { get; } = snapshot;
    internal bool Handled { get; set; }
    internal bool Retire { get; set; }
}

internal sealed partial class NativePlatformWindow
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MacOSTabletCallback(nint snapshot, nint context);
    private readonly MacOSTabletCallback _macOSTabletCallback = OnMacOSTablet;
    private bool _macOSTabletsRegistered;
    internal bool SupportsTabletInput => _macOSTabletsRegistered && !_disposed;

    private void InitializeMacOSTablets()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            _macOSTabletsRegistered = NativeMethods.AppleWindowSetTabletCallback(_handle,
                Marshal.GetFunctionPointerForDelegate(_macOSTabletCallback), GCHandle.ToIntPtr(_selfHandle)) == 0;
        }
        catch (EntryPointNotFoundException) { }
    }

    private void DisposeMacOSTablets()
    {
        if (!_macOSTabletsRegistered) return;
        _macOSTabletsRegistered = false;
        _ = NativeMethods.AppleWindowSetTabletCallback(_handle, 0, 0);
    }

    private static int OnMacOSTablet(nint pointer, nint context)
    {
        if (pointer == 0 || context == 0) return -1;
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not NativePlatformWindow window || window._disposed ||
                window._eventHandler is not { } handler) return -1;
            if (Marshal.ReadInt32(pointer) != 96) return 0;
            var snapshot = Marshal.PtrToStructure<MacOSTabletSnapshot>(pointer);
            if (snapshot.Window != window._handle || snapshot.PointerId < 2 || snapshot.PointerType != 2 ||
                snapshot.Type is < 50 or > 53 || snapshot.MouseType is not (0 or 30 or 31 or 32)) return -1;
            var request = new MacOSTabletRequest(snapshot);
            handler(new PlatformEvent { Type = PlatformEventType.MacOSTablet, MacOSTablet = request });
            return request.Retire ? -1 : request.Handled ? 1 : 0;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[NativePlatformWindow] tablet callback failed: {exception}");
            return -1;
        }
    }
}
