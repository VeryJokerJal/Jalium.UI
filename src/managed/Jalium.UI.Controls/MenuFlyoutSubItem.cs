using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a menu item that displays a sub-menu in a MenuFlyout control.
/// </summary>
[ContentProperty("Items")]
public sealed class MenuFlyoutSubItem : MenuFlyoutItem
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_fallbackArrowBrush = new(Color.FromRgb(180, 180, 180));

    private readonly List<MenuFlyoutItem> _items = new();
    private Popup? _subPopup;
    private Border? _subPopupBorder;
    private MenuPopupScrollHost? _subPopupScrollHost;

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
        if (drawingContext is not DrawingContext dc)
            return;

        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var arrowBrush = ResolveBrush("OneTextSecondary", "TextSecondary", s_fallbackArrowBrush);
        const double arrowSize = 8.0;
        var arrowBounds = new Rect(
            Math.Max(0, RenderSize.Width - 16),
            Math.Max(0, (RenderSize.Height - arrowSize) / 2),
            arrowSize,
            arrowSize);
        ArrowIcons.DrawArrow(dc, arrowBrush, arrowBounds, ArrowIcons.Direction.Right);
    }

    /// <summary>
    /// Shows the sub-menu.
    /// </summary>
    public void ShowSubMenu()
    {
        if (_items.Count == 0)
            return;

        CloseSiblingSubMenus();
        EnsureSubPopup();
        PopulateSubPopup();
        _subPopup!.IsOpen = true;
    }

    /// <summary>
    /// Hides the sub-menu.
    /// </summary>
    public void HideSubMenu()
    {
        CloseDescendantSubMenus();
        _subPopup?.IsOpen = false;
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent == null)
        {
            HideSubMenu();
        }
    }

    private void EnsureSubPopup()
    {
        if (_subPopup != null)
            return;

        _subPopupScrollHost = new MenuPopupScrollHost();
        _subPopupBorder = new Border
        {
            Background = ResolveBrush("OnePopupBackground", "MenuFlyoutPresenterBackground", s_fallbackBackgroundBrush),
            BorderBrush = ResolveBrush("OnePopupBorder", "MenuFlyoutPresenterBorderBrush", s_fallbackBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = _subPopupScrollHost,
            MinWidth = 160
        };

        _subPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Right,
            StaysOpen = false,
            IsLightDismissEnabled = true,
            // Allow nested submenu to use external popup window when it overflows.
            ShouldConstrainToRootBounds = false,
            Child = _subPopupBorder
        };
        _subPopup.Closed += OnSubPopupClosed;
    }

    private void PopulateSubPopup()
    {
        var panel = _subPopupScrollHost?.ItemsPanel;
        if (panel == null)
            return;

        panel.Children.Clear();
        foreach (var item in _items)
        {
            if (item.VisualParent != null)
            {
                item.DetachFromVisualParent();
            }
            panel.Children.Add(item);
        }
    }

    private void OnSubPopupClosed(object? sender, EventArgs e)
    {
        CloseDescendantSubMenus();
        _subPopupScrollHost?.ItemsPanel.Children.Clear();
    }

    private void OnSubItemMouseEnter(object sender, RoutedEventArgs e)
    {
        ShowSubMenu();
    }

    private void OnSubItemMouseLeave(object sender, RoutedEventArgs e)
    {
        // Keep submenu open while pointer moves from item into submenu popup.
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    private void CloseSiblingSubMenus()
    {
        if (VisualParent is not Panel panel)
            return;

        foreach (var child in panel.Children)
        {
            if (child is MenuFlyoutSubItem sibling && !ReferenceEquals(sibling, this))
            {
                sibling.HideSubMenu();
            }
        }
    }

    private void CloseDescendantSubMenus()
    {
        foreach (var item in _items)
        {
            if (item is not MenuFlyoutSubItem childSubItem)
            {
                continue;
            }

            childSubItem.CloseDescendantSubMenus();
            childSubItem._subPopup?.IsOpen = false;
        }
    }
}
