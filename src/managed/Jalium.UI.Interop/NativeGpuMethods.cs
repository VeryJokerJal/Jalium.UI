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
    /// <summary>
    /// Adapter description stored by the native ABI as 128 fixed-width UTF-16 code units.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Name;

    public GpuAdapterType AdapterType;
    public ulong DedicatedVideoMemory;
    public ulong SharedSystemMemory;
    public uint VendorId;
    public uint DeviceId;
}

/// <summary>
/// Mirrors native <c>JaliumPresentInfo</c>. Surfaces 后端实际采用的 swap chain present
/// 配置给宿主：FLIP/BLT、tearing、frame-latency-waitable、最大延迟等。
/// 帮宿主在 IDE 状态栏显示"GPU + FLIP-tearing-1f"诊断信息。
///
/// <para>Backend-dependent fields:</para>
/// <list type="bullet">
///   <item><see cref="SwapEffect"/> uses each backend's native enum (D3D12 →
///   DXGI_SWAP_EFFECT, Vulkan → VkPresentModeKHR). The integer values do NOT
///   share a namespace — consumers should branch on the current backend's
///   type via <see cref="RenderContext"/> before interpreting it.</item>
///   <item><see cref="WaitableEnabled"/> and <see cref="MaxFrameLatency"/>
///   only make sense on D3D12; Vulkan always reports 0 for both (no
///   equivalent OS HANDLE).</item>
/// </list>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PresentInfo
{
    /// <summary>
    /// Raw enum value:
    /// <list type="bullet">
    ///   <item>D3D12 → <c>DXGI_SWAP_EFFECT</c>: 0=Discard, 1=Sequential,
    ///   3=FlipSequential, 4=FlipDiscard.</item>
    ///   <item>Vulkan → <c>VkPresentModeKHR</c>: 0=IMMEDIATE, 1=MAILBOX,
    ///   2=FIFO, 3=FIFO_RELAXED.</item>
    /// </list>
    /// Consumers must branch on the active backend before mapping to a display string.
    /// </summary>
    public int SwapEffect;
    /// <summary>后台缓冲数量（通常 2 或 3）。两端语义一致。</summary>
    public int BufferCount;
    /// <summary>
    /// 1 = "tearing path" 启用. D3D12 = ALLOW_TEARING flag; Vulkan =
    /// VK_PRESENT_MODE_IMMEDIATE_KHR (the closest analogue).
    /// </summary>
    public int TearingEnabled;
    /// <summary>
    /// 1 = D3D12 FRAME_LATENCY_WAITABLE_OBJECT 启用. Always 0 on Vulkan — no
    /// equivalent OS HANDLE; the WSI fence wait subsumes the pacing role.
    /// </summary>
    public int WaitableEnabled;
    /// <summary>
    /// D3D12 SetMaximumFrameLatency 当前值; WaitableEnabled == 0 时通常为 0.
    /// Always 0 on Vulkan.
    /// </summary>
    public int MaxFrameLatency;
    /// <summary>
    /// 1 = composition path (D3D12 DirectComposition, Vulkan
    /// VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR or similar), 0 = 普通 HWND
    /// / opaque surface.
    /// </summary>
    public int Composition;
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

    [DllImport(CoreLib, EntryPoint = "jalium_render_target_get_present_info", ExactSpelling = true)]
    internal static extern int RenderTargetGetPresentInfo(nint renderTarget, out PresentInfo info);
}
