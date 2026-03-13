using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Wrapper element that hosts Popup.Child content within the OverlayLayer.
/// Routes DataContext/resources back to the Popup and supports light-dismiss.
/// </summary>
internal sealed class PopupRoot : Decorator
{
    /// <summary>
    /// Gets the owning Popup control.
    /// </summary>
    internal Popup OwnerPopup { get; }

    /// <summary>
    /// Gets whether this popup root requires light-dismiss behavior.
    /// </summary>
    internal bool IsLightDismiss { get; }

    public PopupRoot(Popup popup, UIElement child, bool isLightDismiss)
    {
        OwnerPopup = popup;
        IsLightDismiss = isLightDismiss;
        Child = child;

        // PopupRoot itself should not capture focus — let child elements receive it
        Focusable = false;
        IsHitTestVisible = true;

        // Inherit DataContext from the Popup (not from OverlayLayer)
        DataContext = popup.DataContext;

        // Track Popup's DataContext changes for dynamic updates
        popup.DataContextChanged += OnOwnerDataContextChanged;

        // Popup itself is detached from the interactive visual tree, so mirror the hosted
        // subtree's hover state back onto the owner Popup for controls that inspect it.
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler), handledEventsToo: true);
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new RoutedEventHandler(OnPreviewMouseDownHandler), handledEventsToo: true);
    }

    private void OnOwnerDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        DataContext = e.NewValue;
    }

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(true);
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(false);
    }

    private void OnPreviewMouseDownHandler(object sender, RoutedEventArgs e)
    {
        OwnerPopup.SetIsMouseOver(true);
    }

    /// <summary>
    /// Detaches event subscriptions. Called when removed from OverlayLayer.
    /// </summary>
    internal void Detach()
    {
        OwnerPopup.DataContextChanged -= OnOwnerDataContextChanged;
        RemoveHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        RemoveHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
        RemoveHandler(PreviewMouseDownEvent, new RoutedEventHandler(OnPreviewMouseDownHandler));
        OwnerPopup.SetIsMouseOver(false);
        Child = null;
    }
}
