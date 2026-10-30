using System.Runtime.InteropServices;

namespace Jalium.UI.Interop.Win32;

// Pointer-sized fields and natural alignment are required on both x86 and x64.
[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public nint lParam;
}

internal static partial class Win32Methods
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);
}
