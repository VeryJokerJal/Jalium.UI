using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an item in a NavigationView with WinUI-style appearance.
/// </summary>
public sealed class NavigationViewItem : ContentControl
{
    #region Constants

    private const double IndentPerLevel = 28;

    #endregion

    #region Fields

    private int _indentLevel;

    #endregion

    #region Template Parts

    private Border? _indentSpacer;
    private Shapes.Path? _chevron;
    private StackPanel? _childrenPanel;
    private Threading.DispatcherTimer? _expandAnimTimer;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(NavigationViewItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>
    /// Identifies the IsExpanded dependency property.
    /// </summary>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the IsPressed dependency property.
    /// </summary>
    public new static readonly DependencyProperty IsPressedProperty = UIElement.IsPressedProperty;

    /// <summary>
    /// Identifies the SelectsOnInvoked dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectsOnInvokedProperty =
        DependencyProperty.Register(nameof(SelectsOnInvoked), typeof(bool), typeof(NavigationViewItem),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the icon for this item. Can be a string (glyph), IconElement, or any UIElement.
    /// </summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this item is selected.
    /// </summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this item is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the item is currently pressed.
    /// </summary>
    public new bool IsPressed => base.IsPressed;

    /// <summary>
    /// Gets or sets a value indicating whether this item becomes selected when invoked.
    /// </summary>
    public bool SelectsOnInvoked
    {
        get => (bool)GetValue(SelectsOnInvokedProperty)!;
        set => SetValue(SelectsOnInvokedProperty, value);
    }

    /// <summary>
    /// Gets the collection of child menu items.
    /// </summary>
    public List<NavigationViewItem> MenuItems { get; } = new();

    /// <summary>
    /// Gets a value indicating whether this item has child menu items.
    /// </summary>
    public bool HasUnrealizedChildren => MenuItems.Count > 0;

    /// <summary>
    /// Gets or sets the indentation level of this item.
    /// </summary>
    public int IndentLevel
    {
        get => _indentLevel;
        set
        {
            _indentLevel = value;
            UpdateIndent();
            // Also update children's indent level
            foreach (var child in MenuItems)
            {
                child.IndentLevel = value + 1;
            }
        }
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItem"/> class.
    /// </summary>
    public NavigationViewItem()
    {
        Focusable = true;

        // Use template-based content management (ContentPresenter in template handles Content)
        UseTemplateContentManagement();

        // Mouse down for click handling (expand/invoke)
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as Border;
        _chevron = GetTemplateChild("PART_Chevron") as Shapes.Path;
        _childrenPanel = GetTemplateChild("PART_ChildrenPanel") as StackPanel;

        // Sync initial state
        UpdateIndent();
        UpdateChevronVisibility();
        SyncExpandedVisualState();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // IsExpanded may be set before the item is attached to a window.
        // Re-sync once attached so layout/render is guaranteed to update.
        if (VisualParent != null)
        {
            SyncExpandedVisualState();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the item is invoked (clicked or tapped).
    /// </summary>
    public event EventHandler<NavigationViewItemInvokedEventArgs>? Invoked;

    /// <summary>
    /// Occurs when the selection state changes.
    /// </summary>
    public event EventHandler<bool>? SelectionChanged;

    /// <summary>
    /// Occurs when the expansion state changes.
    /// </summary>
    public event EventHandler<bool>? ExpansionChanged;

    #endregion

    #region Mouse Event Handlers

    private void OnMouseDownHandler(object? sender, RoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();

            // Find parent NavigationView and delegate click handling
            var navView = FindParentNavigationView();
            if (navView != null)
            {
                navView.HandleItemClicked(this);
            }
            else
            {
                // Fallback: handle locally if no parent NavigationView found
                if (HasUnrealizedChildren)
                {
                    IsExpanded = !IsExpanded;
                }
            }

            mouseArgs.Handled = true;
        }
    }

    private NavigationView? FindParentNavigationView()
    {
        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is NavigationView nav)
                return nav;
            current = current.VisualParent;
        }
        return null;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Invokes this item.
    /// </summary>
    internal void Invoke()
    {
        Invoked?.Invoke(this, new NavigationViewItemInvokedEventArgs(this));
    }

    /// <summary>
    /// Gets the children panel for adding child items.
    /// </summary>
    internal StackPanel? GetChildrenPanel()
    {
        // Ensure template is applied so PART_ChildrenPanel is available
        if (_childrenPanel == null)
        {
            ApplyTemplate();
        }
        return _childrenPanel;
    }

    #endregion

    #region State Updates

    private void UpdateIndent()
    {
        if (_indentSpacer != null)
        {
            _indentSpacer.Width = 12 + _indentLevel * IndentPerLevel;
        }
    }

    private void UpdateChevronVisibility()
    {
        if (_chevron != null)
        {
            _chevron.Visibility = HasUnrealizedChildren ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal void RefreshHierarchyVisualState()
    {
        UpdateChevronVisibility();
        SyncExpandedVisualState();
    }

    private void SyncExpandedVisualState()
    {
        if (_childrenPanel != null)
        {
            _childrenPanel.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_chevron != null)
        {
            var rotateTransform = _chevron.RenderTransform as RotateTransform ?? new RotateTransform();
            rotateTransform.Angle = IsExpanded ? 90 : 0;
            _chevron.RenderTransform = rotateTransform;
            _chevron.InvalidateVisual();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationViewItem item)
        {
            item.SelectionChanged?.Invoke(item, (bool)(e.NewValue ?? false));
        }
    }

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationViewItem item)
        {
            var expanded = (bool)(e.NewValue ?? false);
            item.ExpansionChanged?.Invoke(item, expanded);

            // Animate children panel expand/collapse + chevron rotation
            if (item._childrenPanel != null)
            {
                if (expanded)
                    item._expandAnimTimer = ExpandCollapseAnimator.AnimateExpand(item._childrenPanel, item._expandAnimTimer, item._chevron);
                else
                    item._expandAnimTimer = ExpandCollapseAnimator.AnimateCollapse(item._childrenPanel, item._expandAnimTimer, item._chevron);
            }

            item.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Provides data for the NavigationViewItem.Invoked event.
/// </summary>
public sealed class NavigationViewItemInvokedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the invoked item.
    /// </summary>
    public NavigationViewItem InvokedItem { get; }

    /// <summary>
    /// Gets a value indicating whether the invoked item is a settings item.
    /// </summary>
    public bool IsSettingsInvoked { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemInvokedEventArgs"/> class.
    /// </summary>
    public NavigationViewItemInvokedEventArgs(NavigationViewItem invokedItem, bool isSettingsInvoked = false)
    {
        InvokedItem = invokedItem;
        IsSettingsInvoked = isSettingsInvoked;
    }
}

/// <summary>
/// Represents a header item in a NavigationView.
/// </summary>
public sealed class NavigationViewItemHeader : ContentControl
{
    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.FromRgb(157, 157, 157));

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemHeader"/> class.
    /// </summary>
    public NavigationViewItemHeader()
    {
        Focusable = false;
        Height = 40;
        Margin = new Thickness(12, 8, 12, 4);
        Foreground = s_defaultFgBrush;
    }

    /// <summary>
    /// Override to prevent Content from being added to visual tree.
    /// </summary>
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        // Don't call base - we render content manually
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
            return;

        var text = GetContentText();
        if (!string.IsNullOrEmpty(text))
        {
            var fontMetrics = TextMeasurement.GetFontMetrics("Segoe UI Semibold", 14);
            var brush = Foreground ?? s_defaultFgBrush;
            var formattedText = new FormattedText(text, "Segoe UI Semibold", 14)
            {
                Foreground = brush
            };
            var textY = (ActualHeight - fontMetrics.LineHeight) / 2;
            dc.DrawText(formattedText, new Point(0, textY));
        }
    }

    private string? GetContentText()
    {
        if (Content == null) return null;
        if (Content is string str) return str;
        if (Content is TextBlock textBlock) return textBlock.Text;
        return Content.ToString();
    }
}

/// <summary>
/// Represents a separator in a NavigationView.
/// </summary>
public sealed class NavigationViewItemSeparator : Control
{
    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(60, 60, 60));

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationViewItemSeparator"/> class.
    /// </summary>
    public NavigationViewItemSeparator()
    {
        Focusable = false;
        Height = 1;
        Margin = new Thickness(16, 8, 16, 8);
        Background = s_defaultBackgroundBrush;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var brush = Background ?? s_defaultBackgroundBrush;
        dc.DrawRectangle(brush, null, new Rect(0, 0, ActualWidth, 1));
    }
}
