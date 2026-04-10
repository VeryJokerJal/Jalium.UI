using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// P/Invoke declarations for Windows UI Automation Core.
/// Matches WPF's approach: pass IRawElementProviderSimple directly,
/// let CLR handle COM marshaling automatically.
/// </summary>
internal static partial class UiaNativeMethods
{
    [DllImport("uiautomationcore.dll", EntryPoint = "UiaReturnRawElementProvider", CharSet = CharSet.Unicode)]
    internal static extern nint UiaReturnRawElementProvider(
        nint hwnd, nint wParam, nint lParam,
        IRawElementProviderSimple el);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaHostProviderFromHwnd", CharSet = CharSet.Unicode)]
    internal static extern int UiaHostProviderFromHwnd(
        nint hwnd,
        [MarshalAs(UnmanagedType.Interface)] out IRawElementProviderSimple? provider);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseAutomationEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseAutomationEvent(
        IRawElementProviderSimple provider, int eventId);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseAutomationPropertyChangedEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseAutomationPropertyChangedEvent(
        IRawElementProviderSimple provider,
        int propertyId, object oldValue, object newValue);

    [DllImport("uiautomationcore.dll", EntryPoint = "UiaRaiseStructureChangedEvent", CharSet = CharSet.Unicode)]
    internal static extern int UiaRaiseStructureChangedEvent(
        IRawElementProviderSimple provider,
        int structureChangeType, int[] runtimeId, int runtimeIdLen);

    [DllImport("uiautomationcore.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UiaClientsAreListening();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }
}
