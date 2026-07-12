// Consolidated OLE data-transfer interop structs — see GitHub issue #151.
//
// FORMATETC and STGMEDIUM (objidl.h) were declared identically as private nested types in
// BOTH OleDropTarget and OleDragSource. They live here once. They are kept in the same
// assembly and namespace (Jalium.UI, Jalium.UI.Controls) as their only consumers — the
// hand-built OLE COM vtables and the [DllImport] ReleaseStgMedium in the drag/drop code —
// so no `using` is needed and there is no cross-assembly source-gen marshalling concern
// (both structs are blittable and are only passed by value / by pointer through classic
// [DllImport] and unmanaged function pointers, never through [LibraryImport]).

using System.Runtime.InteropServices;

namespace Jalium.UI;

/// <summary>OLE <c>FORMATETC</c> (objidl.h): describes a data-object format for transfer.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FORMATETC
{
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

/// <summary>OLE <c>STGMEDIUM</c> (objidl.h): a storage medium carrying transferred data.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STGMEDIUM
{
    public uint tymed;
    public nint unionmember;
    public nint pUnkForRelease;
}
