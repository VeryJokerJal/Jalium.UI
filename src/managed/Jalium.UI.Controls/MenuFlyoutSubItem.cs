using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a menu item that displays a sub-menu in a MenuFlyout control.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public sealed class MenuFlyoutSubItem : MenuFlyoutItem
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_fallbackArrowBrush = new(Color.FromRgb(180, 180, 180));

    private readonly List<MenuFlyoutItem> _items = new();
    private Popup? _subPopup;
    private Border? _subPopupBorder;
    private MenuPopupScrollHost? _subPopupScrollHost;
    private Popup? _hostPopup;
    // 装着本项的那一级子菜单外框（由展开它的父项登记；一级项为 null，走视觉树找 presenter）。
    private Border? _hostPopupBorder;

    /// <summary>
    /// Gets the collection of menu elements in the sub-menu.
    /// </summary>
    public IList<MenuFlyoutItem> Items => _items;

    /// <summary>
    /// 子菜单当前是否打开。
    /// </summary>
    internal bool IsSubMenuOpen => _subPopup?.IsOpen == true;

    /// <summary>
    /// Initializes a new instance of the MenuFlyoutSubItem class.
    /// </summary>
    public MenuFlyoutSubItem()
    {
        AddHandler(MouseEnterEvent, new Input.MouseEventHandler(OnSubItemMouseEnter));
        AddHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnSubItemMouseLeave));
    }

    /// <summary>
    /// 子菜单开着时父项保持高亮：指针移进子菜单（外飞时是另一个窗口）后本项的 IsMouseOver 会掉，
    /// 但 Win32/WPF/WinUI 的菜单都让打开子菜单的那一项一直亮着，否则看不出子菜单是从哪儿弹出来的。
    /// </summary>
    protected override bool IsHighlighted => base.IsHighlighted || IsSubMenuOpen;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

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
        // 每次展开重取外框造型：首次建 popup 时上一级可能还没成型，之后主题 / 宿主样式也会变。
        ApplySubMenuChrome();
        AttachHostPopup();
        _subPopup!.IsOpen = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Hides the sub-menu.
    /// </summary>
    public void HideSubMenu()
    {
        CloseDescendantSubMenus();
        var wasOpen = IsSubMenuOpen;
        _subPopup?.IsOpen = false;
        // 弹窗从未真正打开过（没解析到宿主窗口）时 IsOpen=false 不会触发 Closed，这里兜底退订并重画。
        DetachHostPopup();
        if (wasOpen)
            InvalidateVisual();
    }

    internal void FocusFirstSubMenuItem()
    {
        if (_items.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvokeCritical(() =>
        {
            foreach (var item in _items)
            {
                if (!item.IsEnabled || item.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (item.Focus())
                {
                    return;
                }
            }
        });
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
            Child = _subPopupScrollHost,
            MinWidth = 160
        };
        ApplySubMenuChrome();

        _subPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Right,
            StaysOpen = false,
            IsLightDismissEnabled = true,
            // Allow nested submenu to use external popup window when it overflows.
            // 父菜单已经外飞成独立窗口时，Popup 自己会让子菜单跟着外飞（见 Popup.ResolveHostPlacement）。
            ShouldConstrainToRootBounds = false,
            Child = _subPopupBorder
        };
        _subPopup.Closed += OnSubPopupClosed;
    }

    /// <summary>
    /// The border that frames this item's sub-menu popup, once the sub-menu has been created.
    /// </summary>
    internal Border? SubMenuFrame => _subPopupBorder;

    /// <summary>
    /// Gives this sub-menu the same frame as the level that hosts the item.
    /// </summary>
    /// <remarks>
    /// 各级弹出框各建各的外框，写死常量就会跟宿主脱节（应用改了 MenuFlyout 的圆角，子菜单还是 8）。
    /// 这里向上取承载本项的那一级：一级是 MenuFlyoutPresenter，二级以后是上一级子菜单的 Border。
    /// </remarks>
    private void ApplySubMenuChrome()
    {
        if (_subPopupBorder == null)
            return;

        ResolveSubMenuChrome().ApplyTo(_subPopupBorder);
    }

    private MenuPopupChrome ResolveSubMenuChrome()
    {
        // 更深层的子菜单：父项搬运时已经把自己的外框交过来了，直接照抄。
        if (_hostPopupBorder is { } registered)
        {
            return MenuPopupChrome.FromBorder(registered);
        }

        // 视觉父链：本项 → ItemsPanel →（ScrollViewer 模板内部若干层）→ MenuPopupScrollHost →
        // 承载这一级的外框（MenuFlyout 一级是 presenter，二级以后是上一级子菜单的 Border）。
        // 只认 scroll host 的直接父级 —— 见 Border 就取会撞上 ScrollViewer 模板内部那层透明无圆角的 Border。
        DependencyObject? node = VisualParent;
        while (node != null && node is not PopupRoot)
        {
            if (node is MenuPopupScrollHost host)
            {
                if (host.VisualParent is MenuFlyoutPresenter presenter)
                    return presenter.GetChrome();
                if (host.VisualParent is Border hostBorder)
                    return MenuPopupChrome.FromBorder(hostBorder);
                break;
            }

            node = node.VisualParent;
        }

        return MenuPopupChrome.CreateDefault(
            ResolveBrush("OnePopupBackground", "MenuFlyoutPresenterBackground", s_fallbackBackgroundBrush),
            ResolveBrush("OnePopupBorder", "MenuFlyoutPresenterBorderBrush", s_fallbackBorderBrush));
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

            // 更深一级的子菜单照着这一级的外框做（视觉父链此刻还没接上，只能搬运时直接交底）。
            if (item is MenuFlyoutSubItem nested)
            {
                nested._hostPopupBorder = _subPopupBorder;
            }

            panel.Children.Add(item);
        }
    }

    private void OnSubPopupClosed(object? sender, EventArgs e)
    {
        CloseDescendantSubMenus();
        _subPopupScrollHost?.ItemsPanel.Children.Clear();
        DetachHostPopup();
        InvalidateVisual();
    }

    /// <summary>
    /// 订阅承载本项的那个弹窗（祖先 PopupRoot 的 OwnerPopup）的 Closed：父菜单不论因何关闭
    /// （点外面 light dismiss、Hide()、窗口失活、MenuBar 切分支……），子菜单都必须跟着关。
    /// 否则父菜单的呈现器只是从自己的 PopupRoot 上摘下来，本项的 VisualParent（项面板）并没变，
    /// OnVisualParentChanged 不会触发，子菜单就成了留在屏幕上的孤儿窗口。
    /// </summary>
    private void AttachHostPopup()
    {
        var host = FindHostPopup();
        if (ReferenceEquals(host, _hostPopup))
            return;

        DetachHostPopup();
        if (host == null)
            return;

        _hostPopup = host;
        host.Closed += OnHostPopupClosed;
    }

    private void DetachHostPopup()
    {
        if (_hostPopup == null)
            return;

        _hostPopup.Closed -= OnHostPopupClosed;
        _hostPopup = null;
    }

    private void OnHostPopupClosed(object? sender, EventArgs e)
    {
        HideSubMenu();
    }

    private Popup? FindHostPopup()
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (current is PopupRoot popupRoot)
                return popupRoot.OwnerPopup;
            if (current is Window)
                return null;
        }

        return null;
    }

    private void OnSubItemMouseEnter(object sender, Input.MouseEventArgs e)
    {
        ShowSubMenu();
    }

    private void OnSubItemMouseLeave(object sender, Input.MouseEventArgs e)
    {
        // Keep submenu open while pointer moves from item into submenu popup.
    }

    protected override void InvokeItem()
    {
        ShowSubMenu();
        FocusFirstSubMenuItem();
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

        foreach (UIElement child in panel.Children)
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
