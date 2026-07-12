// Consolidated Win32 imaging (DIB) interop structs — see GitHub issue #151.
//
// BITMAPINFOHEADER (wingdi.h) was declared in four places — IconHelper, DevToolsWindow,
// PrintingNativeMethods (uint/ushort) and OleDragSource (int/short). It lives here once, using
// the canonical uint/ushort field types. The struct is in the Jalium.UI.Controls namespace so
// most consumers (child namespaces Jalium.UI.Controls.Helpers / .DevTools / .Printing) see it
// unqualified; OleDragSource (namespace Jalium.UI) reaches it through its existing
// `using Jalium.UI.Controls;`. Keeping it in the same assembly as IconHelper also keeps that
// file's source-generated [LibraryImport] GetDIBits(ref BITMAPINFOHEADER) valid (a cross-assembly
// struct would trip SYSLIB1051).
//
// The former OleDragSource int/short variant is byte-identical (int/uint are both 4 bytes,
// short/ushort both 2) and its only construction site assigns literals (40/1/32/0) plus int
// width/height, all of which fit the uint/ushort fields with no cast — so the merge is
// behaviour-preserving.

using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>Win32 <c>BITMAPINFOHEADER</c> (wingdi.h) describing a device-independent bitmap.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}
