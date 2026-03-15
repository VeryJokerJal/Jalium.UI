using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the position of tabs within a TabControl.
/// </summary>
public enum Dock
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>
/// Represents a control that contains multiple items that share the same space on the screen.
/// </summary>
public class TabControl : Selector
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TabControlAutomationPeer(this);
    }

    // Cached brushes for OnRender fallback paths
    private static readonly SolidColorBrush s_tabStripBackgroundBrush = new(ThemeColors.TabStripBackground);
    private static readonly SolidColorBrush s_tabStripBorderBrush = new(ThemeColors.TabStripBorder);

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedContent dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedContentProperty =
        DependencyProperty.Register(nameof(SelectedContent), typeof(object), typeof(TabControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TabStripPlacement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripPlacementProperty =
        DependencyProperty.Register(nameof(TabStripPlacement), typeof(Dock), typeof(TabControl),
            new PropertyMetadata(Dock.Top, OnTabStripPlacementChanged));

    /// <summary>
    /// Identifies the TabStripBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripBackgroundProperty =
        DependencyProperty.Register(nameof(TabStripBackground), typeof(Brush), typeof(TabControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TabStripBorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TabStripBorderBrushProperty =
        DependencyProperty.Register(nameof(TabStripBorderBrush), typeof(Brush), typeof(TabControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TabStripHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripHeightProperty =
        DependencyProperty.Register(nameof(TabStripHeight), typeof(double), typeof(TabControl),
            new PropertyMetadata(36.0, OnLayoutPropertyChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the content of the selected tab.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedContent
    {
        get => GetValue(SelectedContentProperty);
        set => SetValue(SelectedContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the tab strip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Dock TabStripPlacement
    {
        get => (Dock)GetValue(TabStripPlacementProperty);
        set => SetValue(TabStripPlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the tab strip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Brush? TabStripBackground
    {
        get => (Brush?)GetValue(TabStripBackgroundProperty);
        set => SetValue(TabStripBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush for the tab strip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TabStripBorderBrush
    {
        get => (Brush?)GetValue(TabStripBorderBrushProperty);
        set => SetValue(TabStripBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of the tab strip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public double TabStripHeight
    {
        get => (double)GetValue(TabStripHeightProperty);
        set => SetValue(TabStripHeightProperty, value);
    }

    #endregion

    public TabControl()
    {
        Focusable = true;

        // Subscribe to collection changes
        Items.CollectionChanged += OnTabItemsChanged;

        // Register keyboard handler
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not KeyEventArgs keyArgs || keyArgs.Handled) return;

        var tabCount = Items.Count;
        if (tabCount == 0) return;

        switch (keyArgs.Key)
        {
            case Key.Tab when keyArgs.IsControlDown:
            {
                // Ctrl+Shift+Tab = previous, Ctrl+Tab = next
                var direction = keyArgs.IsShiftDown ? -1 : 1;
                SelectAdjacentTab(direction);
                keyArgs.Handled = true;
                break;
            }
            case Key.Left:
            case Key.Up:
                SelectAdjacentTab(-1);
                keyArgs.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                SelectAdjacentTab(1);
                keyArgs.Handled = true;
                break;
            case Key.Home:
                if (tabCount > 0) SelectedIndex = 0;
                keyArgs.Handled = true;
                break;
            case Key.End:
                if (tabCount > 0) SelectedIndex = tabCount - 1;
                keyArgs.Handled = true;
                break;
        }
    }

    private void SelectAdjacentTab(int direction)
    {
        var tabCount = Items.Count;
        if (tabCount == 0) return;

        var current = SelectedIndex;
        var next = (current + direction + tabCount) % tabCount;
        SelectedIndex = next;
    }

    /// <summary>
    /// Creates the panel that hosts the tab items.
    /// Uses horizontal orientation for Top/Bottom placement, vertical for Left/Right.
    /// </summary>
    protected override Panel CreateItemsPanel()
    {
        var orientation = (TabStripPlacement == Dock.Top || TabStripPlacement == Dock.Bottom)
            ? Orientation.Horizontal
            : Orientation.Vertical;

        return new StackPanel { Orientation = orientation };
    }

    private void OnTabItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Set TabControl reference for old items
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TabItem tabItem)
                {
                    tabItem.TabControl = null;
                }
            }
        }

        // Set TabControl reference for new items
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is TabItem tabItem)
                {
                    tabItem.TabControl = this;
                }
            }
        }

        // Handle reset
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is TabItem tabItem)
                {
                    tabItem.TabControl = this;
                }
            }
        }

        // Adjust SelectedIndex after items changed
        if (Items.Count == 0)
        {
            SelectedIndex = -1;
        }
        else if (SelectedIndex >= Items.Count)
        {
            SelectedIndex = Items.Count - 1;
        }
        else if (SelectedIndex < 0)
        {
            SelectedIndex = 0;
        }

        // Update selection state
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is TabItem tabItem)
            {
                tabItem.IsSelected = (i == SelectedIndex);
            }
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnTabStripPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabControl tabControl)
        {
            tabControl.InvalidateMeasure();
            tabControl.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabControl tabControl)
        {
            tabControl.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabControl tabControl)
        {
            tabControl.InvalidateMeasure();
            tabControl.InvalidateVisual();
        }
    }

    internal void SelectTab(TabItem tabItem)
    {
        var index = Items.IndexOf(tabItem);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        // Update IsSelected on all TabItems
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is TabItem tabItem)
            {
                tabItem.IsSelected = (i == SelectedIndex);
            }
        }

        // Remove old content from visual tree
        if (_selectedContentElement != null)
        {
            RemoveVisualChild(_selectedContentElement);
            _selectedContentElement = null;
        }

        // Update selected content
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            var selectedItem = Items[SelectedIndex];
            if (selectedItem is TabItem tabItem)
            {
                SetValue(SelectedContentProperty, tabItem.Content);

                // Add new content to visual tree if it's a UIElement and has no parent
                if (tabItem.Content is UIElement contentElement && contentElement.VisualParent == null)
                {
                    _selectedContentElement = contentElement;
                    AddVisualChild(contentElement);
                }
            }
        }
        else
        {
            SetValue(SelectedContentProperty, null);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Ensure ItemsHost is created
        if (ItemsHost == null)
        {
            RefreshItems();
        }

        // Calculate tab strip dimensions
        double tabStripWidth = availableSize.Width;
        double tabStripHeight = TabStripHeight;
        double verticalTabStripWidth = 120;

        if (TabStripPlacement == Dock.Left || TabStripPlacement == Dock.Right)
        {
            tabStripWidth = verticalTabStripWidth;
            tabStripHeight = availableSize.Height;
        }

        // Measure ItemsHost (contains tab headers)
        if (ItemsHost != null)
        {
            ItemsHost.Measure(new Size(tabStripWidth, tabStripHeight));
        }

        // Measure tab items based on orientation
        bool isVertical = TabStripPlacement == Dock.Left || TabStripPlacement == Dock.Right;
        foreach (var item in Items)
        {
            if (item is TabItem tabItem)
            {
                if (isVertical)
                {
                    // Vertical: full width of tab strip, height based on content
                    tabItem.Measure(new Size(verticalTabStripWidth, TabStripHeight));
                }
                else
                {
                    // Horizontal: width based on content, height is TabStripHeight
                    tabItem.Measure(new Size(200, TabStripHeight));
                }
            }
        }

        // Calculate content area dimensions
        double contentWidth = availableSize.Width;
        double contentHeight = availableSize.Height - TabStripHeight;

        if (TabStripPlacement == Dock.Left || TabStripPlacement == Dock.Right)
        {
            contentWidth = availableSize.Width - verticalTabStripWidth;
            contentHeight = availableSize.Height;
        }

        // Measure content
        if (_selectedContentElement is FrameworkElement contentElement)
        {
            contentElement.Measure(new Size(contentWidth, contentHeight));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double tabStripHeight = TabStripHeight;
        double verticalTabStripWidth = 120;

        // Calculate tab strip rect
        Rect tabStripRect;
        Rect contentRect;

        switch (TabStripPlacement)
        {
            case Dock.Bottom:
                tabStripRect = new Rect(0, finalSize.Height - tabStripHeight, finalSize.Width, tabStripHeight);
                contentRect = new Rect(0, 0, finalSize.Width, finalSize.Height - tabStripHeight);
                break;
            case Dock.Left:
                tabStripRect = new Rect(0, 0, verticalTabStripWidth, finalSize.Height);
                contentRect = new Rect(verticalTabStripWidth, 0, finalSize.Width - verticalTabStripWidth, finalSize.Height);
                break;
            case Dock.Right:
                tabStripRect = new Rect(finalSize.Width - verticalTabStripWidth, 0, verticalTabStripWidth, finalSize.Height);
                contentRect = new Rect(0, 0, finalSize.Width - verticalTabStripWidth, finalSize.Height);
                break;
            default: // Top
                tabStripRect = new Rect(0, 0, finalSize.Width, tabStripHeight);
                contentRect = new Rect(0, tabStripHeight, finalSize.Width, finalSize.Height - tabStripHeight);
                break;
        }

        // Arrange ItemsHost (the panel containing tab headers)
        // StackPanel will automatically arrange its children (TabItems) based on orientation
        if (ItemsHost != null)
        {
            ItemsHost.Arrange(tabStripRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        // Arrange content
        if (_selectedContentElement is FrameworkElement contentElement)
        {
            contentElement.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #region Visual Children

    private UIElement? _selectedContentElement;

    /// <summary>
    /// Gets the number of visual children.
    /// TabControl has up to 2 visual children: ItemsHost (for tab headers) and selected content element.
    /// </summary>
    public override int VisualChildrenCount
    {
        get
        {
            int count = 0;
            if (ItemsHost != null) count++;
            if (_selectedContentElement != null) count++;
            return count;
        }
    }

    /// <summary>
    /// Gets the visual child at the specified index.
    /// Index 0: ItemsHost (tab header panel)
    /// Index 1: Selected content element (if exists)
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        if (index == 0 && ItemsHost != null)
            return ItemsHost;
        if (index == 1 && _selectedContentElement != null)
            return _selectedContentElement;
        if (index == 0 && ItemsHost == null && _selectedContentElement != null)
            return _selectedContentElement;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        double tabStripSize = TabStripHeight;
        double verticalTabStripWidth = 120;

        // Draw background (content area)
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw tab strip background
        var tabStripBrush = ResolveTabStripBackground();
        Rect tabStripRect;

        switch (TabStripPlacement)
        {
            case Dock.Bottom:
                tabStripRect = new Rect(0, ActualHeight - tabStripSize, ActualWidth, tabStripSize);
                break;
            case Dock.Left:
                tabStripRect = new Rect(0, 0, verticalTabStripWidth, ActualHeight);
                break;
            case Dock.Right:
                tabStripRect = new Rect(ActualWidth - verticalTabStripWidth, 0, verticalTabStripWidth, ActualHeight);
                break;
            default: // Top
                tabStripRect = new Rect(0, 0, ActualWidth, tabStripSize);
                break;
        }

        dc.DrawRectangle(tabStripBrush, null, tabStripRect);

        // Draw border line
        var borderBrush = ResolveTabStripBorderBrush();
        var borderPen = new Pen(borderBrush, 1);
        switch (TabStripPlacement)
        {
            case Dock.Bottom:
                dc.DrawLine(borderPen, new Point(0, ActualHeight - tabStripSize), new Point(ActualWidth, ActualHeight - tabStripSize));
                break;
            case Dock.Left:
                dc.DrawLine(borderPen, new Point(verticalTabStripWidth, 0), new Point(verticalTabStripWidth, ActualHeight));
                break;
            case Dock.Right:
                dc.DrawLine(borderPen, new Point(ActualWidth - verticalTabStripWidth, 0), new Point(ActualWidth - verticalTabStripWidth, ActualHeight));
                break;
            default: // Top
                dc.DrawLine(borderPen, new Point(0, tabStripSize), new Point(ActualWidth, tabStripSize));
                break;
        }

        base.OnRender(drawingContextObj);
    }

    private Brush ResolveTabStripBackground()
    {
        return TabStripBackground
            ?? TryFindResource("TabStripBackground") as Brush
            ?? s_tabStripBackgroundBrush;
    }

    private Brush ResolveTabStripBorderBrush()
    {
        return TabStripBorderBrush
            ?? TryFindResource("TabStripBorder") as Brush
            ?? s_tabStripBorderBrush;
    }
}

/// <summary>
/// Represents an item in a TabControl.
/// </summary>
public class TabItem : HeaderedContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TabItemAutomationPeer(this);
    }

    // Cached brushes for OnRender fallback paths
    private static readonly SolidColorBrush s_selectedBackgroundBrush = new(ThemeColors.TabItemSelectedBackground);
    private static readonly SolidColorBrush s_hoverBackgroundBrush = new(ThemeColors.TabItemHoverBackground);
    private static readonly SolidColorBrush s_transparentBrush = new(Color.Transparent);
    private static readonly SolidColorBrush s_textPrimaryBrush = new(ThemeColors.TextPrimary);
    private static readonly SolidColorBrush s_textSecondaryBrush = new(ThemeColors.TextSecondary);
    private static readonly SolidColorBrush s_indicatorBrush = new(ThemeColors.TabItemIndicator);

    internal TabControl? TabControl { get; set; }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TabItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>
    /// Identifies the IndicatorBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty IndicatorBrushProperty =
        DependencyProperty.Register(nameof(IndicatorBrush), typeof(Brush), typeof(TabItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IndicatorHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty IndicatorHeightProperty =
        DependencyProperty.Register(nameof(IndicatorHeight), typeof(double), typeof(TabItem),
            new PropertyMetadata(2.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectedBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedBackgroundProperty =
        DependencyProperty.Register(nameof(SelectedBackground), typeof(Brush), typeof(TabItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HoverBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(TabItem),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether this tab is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the selection indicator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? IndicatorBrush
    {
        get => (Brush?)GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of the selection indicator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double IndicatorHeight
    {
        get => (double)GetValue(IndicatorHeightProperty);
        set => SetValue(IndicatorHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the background when selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public Brush? SelectedBackground
    {
        get => (Brush?)GetValue(SelectedBackgroundProperty);
        set => SetValue(SelectedBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background when hovered.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? HoverBackground
    {
        get => (Brush?)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    #endregion

    public TabItem()
    {
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
    }

    /// <summary>
    /// Override to prevent Content from being added as a visual child.
    /// TabItem only displays Header in the tab; Content is shown by TabControl in the content area.
    /// </summary>
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        // Do NOT call base - we don't want Content to be a visual child of TabItem
        // The TabControl will handle displaying the content in its content area
        InvalidateMeasure();
    }

    /// <summary>
    /// TabItem has no visual children (Content is handled by TabControl).
    /// </summary>
    public override int VisualChildrenCount => 0;

    /// <summary>
    /// TabItem has no visual children.
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabItem tabItem)
        {
            tabItem.InvalidateVisual();
        }
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        TabControl?.SelectTab(this);
        e.Handled = true;
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabItem tabItem)
        {
            tabItem.InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure based on header content
        var headerText = Header?.ToString() ?? "";
        var charWidth = 14 * 0.6;
        var textWidth = headerText.Length * charWidth;

        // Calculate desired width based on text
        var desiredWidth = Math.Max(textWidth + Padding.Left + Padding.Right, 60);

        // If available width is constrained (e.g., vertical tabs), use that width
        var width = double.IsInfinity(availableSize.Width) ? desiredWidth : Math.Min(desiredWidth, availableSize.Width);

        // Use availableSize.Width for vertical tabs to fill the strip width
        if (availableSize.Width > 0 && availableSize.Width < desiredWidth)
        {
            width = availableSize.Width;
        }

        return new Size(width, availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        // Determine background based on state
        Brush bgBrush;
        if (IsSelected)
        {
            bgBrush = ResolveSelectedBackground();
        }
        else if (IsMouseOver)
        {
            bgBrush = ResolveHoverBackground();
        }
        else
        {
            bgBrush = Background ?? s_transparentBrush;
        }

        dc.DrawRectangle(bgBrush, null, bounds);

        // Draw header text
        var headerText = Header?.ToString() ?? "";
        if (!string.IsNullOrEmpty(headerText))
        {
            // Determine text color based on state
            Brush textBrush;
            if (IsSelected || IsMouseOver)
            {
                textBrush = ResolvePrimaryTextBrush();
            }
            else
            {
                textBrush = ResolveSecondaryTextBrush();
            }

            var fontSize = FontSize > 0 ? FontSize : 13;
            var fontFamily = !string.IsNullOrEmpty(FontFamily) ? FontFamily : "Segoe UI";

            var text = new FormattedText(headerText, fontFamily, fontSize)
            {
                Foreground = textBrush
            };

            // Measure text to get accurate Width/Height
            TextMeasurement.MeasureText(text);

            var textX = (ActualWidth - text.Width) / 2;
            var textY = (ActualHeight - text.Height) / 2;

            dc.DrawText(text, new Point(textX, textY));
        }

        // Draw selection indicator
        if (IsSelected)
        {
            var indicatorBrush = ResolveIndicatorBrush();
            var indicatorHeight = IndicatorHeight;
            var indicatorRect = new Rect(0, ActualHeight - indicatorHeight, ActualWidth, indicatorHeight);
            dc.DrawRectangle(indicatorBrush, null, indicatorRect);
        }

        // Don't call base - we handle all rendering
    }

    private Brush ResolveSelectedBackground()
    {
        return SelectedBackground
            ?? TryFindResource("TabItemSelectedBackground") as Brush
            ?? s_selectedBackgroundBrush;
    }

    private Brush ResolveHoverBackground()
    {
        return HoverBackground
            ?? TryFindResource("TabItemHoverBackground") as Brush
            ?? s_hoverBackgroundBrush;
    }

    private Brush ResolvePrimaryTextBrush()
    {
        return TryFindResource("TextPrimary") as Brush ?? s_textPrimaryBrush;
    }

    private Brush ResolveSecondaryTextBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextSecondary") as Brush
            ?? Foreground
            ?? s_textSecondaryBrush;
    }

    private Brush ResolveIndicatorBrush()
    {
        return IndicatorBrush
            ?? TryFindResource("TabItemIndicator") as Brush
            ?? s_indicatorBrush;
    }
}

/// <summary>
/// Represents a control with a header and content.
/// </summary>
public class HeaderedContentControl : ContentControl
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(HeaderedContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
