using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;

namespace Jalium.UI;

/// <summary>
/// AOT-compatible OLE IDropTarget implementation using a hand-built COM vtable
/// and [UnmanagedCallersOnly] callbacks. Receives external drag-and-drop
/// (e.g. files from Windows Explorer) and routes them as Jalium.UI routed events.
/// </summary>
internal static unsafe class OleDropTarget
{
    // Shared vtable (allocated once, lives for process lifetime)
    private static nint _vtable;

    // Per-window managed state, prevented from GC by a GCHandle stored in the COM object.
    private sealed class DropTargetState
    {
        public required Window Window;
        public UIElement? CurrentTarget;
        public DataObject? CurrentData;
        public nint ComObject;
        public GCHandle SelfHandle;
    }

    private static readonly Dictionary<nint, DropTargetState> _states = new();

    /// <summary>
    /// Initializes the OLE subsystem. Must be called once on the UI thread.
    /// </summary>
    internal static void Initialize() => Win32.OleInitialize(nint.Zero);

    #region Registration

    internal static void RegisterWindow(Window window)
    {
        EnsureVtable();

        // COM object layout: [vtable_ptr, gc_handle_intptr]
        var comObj = (nint*)Marshal.AllocHGlobal(nint.Size * 2);

        var state = new DropTargetState { Window = window, ComObject = (nint)comObj };
        state.SelfHandle = GCHandle.Alloc(state);

        comObj[0] = _vtable;
        comObj[1] = GCHandle.ToIntPtr(state.SelfHandle);

        int hr = Win32.RegisterDragDrop(window.Handle, (nint)comObj);
        if (hr == 0) // S_OK
        {
            _states[window.Handle] = state;
        }
        else
        {
            state.SelfHandle.Free();
            Marshal.FreeHGlobal((nint)comObj);
        }
    }

    internal static void RevokeWindow(Window window)
    {
        if (!_states.Remove(window.Handle, out var state))
            return;

        Win32.RevokeDragDrop(window.Handle);
        state.SelfHandle.Free();
        Marshal.FreeHGlobal(state.ComObject);
    }

    private static void EnsureVtable()
    {
        if (_vtable != nint.Zero) return;

        var vt = (nint*)Marshal.AllocHGlobal(nint.Size * 7);
        vt[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        vt[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        vt[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        vt[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)&OnDragEnter;
        vt[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, long, uint*, int>)&OnDragOver;
        vt[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&OnDragLeave;
        vt[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)&OnDrop;
        _vtable = (nint)vt;
    }

    private static DropTargetState GetState(nint pThis) =>
        (DropTargetState)GCHandle.FromIntPtr(((nint*)pThis)[1]).Target!;

    #endregion

    #region IUnknown

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint pThis, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IDropTarget)
        {
            *ppv = pThis;
            return 0; // S_OK
        }
        *ppv = nint.Zero;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint pThis) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint pThis) => 1;

    #endregion

    #region IDropTarget

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragEnter(nint pThis, nint pDataObj, uint grfKeyState, long pt, uint* pdwEffect)
    {
        try
        {
            var s = GetState(pThis);
            s.CurrentData = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(s.Window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = (s.Window as FrameworkElement)?.HitTest(pos)?.VisualHit as UIElement;
            s.CurrentTarget = DragDropPlatform.FindDropTargetElement(hit);

            if (s.CurrentTarget != null)
            {
                var args = new DragEventArgs(DragDrop.DragEnterEvent, s.CurrentData, keys, allowed, pos);
                s.CurrentTarget.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }
        }
        catch { *pdwEffect = 0; }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragOver(nint pThis, uint grfKeyState, long pt, uint* pdwEffect)
    {
        try
        {
            var s = GetState(pThis);
            if (s.CurrentData == null) { *pdwEffect = 0; return 0; }

            var pos = PointFromScreen(s.Window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = (s.Window as FrameworkElement)?.HitTest(pos)?.VisualHit as UIElement;
            var newTarget = DragDropPlatform.FindDropTargetElement(hit);

            if (newTarget != s.CurrentTarget)
            {
                if (s.CurrentTarget != null)
                {
                    var leave = new DragEventArgs(DragDrop.DragLeaveEvent, s.CurrentData, keys, allowed, pos);
                    s.CurrentTarget.RaiseEvent(leave);
                }
                s.CurrentTarget = newTarget;
                if (s.CurrentTarget != null)
                {
                    var enter = new DragEventArgs(DragDrop.DragEnterEvent, s.CurrentData, keys, allowed, pos);
                    s.CurrentTarget.RaiseEvent(enter);
                }
            }

            if (s.CurrentTarget != null)
            {
                var args = new DragEventArgs(DragDrop.DragOverEvent, s.CurrentData, keys, allowed, pos);
                s.CurrentTarget.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }
        }
        catch { *pdwEffect = 0; }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragLeave(nint pThis)
    {
        try
        {
            var s = GetState(pThis);
            if (s.CurrentTarget != null && s.CurrentData != null)
            {
                var args = new DragEventArgs(DragDrop.DragLeaveEvent, s.CurrentData, DragDropKeyStates.None, DragDropEffects.None, default);
                s.CurrentTarget.RaiseEvent(args);
            }
            s.CurrentTarget = null;
            s.CurrentData = null;
        }
        catch { }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDrop(nint pThis, nint pDataObj, uint grfKeyState, long pt, uint* pdwEffect)
    {
        try
        {
            var s = GetState(pThis);
            var data = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(s.Window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = (s.Window as FrameworkElement)?.HitTest(pos)?.VisualHit as UIElement;
            var target = DragDropPlatform.FindDropTargetElement(hit) ?? s.CurrentTarget;

            if (target != null)
            {
                var args = new DragEventArgs(DragDrop.DropEvent, data, keys, allowed, pos);
                target.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }

            s.CurrentTarget = null;
            s.CurrentData = null;
        }
        catch { *pdwEffect = 0; }
        return 0;
    }

    #endregion

    #region Data Extraction (via raw COM vtable call)

    private static DataObject ExtractDataObject(nint pDataObj)
    {
        var data = new DataObject();
        if (pDataObj == nint.Zero) return data;

        TryExtractFiles(pDataObj, data);
        TryExtractUnicodeText(pDataObj, data);
        return data;
    }

    /// <summary>
    /// Calls IDataObject::GetData (vtable index 3) on a raw COM pointer.
    /// </summary>
    private static int ComGetData(nint pDataObj, FORMATETC* pFmt, STGMEDIUM* pMedium)
    {
        nint vtable = *(nint*)pDataObj;
        var fn = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)(*(nint*)(vtable + 3 * nint.Size));
        return fn(pDataObj, pFmt, pMedium);
    }

    private static void TryExtractFiles(nint pDataObj, DataObject data)
    {
        var fmt = new FORMATETC { cfFormat = 15, dwAspect = 1, lindex = -1, tymed = 1 }; // CF_HDROP, DVASPECT_CONTENT, TYMED_HGLOBAL
        var medium = new STGMEDIUM();

        if (ComGetData(pDataObj, &fmt, &medium) != 0 || medium.unionmember == nint.Zero)
            return;

        try
        {
            uint count = Win32.DragQueryFileW(medium.unionmember, 0xFFFFFFFF, null, 0);
            if (count == 0) return;

            var files = new string[count];
            var buf = new char[520];
            for (uint i = 0; i < count; i++)
            {
                uint len = Win32.DragQueryFileW(medium.unionmember, i, buf, (uint)buf.Length);
                files[i] = new string(buf, 0, (int)len);
            }
            data.SetData(DataFormats.FileDrop, files);
        }
        finally
        {
            Win32.ReleaseStgMedium(&medium);
        }
    }

    private static void TryExtractUnicodeText(nint pDataObj, DataObject data)
    {
        var fmt = new FORMATETC { cfFormat = 13, dwAspect = 1, lindex = -1, tymed = 1 }; // CF_UNICODETEXT
        var medium = new STGMEDIUM();

        if (ComGetData(pDataObj, &fmt, &medium) != 0 || medium.unionmember == nint.Zero)
            return;

        try
        {
            var ptr = Win32.GlobalLock(medium.unionmember);
            if (ptr != nint.Zero)
            {
                var text = Marshal.PtrToStringUni(ptr);
                Win32.GlobalUnlock(medium.unionmember);
                if (!string.IsNullOrEmpty(text))
                {
                    data.SetData(DataFormats.UnicodeText, text);
                    data.SetData(DataFormats.Text, text);
                }
            }
        }
        finally
        {
            Win32.ReleaseStgMedium(&medium);
        }
    }

    #endregion

    #region Helpers

    private static Point PointFromScreen(Window window, long pt)
    {
        int x = unchecked((int)(pt & 0xFFFFFFFF));
        int y = unchecked((int)((pt >> 32) & 0xFFFFFFFF));
        var p = new POINT { X = x, Y = y };
        Win32.ScreenToClient(window.Handle, ref p);
        double dpi = window.DpiScale;
        return new Point(p.X / dpi, p.Y / dpi);
    }

    private static DragDropKeyStates MapKeyStates(uint g)
    {
        var s = DragDropKeyStates.None;
        if ((g & 0x0001) != 0) s |= DragDropKeyStates.LeftMouseButton;
        if ((g & 0x0002) != 0) s |= DragDropKeyStates.RightMouseButton;
        if ((g & 0x0004) != 0) s |= DragDropKeyStates.ShiftKey;
        if ((g & 0x0008) != 0) s |= DragDropKeyStates.ControlKey;
        if ((g & 0x0010) != 0) s |= DragDropKeyStates.MiddleMouseButton;
        if ((g & 0x0020) != 0) s |= DragDropKeyStates.AltKey;
        return s;
    }

    private static DragDropEffects MapEffects(uint e) => (DragDropEffects)(e & 0x7FFFFFFF);
    private static uint MapEffectsBack(DragDropEffects e) => (uint)e & 0x7FFFFFFF;

    #endregion

    #region Native Structs & P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct FORMATETC
    {
        public ushort cfFormat;
        public nint ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STGMEDIUM
    {
        public uint tymed;
        public nint unionmember;
        public nint pUnkForRelease;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static class Win32
    {
        [DllImport("ole32.dll")]
        internal static extern int OleInitialize(nint pvReserved);

        [DllImport("ole32.dll")]
        internal static extern int RegisterDragDrop(nint hwnd, nint pDropTarget);

        [DllImport("ole32.dll")]
        internal static extern int RevokeDragDrop(nint hwnd);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(STGMEDIUM* pmedium);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint DragQueryFileW(nint hDrop, uint iFile, char[]? lpszFile, uint cch);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalLock(nint hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(nint hMem);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);
    }

    #endregion
}
