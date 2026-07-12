// Consolidated GDI device-context + global-memory P/Invokes — see GitHub issue #151.
//
// These low-level helpers were re-declared across the clipboard / drag-drop / printing /
// icon / font / DevTools-screenshot code (GetDC/ReleaseDC/DeleteObject/DeleteDC each had
// 3 copies; GlobalAlloc/Free/Lock/Unlock had 3-4). They share one home here. The kernel32
// global-memory functions live alongside the GDI ones because the same imaging/clipboard
// code allocates the HGLOBAL it later blits into a device context.
//
// Consumers pull them in with `using static Jalium.UI.Interop.Win32.Win32GdiMethods;` so
// existing unqualified call sites keep working. All are blittable (handles only), so
// source-generated [LibraryImport] is used throughout — AOT-safe.

using System.Runtime.InteropServices;

namespace Jalium.UI.Interop.Win32;

internal static partial class Win32GdiMethods
{
    // ── Device contexts (user32 / gdi32) ──────────────────────────────────
    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    // ── Global memory (kernel32) ──────────────────────────────────────────
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalFree(nint hMem);
}
