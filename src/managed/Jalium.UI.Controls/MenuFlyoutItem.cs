using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a command in a MenuFlyout control.
/// </summary>
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_Text", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Accelerator", Type = typeof(TextBlock))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "CommonStates")]
[TemplateVisualState(Name = "SubmenuOpen", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Checked", GroupName = "CheckStates")]
[TemplateVisualState(Name = "Unchecked", GroupName = "CheckStates")]
public class MenuFlyoutItem : Control
{
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.MenuFlyoutItemAutomationPeer(this);
    public static readonly DependencyProperty MenuForegroundProperty = DependencyProperty.Register(nameof(MenuForeground), typeof(Brush), typeof(MenuFlyoutItem), new PropertyMetadata(Brushes.White));
    private static readonly DependencyPropertyKey ItemFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontSize), typeof(double), typeof(MenuFlyoutItem), new PropertyMetadata(14.0));
    public static readonly DependencyProperty ItemFontSizeProperty = ItemFontSizePropertyKey.DependencyProperty;
    public double ItemFontSize => (double)GetValue(ItemFontSizeProperty)!;

    private static readonly DependencyPropertyKey ItemFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontFamily), typeof(FontFamily), typeof(MenuFlyoutItem), new PropertyMetadata(FontFamilyProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontFamilyProperty = ItemFontFamilyPropertyKey.DependencyProperty;
    public FontFamily ItemFontFamily => (FontFamily)GetValue(ItemFontFamilyProperty)!;

    private static readonly DependencyPropertyKey ItemFontWeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemFontWeight), typeof(FontWeight), typeof(MenuFlyoutItem), new PropertyMetadata(FontWeightProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty ItemFontWeightProperty = ItemFontWeightPropertyKey.DependencyProperty;
    public FontWeight ItemFontWeight => (FontWeight)GetValue(ItemFontWeightProperty)!;

    private static readonly DependencyPropertyKey ItemForegroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemForeground), typeof(Brush), typeof(MenuFlyoutItem), new PropertyMetadata(Brushes.White));
    public static readonly DependencyProperty ItemForegroundProperty = ItemForegroundPropertyKey.DependencyProperty;
    public Brush? ItemForeground => (Brush? )GetValue(ItemForegroundProperty);

    private static readonly DependencyPropertyKey IsItemHighlightedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsItemHighlighted), typeof(bool), typeof(MenuFlyoutItem), new PropertyMetadata(false));
    public static readonly DependencyProperty IsItemHighlightedProperty = IsItemHighlightedPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsItemCheckedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsItemChecked), typeof(bool), typeof(MenuFlyoutItem), new PropertyMetadata(false));
    public static readonly DependencyProperty IsItemCheckedProperty = IsItemCheckedPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasSubmenuPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasSubmenu), typeof(bool), typeof(MenuFlyoutItem), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSubmenuProperty = HasSubmenuPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasAcceleratorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasAccelerator), typeof(bool), typeof(MenuFlyoutItem), new PropertyMetadata(false));
    public static readonly DependencyProperty HasAcceleratorProperty = HasAcceleratorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ShowHighlightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowHighlight), typeof(bool), typeof(MenuFlyoutItem), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowHighlightProperty = ShowHighlightPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey AcceleratorFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AcceleratorFontSize), typeof(double), typeof(MenuFlyoutItem), new PropertyMetadata(12.0));
    public static readonly DependencyProperty AcceleratorFontSizeProperty = AcceleratorFontSizePropertyKey.DependencyProperty;
    public Brush? MenuForeground { get => (Brush? )GetValue(MenuForegroundProperty); set => SetValue(MenuForegroundProperty, value); }
    public bool IsItemHighlighted => (bool)GetValue(IsItemHighlightedProperty)!;
    public bool IsItemChecked => (bool)GetValue(IsItemCheckedProperty)!;
    public bool HasSubmenu => (bool)GetValue(HasSubmenuProperty)!;
    public bool HasAccelerator => (bool)GetValue(HasAcceleratorProperty)!;
    public bool ShowHighlight => (bool)GetValue(ShowHighlightProperty)!;
    public double AcceleratorFontSize => (double)GetValue(AcceleratorFontSizeProperty)!;

    static MenuFlyoutItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuFlyoutItem), new PropertyMetadata(typeof(MenuFlyoutItem)));
        FocusableProperty.OverrideMetadata(typeof(MenuFlyoutItem), new PropertyMetadata(true));
    }

#region Dependency Properties
    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MenuFlyoutItem), new PropertyMetadata(string.Empty));
    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuFlyoutItem), new PropertyMetadata(null));
    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MenuFlyoutItem), new PropertyMetadata(null, OnCommandChanged));
    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(MenuFlyoutItem), new PropertyMetadata(null));
    /// <summary>
    /// Identifies the KeyboardAcceleratorTextOverride dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty KeyboardAcceleratorTextOverrideProperty = DependencyProperty.Register(nameof(KeyboardAcceleratorTextOverride), typeof(string), typeof(MenuFlyoutItem), new PropertyMetadata(string.Empty));
#endregion
#region Routed Events
    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuFlyoutItem));
    /// <summary>
    /// Occurs when the menu flyout item is clicked.
    /// </summary>
    public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }

#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the text content of the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text { get => (string? )GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }

    /// <summary>
    /// Gets or sets the graphic content of the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>
    /// Gets or sets the command to invoke when the item is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command { get => (ICommand? )GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>
    /// Gets or sets the parameter to pass to the Command property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>
    /// Gets or sets the keyboard accelerator text override for display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string KeyboardAcceleratorTextOverride { get => (string? )GetValue(KeyboardAcceleratorTextOverrideProperty) ?? string.Empty; set => SetValue(KeyboardAcceleratorTextOverrideProperty, value); }

#endregion
    /// <summary>
    /// Initializes a new instance of the MenuFlyoutItem class.
    /// </summary>
    public MenuFlyoutItem()
    {
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Loaded += (_, _) => UpdateItemPresentation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateItemPresentation();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateItemPresentation();
        return base.MeasureOverride(availableSize);
    }

    protected override void RenderTemplatedBackground(DrawingContext context)
    {
        // Inherited values are resolved lazily. Refresh derived presentation
        // before the template reads them, including color-only ancestor changes.
        UpdateItemPresentation();
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            base.RenderTemplatedBackground(context);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == FontSizeProperty || args.Property == FontFamilyProperty || args.Property == FontWeightProperty || args.Property == KeyboardAcceleratorTextOverrideProperty || args.Property == BackgroundProperty || args.Property == ForegroundProperty || args.Property == MenuForegroundProperty || args.Property == IsMouseOverProperty || args.Property == IsKeyboardFocusedProperty)
            UpdateItemPresentation();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateItemPresentation();
    }

    protected void UpdateItemPresentation()
    {
        var foregroundSource = DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource;
        SetValue(ItemForegroundPropertyKey, foregroundSource == BaseValueSource.Default ? MenuForeground : Foreground);
        SetValue(ItemFontSizePropertyKey, FontSize);
        SetValue(ItemFontFamilyPropertyKey, FontFamily);
        SetValue(ItemFontWeightPropertyKey, FontWeight);
        bool highlighted = IsHighlighted;
        bool isChecked = this is ToggleMenuFlyoutItem toggle && toggle.IsChecked;
        bool hasSubmenu = this is MenuFlyoutSubItem;
        bool transparent = Background is null || Background is SolidColorBrush brush && brush.Color.A == 0;
        SetValue(IsItemHighlightedPropertyKey, highlighted);
        SetValue(IsItemCheckedPropertyKey, isChecked);
        SetValue(HasSubmenuPropertyKey, hasSubmenu);
        SetValue(HasAcceleratorPropertyKey, !string.IsNullOrEmpty(KeyboardAcceleratorTextOverride));
        SetValue(ShowHighlightPropertyKey, IsEnabled && highlighted && transparent);
        SetValue(AcceleratorFontSizePropertyKey, FontSize * (12.0 / 14.0));
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : this is MenuFlyoutSubItem { IsSubMenuOpen: true } ? "SubmenuOpen" : IsKeyboardFocused ? "Focused" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, isChecked ? "Checked" : "Unchecked", false);
    }

    /// <summary>Returns the area left for icons and labels after CSS box edges.</summary>
    protected Rect GetItemContentBounds() => ControlRenderGeometry.GetContentRect(new Rect(RenderSize), CssBoxMetrics.ContentInsets(this, CssLayout?.ContainingWidthCache ?? RenderSize.Width));
    /// <summary>
    /// 本项当前是否应画成高亮（悬停底色）。基类：指针悬停或键盘焦点；
    /// <see cref = "MenuFlyoutSubItem"/> 在子菜单打开期间额外保持高亮。
    /// </summary>
    protected virtual bool IsHighlighted => IsMouseOver || IsKeyboardFocused;

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;
        InvokeItem();
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        CloseSiblingSubMenus();
        UpdateItemPresentation();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        UpdateItemPresentation();
    }

    private void CloseSiblingSubMenus()
    {
        if (VisualParent is not Panel panel)
        {
            return;
        }

        foreach (UIElement child in panel.Children)
        {
            if (child is MenuFlyoutSubItem sibling && !ReferenceEquals(sibling, this))
            {
                sibling.HideSubMenu();
            }
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var handled = e.Key switch
        {
            Key.Enter or Key.Space => InvokeFromKeyboard(),
            Key.Down => MoveFocusToSibling(1),
            Key.Up => MoveFocusToSibling(-1),
            Key.Home => FocusBoundarySibling(last: false),
            Key.End => FocusBoundarySibling(last: true),
            Key.Right => OpenSubMenuAndFocusFirstItem() || MoveFocusToAdjacentMenuBarItem(1),
            Key.Left => MoveFocusToAdjacentMenuBarItem(-1) || CloseParentPopupAndRestoreFocus(),
            Key.Escape => CloseParentPopupAndRestoreFocus(),
            _ => false
        };
        if (handled)
        {
            e.Handled = true;
        }
    }

    internal void InvokeFromAutomation()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("The menu item is disabled.");
        InvokeItem();
    }

    protected virtual void InvokeItem()
    {
        OnItemInvoking();
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        Command?.Execute(CommandParameter);
        CloseMenuChain();
    }

    /// <summary>
    /// Called before the Click event is raised and the Command is executed.
    /// Derived classes can override this to update state that should be visible to Click handlers.
    /// </summary>
    protected virtual void OnItemInvoking()
    {
    }

    /// <summary>
    /// Closes the entire ancestor popup chain (e.g. a submenu popup plus its parent menu popup).
    /// Items hosted inside a submenu should close the whole menu tree on invocation,
    /// mirroring WinUI's MenuFlyout behavior.
    /// </summary>
    private void CloseMenuChain()
    {
        var popupRoot = FindAncestorPopupRoot();
        if (popupRoot == null)
            FindAncestorPresenter()?.CloseMenu();
        while (popupRoot != null)
        {
            var popup = popupRoot.OwnerPopup;
            var placementTarget = popup.PlacementTarget;
            popup.IsOpen = false;
            popupRoot = FindAncestorPopupRootFrom(placementTarget);
        }
    }

    private static PopupRoot? FindAncestorPopupRootFrom(UIElement? element)
    {
        for (Visual? current = element; current != null; current = current.VisualParent)
        {
            if (current is PopupRoot popupRoot)
            {
                return popupRoot;
            }
        }

        return null;
    }

    protected virtual bool InvokeFromKeyboard()
    {
        InvokeItem();
        return true;
    }

    protected bool MoveFocusToSibling(int direction)
    {
        if (VisualParent is not Panel panel)
        {
            return false;
        }

        var currentIndex = panel.Children.IndexOf(this);
        if (currentIndex < 0)
        {
            return false;
        }

        for (int offset = 1; offset < panel.Children.Count; offset++)
        {
            var nextIndex = (currentIndex + (direction * offset) + panel.Children.Count) % panel.Children.Count;
            if (panel.Children[nextIndex] is not UIElement candidate || !candidate.IsEnabled || candidate.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (candidate.Focus())
            {
                return true;
            }
        }

        return false;
    }

    protected bool FocusBoundarySibling(bool last)
    {
        if (VisualParent is not Panel panel || panel.Children.Count == 0)
        {
            return false;
        }

        if (!last)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is UIElement candidate && candidate.IsEnabled && candidate.Visibility == Visibility.Visible && candidate.Focus())
                {
                    return true;
                }
            }

            return false;
        }

        for (int i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is UIElement candidate && candidate.IsEnabled && candidate.Visibility == Visibility.Visible && candidate.Focus())
            {
                return true;
            }
        }

        return false;
    }

    protected bool OpenSubMenuAndFocusFirstItem()
    {
        if (this is not MenuFlyoutSubItem subItem)
        {
            return false;
        }

        subItem.ShowSubMenu();
        subItem.FocusFirstSubMenuItem();
        return true;
    }

    protected bool CloseParentPopupAndRestoreFocus()
    {
        var popupRoot = FindAncestorPopupRoot();
        if (popupRoot == null)
        {
            var presenter = FindAncestorPresenter();
            if (presenter == null)
                return false;
            presenter.CloseAndRestoreFocus();
            return true;
        }

        var placementTarget = popupRoot.OwnerPopup.PlacementTarget as UIElement;
        var parentSubItem = placementTarget as MenuFlyoutSubItem;
        if (placementTarget is MenuBarItem ownerItem)
            ownerItem.CloseMenu();
        else if (parentSubItem != null)
            parentSubItem.HideSubMenu();
        else
            popupRoot.OwnerPopup.SetCurrentValue(Popup.IsOpenProperty, false);
        Dispatcher.BeginInvokeCritical(() =>
        {
            if (parentSubItem != null)
            {
                parentSubItem.Focus();
                return;
            }

            placementTarget?.Focus();
        });
        return true;
    }

    protected bool MoveFocusToAdjacentMenuBarItem(int direction)
    {
        var popupRoot = FindAncestorPopupRoot();
        var ownerItem = popupRoot?.OwnerPopup.PlacementTarget as MenuBarItem ?? FindAncestorPresenter()?.OwnerTarget as MenuBarItem;
        if (ownerItem?.ParentMenuBar == null)
        {
            return false;
        }

        ownerItem.CloseMenu();
        // Switch the top-level owner synchronously. FocusSibling opens the new menu and
        // queues exactly one focus transfer to its first command. Deferring this whole
        // operation would create a nested dispatcher callback; a single ProcessQueue pass
        // could then stop on the MenuBarItem before the command-focus callback runs.
        return ownerItem.ParentMenuBar.FocusSibling(ownerItem, direction, openMenu: true);
    }

    private MenuFlyoutPresenter? FindAncestorPresenter()
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
            if (current is MenuFlyoutPresenter presenter)
                return presenter;
        return null;
    }

    private PopupRoot? FindAncestorPopupRoot()
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (current is PopupRoot popupRoot)
            {
                return popupRoot;
            }
        }

        return null;
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuFlyoutItem item && e.NewValue is XamlUICommand uiCommand)
        {
            if (string.IsNullOrEmpty(item.Text))
                item.Text = uiCommand.Label;
            item.Icon ??= uiCommand.IconSource;
        }
    }

    /// <summary>Gets a local or CSS text brush for derived menu item glyphs.</summary>
    protected Brush? ResolveAuthoredForegroundBrush() => Foreground != null && (HasLocalOrAnimatedValue(ForegroundProperty) || GetEffectiveValueLayer(ForegroundProperty)is (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) || DependencyPropertyHelper.GetValueSource(this, ForegroundProperty).BaseValueSource == BaseValueSource.Inherited) ? Foreground : null;
}
