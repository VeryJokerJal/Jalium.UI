using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal enum NativePlatform
{
    Unknown = 0,
    Windows = 1,
    LinuxX11 = 2,
    Android = 3,
    MacOS = 4
}

internal enum NativeSurfaceKind
{
    NativeWindow = 1,
    CompositionTarget = 2
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSurfaceDescriptor
{
    public NativePlatform Platform { get; init; }

    public NativeSurfaceKind Kind { get; init; }

    public nint Handle0 { get; init; }

    public nint Handle1 { get; init; }

    public nint Handle2 { get; init; }

    public static NativeSurfaceDescriptor ForWindowsHwnd(nint hwnd, bool composition = false)
    {
        return new NativeSurfaceDescriptor
        {
            Platform = NativePlatform.Windows,
            Kind = composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            Handle0 = hwnd
        };
    }

    public static NativeSurfaceDescriptor ForLinuxX11(nint display, nint window, bool composition = false)
    {
        return new NativeSurfaceDescriptor
        {
            Platform = NativePlatform.LinuxX11,
            Kind = composition ? NativeSurfaceKind.CompositionTarget : NativeSurfaceKind.NativeWindow,
            Handle0 = display,
            Handle1 = window
        };
    }

    public bool IsValid => Platform switch
    {
        NativePlatform.LinuxX11 => Handle0 != nint.Zero && Handle1 != nint.Zero,
        _ => Handle0 != nint.Zero
    };
}
