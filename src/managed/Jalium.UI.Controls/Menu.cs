using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using System.Windows.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a menu bar that contains menu items.
/// </summary>
public class Menu : MenuBase
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.MenuAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsMainMenu dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMainMenuProperty =
        DependencyProperty.Register(nameof(IsMainMenu), typeof(bool), typeof(Menu),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this is the main application menu.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMainMenu
    {
        get => (bool)GetValue(IsMainMenuProperty)!;
        set => SetValue(IsMainMenuProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Menu"/> class.
    /// </summary>
    public Menu()
    {
    }

    #endregion

    #region Item Generation

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new StackPanel { Orientation = Orientation.Horizontal };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new MenuItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MenuItem || item is Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is MenuItem menuItem && item is string text)
        {
            menuItem.Header = text;
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var rect = new Rect(RenderSize);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw bottom border
        if (BorderBrush != null)
        {
            var borderPen = new Pen(BorderBrush, 1);
            dc.DrawLine(borderPen, new Point(0, rect.Height - 1), new Point(rect.Width, rect.Height - 1));
        }
    }

    #endregion
}

/// <summary>
/// Represents an item in a menu.
/// </summary>
public class MenuItem : HeaderedItemsControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.MenuItemAutomationPeer(this);
    }

    // Cached brushes for OnRender
    private static readonly SolidColorBrush s_highlightBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_disabledBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_gestureBrush = new(Color.FromRgb(160, 160, 160));
    private static readonly ItemContainerTemplateSelector s_defaultItemContainerTemplateSelector =
        new DefaultMenuItemContainerTemplateSelector();

    private static readonly ResourceKey s_separatorStyleKey =
        new ComponentResourceKey(typeof(MenuItem), nameof(SeparatorStyleKey));
    private static readonly ResourceKey s_submenuHeaderTemplateKey =
        new ComponentResourceKey(typeof(MenuItem), nameof(SubmenuHeaderTemplateKey));
    private static readonly ResourceKey s_submenuItemTemplateKey =
        new ComponentResourceKey(typeof(MenuItem), nameof(SubmenuItemTemplateKey));
    private static readonly ResourceKey s_topLevelHeaderTemplateKey =
        new ComponentResourceKey(typeof(MenuItem), nameof(TopLevelHeaderTemplateKey));
    private static readonly ResourceKey s_topLevelItemTemplateKey =
        new ComponentResourceKey(typeof(MenuItem), nameof(TopLevelItemTemplateKey));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the InputGestureText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty InputGestureTextProperty =
        DependencyProperty.Register(nameof(InputGestureText), typeof(string), typeof(MenuItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsCheckable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckableProperty =
        DependencyProperty.Register(nameof(IsCheckable), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false, OnIsCheckedChanged));

    /// <summary>
    /// Identifies the IsSubmenuOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSubmenuOpenProperty =
        DependencyProperty.Register(nameof(IsSubmenuOpen), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false, OnIsSubmenuOpenChanged));

    /// <summary>
    /// Identifies the IsSelected read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsSelectedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsSelected), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty = IsSelectedPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the Role read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey RolePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Role), typeof(MenuItemRole), typeof(MenuItem),
            new PropertyMetadata(MenuItemRole.SubmenuItem, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Role dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RoleProperty = RolePropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the StaysOpenOnClick dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StaysOpenOnClickProperty =
        DependencyProperty.Register(nameof(StaysOpenOnClick), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        ButtonBase.CommandProperty.AddOwner(typeof(MenuItem),
            new PropertyMetadata(null, OnCommandChanged));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        ButtonBase.CommandParameterProperty.AddOwner(typeof(MenuItem),
            new PropertyMetadata(null, OnCommandStateChanged));

    /// <summary>
    /// Identifies the CommandTarget dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandTargetProperty =
        ButtonBase.CommandTargetProperty.AddOwner(typeof(MenuItem),
            new PropertyMetadata(null, OnCommandStateChanged));

    private static readonly DependencyPropertyKey IsHighlightedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsHighlighted), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsHighlighted read-only dependency property.
    /// </summary>
    public static readonly DependencyProperty IsHighlightedProperty =
        IsHighlightedPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsPressed read-only dependency property.
    /// </summary>
    public new static readonly DependencyProperty IsPressedProperty =
        UIElement.IsPressedProperty.AddOwner(typeof(MenuItem));

    private static readonly DependencyPropertyKey IsSuspendingPopupAnimationPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsSuspendingPopupAnimation), typeof(bool), typeof(MenuItem),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsSuspendingPopupAnimation read-only dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSuspendingPopupAnimationProperty =
        IsSuspendingPopupAnimationPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the ItemContainerTemplateSelector dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemContainerTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ItemContainerTemplateSelector),
            typeof(ItemContainerTemplateSelector), typeof(MenuItem),
            new PropertyMetadata(s_defaultItemContainerTemplateSelector));

    /// <summary>
    /// Identifies the UsesItemContainerTemplate dependency property.
    /// </summary>
    public static readonly DependencyProperty UsesItemContainerTemplateProperty =
        MenuBase.UsesItemContainerTemplateProperty.AddOwner(typeof(MenuItem));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuItem));

    /// <summary>
    /// Identifies the Checked routed event.
    /// </summary>
    public static readonly RoutedEvent CheckedEvent =
        EventManager.RegisterRoutedEvent(nameof(Checked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuItem));

    /// <summary>
    /// Identifies the Unchecked routed event.
    /// </summary>
    public static readonly RoutedEvent UncheckedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unchecked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuItem));

    /// <summary>
    /// Identifies the SubmenuOpened routed event.
    /// </summary>
    public static readonly RoutedEvent SubmenuOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(SubmenuOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuItem));

    /// <summary>
    /// Identifies the SubmenuClosed routed event.
    /// </summary>
    public static readonly RoutedEvent SubmenuClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(SubmenuClosed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuItem));

    /// <summary>
    /// Occurs when the menu item is clicked.
    /// </summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    /// <summary>
    /// Occurs when the menu item is checked.
    /// </summary>
    public event RoutedEventHandler Checked
    {
        add => AddHandler(CheckedEvent, value);
        remove => RemoveHandler(CheckedEvent, value);
    }

    /// <summary>
    /// Occurs when the menu item is unchecked.
    /// </summary>
    public event RoutedEventHandler Unchecked
    {
        add => AddHandler(UncheckedEvent, value);
        remove => RemoveHandler(UncheckedEvent, value);
    }

    /// <summary>
    /// Occurs when the submenu is opened.
    /// </summary>
    public event RoutedEventHandler SubmenuOpened
    {
        add => AddHandler(SubmenuOpenedEvent, value);
        remove => RemoveHandler(SubmenuOpenedEvent, value);
    }

    /// <summary>
    /// Occurs when the submenu is closed.
    /// </summary>
    public event RoutedEventHandler SubmenuClosed
    {
        add => AddHandler(SubmenuClosedEvent, value);
        remove => RemoveHandler(SubmenuClosedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the icon displayed with the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the input gesture text (keyboard shortcut) displayed with the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public string? InputGestureText
    {
        get => (string?)GetValue(InputGestureTextProperty);
        set => SetValue(InputGestureTextProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the menu item can be checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCheckable
    {
        get => (bool)GetValue(IsCheckableProperty)!;
        set => SetValue(IsCheckableProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the menu item is checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty)!;
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the submenu is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSubmenuOpen
    {
        get => (bool)GetValue(IsSubmenuOpenProperty)!;
        set => SetValue(IsSubmenuOpenProperty, value);
    }

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
    public bool StaysOpenOnClick
    {
        get => (bool)GetValue(StaysOpenOnClickProperty)!;
        set => SetValue(StaysOpenOnClickProperty, value);
    }

    /// <summary>
    /// Gets or sets the command invoked when this menu item is clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the value passed to <see cref="Command"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the element on which a routed command is executed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether this menu item is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty)!;
        protected set => SetValue(IsHighlightedPropertyKey, value);
    }

    /// <summary>
    /// Gets a value indicating whether this menu item is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public new bool IsPressed
    {
        get => base.IsPressed;
        protected set => base.SetIsPressed(value);
    }

    /// <summary>
    /// Gets a value indicating whether submenu popup animation is temporarily suspended.
    /// </summary>
    public bool IsSuspendingPopupAnimation =>
        (bool)GetValue(IsSuspendingPopupAnimationProperty)!;

    /// <summary>
    /// Gets or sets the selector used to create submenu item containers.
    /// </summary>
    public ItemContainerTemplateSelector? ItemContainerTemplateSelector
    {
        get => (ItemContainerTemplateSelector?)GetValue(ItemContainerTemplateSelectorProperty);
        set => SetValue(ItemContainerTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether submenu item containers are created from an item-container template.
    /// </summary>
    public bool UsesItemContainerTemplate
    {
        get => (bool)GetValue(UsesItemContainerTemplateProperty)!;
        set => SetValue(UsesItemContainerTemplateProperty, value);
    }

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
    private Popup? _submenuPopup;
    private Border? _submenuBorder;
    private MenuPopupScrollHost? _submenuScrollHost;
    private const double IconColumnWidth = 24;
    private const double GestureColumnWidth = 80;
    private const double ArrowColumnWidth = 16;
    private const double ItemHeight = 28;
    private const double MenuItemPadding = 8;

    // Cached pens
    private Pen? _arrowPen;
    private Brush? _arrowPenBrush;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuItem"/> class.
    /// </summary>
    public MenuItem()
    {
        Focusable = true;
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
    }

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;
        // Reuse the same submenu-open / click logic as the mouse path by
        // synthesising a left-button MouseButtonEventArgs on this element.
        var synthetic = new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            touchArgs.GetTouchPoint(this).Position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: Environment.TickCount)
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

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
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
                throw new InvalidOperationException(
                    "An item-container template for a MenuItem must create a MenuItem or Separator.");
            }
        }

        return new MenuItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MenuItem or Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is MenuItem menuItem &&
            !ReferenceEquals(menuItem, item) &&
            menuItem.Header == null)
        {
            menuItem.Header = item;
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

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
        if (!IsEnabled) return;

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
        var panel = _submenuScrollHost?.ItemsPanel;
        if (panel == null) return;

        foreach (UIElement child in panel.Children)
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

        if (currentIndex < 0) return;

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

    /// <summary>
    /// Called when the menu item is clicked.
    /// </summary>
    protected virtual void OnClick()
    {
        if (IsCheckable)
        {
            IsChecked = !IsChecked;
        }

        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        ExecuteCommand();

        if (!StaysOpenOnClick)
        {
            CloseParentMenus();
        }
    }

    /// <summary>
    /// Raises the <see cref="Checked"/> routed event.
    /// </summary>
    protected virtual void OnChecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="Unchecked"/> routed event.
    /// </summary>
    protected virtual void OnUnchecked(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="SubmenuOpened"/> routed event.
    /// </summary>
    protected virtual void OnSubmenuOpened(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="SubmenuClosed"/> routed event.
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
        var current = FindParentMenuItem();
        while (current != null)
        {
            current.IsSubmenuOpen = false;
            current = current.FindParentMenuItem();
        }
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

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateRole();
    }

    // MenuItem renders everything via OnRender (header, icon, gesture text, etc.).
    // Child items (submenu entries) must NOT be rendered inline 闁?they are shown
    // exclusively through a Popup when the submenu opens.  The base ItemsControl
    // adds a _fallbackItemsHost panel as a visual child, which would cause all
    // sub-items to be laid out and drawn inside the MenuItem.  Returning 0 here
    // prevents that panel from participating in rendering / hit-testing while the
    // Items collection is still available for PopulateSubmenuPopup().
    /// <inheritdoc />
    protected override int VisualChildrenCount => 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index) =>
        throw new ArgumentOutOfRangeException(nameof(index));

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var width = padding.TotalWidth;
        var height = ItemHeight;

        // Determine if we're a top-level menu item or a submenu item
        var isTopLevel = VisualParent is Panel p && p.VisualParent is Menu;

        if (isTopLevel)
        {
            // Top-level: just header
            if (Header is string headerText)
            {
                var formattedText = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14);
                TextMeasurement.MeasureText(formattedText);
                width += formattedText.Width;
            }
        }
        else
        {
            // Submenu item: icon + header + gesture + arrow
            width = IconColumnWidth;

            if (Header is string headerText)
            {
                var formattedText = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14);
                TextMeasurement.MeasureText(formattedText);
                width += formattedText.Width + MenuItemPadding * 2;
            }

            if (!string.IsNullOrEmpty(InputGestureText))
            {
                width += GestureColumnWidth;
            }

            if (HasItems)
            {
                width += ArrowColumnWidth;
            }
        }

        return new Size(Math.Max(width, 50), height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var isTopLevel = VisualParent is Panel p && p.VisualParent is Menu;
        var background = ResolveBackgroundBrush(isTopLevel);

        if (background != null)
        {
            if (IsSelected)
            {
                if (isTopLevel)
                {
                    dc.DrawRoundedRectangle(background, null, rect, 4, 4);
                }
                else
                {
                    const double hoverInset = 2.0;
                    dc.DrawRoundedRectangle(
                        background,
                        null,
                        new Rect(
                            hoverInset,
                            hoverInset,
                            Math.Max(0, rect.Width - hoverInset * 2),
                            Math.Max(0, rect.Height - hoverInset * 2)),
                        4,
                        4);
                }
            }
            else
            {
                dc.DrawRectangle(background, null, rect);
            }
        }

        var fgBrush = IsEnabled
            ? ResolvePrimaryTextBrush()
            : ResolveMenuBrush("TextDisabled", s_disabledBrush);

        if (isTopLevel)
        {
            // Top-level menu item
            if (Header is string headerText)
            {
                var formattedText = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
                {
                    Foreground = fgBrush
                };
                TextMeasurement.MeasureText(formattedText);

                var textX = padding.Left;
                var textY = (rect.Height - formattedText.Height) / 2;
                dc.DrawText(formattedText, new Point(textX, textY));
            }
        }
        else
        {
            // Submenu item
            var currentX = 0.0;

            // Draw check mark or icon
            if (IsCheckable && IsChecked)
            {
                DrawCheckMark(dc, currentX, rect.Height, fgBrush);
            }
            else if (Icon is string iconText)
            {
                var iconFormatted = new FormattedText(iconText, "Segoe UI Symbol", 12)
                {
                    Foreground = fgBrush
                };
                TextMeasurement.MeasureText(iconFormatted);
                dc.DrawText(iconFormatted, new Point(currentX + (IconColumnWidth - iconFormatted.Width) / 2, (rect.Height - iconFormatted.Height) / 2));
            }
            currentX += IconColumnWidth;

            // Draw header
            if (Header is string headerText)
            {
                var headerFormatted = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
                {
                    Foreground = fgBrush
                };
                TextMeasurement.MeasureText(headerFormatted);
                dc.DrawText(headerFormatted, new Point(currentX, (rect.Height - headerFormatted.Height) / 2));
            }

            // Draw input gesture text
            if (!string.IsNullOrEmpty(InputGestureText))
            {
                var gestureFormatted = new FormattedText(InputGestureText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 12)
                {
                    Foreground = ResolveMenuBrush("TextSecondary", s_gestureBrush)
                };
                TextMeasurement.MeasureText(gestureFormatted);
                var arrowReserve = HasItems ? ArrowColumnWidth : 0;
                var gestureColumnLeft = rect.Width - arrowReserve - GestureColumnWidth;
                var gestureX = gestureColumnLeft + GestureColumnWidth - gestureFormatted.Width - MenuItemPadding;
                if (gestureX < gestureColumnLeft)
                {
                    gestureX = gestureColumnLeft;
                }
                dc.DrawText(gestureFormatted, new Point(gestureX, (rect.Height - gestureFormatted.Height) / 2));
            }

            // Draw submenu arrow
            if (HasItems)
            {
                DrawSubmenuArrow(dc, rect.Width - ArrowColumnWidth, rect.Height, fgBrush);
            }
        }
    }

    private void DrawCheckMark(DrawingContext dc, double x, double height, Brush brush)
    {
        if (_arrowPen == null || _arrowPenBrush != brush)
        {
            _arrowPenBrush = brush;
            _arrowPen = new Pen(brush, 2);
        }
        var checkPen = _arrowPen;
        var centerX = x + IconColumnWidth / 2;
        var centerY = height / 2;

        dc.DrawLine(checkPen, new Point(centerX - 4, centerY), new Point(centerX - 1, centerY + 3));
        dc.DrawLine(checkPen, new Point(centerX - 1, centerY + 3), new Point(centerX + 4, centerY - 3));
    }

    private void DrawSubmenuArrow(DrawingContext dc, double x, double height, Brush brush)
    {
        const double arrowSize = 8.0;
        var arrowBounds = new Rect(
            x + (ArrowColumnWidth - arrowSize) / 2,
            (height - arrowSize) / 2,
            arrowSize,
            arrowSize);
        ArrowIcons.DrawArrow(dc, brush, arrowBounds, ArrowIcons.Direction.Right);
    }

    private Brush ResolvePrimaryTextBrush()
    {
        var valueSource = DependencyPropertyHelper.GetValueSource(this, Control.ForegroundProperty).BaseValueSource;
        if (Foreground != null && valueSource != BaseValueSource.Default)
        {
            return Foreground;
        }

        return ResolveMenuBrush("TextPrimary", s_whiteBrush);
    }

    private Brush? ResolveBackgroundBrush(bool isTopLevel)
    {
        if (Background != null)
        {
            return Background;
        }

        if (IsSelected)
        {
            return isTopLevel
                ? ResolveMenuBrush("MenuBarItemBackgroundHover", s_highlightBrush)
                : ResolveMenuBrush("MenuFlyoutItemBackgroundHover", s_highlightBrush);
        }

        return null;
    }

    private SolidColorBrush ResolveMenuBrush(string resourceKey, SolidColorBrush fallback)
    {
        return TryFindResource(resourceKey) as SolidColorBrush ?? fallback;
    }

    #endregion

    #region Submenu Popup

    /// <summary>
    /// Gets a value indicating whether this is a top-level menu item (direct child of a Menu bar).
    /// </summary>
    private bool IsTopLevelMenuItem => VisualParent is Panel p && p.VisualParent is Menu;

    /// <summary>
    /// Ensures the submenu popup is created.
    /// </summary>
    private void EnsureSubmenuPopup()
    {
        if (_submenuPopup != null) return;

        _submenuScrollHost = new MenuPopupScrollHost();
        _submenuBorder = new Border
        {
            Background = ResolveMenuBrush("MenuFlyoutPresenterBackground", new SolidColorBrush(Color.FromRgb(45, 45, 48))),
            BorderBrush = ResolveMenuBrush("MenuFlyoutPresenterBorderBrush", new SolidColorBrush(Color.FromRgb(67, 67, 70))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = _submenuScrollHost
        };

        _submenuPopup = new Popup
        {
            PlacementTarget = this,
            Placement = IsTopLevelMenuItem ? PlacementMode.Bottom : PlacementMode.Right,
            // Menu popups must light-dismiss on outside click; otherwise
            // clicks leak to underlying controls and menus stay open.
            StaysOpen = false,
            IsLightDismissEnabled = true,
            // Let Popup decide overlay vs external window based on overflow.
            ShouldConstrainToRootBounds = false,
            Child = _submenuBorder
        };

        // When popup is closed externally (e.g., light dismiss), sync IsSubmenuOpen
        _submenuPopup.Closed += OnSubmenuPopupClosed;
    }

    /// <summary>
    /// Populates the submenu popup panel with the MenuItem's child items.
    /// Items are detached from the ItemsHost panel and moved into the popup panel.
    /// </summary>
    private void PopulateSubmenuPopup()
    {
        var panel = _submenuScrollHost?.ItemsPanel;
        if (panel == null) return;

        panel.Children.Clear();

        // Preserve generated containers before detaching them from the inline ItemsHost.
        // Data items have already passed through GetContainerForItem when they were added;
        // re-running the selector here would create a second, unrelated container.
        var itemsHost = ItemsHost;
        var existingContainers = new List<UIElement>();
        if (itemsHost != null)
        {
            foreach (UIElement child in itemsHost.Children)
            {
                existingContainers.Add(child);
            }
            itemsHost.Children.Clear();
        }

        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            var element = index < existingContainers.Count
                ? existingContainers[index]
                : CreateSubmenuElement(item);
            if (element != null)
            {
                // Ensure the element is detached from any previous visual parent
                if (element.VisualParent != null)
                {
                    element.DetachFromVisualParent();
                }
                panel.Children.Add(element);
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
                return generated;
            }
        }

        return new MenuItem { Header = item };
    }

    /// <summary>
    /// Returns items from the popup panel back to the ItemsHost panel.
    /// </summary>
    private void ReturnItemsFromPopup()
    {
        var panel = _submenuScrollHost?.ItemsPanel;
        if (panel == null) return;

        // Collect items before clearing
        var items = new List<UIElement>();
        foreach (UIElement child in panel.Children)
        {
            items.Add(child);
        }
        panel.Children.Clear();

        // Return them to ItemsHost if available
        var itemsHost = ItemsHost;
        if (itemsHost != null)
        {
            foreach (var item in items)
            {
                if (item.VisualParent != null)
                {
                    item.DetachFromVisualParent();
                }
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
        // Sync IsSubmenuOpen when popup is closed externally
        if (IsSubmenuOpen && !_isUpdatingSubmenuOpen)
        {
            _isUpdatingSubmenuOpen = true;
            try
            {
                IsSubmenuOpen = false;
            }
            finally
            {
                _isUpdatingSubmenuOpen = false;
            }
        }
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

        var canExecute = command is RoutedCommand routedCommand
            ? routedCommand.CanExecute(CommandParameter, CommandTarget ?? this)
            : command.CanExecute(CommandParameter);

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
            menuItem.InvalidateVisual();
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem)
        {
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
            menuItem.InvalidateVisual();
        }
    }

    private static void OnIsSubmenuOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuItem menuItem && !menuItem._isUpdatingSubmenuOpen)
        {
            menuItem._isUpdatingSubmenuOpen = true;
            try
            {
                var isOpen = (bool)e.NewValue!;

                if (isOpen)
                {
                    menuItem.EnsureSubmenuPopup();
                    menuItem.PopulateSubmenuPopup();
                    menuItem._submenuPopup!.IsOpen = true;
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
                    menuItem.OnSubmenuClosed(new RoutedEventArgs(SubmenuClosedEvent, menuItem));
                }

                menuItem.UpdateIsSelected();
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

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);
        UpdateIsSelected();
    }

    /// <inheritdoc />
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
        }

        UpdateIsSelected();
    }

    private void UpdateIsSelected()
    {
        var isHighlighted =
            IsEnabled && (_isPointerOverMenuItem || IsKeyboardFocused || IsSubmenuOpen);
        IsHighlighted = isHighlighted;
        SetValue(IsSelectedPropertyKey, isHighlighted);
    }

    private void UpdateRole()
    {
        var role = IsTopLevelMenuItem
            ? (HasItems ? MenuItemRole.TopLevelHeader : MenuItemRole.TopLevelItem)
            : (HasItems ? MenuItemRole.SubmenuHeader : MenuItemRole.SubmenuItem);

        SetValue(RolePropertyKey, role);
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateRole();
    }

    private sealed class DefaultMenuItemContainerTemplateSelector : ItemContainerTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
        {
            if (item == null)
                return null;

            return parentItemsControl.TryFindResource(new ItemContainerTemplateKey(item.GetType()))
                as DataTemplate;
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

    private static readonly DependencyPropertyKey HasHeaderPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasHeader), typeof(bool), typeof(HeaderedItemsControl),
            new PropertyMetadata(false));

    /// <summary>Identifies the read-only <see cref="HasHeader"/> dependency property.</summary>
    public static readonly DependencyProperty HasHeaderProperty = HasHeaderPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(HeaderedItemsControl),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    /// <summary>
    /// Identifies the HeaderTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(HeaderedItemsControl),
            new PropertyMetadata(null, OnHeaderTemplatePropertyChanged));

    /// <summary>Identifies the HeaderTemplateSelector dependency property.</summary>
    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(HeaderedItemsControl),
            new PropertyMetadata(null, OnHeaderTemplateSelectorPropertyChanged));

    /// <summary>Identifies the HeaderStringFormat dependency property.</summary>
    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register(nameof(HeaderStringFormat), typeof(string), typeof(HeaderedItemsControl),
            new PropertyMetadata(null, OnHeaderStringFormatPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the template for the header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>Gets whether this control currently has non-null header content.</summary>
    public bool HasHeader => (bool)(GetValue(HasHeaderProperty) ?? false);

    /// <summary>Gets or sets the selector used to choose the header template.</summary>
    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty);
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets the composite format string used for header text.</summary>
    public string? HeaderStringFormat
    {
        get => (string?)GetValue(HeaderStringFormatProperty);
        set => SetValue(HeaderStringFormatProperty, value);
    }

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

    private static void OnHeaderTemplatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedItemsControl)d).OnHeaderTemplateChanged((DataTemplate?)e.OldValue, (DataTemplate?)e.NewValue);

    private static void OnHeaderTemplateSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedItemsControl)d).OnHeaderTemplateSelectorChanged(
            (DataTemplateSelector?)e.OldValue,
            (DataTemplateSelector?)e.NewValue);

    private static void OnHeaderStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedItemsControl)d).OnHeaderStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);

    protected virtual void OnHeaderChanged(object? oldHeader, object? newHeader)
    {
        RemoveLogicalChild(oldHeader);
        AddLogicalChild(newHeader);
        InvalidateMeasure();
    }

    protected virtual void OnHeaderTemplateChanged(DataTemplate? oldHeaderTemplate, DataTemplate? newHeaderTemplate) =>
        InvalidateMeasure();

    protected virtual void OnHeaderTemplateSelectorChanged(
        DataTemplateSelector? oldHeaderTemplateSelector,
        DataTemplateSelector? newHeaderTemplateSelector) => InvalidateMeasure();

    protected virtual void OnHeaderStringFormatChanged(string? oldHeaderStringFormat, string? newHeaderStringFormat) =>
        InvalidateMeasure();

    public override string ToString() => Header?.ToString() ?? base.ToString() ?? nameof(HeaderedItemsControl);

    #endregion
}
