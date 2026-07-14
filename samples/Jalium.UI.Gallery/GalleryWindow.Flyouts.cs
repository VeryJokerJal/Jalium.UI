using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for transient command surfaces: context menus, flyout menus
/// and swipe actions. These controls are popups/overlays that only render when
/// invoked, so the gallery shows their static <em>anchor</em> (the element you
/// click / right-click / swipe) rather than opening the transient surface, which
/// would require a live <c>Dispatcher</c> / interaction loop.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildFlyoutsSection() => Section(
        "Menus & Flyouts",
        "Context menus, flyout menus and swipe actions, shown via their anchors.",
        Card("MenuFlyout", MenuFlyoutAnchor()),
        Card("ContextMenu", ContextMenuAnchor()),
        Card("CommandBarFlyout", Placeholder("CommandBarFlyout", "contextual command flyout")),
        Card("SwipeControl", SwipeControlAnchor()));

    // MenuFlyout is a transient FlyoutBase that opens over its placement target.
    // We show the anchor button that would open it (never opening the flyout).
    private static UIElement MenuFlyoutAnchor()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };

        stack.Children.Add(new Button
        {
            Content = "Open menu",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Opens a MenuFlyout with Cut / Copy / Paste items.",
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });

        return stack;
    }

    // ContextMenu renders nothing and takes no layout space until opened on
    // right-click, so we show the surface it would be attached to.
    private static UIElement ContextMenuAnchor()
    {
        var caption = new TextBlock
        {
            Text = "Right-click me",
            FontSize = 14,
            Foreground = TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new Border
        {
            Background = HeaderBackground,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 92,
            Child = caption,
        };
    }

    // SwipeControl reveals contextual commands when its content is dragged.
    // At rest it simply shows its content, which is what the gallery captures.
    private static UIElement SwipeControlAnchor()
    {
        var label = new TextBlock
        {
            Text = "Swipe for actions",
            FontSize = 14,
            Foreground = TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var swipe = new SwipeControl
        {
            Width = 260,
            Height = 64,
            Background = HeaderBackground,
            Content = label,
        };

        return new Border
        {
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = swipe,
        };
    }
}
