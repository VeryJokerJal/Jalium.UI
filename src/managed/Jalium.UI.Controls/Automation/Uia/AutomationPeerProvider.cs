using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// UIA provider wrapping an AutomationPeer, exposed to native UI Automation as a
/// source-generated COM object (<see cref="GeneratedComClassAttribute"/>). Implements
/// IRawElementProviderSimple, IRawElementProviderFragment, and
/// IRawElementProviderFragmentRoot, so the StrategyBasedComWrappers CCW answers
/// QueryInterface for all three sibling UIA provider IIDs.
/// </summary>
/// <remarks>
/// <para>
/// StandardOleMarshalObject is intentionally NOT a base class here: under ComWrappers the
/// CCW is non-agile by default and exposes no IMarshal, and StandardOleMarshalObject would
/// be inert. Thread affinity is enforced by <see cref="ProviderOptions.UseComThreading"/> +
/// the UI thread being an STA (Jalium calls OleInitialize on it) — the same posture WinForms
/// and WPF use for their AOT UIA providers. Do not add an IMarshal or a free-threaded
/// marshaler: this provider reads UI-thread-only peer/layout state, so a free-threaded CCW
/// would let UIA call it concurrently off-thread and race the layout tree.
/// </para>
/// <para>
/// The members below are the COM vtable surface UIA invokes on this provider. They
/// intentionally do not re-check <see cref="UiaAccessibilityBridge.IsComInteropUnavailable"/>
/// nor guard their managed→COM returns: (1) they are reachable only after the root provider
/// was already marshalled to UIA, which proves the runtime can build wrappers; and (2) a
/// managed exception thrown while UIA calls into this CCW is converted to an HRESULT at the
/// interop boundary, so it cannot crash the process. Only the outbound calls this framework
/// initiates (in <see cref="UiaAccessibilityBridge"/>) need the latch + try/catch.
/// </para>
/// </remarks>
[GeneratedComClass]
internal sealed partial class AutomationPeerProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot
{
    private readonly AutomationPeer _peer;
    private readonly bool _isRoot;
    private readonly int _runtimeId;
    private nint _hwnd;
    private nint _providerPointer;

    internal AutomationPeerProvider(AutomationPeer peer, nint hwnd)
    {
        _peer = peer;
        _hwnd = hwnd;
        _isRoot = peer.GetAutomationControlType() == AutomationControlType.Window;
        _runtimeId = RuntimeHelpers.GetHashCode(peer);
    }

    internal AutomationPeer Peer => _peer;
    internal nint Hwnd => Volatile.Read(ref _hwnd);
    internal int[] GetRuntimeIdArray() => [UiaConstants.AppendRuntimeId, _runtimeId];

    internal void EnsureHwnd(nint hwnd)
    {
        if (hwnd == nint.Zero)
            return;

        _ = Interlocked.CompareExchange(ref _hwnd, hwnd, nint.Zero);
    }

    internal nint GetOrCreateProviderPointer()
    {
        var current = Volatile.Read(ref _providerPointer);
        if (current != nint.Zero)
        {
            UiaTrace.Log($"ProviderPointer reuse peer={PeerLabel()} p=0x{FormatPointer(current)} hwnd=0x{FormatPointer(_hwnd)}");
            return current;
        }

        var created = UiaComInterop.ProviderToPointer(this);
        var prior = Interlocked.CompareExchange(ref _providerPointer, created, nint.Zero);
        if (prior == nint.Zero)
        {
            UiaTrace.Log($"ProviderPointer create peer={PeerLabel()} p=0x{FormatPointer(created)} hwnd=0x{FormatPointer(_hwnd)}");
            return created;
        }

        UiaComInterop.FreeProviderPointer(created);
        UiaTrace.Log($"ProviderPointer lost-race peer={PeerLabel()} created=0x{FormatPointer(created)} existing=0x{FormatPointer(prior)}");
        return prior;
    }

    internal void ReleaseProviderPointer()
    {
        var pointer = Interlocked.Exchange(ref _providerPointer, nint.Zero);
        UiaTrace.Log($"ProviderPointer release peer={PeerLabel()} p=0x{FormatPointer(pointer)} hwnd=0x{FormatPointer(_hwnd)}");
        UiaComInterop.FreeProviderPointer(pointer);
    }

    internal void ReleaseProviderPointerForWindow(nint hwnd)
    {
        ReleaseProviderPointer();
        _ = Interlocked.CompareExchange(ref _hwnd, nint.Zero, hwnd);
    }

    // ========================================================================
    // IRawElementProviderSimple
    // ========================================================================

    // PreserveSig: return the HRESULT ourselves so a NULL pRetVal (some IME/TSF UIA clients pass
    // one while the UI thread is in an input-synchronous message) yields E_POINTER instead of a
    // first-chance NullReferenceException from the generated write-back. See IRawElementProviderSimple.
    public unsafe int get_ProviderOptions(ProviderOptions* pRetVal)
    {
        if (!IsWritableOutPointer(pRetVal, sizeof(ProviderOptions)))
        {
            UiaTrace.Log($"get_ProviderOptions E_POINTER peer={PeerLabel()} pRetVal=0x{FormatPointer(pRetVal)}");
            return unchecked((int)0x80004003); // E_POINTER
        }

        *pRetVal = ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;
        UiaTrace.Log($"get_ProviderOptions S_OK peer={PeerLabel()} pRetVal=0x{FormatPointer(pRetVal)}");
        return 0; // S_OK
    }

    public unsafe int GetPatternProvider(int patternId, nint* pRetVal)
    {
        if (!IsWritableOutPointer(pRetVal, sizeof(nint)))
        {
            UiaTrace.Log($"GetPatternProvider E_POINTER peer={PeerLabel()} pattern=0x{patternId:X8} pRetVal=0x{FormatPointer(pRetVal)}");
            return unchecked((int)0x80004003); // E_POINTER
        }

        *pRetVal = nint.Zero;
        try
        {
            var patternInterface = UiaConstants.MapUiaPatternIdToPatternInterface(patternId);
            if (patternInterface == null)
            {
                UiaTrace.Log($"GetPatternProvider S_OK unsupported-pattern peer={PeerLabel()} pattern=0x{patternId:X8}");
                return 0;
            }

            var pattern = _peer.GetPattern(patternInterface.Value);
            if (pattern == null)
            {
                UiaTrace.Log($"GetPatternProvider S_OK no-provider peer={PeerLabel()} pattern={patternInterface.Value} id=0x{patternId:X8}");
                return 0;
            }

            *pRetVal = WrapPatternToPointer(patternId, pattern);
            UiaTrace.Log($"GetPatternProvider S_OK peer={PeerLabel()} pattern={patternInterface.Value} id=0x{patternId:X8} ret=0x{FormatPointer(*pRetVal)}");
            return 0;
        }
        catch (Exception ex)
        {
            *pRetVal = nint.Zero;
            var hr = Marshal.GetHRForException(ex);
            UiaTrace.Log($"GetPatternProvider HR=0x{hr:X8} peer={PeerLabel()} pattern=0x{patternId:X8} ex={ex.GetType().Name}: {ex.Message}");
            return hr;
        }
    }

    private static unsafe bool IsWritableOutPointer<T>(T* pointer, int byteCount) where T : unmanaged
    {
        if (pointer == null)
            return false;

        if (!OperatingSystem.IsWindows())
            return true;

        if (byteCount <= 0)
            return false;

        nuint current = (nuint)pointer;
        nuint remaining = (nuint)byteCount;

        while (remaining > 0)
        {
            if (UiaNativeMethods.VirtualQuery(
                    (nint)current,
                    out var info,
                    (nuint)Marshal.SizeOf<UiaNativeMethods.MEMORY_BASIC_INFORMATION>()) == 0)
            {
                return false;
            }

            if (!IsWritableMemory(info))
                return false;

            nuint baseAddress = (nuint)info.BaseAddress;
            nuint regionSize = info.RegionSize;
            if (regionSize == 0 || current < baseAddress)
                return false;

            nuint offset = current - baseAddress;
            if (offset >= regionSize)
                return false;

            nuint available = regionSize - offset;
            if (available >= remaining)
                return true;

            if (available > nuint.MaxValue - current)
                return false;

            current += available;
            remaining -= available;
        }

        return true;
    }

    private static bool IsWritableMemory(UiaNativeMethods.MEMORY_BASIC_INFORMATION info)
    {
        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_GUARD = 0x100;
        const uint PAGE_READWRITE = 0x04;
        const uint PAGE_WRITECOPY = 0x08;
        const uint PAGE_EXECUTE_READWRITE = 0x40;
        const uint PAGE_EXECUTE_WRITECOPY = 0x80;

        if (info.State != MEM_COMMIT || (info.Protect & PAGE_GUARD) != 0)
            return false;

        return (info.Protect & 0xff) is PAGE_READWRITE or PAGE_WRITECOPY
            or PAGE_EXECUTE_READWRITE or PAGE_EXECUTE_WRITECOPY;
    }

    public unsafe int GetPropertyValue(int propertyId, UiaVariant* pRetVal)
    {
        if (!IsWritableOutPointer(pRetVal, sizeof(UiaVariant)))
        {
            UiaTrace.Log($"GetPropertyValue E_POINTER peer={PeerLabel()} property=0x{propertyId:X8} pRetVal=0x{FormatPointer(pRetVal)}");
            return unchecked((int)0x80004003); // E_POINTER
        }

        *pRetVal = default;
        try
        {
            *pRetVal = UiaVariant.From(GetPropertyValueCore(propertyId));
            UiaTrace.Log($"GetPropertyValue S_OK peer={PeerLabel()} property=0x{propertyId:X8} vt={pRetVal->vt}");
            return 0;
        }
        catch (Exception ex)
        {
            *pRetVal = default;
            var hr = Marshal.GetHRForException(ex);
            UiaTrace.Log($"GetPropertyValue HR=0x{hr:X8} peer={PeerLabel()} property=0x{propertyId:X8} ex={ex.GetType().Name}: {ex.Message}");
            return hr;
        }
    }

    private object? GetPropertyValueCore(int propertyId) => propertyId switch
    {
        UiaConstants.UIA_ControlTypePropertyId => UiaConstants.MapControlType(_peer.GetAutomationControlType()),
        UiaConstants.UIA_NamePropertyId => NullIfEmpty(_peer.GetName()),
        UiaConstants.UIA_AutomationIdPropertyId => NullIfEmpty(_peer.GetAutomationId()),
        UiaConstants.UIA_ClassNamePropertyId => NullIfEmpty(_peer.GetClassName()),
        UiaConstants.UIA_HelpTextPropertyId => NullIfEmpty(_peer.GetHelpText()),
        UiaConstants.UIA_LocalizedControlTypePropertyId => NullIfEmpty(_peer.GetLocalizedControlType()),
        UiaConstants.UIA_IsEnabledPropertyId => _peer.IsEnabled(),
        UiaConstants.UIA_IsKeyboardFocusablePropertyId => _peer.IsKeyboardFocusable(),
        UiaConstants.UIA_HasKeyboardFocusPropertyId => _peer.HasKeyboardFocus(),
        UiaConstants.UIA_IsContentElementPropertyId => _peer.IsContentElement(),
        UiaConstants.UIA_IsControlElementPropertyId => _peer.IsControlElement(),
        UiaConstants.UIA_IsOffscreenPropertyId => IsActuallyOffscreen(),
        UiaConstants.UIA_ProcessIdPropertyId => Environment.ProcessId,
        UiaConstants.UIA_FrameworkIdPropertyId => "Jalium",
        UiaConstants.UIA_NativeWindowHandlePropertyId => _isRoot ? _hwnd.ToInt32() : 0,
        UiaConstants.UIA_ProviderDescriptionPropertyId => "Jalium.UI UIA Provider",
        _ => GetPatternAvailability(propertyId),
    };

    /// <summary>
    /// Answers the boolean <c>IsXxxPatternAvailable</c> properties by asking the peer whether it
    /// exposes the corresponding pattern. Returns <see langword="null"/> (VT_EMPTY) for property ids
    /// that are not pattern-availability queries, so unrelated properties fall through unchanged.
    /// Without this, a provider that returns a Text/Value/Toggle/... pattern from
    /// <see cref="GetPatternProvider"/> still reported VT_EMPTY here, and UIA clients (Narrator, text
    /// clients) that check availability first would treat the supported pattern as absent.
    /// </summary>
    private object? GetPatternAvailability(int propertyId)
    {
        var patternInterface = UiaConstants.MapAvailabilityPropertyToPatternInterface(propertyId);
        if (patternInterface == null)
            return null;

        return _peer.GetPattern(patternInterface.Value) != null;
    }

    public unsafe int get_HostRawElementProvider(nint* pRetVal)
    {
        if (!IsWritableOutPointer(pRetVal, sizeof(nint)))
        {
            UiaTrace.Log($"get_HostRawElementProvider E_POINTER peer={PeerLabel()} pRetVal=0x{FormatPointer(pRetVal)}");
            return unchecked((int)0x80004003); // E_POINTER
        }

        *pRetVal = nint.Zero;
        if (_isRoot && _hwnd != nint.Zero)
        {
            // UiaHostProviderFromHwnd returns an owned provider reference. Transfer it directly
            // to the COM caller; do not wrap and release it through the managed marshaller.
            int hr = UiaNativeMethods.UiaHostProviderFromHwnd(_hwnd, out nint pHost);
            if (hr >= 0 && pHost != nint.Zero)
            {
                *pRetVal = pHost;
                UiaTrace.Log($"get_HostRawElementProvider S_OK peer={PeerLabel()} host=0x{FormatPointer(pHost)}");
                return 0;
            }

            UiaTrace.Log($"get_HostRawElementProvider HR=0x{hr:X8} peer={PeerLabel()}");
            return hr;
        }

        UiaTrace.Log($"get_HostRawElementProvider S_OK null peer={PeerLabel()}");
        return 0;
    }

    // ========================================================================
    // IRawElementProviderFragment
    // ========================================================================

    public IRawElementProviderFragment? Navigate(NavigateDirection direction)
    {
        switch (direction)
        {
            case NavigateDirection.Parent:
                if (_isRoot) return null;
                var parentPeer = _peer.GetParent();
                return parentPeer != null ? UiaAccessibilityBridge.GetOrCreateProvider(parentPeer, _hwnd) : null;

            case NavigateDirection.FirstChild:
                var children = _peer.GetChildren();
                return children.Count > 0 ? UiaAccessibilityBridge.GetOrCreateProvider(children[0], _hwnd) : null;

            case NavigateDirection.LastChild:
                var kids = _peer.GetChildren();
                return kids.Count > 0 ? UiaAccessibilityBridge.GetOrCreateProvider(kids[^1], _hwnd) : null;

            case NavigateDirection.NextSibling:
                return GetSibling(1);

            case NavigateDirection.PreviousSibling:
                return GetSibling(-1);

            default:
                return null;
        }
    }

    public int[] GetRuntimeId() => [UiaConstants.AppendRuntimeId, _runtimeId];

    public UiaRect get_BoundingRectangle()
    {
        var bounds = _peer.GetBoundingRectangle();
        if (bounds.IsEmpty) return default;
        // The element's own bounds sit at its local origin; map the (0,0,W,H) local rect to screen.
        return LocalRectToScreen(new Rect(0, 0, bounds.Width, bounds.Height));
    }

    /// <summary>
    /// Maps a rectangle expressed in the owner element's local coordinate space to physical screen
    /// pixels. Shared by <see cref="get_BoundingRectangle"/> and the Text pattern's range bounding
    /// rectangles so both use the identical element→root→screen + DPI transform.
    /// </summary>
    internal UiaRect LocalRectToScreen(Rect localBounds)
    {
        if (localBounds.IsEmpty)
            return default;

        if (_peer.Owner is UIElement element)
        {
            var windowRect = element.MapLocalRectToScreen(localBounds);
            var window = FindOwnerWindow(element);
            if (window != null)
                return WindowDipRectToScreenPixels(window, windowRect);

            return new UiaRect
            {
                Left = windowRect.X,
                Top = windowRect.Y,
                Width = windowRect.Width,
                Height = windowRect.Height
            };
        }

        return new UiaRect { Left = localBounds.X, Top = localBounds.Y, Width = localBounds.Width, Height = localBounds.Height };
    }

    private bool IsActuallyOffscreen()
    {
        if (_peer.IsOffscreen())
            return true;

        if (_peer.Owner is not UIElement element)
            return false;

        var bounds = _peer.GetBoundingRectangle();
        if (bounds.IsEmpty)
            return true;

        var window = FindOwnerWindow(element);
        if (window == null)
            return false;

        var elementRect = element.MapLocalRectToScreen(new Rect(0, 0, bounds.Width, bounds.Height));
        if (elementRect.IsEmpty)
            return true;

        var windowRect = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        if (windowRect.IsEmpty)
            return true;

        return !elementRect.IntersectsWith(windowRect);
    }

    // MUST stay null. On the wire GetEmbeddedFragmentRoots is SAFEARRAY(VARIANT) (each VARIANT
    // holding VT_UNKNOWN), NOT the SAFEARRAY(VT_UNKNOWN) that SafeArrayProviderMarshaller builds
    // (which is correct for ISelectionProvider.GetSelection). Returning a non-null array here
    // would hand UIA a VT_UNKNOWN SAFEARRAY where it expects VT_VARIANT. Almost all providers
    // return null (no embedded HWND roots); do not populate this without a SAFEARRAY(VARIANT)
    // marshaller.
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus() => _peer.SetFocus();

    public IRawElementProviderFragmentRoot? get_FragmentRoot()
    {
        if (_isRoot) return this;
        var current = _peer;
        AutomationPeer? parent;
        while ((parent = current.GetParent()) != null)
            current = parent;
        return UiaAccessibilityBridge.GetOrCreateProvider(current, _hwnd);
    }

    // ========================================================================
    // IRawElementProviderFragmentRoot
    // ========================================================================

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        if (_peer.Owner is not FrameworkElement rootElement) return this;
        var window = FindOwnerWindow(rootElement);
        if (window == null) return this;

        double dpi = window.DpiScale;
        var clientOrigin = new UiaNativeMethods.POINT();
        UiaNativeMethods.ClientToScreen(window.Handle, ref clientOrigin);

        double localX = (x - clientOrigin.X) / dpi;
        double localY = (y - clientOrigin.Y) / dpi;

        var hitResult = rootElement.HitTest(new Point(localX, localY));
        if (hitResult?.VisualHit is UIElement hitElement)
        {
            var provider = FindNearestProvider(hitElement);
            if (provider != null)
                return provider;
        }
        return this;
    }

    public IRawElementProviderFragment? GetFocus()
    {
        var focused = Jalium.UI.Input.Keyboard.FocusedElement as UIElement;
        if (focused != null)
        {
            var provider = FindNearestProvider(focused);
            if (provider != null)
                return provider;
        }
        if (_isRoot)
            return this;
        return null;
    }

    /// <summary>
    /// Walks up the visual tree from the given element to find the nearest
    /// ancestor (or self) that has an automation peer. This ensures that
    /// hit-testing and focus queries return meaningful controls (e.g., Button)
    /// rather than internal template parts (e.g., Border inside Button).
    /// </summary>
    private AutomationPeerProvider? FindNearestProvider(UIElement element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (current is UIElement ue)
            {
                var peer = ue.GetAutomationPeer();
                if (peer != null)
                    return UiaAccessibilityBridge.GetOrCreateProvider(peer, _hwnd);
            }
            current = current.VisualParent;
        }
        return null;
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private IRawElementProviderFragment? GetSibling(int offset)
    {
        var parentPeer = _peer.GetParent();
        if (parentPeer == null) return null;
        var siblings = parentPeer.GetChildren();
        int index = -1;
        for (int i = 0; i < siblings.Count; i++)
            if (ReferenceEquals(siblings[i], _peer)) { index = i; break; }
        if (index < 0) return null;
        int target = index + offset;
        if (target < 0 || target >= siblings.Count) return null;
        return UiaAccessibilityBridge.GetOrCreateProvider(siblings[target], _hwnd);
    }

    private static UiaRect WindowDipRectToScreenPixels(Window window, Rect rect)
    {
        double dpi = window.DpiScale;
        if (!double.IsFinite(dpi) || dpi <= 0)
            dpi = 1.0;

        var origin = new UiaNativeMethods.POINT();
        if (window.Handle != nint.Zero)
            UiaNativeMethods.ClientToScreen(window.Handle, ref origin);

        return new UiaRect
        {
            Left = origin.X + rect.X * dpi,
            Top = origin.Y + rect.Y * dpi,
            Width = rect.Width * dpi,
            Height = rect.Height * dpi
        };
    }

    private static Window? FindOwnerWindow(UIElement element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (current is Window w) return w;
            current = current.VisualParent;
        }
        return null;
    }

    private unsafe nint WrapPatternToPointer(int patternId, object pattern) => patternId switch
    {
        UiaConstants.UIA_InvokePatternId when pattern is IInvokeProvider p
            => (nint)ComInterfaceMarshaller<IUiaInvokeProvider>.ConvertToUnmanaged(new UiaInvokeProviderWrapper(p)),
        UiaConstants.UIA_TogglePatternId when pattern is IToggleProvider p
            => (nint)ComInterfaceMarshaller<IUiaToggleProvider>.ConvertToUnmanaged(new UiaToggleProviderWrapper(p)),
        UiaConstants.UIA_ValuePatternId when pattern is IValueProvider p
            => (nint)ComInterfaceMarshaller<IUiaValueProvider>.ConvertToUnmanaged(new UiaValueProviderWrapper(p)),
        UiaConstants.UIA_RangeValuePatternId when pattern is IRangeValueProvider p
            => (nint)ComInterfaceMarshaller<IUiaRangeValueProvider>.ConvertToUnmanaged(new UiaRangeValueProviderWrapper(p)),
        UiaConstants.UIA_ExpandCollapsePatternId when pattern is IExpandCollapseProvider p
            => (nint)ComInterfaceMarshaller<IUiaExpandCollapseProvider>.ConvertToUnmanaged(new UiaExpandCollapseProviderWrapper(p)),
        UiaConstants.UIA_SelectionPatternId when pattern is ISelectionProvider p
            => (nint)ComInterfaceMarshaller<IUiaSelectionProvider>.ConvertToUnmanaged(new UiaSelectionProviderWrapper(p)),
        UiaConstants.UIA_SelectionItemPatternId when pattern is ISelectionItemProvider p
            => (nint)ComInterfaceMarshaller<IUiaSelectionItemProvider>.ConvertToUnmanaged(new UiaSelectionItemProviderWrapper(p)),
        UiaConstants.UIA_ScrollPatternId when pattern is IScrollProvider p
            => (nint)ComInterfaceMarshaller<IUiaScrollProvider>.ConvertToUnmanaged(new UiaScrollProviderWrapper(p)),
        UiaConstants.UIA_ScrollItemPatternId when pattern is IScrollItemProvider p
            => (nint)ComInterfaceMarshaller<IUiaScrollItemProvider>.ConvertToUnmanaged(new UiaScrollItemProviderWrapper(p)),
        UiaConstants.UIA_TextPatternId when pattern is ITextProvider p
            => (nint)ComInterfaceMarshaller<IUiaTextProvider>.ConvertToUnmanaged(new UiaTextProviderWrapper(p, this)),
        _ => nint.Zero,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private string PeerLabel() => $"{_peer.GetType().Name}/{_peer.GetAutomationControlType()}";

    private static unsafe string FormatPointer<T>(T* pointer) where T : unmanaged
        => ((nuint)pointer).ToString("X");

    private static string FormatPointer(nint pointer)
        => ((nuint)pointer).ToString("X");
}
