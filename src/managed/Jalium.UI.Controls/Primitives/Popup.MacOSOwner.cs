using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Primitives;

public partial class Popup
{
    private Window? _macOSObservedOwner;
    private UIElement? _macOSObservedAnchor;
    private HashSet<Visual> _macOSObservedAncestors = [];
    private long _macOSOwnerRevision;
    private object? _macOSOwnerCheckToken;
    private bool _macOSOwnerPositionDirty;
    private bool _macOSOwnerLayoutDirty;
    private long _macOSOwnerLayoutRevision;
    private MacOSPopupPlacementLayout? _macOSOwnerPlacementLayout;

    private readonly record struct MacOSPopupPlacementLayout(
        Rect AnchorBounds, Size OwnerSize, Size PopupSize, double DpiScale);

    private void OnMacOSPlacementTargetChanged()
    {
        if (!OperatingSystem.IsMacOS() || _popupRoot == null) return;
        var parent = GetParentWindow();
        if (_parentWindow != null && parent != null && !ReferenceEquals(parent, _parentWindow) &&
            ReferenceEquals(parent.MacOSInputOwner, _parentWindow.MacOSInputOwner) && IsMacOSPopupAnchorValid(parent))
        {
            var previous = _parentWindow;
            previous.LocationChanged -= OnParentWindowLocationChanged;
            _parentWindow = parent;
            parent.LocationChanged += OnParentWindowLocationChanged;
            if (_macOSPopupWindow != null)
            {
                previous.ActiveExternalPopups.Remove(this);
                if (!parent.ActiveExternalPopups.Contains(this)) parent.ActiveExternalPopups.Add(this);
                if (!_macOSPopupWindow.ReparentMacOSPopupHost(parent))
                { SetCurrentValue(IsOpenProperty, false); return; }
            }
            else if (_overlayLayer != null)
            {
                _overlayLayer.RemovePopupRoot(_popupRoot);
                _overlayLayer = parent.OverlayLayer;
                _overlayLayer.AddPopupRoot(_popupRoot);
            }
        }
        ObserveMacOSPopupOwner();
        _macOSOwnerPositionDirty = true;
        QueueMacOSPopupOwnerCheck();
    }

    private void ObserveMacOSPopupOwner()
    {
        StopObservingMacOSPopupOwner();
        if (_popupRoot == null || _parentWindow == null) return;
        _macOSObservedOwner = _parentWindow;
        _macOSObservedAnchor = PlacementTarget ?? this;
        _macOSObservedOwner.IsEnabledChanged += OnMacOSPopupOwnerStateChanged;
        _macOSObservedOwner.IsVisibleChanged += OnMacOSPopupOwnerStateChanged;
        _macOSObservedOwner.LayoutUpdated += OnMacOSPopupOwnerLayoutUpdated;
        _macOSObservedOwner.MacOSInputLayoutUpdated += OnMacOSPopupOwnerLayoutUpdated;
        _macOSObservedOwner.MacOSInputSubtreeDetached += OnMacOSPopupOwnerSubtreeDetached;
        CaptureMacOSPopupOwnerAncestors();
    }

    private void StopObservingMacOSPopupOwner()
    {
        unchecked { _macOSOwnerRevision++; }
        _macOSOwnerCheckToken = null;
        _macOSOwnerPositionDirty = false;
        _macOSOwnerLayoutDirty = false;
        _macOSOwnerPlacementLayout = null;
        unchecked { _macOSOwnerLayoutRevision++; }
        foreach (var element in _macOSObservedAncestors.OfType<UIElement>())
        {
            element.IsEnabledChanged -= OnMacOSPopupOwnerStateChanged;
            element.IsVisibleChanged -= OnMacOSPopupOwnerStateChanged;
        }
        if (_macOSObservedOwner != null)
        {
            _macOSObservedOwner.IsEnabledChanged -= OnMacOSPopupOwnerStateChanged;
            _macOSObservedOwner.IsVisibleChanged -= OnMacOSPopupOwnerStateChanged;
            _macOSObservedOwner.LayoutUpdated -= OnMacOSPopupOwnerLayoutUpdated;
            _macOSObservedOwner.MacOSInputLayoutUpdated -= OnMacOSPopupOwnerLayoutUpdated;
            _macOSObservedOwner.MacOSInputSubtreeDetached -= OnMacOSPopupOwnerSubtreeDetached;
        }
        _macOSObservedOwner = null;
        _macOSObservedAnchor = null;
        _macOSObservedAncestors.Clear();
    }

    private void CaptureMacOSPopupOwnerAncestors()
    {
        var ancestors = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
        int depth = 0;
        for (Visual? node = _macOSObservedAnchor; node != null && depth++ < 4096;
            node = InputRoute.GetParent(node))
            if (!ancestors.Add(node)) break;
        foreach (var element in _macOSObservedAncestors.Except(ancestors).OfType<UIElement>())
        {
            element.IsEnabledChanged -= OnMacOSPopupOwnerStateChanged;
            element.IsVisibleChanged -= OnMacOSPopupOwnerStateChanged;
        }
        foreach (var element in ancestors.Except(_macOSObservedAncestors).OfType<UIElement>())
        {
            element.IsEnabledChanged += OnMacOSPopupOwnerStateChanged;
            element.IsVisibleChanged += OnMacOSPopupOwnerStateChanged;
        }
        _macOSObservedAncestors = ancestors;
    }

    private bool IsMacOSPopupAnchorValid(Window owner)
    {
        if (PlacementTarget == null)
            return VisualParent == null || ReferenceEquals(ResolveOwningWindow(VisualParent as Visual), owner);
        var visited = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
        for (Visual? node = PlacementTarget; node != null && visited.Count < 4096 && visited.Add(node);
            node = InputRoute.GetParent(node))
        {
            if (node is UIElement element && (!element.IsVisible || element.Visibility != Visibility.Visible ||
                IsHitTestVisible && !element.IsEnabled || Jalium.UI.Styling.CssDisplayProperties.IsExitInert(element))) return false;
            if (ReferenceEquals(node, owner.MacOSInputOwner)) return true;
        }
        return false;
    }

    private void OnMacOSPopupOwnerStateChanged(object? sender, DependencyPropertyChangedEventArgs e) => QueueMacOSPopupOwnerCheck();
    private void OnMacOSPopupOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        _macOSOwnerLayoutDirty = true;
        unchecked { _macOSOwnerLayoutRevision++; }
        QueueMacOSPopupOwnerCheck();
    }
    private void OnMacOSPopupOwnerSubtreeDetached(UIElement subtree)
    {
        if (!_macOSObservedAncestors.Contains(subtree)) return;
        _macOSOwnerPositionDirty = true;
        QueueMacOSPopupOwnerCheck();
    }

    private void QueueMacOSPopupOwnerCheck()
    {
        var root = _popupRoot;
        if (_macOSOwnerCheckToken != null || root == null || _macOSObservedOwner == null || Dispatcher.HasShutdownStarted) return;
        var revision = _macOSOwnerRevision;
        var token = _macOSOwnerCheckToken = new object();
        DispatcherOperation operation;
        try { operation = Dispatcher.BeginInvoke(DispatcherPriority.Input, CheckOwner); }
        catch
        {
            if (ReferenceEquals(token, _macOSOwnerCheckToken)) _macOSOwnerCheckToken = null;
            throw;
        }
        operation.Aborted += OnFinished;
        operation.Completed += OnFinished;
        if (operation.Status is DispatcherOperationStatus.Aborted or DispatcherOperationStatus.Completed)
            OnFinished(operation, EventArgs.Empty);

        void OnFinished(object? sender, EventArgs e)
        {
            operation.Aborted -= OnFinished;
            operation.Completed -= OnFinished;
            // A hook may abort the post, or run it inline and enqueue a newer
            // check. An old completion must not disarm that newer operation.
            if (ReferenceEquals(token, _macOSOwnerCheckToken)) _macOSOwnerCheckToken = null;
        }

        void CheckOwner()
        {
            if (revision != _macOSOwnerRevision || !ReferenceEquals(root, _popupRoot) ||
                !ReferenceEquals(token, _macOSOwnerCheckToken)) return;
            _macOSOwnerCheckToken = null;
            if (!IsOpen) return;
            // Detach/reattach within one dispatcher batch is a move, not a close.
            var owner = _macOSObservedOwner;
            bool valid = owner is { IsMacOSPopupInputOwnerAlive: true, IsEnabled: true, IsVisible: true } &&
                IsMacOSPopupAnchorValid(owner);
            if (!valid)
            {
                SetCurrentValue(IsOpenProperty, false);
                return;
            }
            if (revision != _macOSOwnerRevision || !ReferenceEquals(root, _popupRoot) ||
                !ReferenceEquals(owner, _macOSObservedOwner)) return;
            CaptureMacOSPopupOwnerAncestors();
            if (_macOSOwnerLayoutDirty)
            {
                _macOSOwnerLayoutDirty = false;
                var layoutRevision = _macOSOwnerLayoutRevision;
                var placement = new MacOSPopupPlacementLayout(
                    GetPlacementBounds(PlacementTarget ?? this),
                    new Size(owner!.ActualWidth, owner.ActualHeight),
                    new Size(root.Width, root.Height), owner.DpiScale);
                // Transform/placement queries may run application code. A newer
                // layout or reopened popup owns its own pending notification.
                if (revision != _macOSOwnerRevision || !ReferenceEquals(root, _popupRoot) ||
                    !ReferenceEquals(owner, _macOSObservedOwner) || !IsOpen ||
                    layoutRevision != _macOSOwnerLayoutRevision) return;
                if (_macOSOwnerPlacementLayout != placement)
                    _macOSOwnerPositionDirty = true;
                _macOSOwnerPlacementLayout = placement;
            }
            if (_macOSOwnerPositionDirty)
            {
                _macOSOwnerPositionDirty = false;
                UpdatePosition();
                if (revision != _macOSOwnerRevision || !ReferenceEquals(root, _popupRoot)) return;
            }
            KeyboardFocusWithinState.Update(null, Keyboard.FocusedElement as UIElement);
        }
    }
}
