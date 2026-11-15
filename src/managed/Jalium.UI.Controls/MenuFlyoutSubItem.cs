using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a menu item that displays a sub-menu in a MenuFlyout control.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_PopupBorder", Type = typeof(Border))]
[TemplatePart(Name = "PART_ScrollHost", Type = typeof(MenuPopupScrollHost))]
public sealed class MenuFlyoutSubItem : MenuFlyoutItem
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(67, 67, 70));
    private readonly ObservableCollection<MenuFlyoutItem> _items = new();
    private readonly HashSet<MenuFlyoutItem> _ownedItems = new(ReferenceEqualityComparer.Instance);
    private static readonly DependencyPropertyKey SubmenuBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBackground), typeof(Brush), typeof(MenuFlyoutSubItem), new PropertyMetadata(null));
    public static readonly DependencyProperty SubmenuBackgroundProperty = SubmenuBackgroundPropertyKey.DependencyProperty;
    public Brush? SubmenuBackground => (Brush? )GetValue(SubmenuBackgroundProperty);

    private static readonly DependencyPropertyKey SubmenuBorderBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBorderBrush), typeof(Brush), typeof(MenuFlyoutSubItem), new PropertyMetadata(null));
    public static readonly DependencyProperty SubmenuBorderBrushProperty = SubmenuBorderBrushPropertyKey.DependencyProperty;
    public Brush? SubmenuBorderBrush => (Brush? )GetValue(SubmenuBorderBrushProperty);

    private static readonly DependencyPropertyKey SubmenuBorderThicknessPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBorderThickness), typeof(Thickness), typeof(MenuFlyoutSubItem), new PropertyMetadata(new Thickness(1)));
    public static readonly DependencyProperty SubmenuBorderThicknessProperty = SubmenuBorderThicknessPropertyKey.DependencyProperty;
    public Thickness SubmenuBorderThickness => (Thickness)GetValue(SubmenuBorderThicknessProperty)!;

    private static readonly DependencyPropertyKey SubmenuPaddingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuPadding), typeof(Thickness), typeof(MenuFlyoutSubItem), new PropertyMetadata(new Thickness(4)));
    public static readonly DependencyProperty SubmenuPaddingProperty = SubmenuPaddingPropertyKey.DependencyProperty;
    public Thickness SubmenuPadding => (Thickness)GetValue(SubmenuPaddingProperty)!;

    private static readonly DependencyPropertyKey SubmenuCornerRadiusPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuCornerRadius), typeof(CornerRadius), typeof(MenuFlyoutSubItem), new PropertyMetadata(new CornerRadius(8)));
    public static readonly DependencyProperty SubmenuCornerRadiusProperty = SubmenuCornerRadiusPropertyKey.DependencyProperty;
    public CornerRadius SubmenuCornerRadius => (CornerRadius)GetValue(SubmenuCornerRadiusProperty)!;

    private static readonly DependencyPropertyKey IsSubMenuOpenPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSubMenuOpen), typeof(bool), typeof(MenuFlyoutSubItem), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSubMenuOpenProperty = IsSubMenuOpenPropertyKey.DependencyProperty;
    public bool IsSubMenuOpen => (bool)GetValue(IsSubMenuOpenProperty)!;

    private static readonly DependencyPropertyKey HasItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasItems), typeof(bool), typeof(MenuFlyoutSubItem), new PropertyMetadata(false));
    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;
    public bool HasItems => (bool)GetValue(HasItemsProperty)!;

    private static readonly DependencyPropertyKey ItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Items), typeof(IList<MenuFlyoutItem>), typeof(MenuFlyoutSubItem), new PropertyMetadata(null));
    public static readonly DependencyProperty ItemsProperty = ItemsPropertyKey.DependencyProperty;
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
    protected internal override System.Collections.IEnumerator LogicalChildren => _items.GetEnumerator();

    static MenuFlyoutSubItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuFlyoutSubItem), new PropertyMetadata(typeof(MenuFlyoutSubItem)));
    /// <summary>
    /// Initializes a new instance of the MenuFlyoutSubItem class.
    /// </summary>
    public MenuFlyoutSubItem()
    {
        SetValue(ItemsPropertyKey, _items);
        _items.CollectionChanged += OnItemsChanged;
        AddHandler(MouseEnterEvent, new Input.MouseEventHandler(OnSubItemMouseEnter));
        AddHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnSubItemMouseLeave));
    }

    public override void OnApplyTemplate()
    {
        ReleasePopupParts();
        base.OnApplyTemplate();
        _subPopup = GetTemplateChild("PART_Popup") as Popup;
        _subPopupBorder = GetTemplateChild("PART_PopupBorder") as Border;
        _subPopupScrollHost = GetTemplateChild("PART_ScrollHost") as MenuPopupScrollHost;
        if (_subPopup != null)
        {
            _subPopup.SetCurrentValue(Popup.PlacementTargetProperty, this);
            _subPopup.Closed += OnSubPopupClosed;
        }

        ApplySubMenuChrome();
        if (IsSubMenuOpen)
            OpenCurrentPopup();
        UpdateSubmenuPresentation();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleasePopupParts();
        base.OnTemplateContentClearing();
    }

    private void ReleasePopupParts()
    {
        DetachHostPopup();
        if (_subPopup != null)
        {
            _subPopup.Closed -= OnSubPopupClosed;
            _subPopup.IsOpen = false;
        }

        _subPopupScrollHost?.Items.Clear();
        _subPopup = null;
        _subPopupBorder = null;
        _subPopupScrollHost = null;
    }

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        foreach (var item in _items.ToArray())
            ReactivateBindingsRecursive(item);
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
            HideSubMenu();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == MenuForegroundProperty || e.Property == BackgroundProperty || e.Property == BorderBrushProperty || e.Property == FontSizeProperty)
            ApplySubMenuChrome();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in _ownedItems)
            if (!_items.Contains(item))
            {
                if (item is MenuFlyoutSubItem nested)
                    nested.HideSubMenu();
                RemoveLogicalChild(item);
            }

        _ownedItems.Clear();
        foreach (var item in _items)
        {
            AddLogicalChild(item);
            _ownedItems.Add(item);
        }

        SetValue(HasItemsPropertyKey, _items.Count != 0);
        if (_items.Count == 0)
            HideSubMenu();
        else if (IsSubMenuOpen)
            PopulateSubPopup();
    }

    private void UpdateSubmenuPresentation()
    {
        UpdateItemPresentation();
        VisualStateManager.GoToState(this, IsSubMenuOpen ? "Open" : "Closed", false);
    }

    /// <summary>
    /// 子菜单开着时父项保持高亮：指针移进子菜单（外飞时是另一个窗口）后本项的 IsMouseOver 会掉，
    /// 但 Win32/WPF/WinUI 的菜单都让打开子菜单的那一项一直亮着，否则看不出子菜单是从哪儿弹出来的。
    /// </summary>
    protected override bool IsHighlighted => base.IsHighlighted || IsSubMenuOpen;

    /// <summary>
    /// Shows the sub-menu.
    /// </summary>
    public void ShowSubMenu()
    {
        if (!IsEnabled || _items.Count == 0)
            return;
        CloseSiblingSubMenus();
        EnsureSubPopup();
        SetValue(IsSubMenuOpenPropertyKey, true);
        OpenCurrentPopup();
        UpdateSubmenuPresentation();
    }

    private void OpenCurrentPopup()
    {
        PopulateSubPopup();
        ApplySubMenuChrome();
        AttachHostPopup();
        if (_subPopup != null)
            _subPopup.IsOpen = true;
        _subPopupScrollHost?.PrepareForOpen();
    }

    public void HideSubMenu()
    {
        CloseDescendantSubMenus();
        SetValue(IsSubMenuOpenPropertyKey, false);
        if (_subPopup != null)
            _subPopup.IsOpen = false;
        _subPopupScrollHost?.Items.Clear();
        DetachHostPopup();
        UpdateSubmenuPresentation();
    }

    internal void FocusFirstSubMenuItem()
    {
        if (_items.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvokeCritical(() =>
        {
            if (!IsSubMenuOpen || !IsEnabled)
                return;
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

    /// <inheritdoc/>
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
        UpdateDefaultStyle();
        ApplyImplicitStyleIfNeeded();
        ApplyTemplate();
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
        var chrome = ResolveSubMenuChrome();
        SetValue(SubmenuBackgroundPropertyKey, chrome.Background);
        SetValue(SubmenuBorderBrushPropertyKey, chrome.BorderBrush);
        SetValue(SubmenuBorderThicknessPropertyKey, chrome.BorderThickness);
        SetValue(SubmenuPaddingPropertyKey, chrome.Padding);
        SetValue(SubmenuCornerRadiusPropertyKey, chrome.CornerRadius);
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
                Border? frame = null;
                for (var ancestor = host.VisualParent; ancestor is not null && ancestor is not PopupRoot; ancestor = ancestor.VisualParent)
                {
                    if (ancestor is MenuFlyoutPresenter presenter)
                        return presenter.GetChrome();
                    if (frame is null && ancestor is Border hostBorder)
                        frame = hostBorder;
                }

                if (frame is not null)
                    return MenuPopupChrome.FromBorder(frame);
                break;
            }

            node = node.VisualParent;
        }

        return MenuPopupChrome.CreateDefault(ResolveBrush("OnePopupBackground", "MenuFlyoutPresenterBackground", s_fallbackBackgroundBrush), ResolveBrush("OnePopupBorder", "MenuFlyoutPresenterBorderBrush", s_fallbackBorderBrush));
    }

    private void PopulateSubPopup()
    {
        var items = _subPopupScrollHost?.Items;
        if (items == null)
            return;
        items.BeginBatchUpdate();
        try
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (!_items.Any(item => ReferenceEquals(item, items[i])))
                    items.RemoveAt(i);
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item is MenuFlyoutSubItem nested)
                    nested._hostPopupBorder = _subPopupBorder;
                if (i < items.Count && ReferenceEquals(items[i], item))
                    continue;
                int oldIndex = items.IndexOf(item);
                if (oldIndex >= 0)
                    items.RemoveAt(oldIndex);
                else
                    item.DetachFromVisualParent();
                items.Insert(i, item);
                ReactivateBindingsRecursive(item);
            }
        }
        finally
        {
            items.EndBatchUpdate();
        }
    }

    private void OnSubPopupClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _subPopup))
            return;
        SetValue(IsSubMenuOpenPropertyKey, false);
        CloseDescendantSubMenus();
        _subPopupScrollHost?.Items.Clear();
        DetachHostPopup();
        UpdateSubmenuPresentation();
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
        if (TryFindResource(primaryKey)is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey)is Brush secondary)
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

            childSubItem.HideSubMenu();
        }
    }
}
