// Consolidated Win32 interop structures — see GitHub issue #151.
//
// Historically these OS structs (POINT, RECT, PAINTSTRUCT, …) were re-declared as
// private nested types in many controls (Window, PopupWindow, Popup, DockItem,
// DockManager, DockIndicatorWindow, Win32PointerInterop, DevTools, UIA, …). POINT
// alone had 10 identical copies and RECT had 8. This is the single shared home so
// they are declared once and maintained in one place.
//
// The types live in Jalium.UI.Core (the common ancestor assembly) so every managed
// assembly — Core, Input, Media, Interop, Controls — can share them via its existing
// InternalsVisibleTo grant. They are deliberately `internal`: Win32 interop is an
// implementation detail, never part of the framework's public API surface.
//
// Field names/order/types match the previous copies byte-for-byte, so marshalling is
// identical. Where copies disagreed only on member *casing* (RECT.left vs RECT.Left,
// POINT.X vs POINT.x) the majority spelling was kept and the few outliers updated at
// their call sites.

using System.Runtime.InteropServices;

namespace Jalium.UI.Interop.Win32;

/// <summary>Win32 <c>POINT</c>. Fields are <c>X</c>/<c>Y</c> (majority spelling).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>Win32 <c>RECT</c>. Fields are lowercase <c>left/top/right/bottom</c> (majority spelling).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

/// <summary>Win32 <c>PAINTSTRUCT</c> (used with BeginPaint/EndPaint).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PAINTSTRUCT
{
    public nint hdc;
    public bool fErase;
    public RECT rcPaint;
    public bool fRestore;
    public bool fIncUpdate;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[]? rgbReserved;
}

/// <summary>Win32 <c>MONITORINFO</c> (used with GetMonitorInfo).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

/// <summary>Win32 <c>TRACKMOUSEEVENT</c> (used with TrackMouseEvent).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

/// <summary>Win32 <c>WNDCLASSEXW</c> (used with RegisterClassEx). Strings marshal as UTF-16.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string lpszClassName;
    public nint hIconSm;
}
