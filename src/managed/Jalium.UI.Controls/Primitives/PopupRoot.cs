using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Wrapper element that hosts Popup.Child content within the OverlayLayer.
/// Routes DataContext/resources back to the Popup and supports light-dismiss.
/// </summary>
internal sealed class PopupRoot : Decorator, IInputRouteParentProvider
{
    Visual? IInputRouteParentProvider.InputRouteParent => OperatingSystem.IsMacOS()
        ? OwnerPopup.GetMacOSInputRouteParent(this) : VisualParent as Visual;

    /// <summary>
    /// Gets the owning Popup control.
    /// </summary>
    internal Popup OwnerPopup { get; }

    /// <summary>
    /// Gets whether this popup root requires light-dismiss behavior.
    /// </summary>
    internal bool IsLightDismiss { get; }

    /// <summary>
    /// Gets whether pointer input is currently captured by popup content.
    /// </summary>
    /// <remarks>
    /// Scroll bars and other drag controls keep capture while the pointer moves outside
    /// the popup bounds. That ongoing gesture must not be interpreted as light dismiss.
    /// </remarks>
    internal bool HasPointerCaptureWithin =>
        IsMouseCaptureWithin || IsStylusCaptureWithin || AreAnyTouchesCapturedWithin;

    public PopupRoot(Popup popup, UIElement child, bool isLightDismiss)
    {
        OwnerPopup = popup;
        IsLightDismiss = isLightDismiss;
        Child = child;

        // PopupRoot itself should not capture focus — let child elements receive it
        Focusable = false;
        if (OperatingSystem.IsMacOS())
        {
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
        }

        // Mirror the owner's hit-test policy onto the hosted surface. A tooltip popup is
        // declared non-hit-testable precisely so it cannot steal hover from the element it
        // describes; hard-coding true here made the overlay swallow the pointer, the owning
        // element saw MouseLeave, the tooltip closed and immediately reopened — an endless
        // flicker whenever the popup landed under the cursor.
        IsHitTestVisible = popup.IsHitTestVisible;

        // Inherit DataContext from the Popup (not from OverlayLayer)
        DataContext = popup.DataContext;

        // Track Popup's DataContext changes for dynamic updates
        popup.DataContextChanged += OnOwnerDataContextChanged;

        // Popup itself is detached from the interactive visual tree, so mirror the hosted
        // subtree's hover state back onto the owner Popup for controls that inspect it.
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler), handledEventsToo: true);
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownHandler), handledEventsToo: true);
    }

    private void OnOwnerDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        DataContext = e.NewValue;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(true);
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(false);
    }

    private void OnPreviewMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (OperatingSystem.IsMacOS() && !e.Handled && e.Key == Key.Escape &&
            OwnerPopup.IsOpen && !OwnerPopup.StaysOpen)
        {
            OwnerPopup.SetCurrentValue(Popup.IsOpenProperty, false);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var desiredSize = base.MeasureOverride(constraint);
        OwnerPopup.QueueContentSizeUpdate(this);
        return desiredSize;
    }

    /// <summary>
    /// Detaches event subscriptions. Called when removed from OverlayLayer.
    /// </summary>
    internal void Detach()
    {
        OwnerPopup.DataContextChanged -= OnOwnerDataContextChanged;
        RemoveHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        RemoveHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownHandler));
        OwnerPopup.SetIsMouseOver(false);
        Child = null;
    }
}
