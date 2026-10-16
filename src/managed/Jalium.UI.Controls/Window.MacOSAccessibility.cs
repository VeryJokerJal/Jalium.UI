using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class Window
{
    private int _macOSAccessibilityPresentationGeneration;

    internal Button? FindMacOSDefaultOrCancelButton(bool cancel, bool physicalOnly = false)
    {
        var owner = MacOSInputOwner;
        var route = InputRoute.Capture(owner.GetKeyboardEventTarget(), owner);
        foreach (var node in route)
        {
            if (physicalOnly && !ReferenceEquals(GetMacOSPhysicalWindow(node), this)) continue;
            if (node is PopupRoot root)
            {
                if (FindButton(root, Matches) is { } local) return local;
            }
            else if (node is ContentDialog dialog &&
                (ReferenceEquals(dialog, owner.ActiveContentDialog) || owner.ActiveInPlaceDialogs.Contains(dialog)))
                return FindButton(dialog, Matches);
        }

        // AX window-button relationships must name a physical descendant.
        // Keyboard fallback can continue through a detached popup's owner.
        UIElement fallback = physicalOnly ? ActiveContentDialog ?? (UIElement)this
            : owner.ActiveContentDialog ?? (UIElement)owner;
        return FindButton(fallback, Matches);

        bool Matches(Button button) => cancel ? button.IsCancel : button.IsDefault;
    }

    private UIElement NormalizeMacOSAccessibilityTarget(UIElement target) =>
        ReferenceEquals(target, this) && IsMacOSPopupHost ? _macOSHostedPopupRoot ?? target : target;

    internal bool IsMacOSAccessibilityTargetVisible(UIElement target)
    {
        target = NormalizeMacOSAccessibilityTarget(target);
        var owner = MacOSInputOwner;
        if (!IsMacOSPopupInputOwnerAlive || !owner.IsMacOSPopupInputOwnerAlive || !target.IsVisible ||
            !ReferenceEquals(GetMacOSPhysicalWindow(target), this)) return false;
        var route = InputRoute.Capture(target, owner);
        return route.Count != 0 && route.OfType<UIElement>().All(element =>
            element.Visibility == Visibility.Visible && !Styling.CssDisplayProperties.IsExitInert(element));
    }

    internal bool IsMacOSAccessibilityTargetEnabled(UIElement target)
    {
        target = NormalizeMacOSAccessibilityTarget(target);
        var owner = MacOSInputOwner;
        return IsEnabled && owner.IsEnabled && IsMacOSAccessibilityTargetVisible(target) &&
            InputRoute.IsWithin(target, owner.ActiveContentDialog ?? (UIElement)owner);
    }

    private ContentDialog? GetMacOSAccessibilityDialog(IReadOnlyList<Visual> route)
    {
        var owner = MacOSInputOwner;
        return owner.ActiveContentDialog ?? route.OfType<ContentDialog>()
            .FirstOrDefault(owner.ActiveInPlaceDialogs.Contains);
    }

    internal Func<bool> CaptureMacOSAccessibilityLifetime(UIElement target)
    {
        target = NormalizeMacOSAccessibilityTarget(target);
        var owner = MacOSInputOwner;
        var route = InputRoute.Capture(target, owner);
        var dialog = GetMacOSAccessibilityDialog(route);
        var presentation = dialog?.AccessibilityPresentationToken;
        var platform = _platformWindow;
        var ownerPlatform = owner._platformWindow;
        nint handle = Handle, ownerHandle = owner.Handle;
        int generation = _renderLifecycleGeneration, ownerGeneration = owner._renderLifecycleGeneration;
        int presentationGeneration = _macOSAccessibilityPresentationGeneration;
        int ownerPresentationGeneration = owner._macOSAccessibilityPresentationGeneration;
        return () => InputRoute.IsCurrent(route) && IsMacOSAccessibilityTargetEnabled(target) &&
            generation == _renderLifecycleGeneration && ownerGeneration == owner._renderLifecycleGeneration &&
            presentationGeneration == _macOSAccessibilityPresentationGeneration &&
            ownerPresentationGeneration == owner._macOSAccessibilityPresentationGeneration &&
            ReferenceEquals(platform, _platformWindow) && ReferenceEquals(ownerPlatform, owner._platformWindow) &&
            handle == Handle && ownerHandle == owner.Handle &&
            ReferenceEquals(dialog, GetMacOSAccessibilityDialog(route)) &&
            ReferenceEquals(presentation, dialog?.AccessibilityPresentationToken);
    }
}
