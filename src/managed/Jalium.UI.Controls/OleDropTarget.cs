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

        // The originating native IDataObject*, AddRef'd for the duration of a drag
        // so DragOver (which is not handed the data object) can still annotate it
        // with a Shell drop description. Released on DragLeave/Drop.
        public nint NativeData;

        // Whether a handler set a Shell drop description this drag, so it can be
        // cleared when the pointer leaves or the drop completes.
        public bool DropDescriptionSet;
    }

    // Shell IDropTargetHelper* (CLSID_DragDropHelper), created lazily on the UI
    // thread and cached for the process lifetime. Renders the system drag image
    // over the window automatically.
    private static nint _dropHelper;
    private static bool _dropHelperTried;

    // Registered clipboard format id for "DropDescription" (lazy).
    private static uint _cfDropDescription;

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
            AllowElevatedDragDrop(window.Handle);
        }
        else
        {
            state.SelfHandle.Free();
            Marshal.FreeHGlobal((nint)comObj);
        }
    }

    /// <summary>
    /// When the app runs elevated (high integrity level), UIPI blocks the drag-drop
    /// window messages that Explorer (medium IL) marshals the data payload through,
    /// so OLE drops silently fail. Unblocking these three messages on the drop-target
    /// window restores drops from lower-integrity sources. On a non-elevated process
    /// the messages already flow, so the call is a harmless no-op (and any failure is
    /// ignored — the feature is only relevant when elevated).
    /// </summary>
    private static void AllowElevatedDragDrop(nint hwnd)
    {
        const uint WM_DROPFILES = 0x0233;
        const uint WM_COPYDATA = 0x004A;
        const uint WM_COPYGLOBALDATA = 0x0049;
        const uint MSGFLT_ALLOW = 1;

        try
        {
            _ = Win32.ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, nint.Zero);
            _ = Win32.ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, nint.Zero);
            _ = Win32.ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
        }
        catch { }
    }

    internal static void RevokeWindow(Window window)
    {
        if (!_states.Remove(window.Handle, out var state))
            return;

        Win32.RevokeDragDrop(window.Handle);
        ReleaseNativeData(state);
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
            CacheNativeData(s, pDataObj);
            s.CurrentData = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(s.Window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = (s.Window as FrameworkElement)?.HitTest(pos)?.VisualHit as UIElement;
            s.CurrentTarget = DragDropPlatform.FindDropTargetElement(hit);

            if (s.CurrentTarget != null)
            {
                var args = new DragEventArgs(DragDrop.DragEnterEvent, s.CurrentData, keys, allowed, pos)
                {
                    DropDescriptionSetter = MakeDropDescriptionSetter(s),
                };
                s.CurrentTarget.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }

            // Let the Shell render the system drag image over this window.
            ShellHelperDragEnter(s, pDataObj, pt, *pdwEffect);
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
                    var leave = new DragEventArgs(DragDrop.DragLeaveEvent, s.CurrentData, keys, allowed, pos)
                    {
                        DropDescriptionSetter = MakeDropDescriptionSetter(s),
                    };
                    s.CurrentTarget.RaiseEvent(leave);
                }
                s.CurrentTarget = newTarget;
                if (s.CurrentTarget != null)
                {
                    var enter = new DragEventArgs(DragDrop.DragEnterEvent, s.CurrentData, keys, allowed, pos)
                    {
                        DropDescriptionSetter = MakeDropDescriptionSetter(s),
                    };
                    s.CurrentTarget.RaiseEvent(enter);
                }
            }

            if (s.CurrentTarget != null)
            {
                var args = new DragEventArgs(DragDrop.DragOverEvent, s.CurrentData, keys, allowed, pos)
                {
                    DropDescriptionSetter = MakeDropDescriptionSetter(s),
                };
                s.CurrentTarget.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }

            // Keep the Shell drag image following the pointer with the resolved effect.
            ShellHelperDragOver(pt, *pdwEffect);
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

            ShellHelperDragLeave();
            ClearDropDescriptionIfSet(s);
            ReleaseNativeData(s);
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
            CacheNativeData(s, pDataObj);
            var data = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(s.Window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = (s.Window as FrameworkElement)?.HitTest(pos)?.VisualHit as UIElement;
            var target = DragDropPlatform.FindDropTargetElement(hit) ?? s.CurrentTarget;

            if (target != null)
            {
                var args = new DragEventArgs(DragDrop.DropEvent, data, keys, allowed, pos)
                {
                    DropDescriptionSetter = MakeDropDescriptionSetter(s),
                };
                target.RaiseEvent(args);
                *pdwEffect = MapEffectsBack(args.Effects);
            }
            else
            {
                *pdwEffect = 0;
            }

            // Dismiss the Shell drag image and release the drag's native data object.
            ShellHelperDrop(pDataObj, pt, *pdwEffect);
            ClearDropDescriptionIfSet(s);
            ReleaseNativeData(s);
            s.CurrentTarget = null;
            s.CurrentData = null;
        }
        catch { *pdwEffect = 0; }
        return 0;
    }

    #endregion

    #region Shell Drag Image (IDropTargetHelper)

    /// <summary>
    /// Lazily creates the Shell <c>IDropTargetHelper</c> that renders the system
    /// drag image over the window. Cached for the process lifetime; a single
    /// failed attempt disables it so we never retry every drag.
    /// </summary>
    private static nint GetDropHelper()
    {
        if (_dropHelper != nint.Zero) return _dropHelper;
        if (_dropHelperTried) return nint.Zero;
        _dropHelperTried = true;

        Guid clsid = CLSID_DragDropHelper;
        Guid iid = IID_IDropTargetHelper;
        int hr = Win32.CoCreateInstance(ref clsid, nint.Zero, CLSCTX_INPROC_SERVER, ref iid, out nint p);
        if (hr == 0 && p != nint.Zero)
            _dropHelper = p;
        return _dropHelper;
    }

    // IDropTargetHelper vtable: 3 DragEnter, 4 DragOver, 5 DragLeave, 6 Drop, 7 Show.
    private static void ShellHelperDragEnter(DropTargetState s, nint pDataObj, long pt, uint effect)
    {
        nint helper = GetDropHelper();
        if (helper == nint.Zero) return;
        try
        {
            POINT p = PointFromLong(pt);
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint, POINT*, uint, int>)(*(nint*)(*(nint*)helper + 3 * nint.Size));
            _ = fn(helper, s.Window.Handle, pDataObj, &p, effect);
        }
        catch { }
    }

    private static void ShellHelperDragOver(long pt, uint effect)
    {
        nint helper = GetDropHelper();
        if (helper == nint.Zero) return;
        try
        {
            POINT p = PointFromLong(pt);
            var fn = (delegate* unmanaged[Stdcall]<nint, POINT*, uint, int>)(*(nint*)(*(nint*)helper + 4 * nint.Size));
            _ = fn(helper, &p, effect);
        }
        catch { }
    }

    private static void ShellHelperDragLeave()
    {
        nint helper = GetDropHelper();
        if (helper == nint.Zero) return;
        try
        {
            var fn = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint*)(*(nint*)helper + 5 * nint.Size));
            _ = fn(helper);
        }
        catch { }
    }

    private static void ShellHelperDrop(nint pDataObj, long pt, uint effect)
    {
        nint helper = GetDropHelper();
        if (helper == nint.Zero) return;
        try
        {
            POINT p = PointFromLong(pt);
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, POINT*, uint, int>)(*(nint*)(*(nint*)helper + 6 * nint.Size));
            _ = fn(helper, pDataObj, &p, effect);
        }
        catch { }
    }

    #endregion

    #region Drop Description (CFSTR_DROPDESCRIPTION)

    /// <summary>
    /// Builds the closure a drag event handler invokes via
    /// <see cref="DragEventArgs.SetDropDescription"/>. It writes the description
    /// onto the drag's native data object and records that one was set so it can
    /// be cleared when the pointer leaves or the drop finishes.
    /// </summary>
    private static Action<DropImageType, string?, string?> MakeDropDescriptionSetter(DropTargetState s)
        => (type, message, insert) =>
        {
            SetDropDescription(s.NativeData, type, message, insert);
            s.DropDescriptionSet = type != DropImageType.Invalid;
        };

    private static void ClearDropDescriptionIfSet(DropTargetState s)
    {
        if (!s.DropDescriptionSet) return;
        SetDropDescription(s.NativeData, DropImageType.Invalid, null, null);
        s.DropDescriptionSet = false;
    }

    /// <summary>
    /// Writes a <c>DROPDESCRIPTION</c> block into the native data object under the
    /// registered <c>DropDescription</c> format. The Shell's source-side drag image
    /// window reads it and updates the tooltip shown next to the cursor.
    /// </summary>
    private static void SetDropDescription(nint pDataObj, DropImageType type, string? message, string? insert)
    {
        if (pDataObj == nint.Zero) return;
        uint cf = CfDropDescription();
        if (cf == 0) return;

        // struct DROPDESCRIPTION { int type; WCHAR szMessage[MAX_PATH]; WCHAR szInsert[MAX_PATH]; }
        const int MaxPath = 260;
        int size = sizeof(int) + (MaxPath * 2) + (MaxPath * 2);

        nint hMem = Win32.GlobalAlloc(GHND, (nuint)size);
        if (hMem == nint.Zero) return;

        nint ptr = Win32.GlobalLock(hMem);
        if (ptr == nint.Zero) { _ = Win32.GlobalFree(hMem); return; }
        try
        {
            Marshal.WriteInt32(ptr, (int)type);
            WriteFixedString(ptr + sizeof(int), message, MaxPath);
            WriteFixedString(ptr + sizeof(int) + (MaxPath * 2), insert, MaxPath);
        }
        finally { _ = Win32.GlobalUnlock(hMem); }

        var fmt = new FORMATETC { cfFormat = (ushort)cf, ptd = nint.Zero, dwAspect = 1, lindex = -1, tymed = 1 };
        var medium = new STGMEDIUM { tymed = 1, unionmember = hMem, pUnkForRelease = nint.Zero };

        int hr = ComSetData(pDataObj, &fmt, &medium, 1 /* fRelease = TRUE */);
        if (hr != 0)
        {
            // SetData failed → ownership was not transferred; free our block.
            _ = Win32.GlobalFree(hMem);
        }
    }

    private static uint CfDropDescription()
    {
        if (_cfDropDescription == 0)
            _cfDropDescription = Win32.RegisterClipboardFormatW("DropDescription");
        return _cfDropDescription;
    }

    /// <summary>
    /// Copies up to <paramref name="maxChars"/> - 1 UTF-16 code units into a
    /// zero-initialized fixed buffer, leaving the trailing null terminator intact.
    /// </summary>
    private static void WriteFixedString(nint dest, string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return;
        int n = Math.Min(s.Length, maxChars - 1);
        for (int i = 0; i < n; i++)
            Marshal.WriteInt16(dest + (i * 2), (short)s[i]);
    }

    #endregion

    #region Native Data Object Lifetime

    /// <summary>
    /// Holds an AddRef'd reference to the drag's native data object so DragOver and
    /// the drop-description writer can reach it. Releases any previously held one.
    /// </summary>
    private static void CacheNativeData(DropTargetState s, nint pDataObj)
    {
        if (s.NativeData == pDataObj) return;
        if (s.NativeData != nint.Zero)
        {
            try { _ = ComRelease(s.NativeData); } catch { }
            s.NativeData = nint.Zero;
        }
        if (pDataObj != nint.Zero)
        {
            try
            {
                _ = ComAddRef(pDataObj);
                s.NativeData = pDataObj;
            }
            catch { s.NativeData = nint.Zero; }
        }
    }

    private static void ReleaseNativeData(DropTargetState s)
    {
        if (s.NativeData != nint.Zero)
        {
            try { _ = ComRelease(s.NativeData); } catch { }
            s.NativeData = nint.Zero;
        }
        s.DropDescriptionSet = false;
    }

    private static POINT PointFromLong(long pt) => new POINT
    {
        X = unchecked((int)(pt & 0xFFFFFFFF)),
        Y = unchecked((int)((pt >> 32) & 0xFFFFFFFF)),
    };

    // IUnknown::AddRef / Release and IDataObject::SetData via the raw COM vtable,
    // mirroring the ComGetData convention used for data extraction.
    private static uint ComAddRef(nint pUnk)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint*)(*(nint*)pUnk + 1 * nint.Size));
        return fn(pUnk);
    }

    private static uint ComRelease(nint pUnk)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint*)(*(nint*)pUnk + 2 * nint.Size));
        return fn(pUnk);
    }

    private static int ComSetData(nint pDataObj, FORMATETC* pFmt, STGMEDIUM* pMedium, int fRelease)
    {
        // IDataObject::SetData is vtable index 7.
        var fn = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int, int>)(*(nint*)(*(nint*)pDataObj + 7 * nint.Size));
        return fn(pDataObj, pFmt, pMedium, fRelease);
    }

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint GHND = 0x0042; // GMEM_MOVEABLE | GMEM_ZEROINIT

    private static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    private static readonly Guid IID_IDropTargetHelper = new("4657278B-411B-11D2-839A-00C04FD918D0");

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

        [DllImport("ole32.dll")]
        internal static extern int CoCreateInstance(ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterClipboardFormatW(string lpszFormat);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalFree(nint hMem);

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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);
    }

    #endregion
}
