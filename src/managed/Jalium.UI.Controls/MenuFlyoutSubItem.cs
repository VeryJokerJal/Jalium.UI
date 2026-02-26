using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a menu item that displays a sub-menu in a MenuFlyout control.
/// </summary>
public sealed class MenuFlyoutSubItem : MenuFlyoutItem
{
    private readonly List<MenuFlyoutItem> _items = new();
    private Primitives.Popup? _subPopup;

    /// <summary>
    /// Gets the collection of menu elements in the sub-menu.
    /// </summary>
    public IList<MenuFlyoutItem> Items => _items;

    /// <summary>
    /// Initializes a new instance of the MenuFlyoutSubItem class.
    /// </summary>
    public MenuFlyoutSubItem()
    {
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnSubItemMouseEnter));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnSubItemMouseLeave));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        // Draw submenu arrow
        var arrowBrush = new Jalium.UI.Media.SolidColorBrush(
            Jalium.UI.Media.Color.FromRgb(180, 180, 180));
        var arrowFormatted = new Jalium.UI.Media.FormattedText(
            "\u25B6", FontFamily, 8) { Foreground = arrowBrush }; // ▶
        dc.DrawText(arrowFormatted, new Point(RenderSize.Width - 16, (RenderSize.Height - 8) / 2));
    }

    /// <summary>
    /// Shows the sub-menu.
    /// </summary>
    public void ShowSubMenu()
    {
        if (_subPopup != null)
        {
            _subPopup.IsOpen = true;
            return;
        }

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var item in _items)
        {
            panel.Children.Add(item);
        }

        var border = new Border
        {
            Background = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(45, 45, 48)),
            BorderBrush = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(67, 67, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = panel,
            MinWidth = 160
        };

        _subPopup = new Primitives.Popup
        {
            PlacementTarget = this,
            Placement = Primitives.PlacementMode.Right,
            StaysOpen = false,
            Child = border
        };

        AddVisualChild(_subPopup);
        _subPopup.IsOpen = true;
    }

    /// <summary>
    /// Hides the sub-menu.
    /// </summary>
    public void HideSubMenu()
    {
        if (_subPopup != null)
            _subPopup.IsOpen = false;
    }

    private void OnSubItemMouseEnter(object sender, RoutedEventArgs e)
    {
        ShowSubMenu();
    }

    private void OnSubItemMouseLeave(object sender, RoutedEventArgs e)
    {
        // Delay hiding to allow mouse to move to sub-menu
    }
}
