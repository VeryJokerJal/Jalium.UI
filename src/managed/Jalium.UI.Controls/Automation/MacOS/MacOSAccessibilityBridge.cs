using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Automation.MacOS;

/// <summary>Owns one live AppKit accessibility tree and its reverse P/Invoke.</summary>
internal sealed unsafe class MacOSAccessibilityBridge : IDisposable
{
    private static readonly HashSet<MacOSAccessibilityBridge> s_windows = [];
    private static readonly object s_gate = new();
    private static readonly EventSink s_sink = new();
    private static IAutomationEventSink? s_previousSink;
    private static readonly MacOSAccessibilityNative.Callback s_callback = Request;
    private readonly NativePlatformWindow _platform;
    private readonly Window _window;
    private readonly MacOSAccessibilityTree _tree;
    private GCHandle _context;
    private bool _disposed;

    internal MacOSAccessibilityBridge(Window window, NativePlatformWindow platform)
    {
        _window = window;
        _platform = platform;
        _tree = new(window);
        _context = GCHandle.Alloc(this);
        try
        {
            MacOSAccessibilityNative.SetCallback(platform.PlatformHandle,
                Marshal.GetFunctionPointerForDelegate(s_callback), GCHandle.ToIntPtr(_context));
            lock (s_gate)
            {
                if (s_windows.Count == 0)
                {
                    s_previousSink = AutomationPeer.EventSink;
                    AutomationPeer.EventSink = s_sink;
                }
                s_windows.Add(this);
            }
        }
        catch
        {
            if (platform.PlatformHandle != 0)
                MacOSAccessibilityNative.SetCallback(platform.PlatformHandle, 0, 0);
            _context.Free();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _window.Dispatcher.VerifyAccess();
        _disposed = true;
        if (_platform.PlatformHandle != 0)
            MacOSAccessibilityNative.SetCallback(_platform.PlatformHandle, 0, 0);
        if (_context.IsAllocated) _context.Free();
        lock (s_gate)
        {
            s_windows.Remove(this);
            if (s_windows.Count == 0)
            {
                if (ReferenceEquals(AutomationPeer.EventSink, s_sink))
                    AutomationPeer.EventSink = s_previousSink;
                s_previousSink = null;
            }
        }
    }

    private static int Request(MacOSAXRequest* request, nint context)
    {
        // AppKit queries on the main thread. Never block a foreign AX thread on
        // a dispatcher while the main thread may be servicing an AX request.
        if (request == null || context == 0) return 0;
        try
        {
            var bridge = GCHandle.FromIntPtr(context).Target as MacOSAccessibilityBridge;
            return bridge is { _disposed: false } && bridge._window.Dispatcher.CheckAccess()
                && bridge._tree.Handle(ref *request) ? 1 : 0;
        }
        catch (Exception error)
        {
            // Provider/user action exceptions must not unwind through Objective-C.
            Debug.WriteLine($"[macOS accessibility] {error}");
            return 0;
        }
    }

    private static void Notify(AutomationPeer peer, MacOSAXNotification notification)
    {
        MacOSAccessibilityBridge[] windows;
        lock (s_gate) windows = s_windows.ToArray();
        foreach (var bridge in windows)
        {
            void Publish()
            {
                try
                {
                    if (!bridge._disposed && bridge._tree.TryGetId(peer, out ulong id))
                    {
                        var effectiveNotification = notification == MacOSAXNotification.Focus && !peer.HasKeyboardFocus()
                            ? MacOSAXNotification.Layout : notification;
                        // A hidden or newly omitted peer is no longer a valid
                        // AppKit notification target. Announce changed ancestry
                        // at the live root, preserving cached hidden identities.
                        if (effectiveNotification == MacOSAXNotification.Layout) id = 1;
                        MacOSAccessibilityNative.Notify(bridge._platform.PlatformHandle, id, effectiveNotification);
                    }
                }
                catch (Exception error) { Debug.WriteLine($"[macOS accessibility notification] {error}"); }
            }
            if (bridge._window.Dispatcher.CheckAccess()) Publish();
            else if (!bridge._window.Dispatcher.HasShutdownStarted)
                bridge._window.Dispatcher.BeginInvoke(Publish);
        }
    }

    private sealed class EventSink : IAutomationEventSink
    {
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        {
            s_previousSink?.OnAutomationEventRaised(peer, eventId);
            switch (eventId)
            {
                case AutomationEvents.AutomationFocusChanged: Notify(peer, MacOSAXNotification.Focus); break;
                case AutomationEvents.TextPatternOnTextSelectionChanged: Notify(peer, MacOSAXNotification.Selection); break;
                case AutomationEvents.TextPatternOnTextChanged:
                case AutomationEvents.SelectionItemPatternOnElementSelected:
                case AutomationEvents.SelectionItemPatternOnElementAddedToSelection:
                case AutomationEvents.SelectionItemPatternOnElementRemovedFromSelection:
                    Notify(peer, MacOSAXNotification.Value); break;
                case AutomationEvents.StructureChanged:
                case AutomationEvents.AsyncContentLoaded:
                    Notify(peer, MacOSAXNotification.Layout); break;
            }
        }

        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        {
            s_previousSink?.OnPropertyChangedRaised(peer, property, oldValue, newValue);
            Notify(peer, property == AutomationProperty.NameProperty ? MacOSAXNotification.Title
                : property == AutomationProperty.BoundingRectangleProperty || property == AutomationProperty.IsOffscreenProperty
                    ? MacOSAXNotification.Layout : MacOSAXNotification.Value);
            if (peer is MacOSFallbackAutomationPeer
                && (property == AutomationProperty.NameProperty || property == AutomationProperty.AutomationIdProperty))
                Notify(peer, MacOSAXNotification.Layout);
        }

        // InvalidatePeer also uses this entry point; only announce a focus when
        // it is actually held, and otherwise refresh the structure/layout.
        public void OnFocusChanged(AutomationPeer peer)
        {
            s_previousSink?.OnFocusChanged(peer);
            Notify(peer, MacOSAXNotification.Focus);
        }
    }
}

/// <summary>Renderer-neutral adapter; IDs remain stable while a peer lives.</summary>
internal sealed unsafe partial class MacOSAccessibilityTree
{
    private sealed record NodeId(ulong Value);
    private readonly Window _window;
    private readonly AutomationPeer _root;
    private readonly ConditionalWeakTable<AutomationPeer, NodeId> _ids = new();
    private readonly Dictionary<ulong, WeakReference<AutomationPeer>> _peers = [];
    private ulong _nextId = 1;

    internal MacOSAccessibilityTree(Window window)
    {
        _window = window;
        _root = window.GetAutomationPeer()!;
        GetId(_root);
    }

    internal bool TryGetId(AutomationPeer peer, out ulong id)
    {
        peer = RealizedPeer(peer);
        id = 0;
        if (!BelongsToWindow(peer)) return false;
        id = GetId(peer);
        return true;
    }

    private ulong GetId(AutomationPeer peer)
    {
        peer = RealizedPeer(peer);
        if (_ids.TryGetValue(peer, out var value)) return value.Value;
        ulong id = _nextId++;
        _ids.Add(peer, new(id));
        _peers.Add(id, new(peer));
        if ((id & 511) == 0)
            foreach (var entry in _peers.ToArray())
                if (!entry.Value.TryGetTarget(out _)) _peers.Remove(entry.Key);
        return id;
    }

    // ItemsControl exposes data-item proxy peers. For a realized item, its
    // actual control peer owns the role/name, expansion and selection patterns.
    // Normalize both child and parent IDs, rather than creating two AX objects
    // for a proxy and its container with inconsistent actions or ancestry.
    private static AutomationPeer RealizedPeer(AutomationPeer peer) =>
        peer is ItemAutomationPeer && peer.Owner is UIElement container
            ? container.GetAutomationPeer() ?? peer : peer;

    private bool BelongsToWindow(AutomationPeer peer)
    {
        if (ReferenceEquals(peer, _root)) return true;
        if (peer.Owner is Visual visual)
        {
            for (Visual? parent = visual; parent != null; parent = parent.VisualParent)
                if (parent is Window) return ReferenceEquals(parent, _window);
            return false;
        }
        var seen = new HashSet<AutomationPeer>();
        for (AutomationPeer? parent = peer; parent != null && seen.Add(parent); parent = parent.GetParent())
            if (ReferenceEquals(parent, _root)) return true;
        return false;
    }

    private bool IsVisible(AutomationPeer peer)
    {
        if (!BelongsToWindow(peer)) return false;
        // Clipped descendants remain readable and scrollable in the AX tree.
        // Their geometric visibility is reported separately in Info.
        if (peer.Owner is not UIElement && peer.IsOffscreen() || peer is ItemAutomationPeer && peer.IsOffscreen()) return false;
        if (peer is MacOSFallbackAutomationPeer && !peer.IsControlElement()) return false;
        if (peer.Owner is UIElement { IsVisible: false }) return false;
        return !HasHiddenSubtree(peer);
    }

    private static bool HasHiddenSubtree(AutomationPeer peer)
    {
        // Exit animations retain their presentation, but the subtree has
        // already left input navigation. Cached AX actions follow that rule.
        if (peer.Owner is UIElement element && Jalium.UI.Styling.CssDisplayProperties.IsExitInert(element)) return true;
        if (peer.Owner is Visual visual)
            for (Visual? parent = visual; parent != null; parent = parent.VisualParent)
                if (parent is UIElement { Visibility: not Visibility.Visible }) return true;
        return false;
    }

    private List<AutomationPeer> Children(AutomationPeer peer)
    {
        if (!CanExposeChildren(peer)) return [];
        var children = new List<AutomationPeer>();
        var seen = new HashSet<AutomationPeer> { peer };
        if (peer is ItemsControlAutomationPeer itemsPeer
            && itemsPeer.ItemsOwner.ItemsHostInternal is VirtualizingPanel panel
            && VirtualizingPanel.GetIsVirtualizing(itemsPeer.ItemsOwner))
        {
            // AX exposes realized rows. Reading the logical collection both walks every item
            // in a lazy range and caches item identities that may no longer match recycled
            // containers. The live panel supplies the same realized controls in visual order.
            foreach (UIElement container in panel.Children)
                if (container.GetAutomationPeer() is { } child) AddVisibleChild(child, children, seen);
            return children;
        }
        foreach (var child in peer.GetChildren()) AddVisibleChild(child, children, seen);
        return children;
    }

    private static bool CanExposeChildren(AutomationPeer peer)
    {
        // ItemsControl's logical peers include descendants even while their
        // TreeViewItem is collapsed. AX outline navigation must only disclose
        // rows in expanded branches, independent of template realization.
        if (peer.GetAutomationControlType() == AutomationControlType.TreeItem
            && peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider
                { ExpandCollapseState: ExpandCollapseState.Collapsed }) return false;
        // Atomic controls already provide their text via Name/Value. Exposing
        // their template's text and decoration duplicates VoiceOver stops.
        if (peer.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
            or AutomationControlType.RadioButton or AutomationControlType.Edit or AutomationControlType.Text
            or AutomationControlType.Hyperlink or AutomationControlType.Slider or AutomationControlType.ProgressBar)
            return false;
        return true;
    }

    private void AddVisibleChild(AutomationPeer peer, List<AutomationPeer> children, HashSet<AutomationPeer> seen)
    {
        peer = RealizedPeer(peer);
        if (!seen.Add(peer) || !BelongsToWindow(peer) || HasHiddenSubtree(peer)) return;
        if (IsVisible(peer)) children.Add(peer);
        // CSS visibility:hidden can be overridden by a descendant. Omit the
        // hidden box itself while keeping a visible descendant reachable.
        // Native Visibility and display:none continue to gate the subtree.
        else if ((peer is MacOSFallbackAutomationPeer || peer.Owner is UIElement { IsVisible: false })
            && CanExposeChildren(peer))
            foreach (var child in peer.GetChildren()) AddVisibleChild(child, children, seen);
    }

    private ulong ParentId(AutomationPeer peer)
    {
        if (ReferenceEquals(peer, _root)) return 0;
        var seen = new HashSet<AutomationPeer> { peer };
        for (var parent = peer.GetParent(); parent != null && seen.Add(parent); parent = parent.GetParent())
            if (IsVisible(parent)) return GetId(parent);
        return 1;
    }

    internal bool Handle(ref MacOSAXRequest request)
    {
        if (!_peers.TryGetValue(request.NodeId, out var weak) || !weak.TryGetTarget(out var peer)) return false;
        if (!BelongsToWindow(peer))
        {
            _peers.Remove(request.NodeId);
            _ids.Remove(peer);
            return false;
        }
        if (request.Operation == MacOSAXOperation.Attached) return true;
        if (!IsVisible(peer)) return false;
        switch (request.Operation)
        {
            case MacOSAXOperation.Info: Describe(peer, ref request); return true;
            case MacOSAXOperation.WindowButton:
                if (!ReferenceEquals(peer, _root) || (uint)request.Index > (uint)MacOSAXWindowButton.Zoom) return false;
                var host = (IInputDispatcherHost)_window;
                // Share Enter/Escape's dialog scope and visible-button lookup.
                // Disabled buttons remain discoverable as in AppKit; their
                // normal provider enabled check continues to reject actions.
                UIElement? button;
                if (request.Index < (int)MacOSAXWindowButton.Close)
                {
                    var scope = (UIElement?)host.ActiveContentDialog ?? (UIElement?)host.FindContainingInPlaceDialog() ?? _window;
                    bool cancelButton = request.Index == (int)MacOSAXWindowButton.Cancel;
                    button = host.FindButton(scope, candidate => cancelButton ? candidate.IsCancel : candidate.IsDefault);
                }
                else
                {
                    // Only the Window's installed title bar owns caption
                    // relationships; lookalike buttons in content do not.
                    var captionKind = (MacOSAXWindowButton)request.Index;
                    button = host.IsTitleBarVisible() ? host.TitleBar?.EnumerateButtons().FirstOrDefault(candidate =>
                        captionKind switch
                        {
                            MacOSAXWindowButton.Close => candidate.Kind == TitleBarButtonKind.Close,
                            MacOSAXWindowButton.Minimize => candidate.Kind == TitleBarButtonKind.Minimize,
                            _ => candidate.Kind is TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore
                        }) : null;
                }
                var buttonPeer = button?.GetAutomationPeer();
                request.ResultId = buttonPeer != null && IsVisible(buttonPeer) ? GetId(buttonPeer) : 0;
                return true;
            case MacOSAXOperation.Child:
                var children = Children(peer);
                if ((uint)request.Index >= (uint)children.Count) return false;
                request.ResultId = GetId(children[request.Index]); return true;
            case MacOSAXOperation.String:
                return WriteString(GetString(peer, (MacOSAXString)request.Index), ref request);
            case MacOSAXOperation.Focus:
                var focus = FindFocus(peer, []);
                request.ResultId = focus == null ? 0 : GetId(focus); return true;
            case MacOSAXOperation.HitTest:
                var hit = HitTest(peer, new(request.X, request.Y), []);
                request.ResultId = hit == null ? 0 : GetId(hit); return true;
            case MacOSAXOperation.Action: return PerformAction(peer, (MacOSAXAction)request.Index);
            case MacOSAXOperation.TextNavigation:
                return NavigateText(peer, ref request);
            case MacOSAXOperation.TextStyles:
            case MacOSAXOperation.TextStyleRange:
                return ReadTextStyles(peer, ref request);
            case MacOSAXOperation.SetValue:
                if (!peer.IsEnabled()) return false;
                if (peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider { IsReadOnly: false } range)
                {
                    if (!double.IsFinite(request.Value)) return false;
                    range.SetValue(Math.Clamp(request.Value, range.Minimum, range.Maximum)); return true;
                }
                if (peer.GetPattern(PatternInterface.Value) is IValueProvider { IsReadOnly: false } value
                    && request.TextCount >= 0 && request.TextCount <= request.TextCapacity
                    && (request.TextCount == 0 || request.Text != null))
                {
                    value.SetValue(new string(request.Text, 0, request.TextCount)); return true;
                }
                return false;
            case MacOSAXOperation.TextSelection:
            case MacOSAXOperation.SetTextSelection:
            case MacOSAXOperation.TextBounds:
                if (peer.IsPassword() || GetTextSource(peer) is not { } source) return false;
                if (request.Operation == MacOSAXOperation.TextSelection)
                {
                    request.TextStart = Math.Clamp(source.SelectionStart, 0, source.Text.Length);
                    request.TextLength = Math.Clamp(source.SelectionLength, 0, source.Text.Length - request.TextStart);
                    return true;
                }
                if (request.TextStart < 0 || request.TextLength < 0
                    || request.TextStart > source.Text.Length || request.TextLength > source.Text.Length - request.TextStart)
                    return false;
                if (request.Operation == MacOSAXOperation.SetTextSelection)
                {
                    if (!peer.IsEnabled()) return false;
                    source.Select(request.TextStart, request.TextLength); return true;
                }
                var rectangles = source.GetBoundingRectangles(request.TextStart, request.TextLength);
                if (rectangles.Count == 0) return false;
                Rect bounds = Rect.Empty;
                var visibility = peer.Owner is UIElement textOwner ? new AutomationVisibility(textOwner) : null;
                Matrix destination = peer.Owner is UIElement target ? target.GetRenderMatrix() : Matrix.Identity;
                foreach (Rect rectangle in rectangles)
                {
                    Rect projected = rectangle;
                    if (visibility != null && !visibility.TryClip(rectangle, destination, out projected)) continue;
                    bounds = bounds.IsEmpty ? projected : Rect.Union(bounds, projected);
                }
                if (bounds.IsEmpty) return false;
                // Offset text sources return rectangles in their control's
                // local coordinates, while the native ABI expects root points.
                SetBounds(bounds, ref request); return true;
            default: return false;
        }
    }

    private void Describe(AutomationPeer peer, ref MacOSAXRequest request)
    {
        request.Role = (int)peer.GetAutomationControlType();
        request.ParentId = ParentId(peer);
        request.ChildCount = Children(peer).Count;
        SetBounds(peer.GetBoundingRectangle(), ref request);
        MacOSAXFlags flags = 0;
        if (peer.IsOffscreen()) flags |= MacOSAXFlags.Offscreen;
        if (peer.IsEnabled()) flags |= MacOSAXFlags.Enabled;
        if (peer.IsKeyboardFocusable()) flags |= MacOSAXFlags.Focusable;
        if (peer.HasKeyboardFocus()) flags |= MacOSAXFlags.Focused;
        if (peer.IsDialog()) flags |= MacOSAXFlags.Dialog;
        if (ReferenceEquals(peer, _root) && _window.IsModal) flags |= MacOSAXFlags.Modal;
        if (peer.Owner is TitleBarButton caption)
            flags |= caption.Kind switch
            {
                TitleBarButtonKind.Close => MacOSAXFlags.CloseButton,
                TitleBarButtonKind.Minimize => MacOSAXFlags.MinimizeButton,
                TitleBarButtonKind.Maximize or TitleBarButtonKind.Restore => MacOSAXFlags.ZoomButton,
                _ => 0
            };
        bool password = peer.IsPassword();
        if (password) flags |= MacOSAXFlags.Password;
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider
            || peer.GetPattern(PatternInterface.Toggle) is IToggleProvider
            || peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider)
            flags |= MacOSAXFlags.Pressable;
        if (peer.GetPattern(PatternInterface.Value) is IValueProvider value)
        {
            flags |= MacOSAXFlags.Value;
            if (!value.IsReadOnly) flags |= MacOSAXFlags.Writable;
        }
        if (peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider range)
        {
            flags |= MacOSAXFlags.Range;
            if (!range.IsReadOnly) flags |= MacOSAXFlags.Writable;
            request.Value = range.Value; request.Minimum = range.Minimum; request.Maximum = range.Maximum;
            request.Step = range.SmallChange;
        }
        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            flags |= MacOSAXFlags.Toggle;
            request.Value = toggle.ToggleState == ToggleState.Indeterminate ? 2 : toggle.ToggleState == ToggleState.On ? 1 : 0;
        }
        if (peer.GetPattern(PatternInterface.Selection) is ISelectionProvider) flags |= MacOSAXFlags.Selection;
        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection)
        {
            flags |= MacOSAXFlags.Selectable;
            if (selection.IsSelected) flags |= MacOSAXFlags.Selected;
        }
        if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand
            && expand.ExpandCollapseState != ExpandCollapseState.LeafNode)
        {
            flags |= MacOSAXFlags.Expandable;
            if (expand.ExpandCollapseState is ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded)
                flags |= MacOSAXFlags.Expanded;
        }
        if (!password && GetTextSource(peer) is { } text)
        {
            flags |= MacOSAXFlags.Text;
            if (text is IAutomationTextStyleSource) flags |= MacOSAXFlags.StyledText;
            if (text is IAutomationTextViewSource)
            {
                flags |= MacOSAXFlags.NavigableText;
                if (!text.IsReadOnly) flags |= MacOSAXFlags.EditableText;
            }
            if (peer.Owner is TextBox { AcceptsReturn: true } or RichTextBox or EditControl || text.Text.AsSpan().IndexOfAny('\r', '\n') >= 0)
                flags |= MacOSAXFlags.Multiline;
            request.TextCount = text.Text.Length;
        }
        request.Flags = flags;
    }

    private static IAutomationTextProviderSource? GetTextSource(AutomationPeer peer) => peer.GetPattern(PatternInterface.Text) switch
    {
        AutomationTextProvider provider => provider.Source,
        IAutomationTextProviderSource source => source,
        _ => null
    };

    private static bool NavigateText(AutomationPeer peer, ref MacOSAXRequest request)
    {
        if (peer.IsPassword() || GetTextSource(peer) is not { } source
            || source is not IAutomationTextViewSource view) return false;
        var operation = (MacOSAXTextNavigation)request.Index;
        string text = source.Text;
        if (operation == MacOSAXTextNavigation.ReplaceSelection)
        {
            if (!peer.IsEnabled() || source.IsReadOnly || request.TextCount < 0 || request.TextCount > request.TextCapacity
                || request.TextCount > 16 * 1024 * 1024 || (request.TextCount != 0 && request.Text == null)) return false;
            return view.ReplaceSelection(new string(request.Text, 0, request.TextCount));
        }
        AutomationTextSpan range;
        if (operation == MacOSAXTextNavigation.RangeForIndex)
        {
            if (!AutomationTextNavigation.TryGetCharacterRange(text, request.TextStart, out range)) return false;
        }
        else if (operation == MacOSAXTextNavigation.RangeForPosition)
        {
            if (!double.IsFinite(request.X) || !double.IsFinite(request.Y) || peer.Owner is not UIElement element
                || !element.GetRenderMatrix().TryInvert(out var inverse)
                || !AutomationTextNavigation.TryGetRangeFromPoint(source, view, inverse.Transform(new Point(request.X, request.Y)), out range)) return false;
        }
        else if (operation == MacOSAXTextNavigation.VisibleTextRange)
        {
            var visible = AutomationTextNavigation.VisibleRanges(source, view);
            if (visible.Count == 0) return false;
            int start = visible.Min(item => item.Start), end = visible.Max(item => item.Start + item.Length);
            // AppKit has one visible NSRange; the canonical provider retains
            // disjoint spans (e.g. text on either side of a collapsed fold).
            range = new(start, end - start);
        }
        else
        {
            var lines = view.GetTextLines();
            if (operation is MacOSAXTextNavigation.LineForIndex or MacOSAXTextNavigation.InsertionLine)
            {
                int line = AutomationTextNavigation.LineFromIndex(lines,
                    operation == MacOSAXTextNavigation.InsertionLine ? view.CaretIndex : request.TextStart, text.Length,
                    operation == MacOSAXTextNavigation.InsertionLine && view.CaretHasBackwardAffinity);
                if (line < 0) return false;
                request.TextStart = line; return true;
            }
            if (operation is not (MacOSAXTextNavigation.RangeForLine or MacOSAXTextNavigation.SetInsertionLine)
                || (uint)request.TextStart >= lines.Count) return false;
            var row = lines[request.TextStart]; range = new(row.Start, row.Length);
            if (operation == MacOSAXTextNavigation.SetInsertionLine)
            {
                if (!peer.IsEnabled()) return false;
                source.Select(range.Start, 0); source.ScrollIntoView(range.Start, 0); return true;
            }
        }
        request.TextStart = range.Start; request.TextLength = range.Length; return true;
    }

    private static void SetBounds(Rect bounds, ref MacOSAXRequest request)
    {
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return;
        request.X = bounds.X; request.Y = bounds.Y; request.Width = bounds.Width; request.Height = bounds.Height;
    }

    private static string GetString(AutomationPeer peer, MacOSAXString property) => property switch
    {
        MacOSAXString.Name => GetName(peer),
        MacOSAXString.Help => peer.GetHelpText(),
        MacOSAXString.Identifier => peer.GetAutomationId(),
        MacOSAXString.Placeholder => peer.Owner switch
        {
            TextBox box => box.PlaceholderText ?? string.Empty,
            PasswordBox box => box.PlaceholderText ?? string.Empty,
            _ => string.Empty
        },
        MacOSAXString.Value when peer.IsPassword() => string.Empty,
        MacOSAXString.Value when peer.GetPattern(PatternInterface.Value) is IValueProvider value => value.Value,
        MacOSAXString.Value when peer.GetPattern(PatternInterface.Text) is ITextProvider text => text.DocumentRange.GetText(-1),
        MacOSAXString.Value when peer.GetAutomationControlType() == AutomationControlType.Text => peer.GetName(),
        _ => string.Empty
    };

    private static string GetName(AutomationPeer peer)
    {
        if (peer.Owner is { } owner && !string.IsNullOrEmpty(AutomationProperties.GetName(owner)))
            return AutomationProperties.GetName(owner);
        var label = peer.Owner is { } labeledOwner
            ? AutomationProperties.GetLabeledBy(labeledOwner)?.GetAutomationPeer() : null;
        label ??= peer.GetLabeledBy();
        if (label != null && !string.IsNullOrEmpty(label.GetName())) return label.GetName();
        string name = peer.GetName();
        if (peer.Owner is ContentControl { Content: { } data } control
            && data is not (string or UIElement) && name == data.ToString())
        {
            // Data-item peers fall back to ToString(), which can expose record
            // fields and source snippets. Prefer the realized template's text;
            // explicit metadata and custom peer names stay authoritative.
            var visibleLabels = new List<string>();
            CollectLabels(control, visibleLabels);
            if (visibleLabels.Count != 0) return string.Join(" ", visibleLabels);
        }
        if (!string.IsNullOrEmpty(name)) return name;
        if (peer.GetAutomationControlType() is not (AutomationControlType.Button or AutomationControlType.CheckBox
            or AutomationControlType.RadioButton or AutomationControlType.Hyperlink or AutomationControlType.SplitButton)
            || peer.Owner is not ContentControl { Content: UIElement content }) return name;
        // Atomic controls omit template children from the AX tree. Preserve
        // their visible text when the content is a TextBlock or icon/text panel,
        // rather than exposing an unnamed button or duplicate navigation stops.
        var labels = new List<string>();
        CollectLabels(content, labels);
        return string.Join(" ", labels);
    }

    private static void CollectLabels(UIElement element, List<string> labels)
    {
        if (element.Visibility != Visibility.Visible) return;
        if (element is TextBlock { IsVisible: true, Text: { Length: > 0 } } text)
        {
            labels.Add(text.Text);
            return;
        }
        for (int index = 0; index < element.VisualChildrenCount; index++)
            if (element.GetVisualChild(index) is UIElement child) CollectLabels(child, labels);
    }

    private static bool WriteString(string value, ref MacOSAXRequest request)
    {
        request.TextCount = value.Length;
        if (request.Text == null) return true;
        if (request.TextCapacity < value.Length) return false;
        value.AsSpan().CopyTo(new Span<char>(request.Text, value.Length));
        return true;
    }

    private bool PerformAction(AutomationPeer peer, MacOSAXAction action)
    {
        if (!peer.IsEnabled()) return false;
        switch (action)
        {
            case MacOSAXAction.SetFocus when peer.IsKeyboardFocusable(): peer.SetFocus(); return peer.HasKeyboardFocus();
            case MacOSAXAction.Press:
                if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider)
                {
                    if (_window.Handle == 0 || _window.Dispatcher.HasShutdownStarted) return false;
                    // User code can enter ShowDialog. Return the native AX request
                    // before running it so a modal loop does not hold that request
                    // open and interfere with subsequent AppKit queries/updates.
                    _window.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            if (_window.Handle != 0 && IsVisible(peer) && peer.IsEnabled()
                                && peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
                                invoke.Invoke();
                        }
                        catch (Exception error)
                        {
                            // The accepted request is now asynchronous; preserve
                            // the native boundary's provider-exception isolation.
                            Debug.WriteLine($"[macOS accessibility Invoke] {error}");
                        }
                    });
                    return true;
                }
                if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider select) { select.Select(); return true; }
                if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) { toggle.Toggle(); return true; }
                return false;
            case MacOSAXAction.Deselect:
                if (peer.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider selection) return false;
                selection.RemoveFromSelection(); return true;
            case MacOSAXAction.Increment:
            case MacOSAXAction.Decrement:
                if (peer.GetPattern(PatternInterface.RangeValue) is not IRangeValueProvider { IsReadOnly: false } range
                    || !double.IsFinite(range.SmallChange) || range.SmallChange <= 0) return false;
                range.SetValue(Math.Clamp(range.Value + (action == MacOSAXAction.Increment ? range.SmallChange : -range.SmallChange),
                    range.Minimum, range.Maximum)); return true;
            case MacOSAXAction.Expand:
            case MacOSAXAction.Collapse:
                if (peer.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expand) return false;
                if (action == MacOSAXAction.Expand) expand.Expand(); else expand.Collapse();
                return true;
            default: return false;
        }
    }

    private AutomationPeer? FindFocus(AutomationPeer peer, HashSet<AutomationPeer> seen)
    {
        if (!seen.Add(peer)) return null;
        foreach (var child in Children(peer))
            if (FindFocus(child, seen) is { } found) return found;
        return peer.HasKeyboardFocus() ? peer : null;
    }

    private AutomationPeer? HitTest(AutomationPeer peer, Point point, HashSet<AutomationPeer> seen)
    {
        if (!seen.Add(peer) || !peer.GetBoundingRectangle().Contains(point)) return null;
        if (peer.Owner is UIElement element && (!element.GetRenderMatrix().TryInvert(out Matrix inverse)
            || !new AutomationVisibility(element).Contains(inverse.Transform(point)))) return null;
        var children = Children(peer);
        for (int index = children.Count - 1; index >= 0; index--)
            if (HitTest(children[index], point, seen) is { } found) return found;
        return peer;
    }
}
