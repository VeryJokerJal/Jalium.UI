using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// GPU adapter preference for multi-GPU systems.
/// </summary>
public enum GpuPreference
{
    /// <summary>Let the OS/driver decide (default).</summary>
    Auto = 0,
    /// <summary>Prefer discrete/high-performance GPU.</summary>
    HighPerformance = 1,
    /// <summary>Prefer integrated/low-power GPU.</summary>
    MinimumPower = 2,
}

/// <summary>
/// GPU adapter type classification.
/// </summary>
public enum GpuAdapterType
{
    Unknown = 0,
    Discrete = 1,
    Integrated = 2,
    Software = 3,
}

/// <summary>
/// Information about the selected GPU adapter.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct AdapterInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Name;

    public GpuAdapterType AdapterType;
    public ulong DedicatedVideoMemory;
    public ulong SharedSystemMemory;
    public uint VendorId;
    public uint DeviceId;
}

/// <summary>
/// GPU preference and adapter info P/Invoke methods.
/// Separated from NativeMethods to avoid LibraryImport source generator issues.
/// </summary>
internal static class NativeGpuMethods
{
    private const string CoreLib = "jalium.native.core";

    [DllImport(CoreLib, EntryPoint = "jalium_context_set_gpu_preference", ExactSpelling = true)]
    internal static extern int ContextSetGpuPreference(nint context, GpuPreference gpuPreference);

    [DllImport(CoreLib, EntryPoint = "jalium_context_get_adapter_info", ExactSpelling = true)]
    internal static extern int ContextGetAdapterInfo(nint context, out AdapterInfo info);
}
