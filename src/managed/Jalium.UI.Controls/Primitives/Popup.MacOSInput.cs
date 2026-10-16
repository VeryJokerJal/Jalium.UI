using System.Runtime.ExceptionServices;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.Primitives;

public partial class Popup
{
    private PopupRoot? _macOSClosingRoot;
    private UIElement? _macOSFocusBeforeOpen;

    private bool HasClosingMacOSPopupAncestor()
    {
        int depth = 0;
        for (Jalium.UI.Media.Visual? current = PlacementTarget ?? this; current != null && depth++ < 4096;
            current = InputRoute.GetParent(current))
        {
            if (current is PopupRoot root && root.OwnerPopup._macOSClosingRoot != null) return true;
            if (current is Popup popup && !ReferenceEquals(popup, this) && popup._macOSClosingRoot != null) return true;
        }
        return false;
    }

    private void CloseMacOSPopup()
    {
        var root = _popupRoot;
        if (root == null || ReferenceEquals(root, _macOSClosingRoot)) return;
        var previousClosingRoot = _macOSClosingRoot;
        _macOSClosingRoot = root;
        var owner = _parentWindow;
        var previousFocus = _macOSFocusBeforeOpen;
        _macOSFocusBeforeOpen = null;
        var anchor = PlacementTarget ?? this;
        var originalFocus = Keyboard.FocusedElement as UIElement;
        bool originallyOwnedFocus = originalFocus != null && InputRoute.IsWithin(originalFocus, root);
        Exception? failure = null;
        void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { failure ??= ex; }
        }
        try
        {
            // A child's Closed handler may mutate the overlay or its owner.
            var children = _overlayLayer?.Children.OfType<PopupRoot>()
                .Where(child => !ReferenceEquals(child, root) && IsAncestorOfPopupRoot(child))
                .Select(child => child.OwnerPopup).ToArray() ?? [];
            foreach (var child in children) Run(() => child.IsOpen = false);

            var focus = Keyboard.FocusedElement as UIElement;
            bool ownsFocus = focus != null && (InputRoute.IsWithin(focus, root) ||
                originallyOwnedFocus && ReferenceEquals(focus, originalFocus));
            var compositionTarget = InputMethod.CurrentTarget;
            bool ownsComposition = InputMethod.IsComposing && compositionTarget is UIElement composing &&
                InputRoute.IsWithin(composing, root);
            Run(() => ClosePopupCore(() =>
            {
                // The old host is detached before cancellation callbacks run.
                // A later Closed callback can safely open a fresh host/IME.
                if (ownsComposition && InputMethod.IsComposing &&
                    ReferenceEquals(compositionTarget, InputMethod.CurrentTarget))
                    Run(() => InputMethod.EndComposition());
                if (!IsOpen && ownsFocus && owner != null && ReferenceEquals(focus, Keyboard.FocusedElement))
                    Run(() => owner.RestoreMacOSPopupInputFocus(previousFocus, anchor, focus!));
                _macOSClosingRoot = previousClosingRoot;
                if (IsOpen && _popupRoot == null) Run(OpenPopup);
            }));
        }
        finally
        {
            if (ReferenceEquals(_macOSClosingRoot, root)) _macOSClosingRoot = previousClosingRoot;
        }
        if (IsOpen && _popupRoot == null) Run(OpenPopup);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
