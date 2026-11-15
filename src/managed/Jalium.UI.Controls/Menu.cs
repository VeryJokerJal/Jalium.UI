using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using System.Windows.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a menu bar that contains menu items.
/// </summary>
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
public class Menu : MenuBase
{
    static Menu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Menu), new PropertyMetadata(typeof(Menu)));
    }

    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.MenuAutomationPeer(this);
    }

#region Dependency Properties
    /// <summary>
    /// Identifies the IsMainMenu dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMainMenuProperty = DependencyProperty.Register(nameof(IsMainMenu), typeof(bool), typeof(Menu), new PropertyMetadata(true));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets a value indicating whether this is the main application menu.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMainMenu { get => (bool)GetValue(IsMainMenuProperty)!; set => SetValue(IsMainMenuProperty, value); }

#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "Menu"/> class.
    /// </summary>
    public Menu()
    {
    }

#endregion
#region Item Generation
    /// <inheritdoc/>
    protected override Panel CreateItemsPanel()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
    }

    /// <inheritdoc/>
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new MenuItem();
    }

    /// <inheritdoc/>
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MenuItem || item is Separator;
    }

    /// <inheritdoc/>
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is MenuItem menuItem && item is string text)
        {
            menuItem.Header = text;
        }
    }
#endregion
}

/// <summary>
/// Represents an item in a menu.
/// </summary>
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_Header", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_Icon", Type = typeof(Control))]
[TemplatePart(Name = "PART_Check", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Gesture", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Arrow", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_PopupBorder", Type = typeof(Border))]
[TemplatePart(Name = "PART_ScrollHost", Type = typeof(MenuPopupScrollHost))]
public class MenuItem : HeaderedItemsControl
{
    private protected override bool UsesFallbackItemsHost => false;

    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.MenuItemAutomationPeer(this);
    }

    /// <summary>
    /// The menu that hosts this item, when the item sits at the top level of a dismissable menu.
    /// Set by <see cref = "ContextMenu"/> as it re-parents items into its popup — at that point the
    /// hosting menu is no longer on the item's visual parent chain, so clicking an item could not
    /// otherwise find anything to close. Null for submenu entries (their chain ends at a MenuItem)
    /// and for items in a permanently docked menu bar.
    /// </summary>
    internal MenuBase? OwnerMenu { get; set; }

    // Theme fallbacks for presentation and popup ownership.
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_disabledBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_gestureBrush = new(Color.FromRgb(160, 160, 160));
    private static readonly SolidColorBrush s_submenuFallbackBackground = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_submenuFallbackBorder = new(Color.FromRgb(67, 67, 70));
    private static readonly ItemContainerTemplateSelector s_defaultItemContainerTemplateSelector = new DefaultMenuItemContainerTemplateSelector();
    private static readonly ResourceKey s_separatorStyleKey = new ComponentResourceKey(typeof(MenuItem), nameof(SeparatorStyleKey));
    private static readonly ResourceKey s_submenuHeaderTemplateKey = new ComponentResourceKey(typeof(MenuItem), nameof(SubmenuHeaderTemplateKey));
    private static readonly ResourceKey s_submenuItemTemplateKey = new ComponentResourceKey(typeof(MenuItem), nameof(SubmenuItemTemplateKey));
    private static readonly ResourceKey s_topLevelHeaderTemplateKey = new ComponentResourceKey(typeof(MenuItem), nameof(TopLevelHeaderTemplateKey));
    private static readonly ResourceKey s_topLevelItemTemplateKey = new ComponentResourceKey(typeof(MenuItem), nameof(TopLevelItemTemplateKey));
#region Dependency Properties
    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuItem), new PropertyMetadata(null, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the InputGestureText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty InputGestureTextProperty = DependencyProperty.Register(nameof(InputGestureText), typeof(string), typeof(MenuItem), new PropertyMetadata(null, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsCheckable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckableProperty = DependencyProperty.Register(nameof(IsCheckable), typeof(bool), typeof(MenuItem), new PropertyMetadata(false));
    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(MenuItem), new PropertyMetadata(false, OnIsCheckedChanged));
    /// <summary>
    /// Identifies the IsSubmenuOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSubmenuOpenProperty = DependencyProperty.Register(nameof(IsSubmenuOpen), typeof(bool), typeof(MenuItem), new PropertyMetadata(false, OnIsSubmenuOpenChanged));
    /// <summary>
    /// Identifies the IsSelected read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsSelectedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSelected), typeof(bool), typeof(MenuItem), new PropertyMetadata(false, OnIsSelectedChanged));
    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty = IsSelectedPropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the Role read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey RolePropertyKey = DependencyProperty.RegisterReadOnly(nameof(Role), typeof(MenuItemRole), typeof(MenuItem), new PropertyMetadata(MenuItemRole.SubmenuItem, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the Role dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RoleProperty = RolePropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the StaysOpenOnClick dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StaysOpenOnClickProperty = DependencyProperty.Register(nameof(StaysOpenOnClick), typeof(bool), typeof(MenuItem), new PropertyMetadata(false));
    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty = ButtonBase.CommandProperty.AddOwner(typeof(MenuItem), new PropertyMetadata(null, OnCommandChanged));
    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty = ButtonBase.CommandParameterProperty.AddOwner(typeof(MenuItem), new PropertyMetadata(null, OnCommandStateChanged));
    /// <summary>
    /// Identifies the CommandTarget dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandTargetProperty = ButtonBase.CommandTargetProperty.AddOwner(typeof(MenuItem), new PropertyMetadata(null, OnCommandStateChanged));
    private static readonly DependencyPropertyKey IsHighlightedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsHighlighted), typeof(bool), typeof(MenuItem), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsHighlighted read-only dependency property.
    /// </summary>
    public static readonly DependencyProperty IsHighlightedProperty = IsHighlightedPropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the IsPressed read-only dependency property.
    /// </summary>
    public new static readonly DependencyProperty IsPressedProperty = UIElement.IsPressedProperty.AddOwner(typeof(MenuItem));
    private static readonly DependencyPropertyKey IsSuspendingPopupAnimationPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSuspendingPopupAnimation), typeof(bool), typeof(MenuItem), new PropertyMetadata(false));
    /// <summary>
    /// Identifies the IsSuspendingPopupAnimation read-only dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSuspendingPopupAnimationProperty = IsSuspendingPopupAnimationPropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the ItemContainerTemplateSelector dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemContainerTemplateSelectorProperty = DependencyProperty.Register(nameof(ItemContainerTemplateSelector), typeof(ItemContainerTemplateSelector), typeof(MenuItem), new PropertyMetadata(s_defaultItemContainerTemplateSelector));
    /// <summary>
    /// Identifies the UsesItemContainerTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty UsesItemContainerTemplateProperty = MenuBase.UsesItemContainerTemplateProperty.AddOwner(typeof(MenuItem));
#endregion
#region Routed Events
    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));
    /// <summary>
    /// Identifies the Checked routed event.
    /// </summary>
    public static readonly RoutedEvent CheckedEvent = EventManager.RegisterRoutedEvent(nameof(Checked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));
    /// <summary>
    /// Identifies the Unchecked routed event.
    /// </summary>
    public static readonly RoutedEvent UncheckedEvent = EventManager.RegisterRoutedEvent(nameof(Unchecked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));
    /// <summary>
    /// Identifies the SubmenuOpened routed event.
    /// </summary>
    public static readonly RoutedEvent SubmenuOpenedEvent = EventManager.RegisterRoutedEvent(nameof(SubmenuOpened), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));
    /// <summary>
    /// Identifies the SubmenuClosed routed event.
    /// </summary>
    public static readonly RoutedEvent SubmenuClosedEvent = EventManager.RegisterRoutedEvent(nameof(SubmenuClosed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));
    /// <summary>
    /// Occurs when the menu item is clicked.
    /// </summary>
    public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }

    /// <summary>
    /// Occurs when the menu item is checked.
    /// </summary>
    public event RoutedEventHandler Checked { add => AddHandler(CheckedEvent, value); remove => RemoveHandler(CheckedEvent, value); }

    /// <summary>
    /// Occurs when the menu item is unchecked.
    /// </summary>
    public event RoutedEventHandler Unchecked { add => AddHandler(UncheckedEvent, value); remove => RemoveHandler(UncheckedEvent, value); }

    /// <summary>
    /// Occurs when the submenu is opened.
    /// </summary>
    public event RoutedEventHandler SubmenuOpened { add => AddHandler(SubmenuOpenedEvent, value); remove => RemoveHandler(SubmenuOpenedEvent, value); }

    /// <summary>
    /// Occurs when the submenu is closed.
    /// </summary>
    public event RoutedEventHandler SubmenuClosed { add => AddHandler(SubmenuClosedEvent, value); remove => RemoveHandler(SubmenuClosedEvent, value); }

#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the icon displayed with the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>
    /// Gets or sets the input gesture text (keyboard shortcut) displayed with the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public string? InputGestureText { get => (string? )GetValue(InputGestureTextProperty); set => SetValue(InputGestureTextProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether the menu item can be checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCheckable { get => (bool)GetValue(IsCheckableProperty)!; set => SetValue(IsCheckableProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether the menu item is checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty)!; set => SetValue(IsCheckedProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether the submenu is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSubmenuOpen { get => (bool)GetValue(IsSubmenuOpenProperty)!; set => SetValue(IsSubmenuOpenProperty, value); }

    /// <summary>
    /// Gets a value indicating whether this menu item is currently selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected => (bool)GetValue(IsSelectedProperty)!;

    /// <summary>
    /// Gets the visual role of this menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public MenuItemRole Role => (MenuItemRole)GetValue(RoleProperty)!;

    /// <summary>
    /// Gets or sets a value indicating whether the menu stays open when clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool StaysOpenOnClick { get => (bool)GetValue(StaysOpenOnClickProperty)!; set => SetValue(StaysOpenOnClickProperty, value); }

    /// <summary>
    /// Gets or sets the command invoked when this menu item is clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command { get => (ICommand? )GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>
    /// Gets or sets the value passed to <see cref = "Command"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>
    /// Gets or sets the element on which a routed command is executed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public IInputElement? CommandTarget { get => (IInputElement? )GetValue(CommandTargetProperty); set => SetValue(CommandTargetProperty, value); }

    /// <summary>
    /// Gets a value indicating whether this menu item is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty)!; protected set => SetValue(IsHighlightedPropertyKey, value); }

    /// <summary>
    /// Gets a value indicating whether this menu item is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public new bool IsPressed { get => base.IsPressed; protected set => base.SetIsPressed(value); }
    /// <summary>
    /// Gets a value indicating whether submenu popup animation is temporarily suspended.
    /// </summary>
    public bool IsSuspendingPopupAnimation => (bool)GetValue(IsSuspendingPopupAnimationProperty)!;
    /// <summary>
    /// Gets or sets the selector used to create submenu item containers.
    /// </summary>
    public ItemContainerTemplateSelector? ItemContainerTemplateSelector { get => (ItemContainerTemplateSelector? )GetValue(ItemContainerTemplateSelectorProperty); set => SetValue(ItemContainerTemplateSelectorProperty, value); }
    /// <summary>
    /// Gets or sets whether submenu item containers are created from an item-container template.
    /// </summary>
    public bool UsesItemContainerTemplate { get => (bool)GetValue(UsesItemContainerTemplateProperty)!; set => SetValue(UsesItemContainerTemplateProperty, value); }
    /// <summary>Gets the resource key for the default separator style.</summary>
    public static ResourceKey SeparatorStyleKey => s_separatorStyleKey;
    /// <summary>Gets the resource key for the submenu-header template.</summary>
    public static ResourceKey SubmenuHeaderTemplateKey => s_submenuHeaderTemplateKey;
    /// <summary>Gets the resource key for the submenu-item template.</summary>
    public static ResourceKey SubmenuItemTemplateKey => s_submenuItemTemplateKey;
    /// <summary>Gets the resource key for the top-level-header template.</summary>
    public static ResourceKey TopLevelHeaderTemplateKey => s_topLevelHeaderTemplateKey;
    /// <summary>Gets the resource key for the top-level-item template.</summary>
    public static ResourceKey TopLevelItemTemplateKey => s_topLevelItemTemplateKey;
    /// <summary>
    /// Gets a value indicating whether this menu item has sub-items.
    /// </summary>
    public new bool HasItems => base.HasItems;

#endregion
#region Private Fields
    private bool _isPointerOverMenuItem;
    private bool _isUpdatingSubmenuOpen;
    private bool _commandBaseIsEnabled = true;
    private bool _isUpdatingCommandEnabled;
    private readonly List<UIElement> _retainedSubmenuContainers = new();
    private static readonly DependencyPropertyKey SubmenuBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBackground), typeof(Brush), typeof(MenuItem), new PropertyMetadata(null));
    public static readonly DependencyProperty SubmenuBackgroundProperty = SubmenuBackgroundPropertyKey.DependencyProperty;
    public Brush? SubmenuBackground => (Brush? )GetValue(SubmenuBackgroundProperty);

    private static readonly DependencyPropertyKey SubmenuBorderBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBorderBrush), typeof(Brush), typeof(MenuItem), new PropertyMetadata(null));
    public static readonly DependencyProperty SubmenuBorderBrushProperty = SubmenuBorderBrushPropertyKey.DependencyProperty;
    public Brush? SubmenuBorderBrush => (Brush? )GetValue(SubmenuBorderBrushProperty);

    private static readonly DependencyPropertyKey SubmenuBorderThicknessPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuBorderThickness), typeof(Thickness), typeof(MenuItem), new PropertyMetadata(new Thickness(1)));
    public static readonly DependencyProperty SubmenuBorderThicknessProperty = SubmenuBorderThicknessPropertyKey.DependencyProperty;
    public Thickness SubmenuBorderThickness => (Thickness)GetValue(SubmenuBorderThicknessProperty)!;

    private static readonly DependencyPropertyKey SubmenuPaddingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuPadding), typeof(Thickness), typeof(MenuItem), new PropertyMetadata(new Thickness(4)));
    public static readonly DependencyProperty SubmenuPaddingProperty = SubmenuPaddingPropertyKey.DependencyProperty;
    public Thickness SubmenuPadding => (Thickness)GetValue(SubmenuPaddingProperty)!;

    private static readonly DependencyPropertyKey SubmenuCornerRadiusPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuCornerRadius), typeof(CornerRadius), typeof(MenuItem), new PropertyMetadata(new CornerRadius(8)));
    public static readonly DependencyProperty SubmenuCornerRadiusProperty = SubmenuCornerRadiusPropertyKey.DependencyProperty;
    public CornerRadius SubmenuCornerRadius => (CornerRadius)GetValue(SubmenuCornerRadiusProperty)!;

    private static readonly DependencyPropertyKey SubmenuPlacementPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmenuPlacement), typeof(PlacementMode), typeof(MenuItem), new PropertyMetadata(PlacementMode.Right));
    public static readonly DependencyProperty SubmenuPlacementProperty = SubmenuPlacementPropertyKey.DependencyProperty;
    public PlacementMode SubmenuPlacement => (PlacementMode)GetValue(SubmenuPlacementProperty)!;

    private Popup? _submenuPopup;
    private Border? _submenuBorder;
    private MenuPopupScrollHost? _submenuScrollHost;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, PreparedItem> _preparedItems = new();
    private sealed record PreparedItem(object Value);
    // 装着本项的那一级弹出框外框（由展开它的父项在搬运时登记；顶层项为 null，走 OwnerMenu）。
    private Border? _hostPopupBorder;
#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "MenuItem"/> class.
    /// </summary>
    static MenuItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuItem), new PropertyMetadata(typeof(MenuItem)));
        FocusableProperty.OverrideMetadata(typeof(MenuItem), new PropertyMetadata(true));
    }

    public MenuItem()
    {
        ((System.Collections.Specialized.INotifyCollectionChanged)Items).CollectionChanged += OnItemsCollectionChanged;
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        TouchHelper.SetIsRippleEnabled(this, true);
        _commandBaseIsEnabled = IsEnabled;
        UpdateRole();
        UpdateIsSelected();
        Loaded += (_, _) => UpdateItemPresentation();
    }

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs)
            return;
        if (!TouchHelper.GetIsTouchInteractive(this))
            return;
        // Reuse the same submenu-open / click logic as the mouse path by
        // synthesising a left-button MouseButtonEventArgs on this element.
        var synthetic = new MouseButtonEventArgs(UIElement.MouseDownEvent, touchArgs.GetTouchPoint(this).Position, MouseButton.Left, MouseButtonState.Pressed, clickCount: 1, leftButton: MouseButtonState.Pressed, middleButton: MouseButtonState.Released, rightButton: MouseButtonState.Released, xButton1: MouseButtonState.Released, xButton2: MouseButtonState.Released, modifiers: ModifierKeys.None, timestamp: Environment.TickCount)
        {
            Source = this
        };
        try
        {
            OnMouseDownHandler(this, synthetic);
        }
        finally
        {
            IsPressed = false;
        }

        e.Handled = true;
    }

#endregion
#region Item Generation
    /// <inheritdoc/>
    protected override FrameworkElement GetContainerForItem(object item)
    {
        var retained = _retainedSubmenuContainers.FirstOrDefault(candidate => candidate is FrameworkElement && _preparedItems.TryGetValue(candidate, out var prepared) && (ReferenceEquals(prepared.Value, item) || item is ValueType && Equals(prepared.Value, item)));
        if (retained is FrameworkElement existing)
        {
            _retainedSubmenuContainers.Remove(existing);
            existing.DetachFromVisualParent();
            return existing;
        }

        if (UsesItemContainerTemplate)
        {
            var template = ItemContainerTemplateSelector?.SelectTemplate(item, this);
            var generated = template?.LoadContent();
            if (generated is MenuItem or Separator)
            {
                return generated;
            }

            if (generated != null)
            {
                throw new InvalidOperationException("An item-container template for a MenuItem must create a MenuItem or Separator.");
            }
        }

        return new MenuItem();
    }

    /// <inheritdoc/>
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MenuItem or Separator;
    }

    /// <inheritdoc/>
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        _preparedItems.Remove(element);
        _preparedItems.Add(element, new PreparedItem(item));
        if (element is MenuItem menuItem && !ReferenceEquals(menuItem, item) && menuItem.Header == null)
        {
            menuItem.Header = item;
        }
    }

#endregion
#region Input Handling
    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;
        if (e.ChangedButton == MouseButton.Left)
        {
            IsPressed = true;
            if (HasItems)
            {
                if (IsTopLevelMenuItem)
                {
                    if (IsSubmenuOpen)
                    {
                        IsSubmenuOpen = false;
                    }
                    else
                    {
                        CloseSiblingSubmenus();
                        IsSubmenuOpen = true;
                    }
                }
                else
                {
                    var willOpen = !IsSubmenuOpen;
                    IsSubmenuOpen = willOpen;
                    if (willOpen)
                    {
                        CloseSiblingSubmenus();
                    }
                }
            }
            else
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            IsPressed = false;
        }
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        if (!IsEnabled)
        {
            _isPointerOverMenuItem = false;
            UpdateIsSelected();
            return;
        }

        _isPointerOverMenuItem = IsEnabled;
        UpdateIsSelected();
        if (!IsTopLevelMenuItem)
        {
            // In submenus, hover-switching is expected behavior.
            CloseSiblingSubmenus();
            if (HasItems)
            {
                IsSubmenuOpen = true;
            }

            return;
        }

        // Top-level: only switch/open on hover once menu mode is active
        // (i.e., one top-level submenu is already open by click/keyboard).
        if (IsTopLevelMenuModeActive())
        {
            CloseSiblingSubmenus();
            if (HasItems)
            {
                IsSubmenuOpen = true;
            }
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        _isPointerOverMenuItem = false;
        IsPressed = false;
        UpdateIsSelected();
    // Don't close submenu here 闁?let sibling mouse enter or popup dismiss handle it.
    // This prevents the submenu from closing when the mouse moves from the
    // MenuItem into the popup content area.
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
            return;
        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            if (HasItems)
            {
                IsSubmenuOpen = true;
                FocusFirstSubmenuItem();
            }
            else
            {
                OnClick();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (HasItems)
            {
                IsSubmenuOpen = true;
                FocusFirstSubmenuItem();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (IsSubmenuOpen)
            {
                IsSubmenuOpen = false;
            }
            else
            {
                // Close parent submenu and return focus to parent
                var parent = FindParentMenuItem();
                if (parent != null)
                {
                    parent.IsSubmenuOpen = false;
                    parent.Focus();
                }
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            FocusSiblingMenuItem(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            // If top-level, open submenu; otherwise navigate to next sibling
            var isTopLevel = VisualParent is Panel p && p.VisualParent is Menu;
            if (isTopLevel && HasItems)
            {
                IsSubmenuOpen = true;
                FocusFirstSubmenuItem();
            }
            else
            {
                FocusSiblingMenuItem(1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (IsSubmenuOpen)
            {
                IsSubmenuOpen = false;
            }
            else
            {
                CloseParentMenus();
            }

            e.Handled = true;
        }
    }

    private void FocusFirstSubmenuItem()
    {
        var items = _submenuScrollHost?.Items;
        if (items == null)
            return;
        foreach (UIElement child in items)
        {
            if (child is MenuItem item && item.IsEnabled)
            {
                item.Focus();
                return;
            }
        }
    }

    private void FocusSiblingMenuItem(int direction)
    {
        // Find our sibling items in the parent panel
        if (VisualParent is not Panel parentPanel)
            return;
        var siblings = parentPanel.Children;
        var currentIndex = -1;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i] == this)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
            return;
        // Search for next enabled MenuItem in the specified direction, wrapping around
        for (int i = 1; i < siblings.Count; i++)
        {
            var nextIndex = (currentIndex + direction * i % siblings.Count + siblings.Count) % siblings.Count;
            if (siblings[nextIndex] is MenuItem nextItem && nextItem.IsEnabled)
            {
                nextItem.Focus();
                return;
            }
        }
    }

    internal void InvokeFromAutomation()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Cannot invoke a disabled MenuItem.");
        OnClick();
    }

    /// <summary>
    /// Called when the menu item is clicked.
    /// </summary>
    protected virtual void OnClick()
    {
        if (IsCheckable)
        {
            SetCurrentValue(IsCheckedProperty, !IsChecked);
        }

        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        ExecuteCommand();
        if (!StaysOpenOnClick)
        {
            CloseParentMenus();
        }
    }

    /// <summary>
    /// Raises the <see cref = "Checked"/> routed event.
    /// </summary>
    protected virtual void OnChecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref = "Unchecked"/> routed event.
    /// </summary>
    protected virtual void OnUnchecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref = "SubmenuOpened"/> routed event.
    /// </summary>
    protected virtual void OnSubmenuOpened(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref = "SubmenuClosed"/> routed event.
    /// </summary>
    protected virtual void OnSubmenuClosed(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    private void ExecuteCommand()
    {
        var command = Command;
        if (command == null)
            return;
        var parameter = CommandParameter;
        if (command is RoutedCommand routedCommand)
        {
            var target = CommandTarget ?? this;
            if (routedCommand.CanExecute(parameter, target))
            {
                routedCommand.Execute(parameter, target);
            }
        }
        else if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private void CloseParentMenus()
    {
        // Close all parent menus.
        // When items are hosted in a Popup, the visual parent chain is:
        //   MenuItem -> StackPanel (_submenuScrollHost.ItemsPanel) -> MenuPopupScrollHost -> Border (_submenuBorder) -> PopupRoot -> ...
        // We need to find parent MenuItems through the popup's PlacementTarget chain.
        var topLevel = this;
        var current = FindParentMenuItem();
        while (current != null)
        {
            current.IsSubmenuOpen = false;
            topLevel = current;
            current = current.FindParentMenuItem();
        }

        // Walking the parent chain only collapses the submenus owned by *MenuItems*. The menu that
        // hosts the top-level item — a ContextMenu's popup — is not a MenuItem and is not on the
        // visual parent chain either (its items are re-parented into the popup), so without this it
        // never closes: the user picks "Copy", the command runs, and the menu just sits there.
        topLevel.OwnerMenu?.DismissMenu();
    }

    private void CloseSubmenuBranch()
    {
        CloseDescendantSubmenus();
        if (IsSubmenuOpen)
        {
            IsSubmenuOpen = false;
        }
    }

    private void CloseDescendantSubmenus()
    {
        foreach (var item in Items)
        {
            if (item is not MenuItem childMenuItem)
            {
                continue;
            }

            childMenuItem.CloseDescendantSubmenus();
            if (childMenuItem.IsSubmenuOpen)
            {
                childMenuItem.IsSubmenuOpen = false;
            }
        }
    }

    /// <summary>
    /// Finds the parent MenuItem that owns the submenu popup containing this item.
    /// Walks the visual tree up, and if a Popup is found, uses its PlacementTarget.
    /// </summary>
    private MenuItem? FindParentMenuItem()
    {
        Visual? parent = ParentVisual;
        while (parent != null)
        {
            if (parent is MenuItem menuItem)
            {
                return menuItem;
            }

            // If we reach a PopupRoot, follow the Popup's PlacementTarget
            if (parent is PopupRoot popupRoot)
            {
                // PopupRoot is owned by a Popup 闁?get the Popup's PlacementTarget
                var popup = popupRoot.OwnerPopup;
                if (popup?.PlacementTarget is MenuItem ownerItem)
                {
                    return ownerItem;
                }

                // Fallback: walk up from the PlacementTarget
                parent = popup?.PlacementTarget as Visual;
                continue;
            }

            parent = parent.VisualParent;
        }

        return null;
    }

#endregion
#region Visual Tree
    /// <inheritdoc/>
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateRole();
    }

    // Submenu containers live in a hidden, zero-sized template ItemsPresenter.
    // Only the menu row is visible; the existing popup pipeline moves containers
    // from that presenter into the active popup and returns them on close.
    protected override int VisualChildrenCount => Template != null ? base.VisualChildrenCount : 0;

    protected override Visual? GetVisualChild(int index) => Template != null ? base.GetVisualChild(index) : throw new ArgumentOutOfRangeException(nameof(index));
    internal override void OnTemplateContentClearing()
    {
        ReleaseSubmenuParts();
        base.OnTemplateContentClearing();
    }

    public override void OnApplyTemplate()
    {
        ReleaseSubmenuParts();
        base.OnApplyTemplate();
        _submenuPopup = GetTemplateChild("PART_Popup") as Popup;
        _submenuBorder = GetTemplateChild("PART_PopupBorder") as Border;
        _submenuScrollHost = GetTemplateChild("PART_ScrollHost") as MenuPopupScrollHost;
        if (_submenuPopup != null)
        {
            _submenuPopup.SetCurrentValue(Popup.PlacementTargetProperty, this);
            _submenuPopup.Closed += OnSubmenuPopupClosed;
        }

        if (GetTemplateChild("PART_ItemsPresenter")is ItemsPresenter presenter)
            presenter.Measure(new Size(0, 0));
        UpdateRole();
        UpdateItemPresentation();
        ApplySubmenuChrome();
        if (IsSubmenuOpen && !_isUpdatingSubmenuOpen)
        {
            PopulateSubmenuPopup();
            if (_submenuPopup != null)
                _submenuPopup.IsOpen = true;
            _submenuScrollHost?.PrepareForOpen();
        }
    }

    private void ReleaseSubmenuParts()
    {
        if (IsSubmenuOpen && ItemsHost is { } staging)
            foreach (UIElement element in staging.Children)
                if (!_retainedSubmenuContainers.Contains(element))
                    _retainedSubmenuContainers.Add(element);
        if (IsSubmenuOpen && _submenuScrollHost != null)
            foreach (var element in _submenuScrollHost.Items)
                if (!_retainedSubmenuContainers.Contains(element))
                    _retainedSubmenuContainers.Add(element);
        ReturnItemsFromPopup();
        if (_submenuPopup != null)
        {
            _submenuPopup.Closed -= OnSubmenuPopupClosed;
            _submenuPopup.IsOpen = false;
        }

        _submenuPopup = null;
        _submenuBorder = null;
        _submenuScrollHost = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateRole();
        UpdateItemPresentation();
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize) => Template != null ? base.ArrangeOverride(finalSize) : finalSize;
    protected override void RenderTemplatedBackground(DrawingContext context)
    {
        UpdateItemPresentation();
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            base.RenderTemplatedBackground(context);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == ForegroundProperty || args.Property == FontSizeProperty || args.Property == FontFamilyProperty || args.Property == FontWeightProperty || args.Property == BackgroundProperty || args.Property == PaddingProperty || args.Property == IsCheckableProperty || args.Property == IsPressedProperty)
            UpdateItemPresentation();
    }

#endregion
#region Presentation
    private static readonly DependencyPropertyKey ItemForegroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemForeground), typeof(Brush), typeof(MenuItem), new PropertyMetadata(s_whiteBrush));
    public static readonly DependencyProperty ItemForegroundProperty = ItemForegroundPropertyKey.DependencyProperty;
    public Brush? ItemForeground => (Brush? )GetValue(ItemForegroundProperty);

    private static readonly DependencyPropertyKey GestureForegroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(GestureForeground), typeof(Brush), typeof(MenuItem), new PropertyMetadata(s_gestureBrush));
    public static readonly DependencyProperty GestureForegroundProperty = GestureForegroundPropertyKey.DependencyProperty;
    public Brush? GestureForeground => (Brush? )GetValue(GestureForegroundProperty);

    private static readonly DependencyPropertyKey ItemFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontSize), typeof(double), typeof(MenuItem), new PropertyMetadata(14.0));
    public static readonly DependencyProperty ItemFontSizeProperty = ItemFontSizePropertyKey.DependencyProperty;
    public double ItemFontSize => (double)GetValue(ItemFontSizeProperty)!;

    private static readonly DependencyPropertyKey ItemFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontFamily), typeof(FontFamily), typeof(MenuItem), new PropertyMetadata(FontFamilyProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontFamilyProperty = ItemFontFamilyPropertyKey.DependencyProperty;
    public FontFamily ItemFontFamily => (FontFamily)GetValue(ItemFontFamilyProperty)!;

    private static readonly DependencyPropertyKey ItemFontWeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontWeight), typeof(FontWeight), typeof(MenuItem), new PropertyMetadata(FontWeightProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontWeightProperty = ItemFontWeightPropertyKey.DependencyProperty;
    public FontWeight ItemFontWeight => (FontWeight)GetValue(ItemFontWeightProperty)!;

    private static readonly DependencyPropertyKey IconFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IconFontSize), typeof(double), typeof(MenuItem), new PropertyMetadata(12.0));
    public static readonly DependencyProperty IconFontSizeProperty = IconFontSizePropertyKey.DependencyProperty;
    public double IconFontSize => (double)GetValue(IconFontSizeProperty)!;

    private static readonly DependencyPropertyKey ItemContentPaddingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemContentPadding), typeof(Thickness), typeof(MenuItem), new PropertyMetadata(default(Thickness)));
    public static readonly DependencyProperty ItemContentPaddingProperty = ItemContentPaddingPropertyKey.DependencyProperty;
    public Thickness ItemContentPadding => (Thickness)GetValue(ItemContentPaddingProperty)!;

    private static readonly DependencyPropertyKey IconColumnSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IconColumnSize), typeof(double), typeof(MenuItem), new PropertyMetadata(24.0));
    public static readonly DependencyProperty IconColumnSizeProperty = IconColumnSizePropertyKey.DependencyProperty;
    public double IconColumnSize => (double)GetValue(IconColumnSizeProperty)!;

    private static readonly DependencyPropertyKey GestureColumnSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(GestureColumnSize), typeof(double), typeof(MenuItem), new PropertyMetadata(0.0));
    public static readonly DependencyProperty GestureColumnSizeProperty = GestureColumnSizePropertyKey.DependencyProperty;
    public double GestureColumnSize => (double)GetValue(GestureColumnSizeProperty)!;

    private static readonly DependencyPropertyKey ArrowColumnSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ArrowColumnSize), typeof(double), typeof(MenuItem), new PropertyMetadata(0.0));
    public static readonly DependencyProperty ArrowColumnSizeProperty = ArrowColumnSizePropertyKey.DependencyProperty;
    public double ArrowColumnSize => (double)GetValue(ArrowColumnSizeProperty)!;

    private static readonly DependencyPropertyKey ShowCheckPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowCheck), typeof(bool), typeof(MenuItem), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowCheckProperty = ShowCheckPropertyKey.DependencyProperty;
    public bool ShowCheck => (bool)GetValue(ShowCheckProperty)!;

    private void UpdateItemPresentation()
    {
        bool topLevel = IsTopLevelMenuItem;
        SetValue(ItemForegroundPropertyKey, ResolveAuthoredForegroundBrush() ?? (IsEnabled ? ResolvePrimaryTextBrush() : ResolveMenuBrush("TextDisabled", s_disabledBrush)));
        SetValue(GestureForegroundPropertyKey, ResolveAuthoredForegroundBrush() ?? ResolveMenuBrush("TextSecondary", s_gestureBrush));
        SetValue(ItemFontSizePropertyKey, FontSize);
        SetValue(ItemFontFamilyPropertyKey, FontFamily);
        SetValue(ItemFontWeightPropertyKey, FontWeight);
        SetValue(IconFontSizePropertyKey, FontSize * (12.0 / 14.0));
        SetValue(ItemContentPaddingPropertyKey, !topLevel && GetEffectiveValueLayer(PaddingProperty) == DependencyValueStore.Layer.StyleSetter ? default(Thickness) : Padding);
        SetValue(IconColumnSizePropertyKey, topLevel ? 0.0 : 24.0);
        SetValue(GestureColumnSizePropertyKey, !topLevel && !string.IsNullOrEmpty(InputGestureText) ? 80.0 : 0.0);
        SetValue(ArrowColumnSizePropertyKey, !topLevel && HasItems ? 16.0 : 0.0);
        SetValue(ShowCheckPropertyKey, !topLevel && IsCheckable && IsChecked);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsSubmenuOpen ? "SubmenuOpen" : IsPressed ? "Pressed" : IsKeyboardFocused ? "Focused" : IsSelected ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsChecked ? "Checked" : "Unchecked", false);
        VisualStateManager.GoToState(this, IsSubmenuOpen ? "Open" : "Closed", false);
        SetValue(SubmenuPlacementPropertyKey, topLevel ? PlacementMode.Bottom : PlacementMode.Right);
        if (IsSubmenuOpen)
            ApplySubmenuChrome();
    }

    private Brush ResolvePrimaryTextBrush()
    {
        if (ResolveAuthoredForegroundBrush()is { } authored)
            return authored;
        var valueSource = DependencyPropertyHelper.GetValueSource(this, Control.ForegroundProperty).BaseValueSource;
        if (Foreground != null && valueSource != BaseValueSource.Default)
        {
            return Foreground;
        }

        return ResolveMenuBrush("TextPrimary", s_whiteBrush);
    }

    private Brush? ResolveAuthoredForegroundBrush() => Foreground != null && (HasLocalOrAnimatedValue(ForegroundProperty) || GetEffectiveValueLayer(ForegroundProperty)is (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) || DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource == BaseValueSource.Inherited) ? Foreground : null;
    private SolidColorBrush ResolveMenuBrush(string resourceKey, SolidColorBrush fallback)
    {
        return TryFindResource(resourceKey) as SolidColorBrush ?? fallback;
    }

#endregion
#region Submenu Popup
    /// <summary>
    /// Gets a value indicating whether this is a top-level menu item (direct child of a Menu bar).
    /// </summary>
    private bool IsTopLevelMenuItem => ItemsControlFromItemContainer(this) is Menu;

    /// <summary>
    /// Ensures the submenu popup is created.
    /// </summary>
    private void EnsureSubmenuPopup()
    {
        UpdateDefaultStyle();
        ApplyImplicitStyleIfNeeded();
        ApplyTemplate();
    }

    /// <summary>
    /// The border that frames this item's submenu popup, once the submenu has been created.
    /// </summary>
    internal Border? SubmenuFrame => _submenuBorder;

    /// <summary>
    /// Gives this item's submenu popup the same frame as the level that hosts the item.
    /// </summary>
    /// <remarks>
    /// 每一级子菜单都是自己建的独立弹出框，以前这里写死 CornerRadius=4 / Padding=2，而
    /// ContextMenu 的主题 Style 给的是 14 / 5 —— 同一次交互里一级是大圆角、二级几乎是直角。
    /// 改成向宿主取造型后整条链一致；每次展开都重取，主题切换 / 宿主换样式都跟得上。
    /// </remarks>
    private void ApplySubmenuChrome()
    {
        var chrome = ResolveSubmenuChrome();
        SetValue(SubmenuBackgroundPropertyKey, chrome.Background);
        SetValue(SubmenuBorderBrushPropertyKey, chrome.BorderBrush);
        SetValue(SubmenuBorderThicknessPropertyKey, chrome.BorderThickness);
        SetValue(SubmenuPaddingPropertyKey, chrome.Padding);
        SetValue(SubmenuCornerRadiusPropertyKey, chrome.CornerRadius);
    }

    private MenuPopupChrome ResolveSubmenuChrome()
    {
        // 承载「我」的那个弹出框就是上一级子菜单的外框：直接照抄，造型逐级向下传。
        if ((_hostPopupBorder ?? FindParentMenuItem()?._submenuBorder)is { } hostBorder)
        {
            return MenuPopupChrome.FromBorder(hostBorder);
        }

        // 顶层项：跟宿主菜单自己的弹出框走（ContextMenu 有；常驻菜单栏没有，返回 null）。
        if (FindOwnerMenu()?.GetPopupChrome()is { } ownerChrome)
        {
            return ownerChrome;
        }

        return MenuPopupChrome.CreateDefault(ResolveMenuBrush("MenuFlyoutPresenterBackground", s_submenuFallbackBackground), ResolveMenuBrush("MenuFlyoutPresenterBorderBrush", s_submenuFallbackBorder));
    }

    /// <summary>
    /// Walks up the submenu chain to the top-level item and returns the menu that hosts it.
    /// </summary>
    private MenuBase? FindOwnerMenu()
    {
        var item = this;
        while (true)
        {
            if (item.OwnerMenu != null)
            {
                return item.OwnerMenu;
            }

            var parent = item.FindParentMenuItem();
            if (parent == null)
            {
                return null;
            }

            item = parent;
        }
    }

    /// <summary>
    /// Populates the submenu popup panel with the MenuItem's child items.
    /// Items are detached from the ItemsHost panel and moved into the popup panel.
    /// </summary>
    private void PopulateSubmenuPopup()
    {
        var items = _submenuScrollHost?.Items;
        if (items == null)
            return;
        // The inline host can contain only the newly inserted entries while
        // existing entries are already in the popup. Match prepared containers
        // to their data instead of treating that partial host as an index map.
        var existingContainers = items.ToList();
        foreach (var retained in _retainedSubmenuContainers)
            if (!existingContainers.Contains(retained))
                existingContainers.Add(retained);
        _retainedSubmenuContainers.Clear();
        var itemsHost = ItemsHost;
        if (itemsHost != null)
        {
            foreach (UIElement child in itemsHost.Children)
                if (!existingContainers.Contains(child))
                    existingContainers.Add(child);
            itemsHost.Children.Clear();
        }

        items.Clear();
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            var element = item as UIElement ?? existingContainers.FirstOrDefault(candidate => _preparedItems.TryGetValue(candidate, out var prepared) && (ReferenceEquals(prepared.Value, item) || item is ValueType && Equals(prepared.Value, item)));
            element ??= CreateSubmenuElement(item);
            if (element != null)
                existingContainers.Remove(element);
            if (element != null)
            {
                // Ensure the element is detached from any previous visual parent
                if (element.VisualParent != null)
                {
                    element.DetachFromVisualParent();
                }

                // 告诉子项它现在被哪一级弹出框装着：子项展开自己的子菜单时照着这个外框做，
                // 整条链的圆角 / 内边距才不会各写各的。视觉父链此时还没接上（popup 尚未实体化），
                // 只能像 ContextMenu 登记 OwnerMenu 那样在搬运时直接交底。
                if (element is MenuItem hostedItem)
                {
                    hostedItem._hostPopupBorder = _submenuBorder;
                }

                items.Add(element);
            }
        }
    }

    private UIElement? CreateSubmenuElement(object item)
    {
        if (item is UIElement element)
        {
            return element;
        }

        if (UsesItemContainerTemplate)
        {
            var template = ItemContainerTemplateSelector?.SelectTemplate(item, this);
            var generated = template?.LoadContent();
            if (generated != null)
            {
                PrepareContainerForItem(generated, item);
                return generated;
            }
        }

        var fallback = new MenuItem
        {
            Header = item
        };
        PrepareContainerForItem(fallback, item);
        return fallback;
    }

    /// <summary>
    /// Returns items from the popup panel back to the ItemsHost panel.
    /// </summary>
    private void ReturnItemsFromPopup()
    {
        var popupItems = _submenuScrollHost?.Items;
        if (popupItems == null)
            return;
        var items = popupItems.ToArray();
        popupItems.Clear();
        var itemsHost = ItemsHost;
        if (itemsHost != null)
        {
            foreach (var item in items)
            {
                // A template refresh may already have restored own-container
                // items into the current staging host before the popup closes.
                if (itemsHost.Children.Contains(item))
                {
                    if (ReferenceEquals(item.VisualParent, itemsHost))
                        continue;
                    itemsHost.Children.Remove(item);
                }

                if (item.VisualParent != null)
                    item.DetachFromVisualParent();
                itemsHost.Children.Add(item);
            }
        }
    }

    /// <summary>
    /// Closes all sibling MenuItem submenus.
    /// </summary>
    private void CloseSiblingSubmenus()
    {
        var parent = VisualParent;
        if (parent is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is MenuItem sibling && sibling != this)
                {
                    sibling.CloseSubmenuBranch();
                }
            }
        }
    }

    private bool IsTopLevelMenuModeActive()
    {
        if (!IsTopLevelMenuItem)
        {
            return true;
        }

        if (IsSubmenuOpen)
        {
            return true;
        }

        if (VisualParent is not Panel panel)
        {
            return false;
        }

        foreach (UIElement child in panel.Children)
        {
            if (child is MenuItem sibling && sibling != this && sibling.IsSubmenuOpen)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handles the popup being closed externally.
    /// </summary>
    private void OnSubmenuPopupClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _submenuPopup) && IsSubmenuOpen && !_isUpdatingSubmenuOpen)
            SetCurrentValue(IsSubmenuOpenProperty, false);
    }

#endregion
#region Property Changed Callbacks
    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem menuItem)
            return;
        if (e.OldValue is ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= menuItem.OnCanExecuteChanged;
        }

        if (e.OldValue is null && e.NewValue is ICommand)
        {
            menuItem._commandBaseIsEnabled = menuItem.IsEnabled;
        }

        if (e.NewValue is ICommand newCommand)
        {
            newCommand.CanExecuteChanged += menuItem.OnCanExecuteChanged;
            menuItem.UpdateCanExecute();
        }
        else
        {
            menuItem.SetCommandEnabled(menuItem._commandBaseIsEnabled);
        }
    }

    private static void OnCommandStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem)
        {
            menuItem.UpdateCanExecute();
        }
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateCanExecute();
    }

    private void UpdateCanExecute()
    {
        var command = Command;
        if (command == null)
            return;
        var canExecute = command is RoutedCommand routedCommand ? routedCommand.CanExecute(CommandParameter, CommandTarget ?? this) : command.CanExecute(CommandParameter);
        SetCommandEnabled(_commandBaseIsEnabled && canExecute);
    }

    private void SetCommandEnabled(bool value)
    {
        if (IsEnabled == value)
            return;
        _isUpdatingCommandEnabled = true;
        try
        {
            SetCurrentValue(UIElement.IsEnabledProperty, value);
        }
        finally
        {
            _isUpdatingCommandEnabled = false;
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem)
        {
            menuItem.UpdateItemPresentation();
            menuItem.InvalidateVisual();
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem)
        {
            menuItem.UpdateItemPresentation();
            menuItem.InvalidateVisual();
        }
    }

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem)
        {
            if ((bool)e.NewValue!)
            {
                menuItem.OnChecked(new RoutedEventArgs(CheckedEvent, menuItem));
            }
            else
            {
                menuItem.OnUnchecked(new RoutedEventArgs(UncheckedEvent, menuItem));
            }

            menuItem.UpdateItemPresentation();
            menuItem.InvalidateVisual();
        }
    }

    private static void OnIsSubmenuOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem && !menuItem._isUpdatingSubmenuOpen)
        {
            if (e.NewValue is true && !menuItem.IsEnabled)
            {
                menuItem._isUpdatingSubmenuOpen = true;
                try
                {
                    menuItem.SetCurrentValue(IsSubmenuOpenProperty, false);
                }
                finally
                {
                    menuItem._isUpdatingSubmenuOpen = false;
                }

                menuItem.UpdateItemPresentation();
                return;
            }

            menuItem._isUpdatingSubmenuOpen = true;
            try
            {
                var isOpen = (bool)e.NewValue!;
                if (isOpen)
                {
                    menuItem.EnsureSubmenuPopup();
                    menuItem.PopulateSubmenuPopup();
                    // 每次展开重取外框造型：首次建 popup 时上一级可能还没成型，之后主题 / 宿主样式也会变。
                    menuItem.ApplySubmenuChrome();
                    if (menuItem._submenuPopup != null)
                        menuItem._submenuPopup.IsOpen = true;
                    menuItem._submenuScrollHost?.PrepareForOpen();
                    menuItem.OnSubmenuOpened(new RoutedEventArgs(SubmenuOpenedEvent, menuItem));
                }
                else
                {
                    menuItem.CloseDescendantSubmenus();
                    if (menuItem._submenuPopup != null)
                    {
                        menuItem._submenuPopup.IsOpen = false;
                    }

                    menuItem.ReturnItemsFromPopup();
                    menuItem._retainedSubmenuContainers.Clear();
                    menuItem.OnSubmenuClosed(new RoutedEventArgs(SubmenuClosedEvent, menuItem));
                }

                menuItem.UpdateIsSelected();
                menuItem.UpdateItemPresentation();
                menuItem.InvalidateVisual();
            }
            finally
            {
                menuItem._isUpdatingSubmenuOpen = false;
            }
        }
    }

#endregion
#region State Helpers
    /// <inheritdoc/>
    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);
        UpdateIsSelected();
    }

    /// <inheritdoc/>
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!_isUpdatingCommandEnabled && Command != null)
        {
            _commandBaseIsEnabled = newValue;
            if (newValue)
            {
                UpdateCanExecute();
            }
        }

        if (!newValue)
        {
            _isPointerOverMenuItem = false;
            IsPressed = false;
            if (IsSubmenuOpen)
                SetCurrentValue(IsSubmenuOpenProperty, false);
        }

        UpdateIsSelected();
    }

    private void UpdateIsSelected()
    {
        var isHighlighted = IsEnabled && (_isPointerOverMenuItem || IsKeyboardFocused || IsSubmenuOpen);
        IsHighlighted = isHighlighted;
        SetValue(IsSelectedPropertyKey, isHighlighted);
        UpdateItemPresentation();
    }

    private void UpdateRole()
    {
        var role = IsTopLevelMenuItem ? (HasItems ? MenuItemRole.TopLevelHeader : MenuItemRole.TopLevelItem) : (HasItems ? MenuItemRole.SubmenuHeader : MenuItemRole.SubmenuItem);
        SetValue(RolePropertyKey, role);
        UpdateItemPresentation();
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateRole();
        if (IsSubmenuOpen && _submenuScrollHost != null)
            PopulateSubmenuPopup();
    }

    private sealed class DefaultMenuItemContainerTemplateSelector : ItemContainerTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
        {
            if (item == null)
                return null;
            return parentItemsControl.TryFindResource(new ItemContainerTemplateKey(item.GetType())) as DataTemplate;
        }
    }
#endregion
}

/// <summary>
/// Base class for controls that have both a header and items.
/// </summary>
public class HeaderedItemsControl : ItemsControl
{
#region Dependency Properties
    private static readonly DependencyPropertyKey HasHeaderPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasHeader), typeof(bool), typeof(HeaderedItemsControl), new PropertyMetadata(false));
    /// <summary>Identifies the read-only <see cref = "HasHeader"/> dependency property.</summary>
    public static readonly DependencyProperty HasHeaderProperty = HasHeaderPropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(object), typeof(HeaderedItemsControl), new PropertyMetadata(null, OnHeaderPropertyChanged));
    /// <summary>
    /// Identifies the HeaderTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderTemplateProperty = DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(HeaderedItemsControl), new PropertyMetadata(null, OnHeaderTemplatePropertyChanged));
    /// <summary>Identifies the HeaderTemplateSelector dependency property.</summary>
    public static readonly DependencyProperty HeaderTemplateSelectorProperty = DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(HeaderedItemsControl), new PropertyMetadata(null, OnHeaderTemplateSelectorPropertyChanged));
    /// <summary>Identifies the HeaderStringFormat dependency property.</summary>
    public static readonly DependencyProperty HeaderStringFormatProperty = DependencyProperty.Register(nameof(HeaderStringFormat), typeof(string), typeof(HeaderedItemsControl), new PropertyMetadata(null, OnHeaderStringFormatPropertyChanged));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>
    /// Gets or sets the template for the header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? HeaderTemplate { get => (DataTemplate? )GetValue(HeaderTemplateProperty); set => SetValue(HeaderTemplateProperty, value); }
    /// <summary>Gets whether this control currently has non-null header content.</summary>
    public bool HasHeader => (bool)(GetValue(HasHeaderProperty) ?? false);
    /// <summary>Gets or sets the selector used to choose the header template.</summary>
    public DataTemplateSelector? HeaderTemplateSelector { get => (DataTemplateSelector? )GetValue(HeaderTemplateSelectorProperty); set => SetValue(HeaderTemplateSelectorProperty, value); }
    /// <summary>Gets or sets the composite format string used for header text.</summary>
    public string? HeaderStringFormat { get => (string? )GetValue(HeaderStringFormatProperty); set => SetValue(HeaderStringFormatProperty, value); }

    protected internal override System.Collections.IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            if (Header != null)
            {
                children.Add(Header);
            }

            var baseChildren = base.LogicalChildren;
            while (baseChildren.MoveNext())
            {
                if (baseChildren.Current != null && !ReferenceEquals(baseChildren.Current, Header))
                {
                    children.Add(baseChildren.Current);
                }
            }

            return children.GetEnumerator();
        }
    }

#endregion
#region Property Changed Callbacks
    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (HeaderedItemsControl)d;
        control.SetValue(HasHeaderPropertyKey, e.NewValue != null);
        control.OnHeaderChanged(e.OldValue, e.NewValue);
    }

    private static void OnHeaderTemplatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HeaderedItemsControl)d).OnHeaderTemplateChanged((DataTemplate? )e.OldValue, (DataTemplate? )e.NewValue);
    private static void OnHeaderTemplateSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HeaderedItemsControl)d).OnHeaderTemplateSelectorChanged((DataTemplateSelector? )e.OldValue, (DataTemplateSelector? )e.NewValue);
    private static void OnHeaderStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HeaderedItemsControl)d).OnHeaderStringFormatChanged((string? )e.OldValue, (string? )e.NewValue);
    protected virtual void OnHeaderChanged(object? oldHeader, object? newHeader)
    {
        RemoveLogicalChild(oldHeader);
        AddLogicalChild(newHeader);
        InvalidateMeasure();
    }

    protected virtual void OnHeaderTemplateChanged(DataTemplate? oldHeaderTemplate, DataTemplate? newHeaderTemplate) => InvalidateMeasure();
    protected virtual void OnHeaderTemplateSelectorChanged(DataTemplateSelector? oldHeaderTemplateSelector, DataTemplateSelector? newHeaderTemplateSelector) => InvalidateMeasure();
    protected virtual void OnHeaderStringFormatChanged(string? oldHeaderStringFormat, string? newHeaderStringFormat) => InvalidateMeasure();
    public override string ToString() => Header?.ToString() ?? base.ToString() ?? nameof(HeaderedItemsControl);
#endregion
}
