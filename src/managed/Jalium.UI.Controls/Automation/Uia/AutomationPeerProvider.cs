using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// UIA provider wrapping an AutomationPeer. Follows WPF's ElementProxy pattern:
/// implements IRawElementProviderFragmentRoot (which inherits Fragment and Simple).
/// Inherits StandardOleMarshalObject so COM calls from UIA bypass the RPC channel
/// and execute directly on the calling thread — this avoids RPC_E_CANTCALLOUT_ININPUTSYNCCALL
/// during WM_GETOBJECT processing (an input-synchronous SendMessage).
/// </summary>
[ComVisible(true)]
internal sealed class AutomationPeerProvider : StandardOleMarshalObject, IRawElementProviderFragmentRoot
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

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public object? GetPatternProvider(int patternId)
    {
        var patternInterface = UiaConstants.MapUiaPatternIdToPatternInterface(patternId);
        if (patternInterface == null) return null;

        var pattern = _peer.GetPattern(patternInterface.Value);
        if (pattern == null) return null;

        return WrapPattern(patternId, pattern);
    }

    public object? GetPropertyValue(int propertyId) => GetPropertyValueCore(propertyId);

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

    public IRawElementProviderSimple? HostRawElementProvider
    {
        get
        {
            if (_isRoot && _hwnd != nint.Zero)
            {
                UiaNativeMethods.UiaHostProviderFromHwnd(_hwnd, out var host);
                return host;
            }
            return null;
        }
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

    public UiaRect BoundingRectangle
    {
        get
        {
            var bounds = _peer.GetBoundingRectangle();
            if (bounds.IsEmpty) return default;

            if (_peer.Owner is FrameworkElement fe)
            {
                var offset = fe.TransformToAncestor(null);
                var window = FindOwnerWindow(fe);
                if (window != null)
                {
                    double dpi = window.DpiScale;
                    var pt = new UiaNativeMethods.POINT
                    {
                        X = (int)(offset.X * dpi),
                        Y = (int)(offset.Y * dpi)
                    };
                    UiaNativeMethods.ClientToScreen(window.Handle, ref pt);
                    return new UiaRect { Left = pt.X, Top = pt.Y, Width = bounds.Width * dpi, Height = bounds.Height * dpi };
                }
            }

            return new UiaRect { Left = bounds.X, Top = bounds.Y, Width = bounds.Width, Height = bounds.Height };
        }
    }

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus() => _peer.SetFocus();

    public IRawElementProviderFragmentRoot FragmentRoot
    {
        get
        {
            if (_isRoot) return this;
            var current = _peer;
            AutomationPeer? parent;
            while ((parent = current.GetParent()) != null)
                current = parent;
            return UiaAccessibilityBridge.GetOrCreateProvider(current, _hwnd);
        }
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

    private static object? WrapPattern(int patternId, object pattern) => patternId switch
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
        _ => null,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
