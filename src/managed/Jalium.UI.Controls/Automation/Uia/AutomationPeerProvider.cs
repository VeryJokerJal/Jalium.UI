using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// UIA provider wrapping an AutomationPeer, exposed to native UI Automation as a
/// source-generated COM object (<see cref="GeneratedComClassAttribute"/>). Implements
/// IRawElementProviderFragmentRoot (which inherits Fragment and Simple), so the
/// StrategyBasedComWrappers CCW answers QueryInterface for all three IIDs.
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
internal sealed partial class AutomationPeerProvider : IRawElementProviderFragmentRoot
{
    private readonly AutomationPeer _peer;
    private readonly nint _hwnd;
    private readonly bool _isRoot;
    private readonly int _runtimeId;

    internal AutomationPeerProvider(AutomationPeer peer, nint hwnd)
    {
        _peer = peer;
        _hwnd = hwnd;
        _isRoot = peer.GetAutomationControlType() == AutomationControlType.Window;
        _runtimeId = RuntimeHelpers.GetHashCode(peer);
    }

    internal AutomationPeer Peer => _peer;
    internal int[] GetRuntimeIdArray() => [UiaConstants.AppendRuntimeId, _runtimeId];

    // ========================================================================
    // IRawElementProviderSimple
    // ========================================================================

    // PreserveSig: return the HRESULT ourselves so a NULL pRetVal (some IME/TSF UIA clients pass
    // one while the UI thread is in an input-synchronous message) yields E_POINTER instead of a
    // first-chance NullReferenceException from the generated write-back. See IRawElementProviderSimple.
    public unsafe int get_ProviderOptions(ProviderOptions* pRetVal)
    {
        if (pRetVal == null)
            return unchecked((int)0x80004003); // E_POINTER
        *pRetVal = ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;
        return 0; // S_OK
    }

    public object? GetPatternProvider(int patternId)
    {
        var patternInterface = UiaConstants.MapUiaPatternIdToPatternInterface(patternId);
        if (patternInterface == null) return null;

        var pattern = _peer.GetPattern(patternInterface.Value);
        if (pattern == null) return null;

        return WrapPattern(patternId, pattern);
    }

    public UiaVariant GetPropertyValue(int propertyId) => UiaVariant.From(GetPropertyValueCore(propertyId));

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
        UiaConstants.UIA_IsOffscreenPropertyId => _peer.IsOffscreen(),
        UiaConstants.UIA_ProcessIdPropertyId => Environment.ProcessId,
        UiaConstants.UIA_FrameworkIdPropertyId => "Jalium",
        UiaConstants.UIA_NativeWindowHandlePropertyId => _isRoot ? _hwnd.ToInt32() : 0,
        UiaConstants.UIA_ProviderDescriptionPropertyId => "Jalium.UI UIA Provider",
        _ => null,
    };

    public IRawElementProviderSimple? get_HostRawElementProvider()
    {
        if (_isRoot && _hwnd != nint.Zero)
        {
            // Ask UIA for the HWND's default host provider, then wrap it as an RCW with the
            // same marshalling instance the generated stubs use and release our own ref.
            int hr = UiaNativeMethods.UiaHostProviderFromHwnd(_hwnd, out nint pHost);
            if (hr >= 0 && pHost != nint.Zero)
            {
                var host = UiaComInterop.ProviderFromPointer(pHost);
                Marshal.Release(pHost);
                return host;
            }
        }
        return null;
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
        if (_peer.Owner is FrameworkElement fe)
        {
            var offset = fe.TransformToAncestor(null);
            var window = FindOwnerWindow(fe);
            if (window != null)
            {
                double dpi = window.DpiScale;
                var pt = new UiaNativeMethods.POINT
                {
                    X = (int)((offset.X + localBounds.X) * dpi),
                    Y = (int)((offset.Y + localBounds.Y) * dpi)
                };
                UiaNativeMethods.ClientToScreen(window.Handle, ref pt);
                return new UiaRect { Left = pt.X, Top = pt.Y, Width = localBounds.Width * dpi, Height = localBounds.Height * dpi };
            }
        }

        return new UiaRect { Left = localBounds.X, Top = localBounds.Y, Width = localBounds.Width, Height = localBounds.Height };
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

    private static Window? FindOwnerWindow(FrameworkElement element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (current is Window w) return w;
            current = current.VisualParent;
        }
        return null;
    }

    private object? WrapPattern(int patternId, object pattern) => patternId switch
    {
        UiaConstants.UIA_InvokePatternId when pattern is IInvokeProvider p => new UiaInvokeProviderWrapper(p),
        UiaConstants.UIA_TogglePatternId when pattern is IToggleProvider p => new UiaToggleProviderWrapper(p),
        UiaConstants.UIA_ValuePatternId when pattern is IValueProvider p => new UiaValueProviderWrapper(p),
        UiaConstants.UIA_RangeValuePatternId when pattern is IRangeValueProvider p => new UiaRangeValueProviderWrapper(p),
        UiaConstants.UIA_ExpandCollapsePatternId when pattern is IExpandCollapseProvider p => new UiaExpandCollapseProviderWrapper(p),
        UiaConstants.UIA_SelectionPatternId when pattern is ISelectionProvider p => new UiaSelectionProviderWrapper(p),
        UiaConstants.UIA_SelectionItemPatternId when pattern is ISelectionItemProvider p => new UiaSelectionItemProviderWrapper(p),
        UiaConstants.UIA_ScrollPatternId when pattern is IScrollProvider p => new UiaScrollProviderWrapper(p),
        UiaConstants.UIA_ScrollItemPatternId when pattern is IScrollItemProvider p => new UiaScrollItemProviderWrapper(p),
        UiaConstants.UIA_TextPatternId when pattern is ITextProvider p => new UiaTextProviderWrapper(p, this),
        _ => null,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
