using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// AOT-compatible OLE drag <em>source</em>. Unlike the in-app managed drag loop in
/// <see cref="DragDropPlatform"/> (which composites a visual in the window's overlay
/// and never leaves the process), this runs a real Win32 <c>DoDragDrop</c> so the
/// payload can be dropped onto Explorer or any other application. It builds three
/// hand-rolled COM objects — <c>IDataObject</c>, <c>IDropSource</c> and
/// <c>IEnumFORMATETC</c> — from managed vtables and <c>[UnmanagedCallersOnly]</c>
/// callbacks (no runtime COM interop), and optionally hands the caller's drag image
/// to the Shell's <c>IDragSourceHelper</c> so Windows renders the system drag image.
///
/// Reached through <see cref="DragDrop.DoShellDragDrop"/>; the existing in-app path is
/// left untouched.
/// </summary>
internal static unsafe class OleDragSource
{
    #region Public entry point

    /// <summary>
    /// Runs a real OLE drag from <paramref name="data"/>. Must be called on the UI
    /// (OLE-initialized) thread, typically from a mouse handler. Honors
    /// <see cref="DragDrop.PendingDragImage"/> for the Shell drag image.
    /// </summary>
    internal static DragDropEffects DoDragDrop(DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        // The OS drag loop performs its own capture; drop any framework capture first
        // so the two don't fight over the mouse.
        (dragSource as UIElement)?.ReleaseMouseCapture();

        nint pDataObject = CreateDataObject(data);
        if (pDataObject == nint.Zero)
            return DragDropEffects.None;

        nint pDropSource = CreateDropSource();
        if (pDropSource == nint.Zero)
        {
            _ = ObjRelease(pDataObject);
            return DragDropEffects.None;
        }

        // Optional Shell drag image, stashed into the data object before the drag.
        TrySetDragImage(pDataObject, DragDrop.PendingDragImage, DragDrop.PendingDragImageOffset);

        DragDropEffects result = DragDropEffects.None;
        try
        {
            uint effect = 0;
            int hr = Win32.DoDragDrop(pDataObject, pDropSource, (uint)allowedEffects & AllowedMask, &effect);
            // DoDragDrop returns DRAGDROP_S_DROP (not S_OK) on a completed drop, and
            // DRAGDROP_S_CANCEL when the user aborts; anything else is a failure.
            result = hr == DRAGDROP_S_DROP ? (DragDropEffects)(effect & AllowedMask) : DragDropEffects.None;
        }
        finally
        {
            _ = ObjRelease(pDropSource);
            _ = ObjRelease(pDataObject);
        }

        return result;
    }

    // Copy | Move | Link | Scroll — the bits DROPEFFECT and DragDropEffects share.
    private const uint AllowedMask = 0x80000007;

    #endregion

    #region Shared IUnknown ref-counting

    // Each COM object is a native block: [ vtable*, GCHandle-as-intptr ]. The managed
    // state carries the ref count; when it reaches zero the block and handle are freed.
    private abstract class ComState
    {
        public nint ComObject;
        public GCHandle SelfHandle;
        public int RefCount = 1;

        // Overridden by the data object to release the HGLOBAL/STGMEDIUM store it owns.
        public virtual void OnZeroRef() { }
    }

    private static T GetState<T>(nint pThis) where T : ComState =>
        (T)GCHandle.FromIntPtr(((nint*)pThis)[1]).Target!;

    private static nint AllocComObject(nint vtable, ComState state, int slots = 2)
    {
        var comObj = (nint*)Marshal.AllocHGlobal(nint.Size * slots);
        state.ComObject = (nint)comObj;
        state.SelfHandle = GCHandle.Alloc(state);
        comObj[0] = vtable;
        comObj[1] = GCHandle.ToIntPtr(state.SelfHandle);
        return (nint)comObj;
    }

    private static uint CommonAddRef(ComState s) => (uint)Interlocked.Increment(ref s.RefCount);

    private static uint CommonRelease(ComState s)
    {
        int n = Interlocked.Decrement(ref s.RefCount);
        if (n == 0)
        {
            s.OnZeroRef();
            nint block = s.ComObject;
            s.ComObject = nint.Zero;
            if (s.SelfHandle.IsAllocated) s.SelfHandle.Free();
            if (block != nint.Zero) Marshal.FreeHGlobal(block);
        }
        return (uint)Math.Max(n, 0);
    }

    // Calls IUnknown::Release on a raw COM pointer we own.
    private static uint ObjRelease(nint pUnk)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint*)(*(nint*)pUnk + 2 * nint.Size));
        return fn(pUnk);
    }

    #endregion

    #region IDataObject

    private sealed class DataObjectState : ComState
    {
        // Every format we can serve or that the Shell parked on us, each owning its medium.
        public readonly List<StoredMedium> Formats = new();

        public override void OnZeroRef()
        {
            foreach (var m in Formats)
                m.Release();
            Formats.Clear();
        }
    }

    private sealed class StoredMedium
    {
        public ushort Cf;
        public uint Aspect;
        public int Lindex;
        public uint Tymed;
        public nint Handle;          // hGlobal (TYMED_HGLOBAL) or other medium handle
        public nint UnkForRelease;

        public STGMEDIUM ToMedium() => new() { tymed = Tymed, unionmember = Handle, pUnkForRelease = UnkForRelease };

        public void Release()
        {
            var m = ToMedium();
            Win32.ReleaseStgMedium(&m);
            Handle = nint.Zero;
            UnkForRelease = nint.Zero;
        }
    }

    private static nint _dataVtable;

    private static nint CreateDataObject(IDataObject data)
    {
        EnsureDataVtable();
        var state = new DataObjectState();

        // Seed the store with the payload the drop target will read.
        if (TryGetString(data, out string? text) && text != null)
            AddOwnedGlobal(state, CF_UNICODETEXT, BuildUnicodeText(text));

        if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            AddOwnedGlobal(state, CF_HDROP, BuildHDrop(files));

        return AllocComObject(_dataVtable, state);
    }

    private static void EnsureDataVtable()
    {
        if (_dataVtable != nint.Zero) return;
        var vt = (nint*)Marshal.AllocHGlobal(nint.Size * 12);
        vt[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&DO_QueryInterface;
        vt[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&DO_AddRef;
        vt[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&DO_Release;
        vt[3] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)&DO_GetData;
        vt[4] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)&DO_GetDataHere;
        vt[5] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, int>)&DO_QueryGetData;
        vt[6] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, FORMATETC*, int>)&DO_GetCanonicalFormatEtc;
        vt[7] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int, int>)&DO_SetData;
        vt[8] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)&DO_EnumFormatEtc;
        vt[9] = (nint)(delegate* unmanaged[Stdcall]<nint, FORMATETC*, uint, nint, uint*, int>)&DO_DAdvise;
        vt[10] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&DO_DUnadvise;
        vt[11] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&DO_EnumDAdvise;
        _dataVtable = (nint)vt;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_QueryInterface(nint pThis, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IDataObject)
        {
            *ppv = pThis;
            _ = CommonAddRef(GetState<DataObjectState>(pThis));
            return S_OK;
        }
        *ppv = nint.Zero;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint DO_AddRef(nint pThis) => CommonAddRef(GetState<DataObjectState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint DO_Release(nint pThis) => CommonRelease(GetState<DataObjectState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_GetData(nint pThis, FORMATETC* pFmt, STGMEDIUM* pMedium)
    {
        try
        {
            var s = GetState<DataObjectState>(pThis);
            var e = Find(s, pFmt);
            if (e == null) return DV_E_FORMATETC;
            if ((pFmt->tymed & TYMED_HGLOBAL) == 0 || e.Tymed != TYMED_HGLOBAL) return DV_E_TYMED;

            nint dup = DuplicateGlobal(e.Handle);
            if (dup == nint.Zero) return E_OUTOFMEMORY;

            pMedium->tymed = TYMED_HGLOBAL;
            pMedium->unionmember = dup;
            pMedium->pUnkForRelease = nint.Zero;
            return S_OK;
        }
        catch { return E_UNEXPECTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_GetDataHere(nint pThis, FORMATETC* pFmt, STGMEDIUM* pMedium) => E_NOTIMPL;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_QueryGetData(nint pThis, FORMATETC* pFmt)
    {
        try
        {
            var s = GetState<DataObjectState>(pThis);
            var e = Find(s, pFmt);
            if (e == null) return DV_E_FORMATETC;
            if ((pFmt->tymed & TYMED_HGLOBAL) == 0) return DV_E_TYMED;
            return S_OK;
        }
        catch { return E_UNEXPECTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_GetCanonicalFormatEtc(nint pThis, FORMATETC* pIn, FORMATETC* pOut)
    {
        // We do not use target devices; the format is already canonical.
        if (pOut != null)
        {
            if (pIn != null) *pOut = *pIn;
            pOut->ptd = nint.Zero;
        }
        return DATA_S_SAMEFORMATETC;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_SetData(nint pThis, FORMATETC* pFmt, STGMEDIUM* pMedium, int fRelease)
    {
        try
        {
            var s = GetState<DataObjectState>(pThis);
            if (pFmt == null || pMedium == null) return E_INVALIDARG;

            // Replace any existing entry for this exact format.
            RemoveMatching(s, pFmt);

            var entry = new StoredMedium
            {
                Cf = pFmt->cfFormat,
                Aspect = pFmt->dwAspect,
                Lindex = pFmt->lindex,
                Tymed = pMedium->tymed,
            };

            if (fRelease != 0)
            {
                // Ownership transfers to us.
                entry.Handle = pMedium->unionmember;
                entry.UnkForRelease = pMedium->pUnkForRelease;
            }
            else if (pMedium->tymed == TYMED_HGLOBAL)
            {
                // Must not take the caller's handle — keep a private copy.
                entry.Handle = DuplicateGlobal(pMedium->unionmember);
                if (entry.Handle == nint.Zero) return E_OUTOFMEMORY;
            }
            else
            {
                // Non-HGLOBAL without release: duplicate handle-based mediums so we
                // own our copy. Never store the caller's handle — it may free it out
                // from under us (use-after-free). Unsupported mediums are declined.
                nint dup = Win32.OleDuplicateData(pMedium->unionmember, pFmt->cfFormat, 0);
                if (dup == nint.Zero) return E_NOTIMPL;
                entry.Handle = dup;
                entry.UnkForRelease = nint.Zero;
            }

            s.Formats.Add(entry);
            return S_OK;
        }
        catch { return E_UNEXPECTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_EnumFormatEtc(nint pThis, uint dwDirection, nint* ppenum)
    {
        try
        {
            if (ppenum == null) return E_INVALIDARG;
            *ppenum = nint.Zero;
            if (dwDirection != DATADIR_GET) return E_NOTIMPL; // no SetData enumeration

            var s = GetState<DataObjectState>(pThis);
            var snapshot = new FORMATETC[s.Formats.Count];
            for (int i = 0; i < s.Formats.Count; i++)
            {
                var f = s.Formats[i];
                snapshot[i] = new FORMATETC { cfFormat = f.Cf, ptd = nint.Zero, dwAspect = f.Aspect, lindex = f.Lindex, tymed = f.Tymed };
            }

            *ppenum = CreateEnumFormatEtc(snapshot, 0);
            return *ppenum != nint.Zero ? S_OK : E_OUTOFMEMORY;
        }
        catch { return E_UNEXPECTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_DAdvise(nint pThis, FORMATETC* pFmt, uint advf, nint pSink, uint* pdwConn) => OLE_E_ADVISENOTSUPPORTED;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_DUnadvise(nint pThis, uint dwConn) => OLE_E_ADVISENOTSUPPORTED;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DO_EnumDAdvise(nint pThis, nint* ppenum) => OLE_E_ADVISENOTSUPPORTED;

    private static StoredMedium? Find(DataObjectState s, FORMATETC* pFmt)
    {
        foreach (var e in s.Formats)
        {
            if (e.Cf == pFmt->cfFormat && (e.Aspect & pFmt->dwAspect) != 0 &&
                (e.Lindex == pFmt->lindex || pFmt->lindex == -1 || e.Lindex == -1) &&
                (e.Tymed & pFmt->tymed) != 0)
                return e;
        }
        return null;
    }

    private static void RemoveMatching(DataObjectState s, FORMATETC* pFmt)
    {
        for (int i = s.Formats.Count - 1; i >= 0; i--)
        {
            var e = s.Formats[i];
            if (e.Cf == pFmt->cfFormat && e.Aspect == pFmt->dwAspect && e.Lindex == pFmt->lindex)
            {
                e.Release();
                s.Formats.RemoveAt(i);
            }
        }
    }

    private static void AddOwnedGlobal(DataObjectState s, ushort cf, nint hGlobal)
    {
        if (hGlobal == nint.Zero) return;
        s.Formats.Add(new StoredMedium
        {
            Cf = cf,
            Aspect = DVASPECT_CONTENT,
            Lindex = -1,
            Tymed = TYMED_HGLOBAL,
            Handle = hGlobal,
            UnkForRelease = nint.Zero,
        });
    }

    #endregion

    #region IEnumFORMATETC

    private sealed class EnumState : ComState
    {
        public required FORMATETC[] Items;
        public int Index;
    }

    private static nint _enumVtable;

    private static nint CreateEnumFormatEtc(FORMATETC[] items, int index)
    {
        EnsureEnumVtable();
        var state = new EnumState { Items = items, Index = index };
        return AllocComObject(_enumVtable, state);
    }

    private static void EnsureEnumVtable()
    {
        if (_enumVtable != nint.Zero) return;
        var vt = (nint*)Marshal.AllocHGlobal(nint.Size * 7);
        vt[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&EN_QueryInterface;
        vt[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&EN_AddRef;
        vt[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&EN_Release;
        vt[3] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, FORMATETC*, uint*, int>)&EN_Next;
        vt[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&EN_Skip;
        vt[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&EN_Reset;
        vt[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&EN_Clone;
        _enumVtable = (nint)vt;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EN_QueryInterface(nint pThis, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IEnumFORMATETC)
        {
            *ppv = pThis;
            _ = CommonAddRef(GetState<EnumState>(pThis));
            return S_OK;
        }
        *ppv = nint.Zero;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint EN_AddRef(nint pThis) => CommonAddRef(GetState<EnumState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint EN_Release(nint pThis) => CommonRelease(GetState<EnumState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EN_Next(nint pThis, uint celt, FORMATETC* rgelt, uint* pceltFetched)
    {
        try
        {
            var s = GetState<EnumState>(pThis);
            uint fetched = 0;
            while (fetched < celt && s.Index < s.Items.Length)
            {
                rgelt[fetched] = s.Items[s.Index];
                rgelt[fetched].ptd = nint.Zero;
                fetched++;
                s.Index++;
            }
            if (pceltFetched != null) *pceltFetched = fetched;
            return fetched == celt ? S_OK : S_FALSE;
        }
        catch { return E_UNEXPECTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EN_Skip(nint pThis, uint celt)
    {
        var s = GetState<EnumState>(pThis);
        long next = (long)s.Index + celt;
        if (next > s.Items.Length) { s.Index = s.Items.Length; return S_FALSE; }
        s.Index = (int)next;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EN_Reset(nint pThis)
    {
        GetState<EnumState>(pThis).Index = 0;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EN_Clone(nint pThis, nint* ppenum)
    {
        try
        {
            if (ppenum == null) return E_INVALIDARG;
            var s = GetState<EnumState>(pThis);
            *ppenum = CreateEnumFormatEtc(s.Items, s.Index);
            return *ppenum != nint.Zero ? S_OK : E_OUTOFMEMORY;
        }
        catch { return E_UNEXPECTED; }
    }

    #endregion

    #region IDropSource

    private sealed class DropSourceState : ComState { }

    private static nint _dropSourceVtable;

    private static nint CreateDropSource()
    {
        EnsureDropSourceVtable();
        return AllocComObject(_dropSourceVtable, new DropSourceState());
    }

    private static void EnsureDropSourceVtable()
    {
        if (_dropSourceVtable != nint.Zero) return;
        var vt = (nint*)Marshal.AllocHGlobal(nint.Size * 5);
        vt[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&DS_QueryInterface;
        vt[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&DS_AddRef;
        vt[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&DS_Release;
        vt[3] = (nint)(delegate* unmanaged[Stdcall]<nint, int, uint, int>)&DS_QueryContinueDrag;
        vt[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&DS_GiveFeedback;
        _dropSourceVtable = (nint)vt;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DS_QueryInterface(nint pThis, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IDropSource)
        {
            *ppv = pThis;
            _ = CommonAddRef(GetState<DropSourceState>(pThis));
            return S_OK;
        }
        *ppv = nint.Zero;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint DS_AddRef(nint pThis) => CommonAddRef(GetState<DropSourceState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint DS_Release(nint pThis) => CommonRelease(GetState<DropSourceState>(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DS_QueryContinueDrag(nint pThis, int fEscapePressed, uint grfKeyState)
    {
        if (fEscapePressed != 0) return DRAGDROP_S_CANCEL;
        // Drop once the mouse buttons that could drive the drag are all released.
        if ((grfKeyState & (MK_LBUTTON | MK_RBUTTON | MK_MBUTTON)) == 0) return DRAGDROP_S_DROP;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DS_GiveFeedback(nint pThis, uint dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;

    #endregion

    #region Shell drag image (IDragSourceHelper)

    /// <summary>
    /// Converts the caller's drag image to an HBITMAP and hands it to the Shell's
    /// <c>IDragSourceHelper</c>, which parks the bits on <paramref name="pDataObject"/>
    /// so Windows renders the system drag image. Silently does nothing when there is
    /// no image or the helper is unavailable.
    /// </summary>
    private static void TrySetDragImage(nint pDataObject, object? image, Point offset)
    {
        if (image == null) return;
        if (!TryGetBgraPixels(image, out byte[]? pixels, out int w, out int h) || pixels == null)
            return;

        nint hBitmap = CreatePremultipliedHBitmap(pixels, w, h);
        if (hBitmap == nint.Zero) return;

        nint helper = nint.Zero;
        bool ownershipTaken = false;
        try
        {
            Guid clsid = CLSID_DragDropHelper;
            Guid iid = IID_IDragSourceHelper;
            int hr = Win32.CoCreateInstance(ref clsid, nint.Zero, CLSCTX_INPROC_SERVER, ref iid, out helper);
            if (hr != 0 || helper == nint.Zero) return;

            var shdi = new SHDRAGIMAGE
            {
                sizeX = w,
                sizeY = h,
                ptOffsetX = (int)offset.X,
                ptOffsetY = (int)offset.Y,
                hbmpDragImage = hBitmap,
                crColorKey = 0xFFFFFFFF, // CLR_NONE — use the per-pixel alpha channel
            };

            // IDragSourceHelper::InitializeFromBitmap is vtable index 3.
            var fn = (delegate* unmanaged[Stdcall]<nint, SHDRAGIMAGE*, nint, int>)(*(nint*)(*(nint*)helper + 3 * nint.Size));
            int ihr = fn(helper, &shdi, pDataObject);
            // On success the helper owns the bitmap (it lives on the data object).
            ownershipTaken = ihr == 0;
        }
        catch { }
        finally
        {
            if (helper != nint.Zero) _ = ObjRelease(helper);
            if (!ownershipTaken) _ = Win32.DeleteObject(hBitmap);
        }
    }

    private static bool TryGetBgraPixels(object image, out byte[]? pixels, out int width, out int height)
    {
        pixels = null; width = 0; height = 0;

        // Fast path: BitmapImage already holds straight BGRA.
        if (image is BitmapImage bi && bi.RawPixelData is { Length: > 0 } raw)
        {
            width = bi.PixelWidth;
            height = bi.PixelHeight;
            if (width <= 0 || height <= 0) return false;

            int dstStride = width * 4;
            // Use the authoritative row stride — WIC-decoded images can pad rows,
            // so inferring it from the buffer length would shear the drag image.
            int srcStride = bi.PixelStride > 0 ? bi.PixelStride : dstStride;
            if (raw.Length < (long)srcStride * height) return false;

            if (srcStride == dstStride)
            {
                pixels = raw;
            }
            else
            {
                pixels = new byte[dstStride * height];
                for (int y = 0; y < height; y++)
                    Array.Copy(raw, y * srcStride, pixels, y * dstStride, dstStride);
            }
            return true;
        }

        // General path: any bitmap source that can copy pixels.
        if (image is BitmapSource bs)
        {
            width = bs.PixelWidth;
            height = bs.PixelHeight;
            if (width <= 0 || height <= 0) return false;
            int stride = width * 4;
            var buf = new byte[stride * height];
            try { bs.CopyPixels(buf, stride, 0); }
            catch { return false; }
            pixels = buf;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a top-down 32bpp DIB section from straight BGRA pixels, premultiplying
    /// the color channels so the Shell's alpha-blended drag image renders correctly.
    /// </summary>
    private static nint CreatePremultipliedHBitmap(byte[] bgra, int width, int height)
    {
        var bmi = new BITMAPINFOHEADER
        {
            biSize = 40,
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        nint bits;
        nint hbmp = Win32.CreateDIBSection(nint.Zero, &bmi, 0 /* DIB_RGB_COLORS */, out bits, nint.Zero, 0);
        if (hbmp == nint.Zero || bits == nint.Zero)
        {
            if (hbmp != nint.Zero) _ = Win32.DeleteObject(hbmp);
            return nint.Zero;
        }

        int stride = width * 4;
        var dst = (byte*)bits;
        for (int i = 0; i + 3 < bgra.Length && i + 3 < stride * height; i += 4)
        {
            byte b = bgra[i + 0];
            byte g = bgra[i + 1];
            byte r = bgra[i + 2];
            byte a = bgra[i + 3];
            dst[i + 0] = (byte)(b * a / 255);
            dst[i + 1] = (byte)(g * a / 255);
            dst[i + 2] = (byte)(r * a / 255);
            dst[i + 3] = a;
        }
        return hbmp;
    }

    #endregion

    #region HGLOBAL builders

    private static bool TryGetString(IDataObject data, out string? text)
    {
        text = data.GetData(DataFormats.UnicodeText) as string
            ?? data.GetData(DataFormats.Text) as string;
        return text != null;
    }

    private static nint BuildUnicodeText(string text)
    {
        int bytes = (text.Length + 1) * 2; // + null terminator
        nint h = Win32.GlobalAlloc(GHND, (nuint)bytes);
        if (h == nint.Zero) return nint.Zero;
        nint p = Win32.GlobalLock(h);
        if (p == nint.Zero) { _ = Win32.GlobalFree(h); return nint.Zero; }
        try
        {
            for (int i = 0; i < text.Length; i++)
                Marshal.WriteInt16(p + i * 2, (short)text[i]);
            Marshal.WriteInt16(p + text.Length * 2, 0);
        }
        finally { _ = Win32.GlobalUnlock(h); }
        return h;
    }

    private static nint BuildHDrop(string[] files)
    {
        // DROPFILES header + double-null-terminated wide path list.
        int header = sizeof(uint) + (sizeof(int) * 2) + (sizeof(int) * 2); // pFiles, pt.x/y, fNC, fWide
        int chars = 1; // trailing extra null
        foreach (var f in files) chars += f.Length + 1;
        int total = header + chars * 2;

        nint h = Win32.GlobalAlloc(GHND, (nuint)total);
        if (h == nint.Zero) return nint.Zero;
        nint p = Win32.GlobalLock(h);
        if (p == nint.Zero) { _ = Win32.GlobalFree(h); return nint.Zero; }
        try
        {
            Marshal.WriteInt32(p + 0, header);   // pFiles = offset to the list
            Marshal.WriteInt32(p + 4, 0);        // pt.x
            Marshal.WriteInt32(p + 8, 0);        // pt.y
            Marshal.WriteInt32(p + 12, 0);       // fNC = FALSE
            Marshal.WriteInt32(p + 16, 1);       // fWide = TRUE

            nint cur = p + header;
            foreach (var f in files)
            {
                for (int i = 0; i < f.Length; i++)
                {
                    Marshal.WriteInt16(cur, (short)f[i]);
                    cur += 2;
                }
                Marshal.WriteInt16(cur, 0); // path terminator
                cur += 2;
            }
            Marshal.WriteInt16(cur, 0); // list terminator
        }
        finally { _ = Win32.GlobalUnlock(h); }
        return h;
    }

    private static nint DuplicateGlobal(nint src)
    {
        if (src == nint.Zero) return nint.Zero;
        nuint size = Win32.GlobalSize(src);
        if (size == 0) return nint.Zero;

        nint dst = Win32.GlobalAlloc(GHND, size);
        if (dst == nint.Zero) return nint.Zero;

        nint ps = Win32.GlobalLock(src);
        nint pd = Win32.GlobalLock(dst);
        if (ps == nint.Zero || pd == nint.Zero)
        {
            if (ps != nint.Zero) _ = Win32.GlobalUnlock(src);
            if (pd != nint.Zero) _ = Win32.GlobalUnlock(dst);
            _ = Win32.GlobalFree(dst);
            return nint.Zero;
        }
        try { Buffer.MemoryCopy((void*)ps, (void*)pd, (long)size, (long)size); }
        finally { _ = Win32.GlobalUnlock(src); _ = Win32.GlobalUnlock(dst); }
        return dst;
    }

    #endregion

    #region Constants, structs & P/Invoke

    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    private const int DATA_S_SAMEFORMATETC = 0x00040130;
    private const int DRAGDROP_S_DROP = 0x00040100;
    private const int DRAGDROP_S_CANCEL = 0x00040101;
    private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

    private const ushort CF_UNICODETEXT = 13;
    private const ushort CF_HDROP = 15;
    private const uint DVASPECT_CONTENT = 1;
    private const uint TYMED_HGLOBAL = 1;
    private const uint DATADIR_GET = 1;
    private const uint GHND = 0x0042; // GMEM_MOVEABLE | GMEM_ZEROINIT
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint MK_LBUTTON = 0x0001;
    private const uint MK_RBUTTON = 0x0002;
    private const uint MK_MBUTTON = 0x0010;

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropSource = new("00000121-0000-0000-C000-000000000046");
    private static readonly Guid IID_IEnumFORMATETC = new("00000103-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDragSourceHelper = new("DE5BF786-477A-11D2-839D-00C04FD918D0");
    private static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");

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
    private struct SHDRAGIMAGE
    {
        public int sizeX;      // SIZE.cx
        public int sizeY;      // SIZE.cy
        public int ptOffsetX;  // POINT.x
        public int ptOffsetY;  // POINT.y
        public nint hbmpDragImage;
        public uint crColorKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    private static class Win32
    {
        [DllImport("ole32.dll")]
        internal static extern int DoDragDrop(nint pDataObj, nint pDropSource, uint dwOKEffects, uint* pdwEffect);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(STGMEDIUM* pmedium);

        [DllImport("ole32.dll")]
        internal static extern int CoCreateInstance(ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

        [DllImport("ole32.dll")]
        internal static extern nint OleDuplicateData(nint hSrc, ushort cfFormat, uint uiFlags);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalFree(nint hMem);

        [DllImport("kernel32.dll")]
        internal static extern nint GlobalLock(nint hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(nint hMem);

        [DllImport("kernel32.dll")]
        internal static extern nuint GlobalSize(nint hMem);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint ho);
    }

    #endregion
}
