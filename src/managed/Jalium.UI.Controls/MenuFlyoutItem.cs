using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a command in a MenuFlyout control.
/// </summary>
public class MenuFlyoutItem : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.MenuFlyoutItemAutomationPeer(this);

    private static readonly SolidColorBrush s_fallbackHoverBrush = new(Color.FromRgb(62, 62, 64));
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush s_fallbackDisabledTextBrush = new(Color.FromRgb(90, 90, 90));
    private static readonly SolidColorBrush s_fallbackAcceleratorBrush = new(Color.FromRgb(136, 136, 136));
    private const double LeftPadding = 12;
    private const double RightPadding = 12;
    private const double IconColumnWidth = 28;
    private const double TextTrailingPadding = 16;
    private const double AcceleratorColumnWidth = 80;
    private const double TextToAcceleratorGap = 16;
    private const double ItemHeight = 32;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MenuFlyoutItem),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuFlyoutItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MenuFlyoutItem),
            new PropertyMetadata(null, OnCommandChanged));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(MenuFlyoutItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the KeyboardAcceleratorTextOverride dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty KeyboardAcceleratorTextOverrideProperty =
        DependencyProperty.Register(nameof(KeyboardAcceleratorTextOverride), typeof(string), typeof(MenuFlyoutItem),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuFlyoutItem));

    /// <summary>
    /// Occurs when the menu flyout item is clicked.
    /// </summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text content of the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the graphic content of the menu item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to invoke when the item is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the Command property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the keyboard accelerator text override for display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string KeyboardAcceleratorTextOverride
    {
        get => (string?)GetValue(KeyboardAcceleratorTextOverrideProperty) ?? string.Empty;
        set => SetValue(KeyboardAcceleratorTextOverrideProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the MenuFlyoutItem class.
    /// </summary>
    public MenuFlyoutItem()
    {
        Focusable = true;
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontSize = FontSize > 0 ? FontSize : 14;
        double textWidth = 0;
        if (!string.IsNullOrEmpty(Text))
        {
            var formattedText = new FormattedText(Text, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize);
            TextMeasurement.MeasureText(formattedText);
            textWidth = formattedText.Width;
        }

        var contentWidth = LeftPadding + IconColumnWidth + textWidth + TextTrailingPadding + RightPadding;

        if (!string.IsNullOrEmpty(KeyboardAcceleratorTextOverride))
        {
            // Reserve a fixed right-side gesture column so shortcuts align to the far edge.
            contentWidth += TextToAcceleratorGap + AcceleratorColumnWidth;
        }

        return new Size(contentWidth, Math.Min(ItemHeight, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        // Background (hover state handled by IsMouseOver)
        if (IsMouseOver || IsKeyboardFocused)
        {
            const double hoverInset = 2.0;
            var hoverBrush = ResolveBrush("OneSurfaceHover", "MenuFlyoutItemBackgroundHover", s_fallbackHoverBrush);
            dc.DrawRoundedRectangle(hoverBrush, null,
                new Rect(
                    hoverInset,
                    hoverInset,
                    Math.Max(0, RenderSize.Width - hoverInset * 2),
                    Math.Max(0, RenderSize.Height - hoverInset * 2)),
                4, 4);
        }

        // Focus indicator is painted by FocusVisualManager into the adorner layer.

        var textBrush = IsEnabled
            ? ResolveForegroundBrush()
            : ResolveBrush("OneTextDisabled", "TextDisabled", s_fallbackDisabledTextBrush);

        double x = LeftPadding;

        var fontSize = FontSize > 0 ? FontSize : 14;

        // Icon
        if (Icon is string iconText && !string.IsNullOrEmpty(iconText))
        {
            var iconFormatted = new FormattedText(
                iconText, "Segoe MDL2 Assets", 14) { Foreground = textBrush };
            TextMeasurement.MeasureText(iconFormatted);
            dc.DrawText(iconFormatted, new Point(x, (RenderSize.Height - iconFormatted.Height) / 2));
        }
        // Reserve icon/check column even when there is no icon so labels line up.
        x += IconColumnWidth;

        // Text
        if (!string.IsNullOrEmpty(Text))
        {
            var textFormatted = new FormattedText(
                Text, FontFamily ?? FrameworkElement.DefaultFontFamilyName, fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(textFormatted);
            dc.DrawText(textFormatted, new Point(x, (RenderSize.Height - textFormatted.Height) / 2));
        }

        // Keyboard accelerator text (right-aligned)
        if (!string.IsNullOrEmpty(KeyboardAcceleratorTextOverride))
        {
            var accelBrush = ResolveBrush("OneTextSecondary", "TextSecondary", s_fallbackAcceleratorBrush);
            var accelFormatted = new FormattedText(
                KeyboardAcceleratorTextOverride, FontFamily ?? FrameworkElement.DefaultFontFamilyName, 12) { Foreground = accelBrush };
            TextMeasurement.MeasureText(accelFormatted);
            var accelX = RenderSize.Width - accelFormatted.Width - RightPadding;
            dc.DrawText(accelFormatted, new Point(accelX, (RenderSize.Height - accelFormatted.Height) / 2));
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        InvokeItem();
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        CloseSiblingSubMenus();
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        InvalidateVisual();
    }

    private void CloseSiblingSubMenus()
    {
        if (VisualParent is not Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children)
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
            if (panel.Children[nextIndex] is not UIElement candidate ||
                !candidate.IsEnabled ||
                candidate.Visibility != Visibility.Visible)
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
                if (panel.Children[i] is UIElement candidate &&
                    candidate.IsEnabled &&
                    candidate.Visibility == Visibility.Visible &&
                    candidate.Focus())
                {
                    return true;
                }
            }

            return false;
        }

        for (int i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is UIElement candidate &&
                candidate.IsEnabled &&
                candidate.Visibility == Visibility.Visible &&
                candidate.Focus())
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
            return false;
        }

        var placementTarget = popupRoot.OwnerPopup.PlacementTarget as UIElement;
        var parentSubItem = placementTarget as MenuFlyoutSubItem;
        popupRoot.OwnerPopup.IsOpen = false;

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
        if (popupRoot?.OwnerPopup.PlacementTarget is not MenuBarItem ownerItem || ownerItem.ParentMenuBar == null)
        {
            return false;
        }

        popupRoot.OwnerPopup.IsOpen = false;
        Dispatcher.BeginInvokeCritical(() =>
        {
            ownerItem.ParentMenuBar.FocusSibling(ownerItem, direction, openMenu: true);
        });

        return true;
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

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return ResolveBrush("OnePopupText", "TextPrimary", s_fallbackTextBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
