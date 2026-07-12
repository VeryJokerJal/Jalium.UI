using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// P/Invoke declarations for Windows UI Automation Core.
/// </summary>
/// <remarks>
/// Provider arguments are passed as raw <see cref="nint"/> COM interface pointers rather
/// than as an <c>IRawElementProviderSimple</c> parameter. The managed provider is turned
/// into a COM-callable wrapper explicitly in <see cref="UiaAccessibilityBridge"/> via
/// <see cref="UiaComInterop"/> (source-generated ComWrappers), which works under both JIT
/// and NativeAOT — unlike the runtime's built-in interface marshalling, which NativeAOT
/// removes. VARIANT arguments use <see cref="ComVariant"/> (the source-gen VARIANT type).
/// </remarks>
internal static partial class UiaNativeMethods
{
    [DllImport("uiautomationcore.dll", EntryPoint = "UiaReturnRawElementProvider", CharSet = CharSet.Unicode)]
    internal static extern nint UiaReturnRawElementProvider(
        nint hwnd, nint wParam, nint lParam, nint el);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaHostProviderFromHwnd", CharSet = CharSet.Unicode)]
    internal static extern int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseAutomationEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseAutomationEvent(nint provider, int eventId);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseAsyncContentLoadedEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseAsyncContentLoadedEvent(
        nint provider, int asyncContentLoadedState, double percentComplete);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseNotificationEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseNotificationEvent(
        nint provider,
        int notificationKind,
        int notificationProcessing,
        string displayString,
        string activityId);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseAutomationPropertyChangedEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseAutomationPropertyChangedEvent(
        nint provider, int propertyId, UiaVariant oldValue, UiaVariant newValue);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseStructureChangedEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseStructureChangedEvent(
        nint provider, int structureChangeType, int[] runtimeId, int runtimeIdLen);

    [DllImport("uiautomationcore.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UiaClientsAreListening();

    [DllImport("kernel32.dll", SetLastError = false)]
    internal static extern nuint VirtualQuery(
        nint lpAddress,
        out MEMORY_BASIC_INFORMATION lpBuffer,
        nuint dwLength);

    // Retrieves UIA's process-wide reserved "not supported" sentinel. Returned as an owned
    // IUnknown reference (COM out-param convention): transfer it to the caller — do not release.
    [DllImport("uiautomationcore.dll", EntryPoint = "UiaGetReservedNotSupportedValue")]
    internal static extern int UiaGetReservedNotSupportedValue(out nint punkNotSupportedValue);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}
