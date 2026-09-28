using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Threading;
using static Jalium.UI.Interop.Win32.Win32GdiMethods;

namespace Jalium.UI;

/// <summary>
/// AOT-compatible OLE IDropTarget implementation using a hand-built COM vtable
/// and [UnmanagedCallersOnly] callbacks. Receives external drag-and-drop
/// (e.g. files from Windows Explorer) and routes them as Jalium.UI routed events.
/// </summary>
internal static unsafe class OleDropTarget
{
    // Keep all registration dictionaries, GUID parsing, and other static state behind
    // the first actual OLE use. The explicit constructor prevents beforefieldinit from
    // letting NativeAOT realize this type during an otherwise empty Window startup.
    static OleDropTarget()
    {
    }

    // Shared vtable (allocated once, lives for process lifetime)
    private static nint _vtable;

    private const int S_OK = 0;
    private const int E_POINTER = unchecked((int)0x80004003);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int DRAGDROP_E_NOTREGISTERED = unchecked((int)0x80040100);

    [ThreadStatic]
    private static OleApartmentState? t_oleApartment;

    private sealed class OleApartmentState
    {
        internal int LeaseCount;
        internal nint DropHelper;
        internal bool DropHelperTried;
    }

    /// <summary>
    /// One logical OLE consumer on the current UI thread. OleInitialize is called
    /// exactly once for the first consumer and OleUninitialize exactly once after
    /// the last consumer (registered Window or explicit Shell drag) releases it.
    /// </summary>
    internal sealed class OleApartmentLease : IDisposable
    {
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private readonly Dispatcher _ownerDispatcher = Dispatcher.CurrentDispatcher;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId == _ownerThreadId)
            {
                ReleaseOleApartmentOnCurrentThread();
                return;
            }

            // COM normally releases the apartment-bound drop target on its owning
            // STA. Keep the backstop for an unusual cross-thread Release: OLE must
            // still be uninitialized on the exact thread that initialized it.
            try
            {
                _ownerDispatcher.BeginInvoke(ReleaseOleApartmentOnCurrentThread);
            }
            catch
            {
                // Calling OleUninitialize on the wrong thread is worse than retaining
                // the lease until dispatcher shutdown, so intentionally do nothing.
            }
        }
    }

    // Per-window managed state, prevented from GC by a GCHandle stored in the COM object.
    private sealed class DropTargetState
    {
        public Window? Window;
        public UIElement? CurrentTarget;
        public DataObject? CurrentData;
        public nint ComObject;
        public GCHandle SelfHandle;
        public nint Hwnd;
        public OleApartmentLease? OleLease;
        public int RefCount = 1; // owner reference held while present in _states
        public int Destroyed;
        public int RevokeInProgress;
        public bool Registered;
        public bool PendingRevoke;
        public bool Closing;
        public bool DragActive;

        // The originating native IDataObject*, AddRef'd for the duration of a drag
        // so DragOver (which is not handed the data object) can still annotate it
        // with a Shell drop description. Released on DragLeave/Drop.
        public nint NativeData;

        // Whether a handler set a Shell drop description this drag, so it can be
        // cleared when the pointer leaves or the drop completes.
        public bool DropDescriptionSet;
    }

    // Registered clipboard format id for "DropDescription" (lazy).
    private static uint _cfDropDescription;

    private static readonly object _statesGate = new();
    private static readonly Dictionary<nint, DropTargetState> _states = new();

    internal static int CurrentThreadOleLeaseCountForTesting =>
        t_oleApartment?.LeaseCount ?? 0;

    internal static bool IsWindowRegisteredForTesting(nint hwnd)
    {
        lock (_statesGate)
        {
            return _states.TryGetValue(hwnd, out var state) && state.Registered;
        }
    }

    internal static nint GetWindowComObjectForTesting(nint hwnd)
    {
        lock (_statesGate)
        {
            return _states.TryGetValue(hwnd, out var state) ? state.ComObject : nint.Zero;
        }
    }

    /// <summary>
    /// Acquires OLE for one operation on the current thread. A failed OleInitialize
    /// call leaves no latched state, so a later attempt can retry.
    /// </summary>
    internal static OleApartmentLease? TryAcquireOleApartment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        OleApartmentState? apartment = t_oleApartment;
        bool initializedHere = false;
        if (apartment is null)
        {
            int hr = Win32.OleInitialize(nint.Zero);
            if (hr < 0)
            {
                return null;
            }

            initializedHere = true;
            try
            {
                apartment = new OleApartmentState();
                t_oleApartment = apartment;
            }
            catch
            {
                Win32.OleUninitialize();
                throw;
            }
        }

        try
        {
            // Construct first because Dispatcher.CurrentDispatcher may allocate. If
            // that throws after our first OleInitialize, roll the apartment back
            // without ever publishing a phantom lease count.
            var lease = new OleApartmentLease();
            checked
            {
                apartment.LeaseCount++;
            }
            return lease;
        }
        catch
        {
            if (initializedHere && apartment.LeaseCount == 0 &&
                ReferenceEquals(t_oleApartment, apartment))
            {
                t_oleApartment = null;
                Win32.OleUninitialize();
            }
            throw;
        }
    }

    private static void ReleaseOleApartmentOnCurrentThread()
    {
        OleApartmentState? apartment = t_oleApartment;
        if (apartment is null || apartment.LeaseCount <= 0)
        {
            return;
        }

        apartment.LeaseCount--;
        if (apartment.LeaseCount != 0)
        {
            return;
        }

        ReleaseDropHelper(apartment);
        t_oleApartment = null;
        Win32.OleUninitialize();
    }

    #region Registration

    internal static bool RegisterWindow(Window window)
    {
        nint hwnd = window.Handle;
        if (hwnd == nint.Zero)
        {
            return false;
        }

        lock (_statesGate)
        {
            if (_states.TryGetValue(hwnd, out var existing))
            {
                if (existing.Closing)
                {
                    return false;
                }

                existing.Window = window;
                existing.PendingRevoke = false;
                return existing.Registered;
            }
        }

        OleApartmentLease? lease = TryAcquireOleApartment();
        if (lease is null)
        {
            return false;
        }

        DropTargetState? state = null;
        try
        {
            EnsureVtable();

            // COM object layout: [vtable_ptr, gc_handle_intptr]
            var comObj = (nint*)Marshal.AllocHGlobal(nint.Size * 2);
            state = new DropTargetState
            {
                Window = window,
                Hwnd = hwnd,
                ComObject = (nint)comObj,
                OleLease = lease,
            };
            state.SelfHandle = GCHandle.Alloc(state);

            comObj[0] = _vtable;
            comObj[1] = GCHandle.ToIntPtr(state.SelfHandle);

            int hr;
            bool duplicateRegistration;
            bool duplicateRegistered = false;
            lock (_statesGate)
            {
                // HWND ownership is apartment-affine, so two callers should not race in
                // practice. Recheck under the gate to keep duplicate COM blocks impossible
                // even when tests or multiple UI threads call this path concurrently.
                duplicateRegistration = _states.TryGetValue(hwnd, out var existing);
                if (duplicateRegistration)
                {
                    existing!.Window = window;
                    existing.PendingRevoke = false;
                    duplicateRegistered = existing.Registered;
                    hr = S_OK;
                }
                else
                {
                    hr = Win32.RegisterDragDrop(hwnd, (nint)comObj);
                    if (hr == S_OK)
                    {
                        state.Registered = true;
                        _states.Add(hwnd, state);
                    }
                }
            }

            if (duplicateRegistration)
            {
                ReleaseStateReference(state);
                state = null;
                return duplicateRegistered;
            }

            if (hr != S_OK)
            {
                // The owner reference is the only reference left after a failed native
                // registration. Releasing it also balances the apartment lease.
                ReleaseStateReference(state);
                state = null;
                return false;
            }

            AllowElevatedDragDrop(hwnd);
            return true;
        }
        catch
        {
            if (state is not null)
            {
                if (state.Registered)
                {
                    try { _ = Win32.RevokeDragDrop(hwnd); }
                    catch { }
                    state.Registered = false;

                    lock (_statesGate)
                    {
                        if (_states.TryGetValue(hwnd, out var current) &&
                            ReferenceEquals(current, state))
                        {
                            _states.Remove(hwnd);
                        }
                    }
                }

                ReleaseStateReference(state);
            }
            else
            {
                lease.Dispose();
            }
            throw;
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
        => RevokeWindow(window.Handle, nativeWindowAlive: true);

    /// <summary>
    /// Releases the registration keyed by the original HWND. The Window may
    /// already have cleared its public Handle (or Win32 may already have sent
    /// WM_DESTROY), but the managed GCHandle/HGlobal state must still be
    /// reclaimed exactly once.
    /// </summary>
    internal static void RevokeWindow(nint hwnd, bool nativeWindowAlive)
    {
        _ = RequestRevokeWindowCore(hwnd, nativeWindowAlive, closing: true);
    }

    /// <summary>
    /// Requests revocation because a live Window no longer contains an AllowDrop
    /// target. During an active external drag the request is deferred until Leave or
    /// Drop, avoiding a re-entrant revoke while OLE is dispatching target callbacks.
    /// Returns true while a native/managed registration state still remains.
    /// </summary>
    internal static bool RequestRevokeWindow(nint hwnd, bool nativeWindowAlive)
    {
        return RequestRevokeWindowCore(hwnd, nativeWindowAlive, closing: false);
    }

    private static bool RequestRevokeWindowCore(nint hwnd, bool nativeWindowAlive, bool closing)
    {
        if (hwnd == nint.Zero)
        {
            return false;
        }

        DropTargetState? state;
        lock (_statesGate)
        {
            if (!_states.TryGetValue(hwnd, out state))
            {
                return false;
            }

            // Synthetic states used by lifetime tests predate the explicit HWND
            // field. The dictionary key remains the source of truth.
            if (state.Hwnd == nint.Zero)
            {
                state.Hwnd = hwnd;
            }
            state.PendingRevoke = true;
            if (closing)
            {
                state.Closing = true;
                state.Window = null;
            }
        }

        if (!closing && state.DragActive)
        {
            return true;
        }

        return !TryFinalizeRevoke(state, nativeWindowAlive, closing);
    }

    private static bool TryFinalizeRevoke(
        DropTargetState state,
        bool nativeWindowAlive,
        bool closing)
    {
        if (Interlocked.Exchange(ref state.RevokeInProgress, 1) != 0)
        {
            return false;
        }

        bool canReleaseOwner = false;
        try
        {
            if (state.Registered && nativeWindowAlive)
            {
                int hr = Win32.RevokeDragDrop(state.Hwnd);
                if (hr != S_OK && hr != DRAGDROP_E_NOTREGISTERED && !closing)
                {
                    return false;
                }
            }

            state.Registered = false;
            state.PendingRevoke = false;
            state.Window = null;
            ResetDragState(state, dismissShellImage: true);

            lock (_statesGate)
            {
                if (_states.TryGetValue(state.Hwnd, out var current) &&
                    ReferenceEquals(current, state))
                {
                    _states.Remove(state.Hwnd);
                    canReleaseOwner = true;
                }
            }

            return canReleaseOwner;
        }
        finally
        {
            Volatile.Write(ref state.RevokeInProgress, 0);
            if (canReleaseOwner)
            {
                ReleaseStateReference(state);
            }
        }
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
        nint allocated = (nint)vt;
        nint winner = Interlocked.CompareExchange(ref _vtable, allocated, nint.Zero);
        if (winner != nint.Zero)
        {
            Marshal.FreeHGlobal(allocated);
        }
    }

    private static DropTargetState GetState(nint pThis) =>
        (DropTargetState)GCHandle.FromIntPtr(((nint*)pThis)[1]).Target!;

    private static uint AddStateReference(DropTargetState state) =>
        (uint)Interlocked.Increment(ref state.RefCount);

    private static uint ReleaseStateReference(DropTargetState state)
    {
        int remaining = Interlocked.Decrement(ref state.RefCount);
        if (remaining == 0 && Interlocked.Exchange(ref state.Destroyed, 1) == 0)
        {
            ResetDragState(state, dismissShellImage: false);
            state.Window = null;

            nint block = state.ComObject;
            state.ComObject = nint.Zero;
            if (state.SelfHandle.IsAllocated)
            {
                state.SelfHandle.Free();
            }
            if (block != nint.Zero)
            {
                Marshal.FreeHGlobal(block);
            }

            OleApartmentLease? lease = state.OleLease;
            state.OleLease = null;
            lease?.Dispose();
        }

        return (uint)Math.Max(remaining, 0);
    }

    #endregion

    #region IUnknown

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint pThis, Guid* riid, nint* ppv)
    {
        if (riid == null || ppv == null)
        {
            return E_POINTER;
        }

        if (*riid == IID_IUnknown || *riid == IID_IDropTarget)
        {
            *ppv = pThis;
            _ = AddStateReference(GetState(pThis));
            return S_OK;
        }
        *ppv = nint.Zero;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint pThis) => AddStateReference(GetState(pThis));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint pThis) => ReleaseStateReference(GetState(pThis));

    #endregion

    #region IDropTarget

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragEnter(nint pThis, nint pDataObj, uint grfKeyState, long pt, uint* pdwEffect)
    {
        if (pdwEffect == null)
        {
            return E_POINTER;
        }

        DropTargetState? s = null;
        try
        {
            s = GetState(pThis);
            _ = AddStateReference(s); // keep the native block alive through re-entrant Close/Revoke
            if (s.DragActive)
            {
                ResetDragState(s, dismissShellImage: true);
            }

            Window? window = s.Window;
            if (window is null || s.Closing)
            {
                *pdwEffect = 0;
                return S_OK;
            }

            s.DragActive = true;
            CacheNativeData(s, pDataObj);
            s.CurrentData = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = window.HitTest(pos)?.VisualHit as UIElement;
            s.CurrentTarget = DragDropPlatform.FindDropTargetElement(hit);

            if (s.CurrentTarget != null)
            {
                *pdwEffect = MapEffectsBack(RaiseDragEvent(
                    s, s.CurrentTarget, DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent,
                    s.CurrentData, keys, allowed, pos));
            }
            else
            {
                *pdwEffect = 0;
            }

            if (s.Closing || s.Window is null)
            {
                *pdwEffect = 0;
            }
            else
            {
                // Let the Shell render the system drag image over this window.
                if (pDataObj != nint.Zero)
                {
                    ShellHelperDragEnter(s, pDataObj, pt, *pdwEffect);
                }
            }
        }
        catch
        {
            *pdwEffect = 0;
            if (s is not null)
            {
                CompleteDragSession(s, shellImageAlreadyDismissed: false);
            }
        }
        finally
        {
            if (s is not null)
            {
                _ = ReleaseStateReference(s);
            }
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragOver(nint pThis, uint grfKeyState, long pt, uint* pdwEffect)
    {
        if (pdwEffect == null)
        {
            return E_POINTER;
        }

        DropTargetState? s = null;
        try
        {
            s = GetState(pThis);
            _ = AddStateReference(s);
            Window? window = s.Window;
            if (window is null || s.Closing || s.CurrentData == null)
            {
                *pdwEffect = 0;
                return S_OK;
            }

            var pos = PointFromScreen(window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = window.HitTest(pos)?.VisualHit as UIElement;
            var newTarget = DragDropPlatform.FindDropTargetElement(hit);

            if (newTarget != s.CurrentTarget)
            {
                if (s.CurrentTarget != null)
                {
                    RaiseDragEvent(s, s.CurrentTarget, DragDrop.PreviewDragLeaveEvent, DragDrop.DragLeaveEvent,
                        s.CurrentData, keys, allowed, pos);
                }
                s.CurrentTarget = newTarget;
                if (s.CurrentTarget != null)
                {
                    RaiseDragEvent(s, s.CurrentTarget, DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent,
                        s.CurrentData, keys, allowed, pos);
                }
            }

            if (s.CurrentTarget != null)
            {
                *pdwEffect = MapEffectsBack(RaiseDragEvent(
                    s, s.CurrentTarget, DragDrop.PreviewDragOverEvent, DragDrop.DragOverEvent,
                    s.CurrentData, keys, allowed, pos));
            }
            else
            {
                *pdwEffect = 0;
            }

            if (s.Closing || s.Window is null)
            {
                *pdwEffect = 0;
            }
            else
            {
                // Keep the Shell drag image following the pointer with the resolved effect.
                ShellHelperDragOver(pt, *pdwEffect);
            }
        }
        catch { *pdwEffect = 0; }
        finally
        {
            if (s is not null)
            {
                _ = ReleaseStateReference(s);
            }
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDragLeave(nint pThis)
    {
        DropTargetState? s = null;
        try
        {
            s = GetState(pThis);
            _ = AddStateReference(s);
            if (s.CurrentTarget != null && s.CurrentData != null)
            {
                RaiseDragEvent(s, s.CurrentTarget, DragDrop.PreviewDragLeaveEvent, DragDrop.DragLeaveEvent,
                    s.CurrentData, DragDropKeyStates.None, DragDropEffects.None, default);
            }
        }
        catch { }
        finally
        {
            if (s is not null)
            {
                CompleteDragSession(s, shellImageAlreadyDismissed: false);
                _ = ReleaseStateReference(s);
            }
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDrop(nint pThis, nint pDataObj, uint grfKeyState, long pt, uint* pdwEffect)
    {
        if (pdwEffect == null)
        {
            return E_POINTER;
        }

        DropTargetState? s = null;
        bool shellImageDismissed = false;
        try
        {
            s = GetState(pThis);
            _ = AddStateReference(s);
            Window? window = s.Window;
            if (window is null || s.Closing)
            {
                *pdwEffect = 0;
                return S_OK;
            }

            CacheNativeData(s, pDataObj);
            var data = ExtractDataObject(pDataObj);
            var pos = PointFromScreen(window, pt);
            var keys = MapKeyStates(grfKeyState);
            var allowed = MapEffects(*pdwEffect);

            var hit = window.HitTest(pos)?.VisualHit as UIElement;
            var target = DragDropPlatform.FindDropTargetElement(hit);
            if (target is null && s.CurrentTarget is { } previousTarget &&
                DragDrop.GetAllowDrop(previousTarget) &&
                ReferenceEquals(Window.GetWindow(previousTarget), window))
            {
                // A final Drop can arrive without another DragOver. Reuse the previous
                // target only while it is still attached to this Window and still has
                // an effective AllowDrop value; late removal must not receive a drop.
                target = previousTarget;
            }

            if (target != null)
            {
                *pdwEffect = MapEffectsBack(RaiseDragEvent(
                    s, target, DragDrop.PreviewDropEvent, DragDrop.DropEvent,
                    data, keys, allowed, pos));
            }
            else
            {
                *pdwEffect = 0;
            }

            // Dismiss the Shell drag image and release the drag's native data object.
            ShellHelperDrop(pDataObj, pt, *pdwEffect);
            shellImageDismissed = true;
        }
        catch { *pdwEffect = 0; }
        finally
        {
            if (s is not null)
            {
                CompleteDragSession(s, shellImageAlreadyDismissed: shellImageDismissed);
                _ = ReleaseStateReference(s);
            }
        }
        return S_OK;
    }

    private static void CompleteDragSession(
        DropTargetState state,
        bool shellImageAlreadyDismissed)
    {
        ResetDragState(state, dismissShellImage: !shellImageAlreadyDismissed);
        if (state.PendingRevoke && !state.Closing)
        {
            _ = TryFinalizeRevoke(state, nativeWindowAlive: true, closing: false);
        }
    }

    private static void ResetDragState(DropTargetState state, bool dismissShellImage)
    {
        if (dismissShellImage && state.DragActive)
        {
            ShellHelperDragLeaveIfCreated();
        }

        ClearDropDescriptionIfSet(state);
        ReleaseNativeData(state);
        state.CurrentTarget = null;
        state.CurrentData = null;
        state.DragActive = false;
    }

    /// <summary>
    /// Raises an external/OLE drag notification through the same preview-then-bubble
    /// sequence the in-app managed drag path (<see cref="DragDropPlatform"/>) uses: the
    /// tunneling <c>Preview*</c> event first, and — only if no handler marked it
    /// <see cref="RoutedEventArgs.Handled"/> — the bubbling event afterwards. The resolved
    /// <see cref="DragEventArgs.Effects"/> is taken from whichever leg ran last, so a preview
    /// handler that sets an effect and marks the event handled is honored instead of being
    /// overridden by an (unreached) bubble leg. Both legs carry the Shell drop-description
    /// setter so a preview handler can annotate the drag too.
    /// </summary>
    private static DragDropEffects RaiseDragEvent(
        DropTargetState s, UIElement target, RoutedEvent previewEvent, RoutedEvent bubbleEvent,
        DataObject data, DragDropKeyStates keys, DragDropEffects allowed, Point pos)
    {
        var preview = new DragEventArgs(previewEvent, data, keys, allowed, pos)
        {
            DropDescriptionSetter = MakeDropDescriptionSetter(s),
        };
        target.RaiseEvent(preview);
        if (preview.Handled)
            return preview.Effects;

        var bubble = new DragEventArgs(bubbleEvent, data, keys, allowed, pos)
        {
            DropDescriptionSetter = MakeDropDescriptionSetter(s),
        };
        target.RaiseEvent(bubble);
        return bubble.Effects;
    }

    #endregion

    #region Shell Drag Image (IDropTargetHelper)

    /// <summary>
    /// Lazily creates the Shell <c>IDropTargetHelper</c> that renders the system
    /// drag image over the window. The helper is apartment-bound and therefore
    /// lives only until the current UI thread's final OLE lease is released.
    /// </summary>
    private static nint GetDropHelper()
    {
        OleApartmentState? apartment = t_oleApartment;
        if (apartment is null || apartment.LeaseCount == 0)
        {
            return nint.Zero;
        }

        if (apartment.DropHelper != nint.Zero) return apartment.DropHelper;
        if (apartment.DropHelperTried) return nint.Zero;
        apartment.DropHelperTried = true;

        Guid clsid = CLSID_DragDropHelper;
        Guid iid = IID_IDropTargetHelper;
        int hr = Win32.CoCreateInstance(ref clsid, nint.Zero, CLSCTX_INPROC_SERVER, ref iid, out nint p);
        if (hr == 0 && p != nint.Zero)
            apartment.DropHelper = p;
        return apartment.DropHelper;
    }

    private static void ReleaseDropHelper(OleApartmentState apartment)
    {
        nint helper = apartment.DropHelper;
        apartment.DropHelper = nint.Zero;
        apartment.DropHelperTried = false;
        if (helper != nint.Zero)
        {
            try { _ = ComRelease(helper); }
            catch { }
        }
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
            _ = fn(helper, s.Hwnd, pDataObj, &p, effect);
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

    private static void ShellHelperDragLeaveIfCreated()
    {
        nint helper = t_oleApartment?.DropHelper ?? nint.Zero;
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

        nint hMem = GlobalAlloc(GHND, (nuint)size);
        if (hMem == nint.Zero) return;

        nint ptr = GlobalLock(hMem);
        if (ptr == nint.Zero) { _ = GlobalFree(hMem); return; }
        try
        {
            Marshal.WriteInt32(ptr, (int)type);
            WriteFixedString(ptr + sizeof(int), message, MaxPath);
            WriteFixedString(ptr + sizeof(int) + (MaxPath * 2), insert, MaxPath);
        }
        finally { _ = GlobalUnlock(hMem); }

        var fmt = new FORMATETC { cfFormat = (ushort)cf, ptd = nint.Zero, dwAspect = 1, lindex = -1, tymed = 1 };
        var medium = new STGMEDIUM { tymed = 1, unionmember = hMem, pUnkForRelease = nint.Zero };

        int hr = ComSetData(pDataObj, &fmt, &medium, 1 /* fRelease = TRUE */);
        if (hr != 0)
        {
            // SetData failed → ownership was not transferred; free our block.
            _ = GlobalFree(hMem);
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
            var ptr = GlobalLock(medium.unionmember);
            if (ptr != nint.Zero)
            {
                var text = Marshal.PtrToStringUni(ptr);
                GlobalUnlock(medium.unionmember);
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
    private struct POINT { public int X, Y; }

    private static class Win32
    {
        [DllImport("ole32.dll")]
        internal static extern int OleInitialize(nint pvReserved);

        [DllImport("ole32.dll")]
        internal static extern void OleUninitialize();

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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint DragQueryFileW(nint hDrop, uint iFile, char[]? lpszFile, uint cch);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);
    }

    #endregion
}
