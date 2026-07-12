using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal enum NativePlatform
{
    Unknown = 0,
    Windows = 1,
    LinuxX11 = 2,
    Android = 3,
    MacOS = 4,
    LinuxWayland = 5
}

internal enum NativeSurfaceKind
{
    NativeWindow = 1,
    CompositionTarget = 2
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSurfaceDescriptor
{
    // Explicit fields keep the managed return-by-value layout identical to
    // JaliumSurfaceDescriptor on every architecture.
    public readonly NativePlatform Platform;

    public readonly NativeSurfaceKind Kind;

    public readonly nint Handle0;

    public readonly nint Handle1;

    public readonly nint Handle2;

    private NativeSurfaceDescriptor(
        NativePlatform platform,
        NativeSurfaceKind kind,
        nint handle0,
        nint handle1 = default,
        nint handle2 = default)
    {
        Platform = platform;
        Kind = kind;
        Handle0 = handle0;
        Handle1 = handle1;
        Handle2 = handle2;
    }

    public static NativeSurfaceDescriptor ForWindowsHwnd(nint hwnd, bool composition = false)
    {
        return new NativeSurfaceDescriptor(
            NativePlatform.Windows,
            composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            hwnd);
    }

    public static NativeSurfaceDescriptor ForLinuxX11(nint display, nint window, bool composition = false)
    {
        return new NativeSurfaceDescriptor(
            NativePlatform.LinuxX11,
            composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            display,
            window);
    }

    public static NativeSurfaceDescriptor ForLinuxWayland(nint display, nint surface, bool composition = false)
    {
        return new NativeSurfaceDescriptor(
            NativePlatform.LinuxWayland,
            composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            display,
            surface);
    }

    public static NativeSurfaceDescriptor ForAndroidNativeWindow(nint nativeWindow, bool composition = false)
    {
        return new NativeSurfaceDescriptor(
            NativePlatform.Android,
            composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            nativeWindow);
    }

    public static NativeSurfaceDescriptor ForMacOSView(nint viewHandle, bool composition = false)
    {
        return new NativeSurfaceDescriptor(
            NativePlatform.MacOS,
            composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            viewHandle);
    }

    public bool IsValid => Platform switch
    {
        NativePlatform.LinuxX11 or NativePlatform.LinuxWayland => Handle0 != nint.Zero && Handle1 != nint.Zero,
        _ => Handle0 != nint.Zero
    };
}
