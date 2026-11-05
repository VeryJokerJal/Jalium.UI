using Jalium.UI.Controls.Primitives;
using System.Collections.Specialized;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the position of tabs within a TabControl.
/// </summary>
public enum Dock
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3
}

/// <summary>
/// Represents a control that contains multiple items that share the same space on the screen.
/// </summary>
[StyleTypedProperty(Property = nameof(ItemContainerStyle), StyleTargetType = typeof(TabItem))]
[TemplatePart(Name = "PART_SelectedContentHost", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
[TemplateVisualState(Name = "Top", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Bottom", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Left", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Right", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
public class TabControl : Selector
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TabControlAutomationPeer(this);
    }

    private bool _isSynchronizingContainerSelection;
    private readonly HashSet<TabItem> _directTabItems = new();

    #region Dependency Properties

    private static readonly DependencyPropertyKey SelectedContentPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContent), typeof(object), typeof(TabControl),
            new PropertyMetadata(null));

    /// <summary>Identifies the read-only <see cref="SelectedContent"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedContentProperty = SelectedContentPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedContentTemplatePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContentTemplate), typeof(DataTemplate), typeof(TabControl),
            new PropertyMetadata(null));

    /// <summary>Identifies the read-only <see cref="SelectedContentTemplate"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedContentTemplateProperty =
        SelectedContentTemplatePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedContentTemplateSelectorPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContentTemplateSelector), typeof(DataTemplateSelector), typeof(TabControl),
            new PropertyMetadata(null));

    /// <summary>Identifies the read-only <see cref="SelectedContentTemplateSelector"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedContentTemplateSelectorProperty =
        SelectedContentTemplateSelectorPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedContentStringFormatPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContentStringFormat), typeof(string), typeof(TabControl),
            new PropertyMetadata(null));

    /// <summary>Identifies the read-only <see cref="SelectedContentStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedContentStringFormatProperty =
        SelectedContentStringFormatPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedContentHorizontalAlignmentPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContentHorizontalAlignment), typeof(HorizontalAlignment), typeof(TabControl),
            new PropertyMetadata(HorizontalAlignment.Stretch));

    /// <summary>Identifies the alignment used by the selected-content presenter.</summary>
    public static readonly DependencyProperty SelectedContentHorizontalAlignmentProperty = SelectedContentHorizontalAlignmentPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedContentVerticalAlignmentPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectedContentVerticalAlignment), typeof(VerticalAlignment), typeof(TabControl),
            new PropertyMetadata(VerticalAlignment.Stretch));

    /// <summary>Identifies the alignment used by the selected-content presenter.</summary>
    public static readonly DependencyProperty SelectedContentVerticalAlignmentProperty = SelectedContentVerticalAlignmentPropertyKey.DependencyProperty;

    /// <summary>Identifies the <see cref="ContentTemplate"/> dependency property.</summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(TabControl),
            new PropertyMetadata(null, OnContentPresentationPropertyChanged));

    /// <summary>Identifies the <see cref="ContentTemplateSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(TabControl),
            new PropertyMetadata(null, OnContentPresentationPropertyChanged));

    /// <summary>Identifies the <see cref="ContentStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register(nameof(ContentStringFormat), typeof(string), typeof(TabControl),
            new PropertyMetadata(null, OnContentPresentationPropertyChanged));

    /// <summary>
    /// Identifies the TabStripPlacement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripPlacementProperty =
        DependencyProperty.Register(nameof(TabStripPlacement), typeof(Dock), typeof(TabControl),
            new PropertyMetadata(Dock.Top, OnTabStripPlacementChanged), IsValidDock);

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

    /// <summary>Gets the selected tab's horizontal content alignment.</summary>
    public HorizontalAlignment SelectedContentHorizontalAlignment => (HorizontalAlignment)GetValue(SelectedContentHorizontalAlignmentProperty)!;

    /// <summary>Gets the selected tab's vertical content alignment.</summary>
    public VerticalAlignment SelectedContentVerticalAlignment => (VerticalAlignment)GetValue(SelectedContentVerticalAlignmentProperty)!;

    /// <summary>
    /// Gets the content of the selected tab.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public object? SelectedContent => GetValue(SelectedContentProperty);

    /// <summary>Gets the content template selected for the current tab.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DataTemplate? SelectedContentTemplate => (DataTemplate?)GetValue(SelectedContentTemplateProperty);

    /// <summary>Gets the content-template selector selected for the current tab.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DataTemplateSelector? SelectedContentTemplateSelector =>
        (DataTemplateSelector?)GetValue(SelectedContentTemplateSelectorProperty);

    /// <summary>Gets the string format selected for the current tab.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? SelectedContentStringFormat => (string?)GetValue(SelectedContentStringFormatProperty);

    /// <summary>Gets or sets the fallback template for selected tab content.</summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Gets or sets the fallback template selector for selected tab content.</summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets the fallback composite format for selected tab content.</summary>
    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the tab strip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Dock TabStripPlacement
    {
        get => (Dock)GetValue(TabStripPlacementProperty)!;
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
        get => (double)GetValue(TabStripHeightProperty)!;
        set => SetValue(TabStripHeightProperty, value);
    }

    #endregion

    public TabControl()
    {
        Focusable = true;

        // Register keyboard handler
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));

        // Swipe-to-switch-tab on touch.
        IsManipulationEnabled = true;
        AddHandler(ManipulationCompletedEvent, new RoutedEventHandler(OnManipulationCompletedHandler));
        AddHandler(UIElement.StylusSystemGestureEvent, new RoutedEventHandler(OnSystemGestureHandler));
    }

    /// <summary>
    /// Identifies the IsSwipeEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsSwipeEnabledProperty =
        DependencyProperty.Register(nameof(IsSwipeEnabled), typeof(bool), typeof(TabControl),
            new PropertyMetadata(true));

    /// <summary>
    /// True (default) to allow horizontal swipe gestures to switch between tabs on touch surfaces.
    /// </summary>
    public bool IsSwipeEnabled
    {
        get => (bool)(GetValue(IsSwipeEnabledProperty) ?? true);
        set => SetValue(IsSwipeEnabledProperty, value);
    }

    private const double SwipeSwitchThresholdDips = 50.0;

    private void OnManipulationCompletedHandler(object sender, RoutedEventArgs e)
    {
        if (!IsSwipeEnabled || e.Handled) return;
        if (e is not ManipulationCompletedEventArgs args) return;
        var totalX = args.TotalManipulation?.Translation.X ?? 0;
        if (Math.Abs(totalX) < SwipeSwitchThresholdDips) return;

        int direction = totalX < 0 ? +1 : -1; // swipe left ⇒ next tab; swipe right ⇒ previous
        SelectAdjacentTab(direction);
        e.Handled = true;
    }

    private void OnSystemGestureHandler(object sender, RoutedEventArgs e)
    {
        if (!IsSwipeEnabled || e.Handled) return;
        if (e is not Input.StylusSystemGestureEventArgs gesture) return;
        if (gesture.SystemGesture != Input.SystemGesture.Flick) return;
        // Direction is not known from SystemGesture alone — the recent
        // ManipulationCompleted (if any) already handled the swipe direction.
        // Here we just consume the flick so it does not cascade further.
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled) return;

        if (!(e.Key == Key.Tab && e.IsControlDown) && !IsHeaderKeySource(e.OriginalSource)) return;

        var tabCount = Items.Count;
        if (tabCount == 0) return;

        switch (e.Key)
        {
            case Key.Tab when e.IsControlDown:
            {
                // Ctrl+Shift+Tab = previous, Ctrl+Tab = next
                var direction = e.IsShiftDown ? -1 : 1;
                SelectAdjacentTab(direction, focusHeader: true);
                e.Handled = true;
                break;
            }
            case Key.Left:
            case Key.Up:
                SelectAdjacentTab(e.Key == Key.Left && FlowDirection == FlowDirection.RightToLeft ? 1 : -1, focusHeader: true);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                SelectAdjacentTab(e.Key == Key.Right && FlowDirection == FlowDirection.RightToLeft ? -1 : 1, focusHeader: true);
                e.Handled = true;
                break;
            case Key.Home:
                SelectAvailableTab(0, 1, focusHeader: true);
                e.Handled = true;
                break;
            case Key.End:
                SelectAvailableTab(tabCount - 1, -1, focusHeader: true);
                e.Handled = true;
                break;
        }
    }

    private bool IsHeaderKeySource(object? source)
    {
        if (ReferenceEquals(source, this) || source == null) return true;
        for (var element = source as Visual; element != null; element = element.VisualParent)
        {
            if (element is TabItem item) return ReferenceEquals(item.TabControl, this);
            if (ReferenceEquals(element, this)) break;
        }
        return false;
    }

    private void SelectAdjacentTab(int direction, bool focusHeader = false)
    {
        var tabCount = Items.Count;
        if (tabCount == 0) return;

        var current = SelectedIndex;
        var next = current < 0 ? (direction > 0 ? 0 : tabCount - 1) : (current + direction + tabCount) % tabCount;
        SelectAvailableTab(next, direction, focusHeader);
    }

    private void SelectAvailableTab(int start, int direction, bool focusHeader = false)
    {
        bool moveFocus = focusHeader || IsKeyboardFocused || Keyboard.FocusedElement != null && IsHeaderKeySource(Keyboard.FocusedElement);
        for (int attempt = 0, index = start; attempt < Items.Count; attempt++, index = (index + direction + Items.Count) % Items.Count)
        {
            var item = GetTabItemAt(index);
            if (item is { IsEnabled: false } || item is { Visibility: not Visibility.Visible }) continue;
            SetCurrentValue(SelectedIndexProperty, index);
            if (moveFocus) item?.Focus();
            return;
        }
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item) => new TabItem();

    /// <inheritdoc />
    protected override DependencyObject GetContainerForItemOverride() => new TabItem();

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is TabItem;

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is not TabItem tabItem)
        {
            return;
        }

        if (!ReferenceEquals(tabItem, item))
        {
            // WPF's generated TabItem uses the logical item as both Header and Content.
            // ItemTemplate belongs to the header; the selected-content template is chosen
            // separately from TabItem.ContentTemplate or the TabControl fallback properties.
            if (item is Visual)
            {
                tabItem.ClearValue(HeaderedContentControl.HeaderProperty);
            }
            else
            {
                tabItem.Header = item;
            }
            SetOrClearValue(tabItem, HeaderedContentControl.HeaderTemplateProperty, ItemTemplate);
            SetOrClearValue(tabItem, HeaderedContentControl.HeaderTemplateSelectorProperty, ItemTemplateSelector);
            SetOrClearValue(tabItem, HeaderedContentControl.HeaderStringFormatProperty, ItemStringFormat);
            tabItem.ClearValue(ContentControl.ContentTemplateProperty);
            tabItem.ClearValue(ContentControl.ContentTemplateSelectorProperty);
            tabItem.ClearValue(ContentControl.ContentStringFormatProperty);
        }

        tabItem.TabControl = this;

        _isSynchronizingContainerSelection = true;
        try
        {
            tabItem.IsSelected = GetIndexOf(item) == SelectedIndex;
        }
        finally
        {
            _isSynchronizingContainerSelection = false;
        }

        if (tabItem.IsSelected)
        {
            UpdateSelectedContent();
        }
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            throw new InvalidOperationException("A TabControl item container must be a FrameworkElement.");
        }

        PrepareContainerForItem(frameworkElement, item);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (element is not TabItem tabItem || ReferenceEquals(element, item))
        {
            base.ClearContainerForItem(element, item);
            return;
        }

        _isSynchronizingContainerSelection = true;
        try
        {
            tabItem.IsSelected = false;
        }
        finally
        {
            _isSynchronizingContainerSelection = false;
        }

        tabItem.TabControl = null;
        base.ClearContainerForItem(element, item);
        tabItem.ClearValue(HeaderedContentControl.HeaderProperty);
        tabItem.ClearValue(HeaderedContentControl.HeaderTemplateProperty);
        tabItem.ClearValue(HeaderedContentControl.HeaderTemplateSelectorProperty);
        tabItem.ClearValue(HeaderedContentControl.HeaderStringFormatProperty);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is FrameworkElement frameworkElement)
        {
            ClearContainerForItem(frameworkElement, item);
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);

        var currentDirectTabItems = new HashSet<TabItem>();
        foreach (var item in Items)
        {
            if (item is TabItem tabItem)
            {
                currentDirectTabItems.Add(tabItem);
            }
        }

        foreach (var oldTabItem in _directTabItems)
        {
            if (!currentDirectTabItems.Contains(oldTabItem))
            {
                _isSynchronizingContainerSelection = true;
                try
                {
                    oldTabItem.IsSelected = false;
                }
                finally
                {
                    _isSynchronizingContainerSelection = false;
                }

                oldTabItem.TabControl = null;
            }
        }

        _directTabItems.Clear();
        foreach (var tabItem in currentDirectTabItems)
        {
            _directTabItems.Add(tabItem);
            tabItem.TabControl = this;
        }

        var itemCount = GetItemCount();
        if (itemCount == 0 && !HasDeferredSelectedIndex)
        {
            SelectedIndex = -1;
        }
        else
        {
            var currentItemIndex = SelectedItem == null ? -1 : GetIndexOf(SelectedItem);
            if (currentItemIndex >= 0)
            {
                if (SelectedIndex != currentItemIndex)
                {
                    SelectedIndex = currentItemIndex;
                }
            }
            else if (SelectedItem != null)
            {
                var replacementIndex = e.OldStartingIndex >= 0
                    ? Math.Min(e.OldStartingIndex, itemCount - 1)
                    : Math.Clamp(SelectedIndex, 0, itemCount - 1);
                SelectedItem = GetItemAt(replacementIndex);
            }
            else if (!HasDeferredSelectedIndex && (SelectedIndex < 0 || SelectedIndex >= itemCount))
            {
                SelectedIndex = 0;
            }
        }

        UpdateContainerSelection();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnTabStripPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabControl tabControl)
        {
            for (var i = 0; i < tabControl.GetItemCount(); i++)
            {
                tabControl.GetTabItemAt(i)?.UpdateTabStripPlacement();
            }

            tabControl.UpdateVisualStates();
            tabControl.InvalidateMeasure();
        }
    }

    private static bool IsValidDock(object? value) =>
        value is Dock.Left or Dock.Top or Dock.Right or Dock.Bottom;

    private static void OnContentPresentationPropertyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((TabControl)d).UpdateSelectedContent();

    private static void SetOrClearValue(DependencyObject target, DependencyProperty property, object? value)
    {
        if (value == null)
        {
            target.ClearValue(property);
        }
        else
        {
            target.SetValue(property, value);
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
        var index = GetIndexOf(tabItem);
        if (index < 0)
        {
            index = ItemContainerGenerator.IndexFromContainer(tabItem);
        }

        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    internal void OnContainerSelectionChanged(TabItem tabItem, bool isSelected)
    {
        if (_isSynchronizingContainerSelection)
        {
            return;
        }

        var index = GetIndexOf(tabItem);
        if (index < 0)
        {
            index = ItemContainerGenerator.IndexFromContainer(tabItem);
        }

        if (index < 0)
        {
            return;
        }

        if (isSelected)
        {
            SelectedIndex = index;
        }
        else if (SelectedIndex == index)
        {
            SelectedIndex = -1;
        }
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        _isSynchronizingContainerSelection = true;
        try
        {
            for (var i = 0; i < GetItemCount(); i++)
            {
                if (GetTabItemAt(i) is { } tabItem)
                {
                    tabItem.IsSelected = i == SelectedIndex;
                }
            }
        }
        finally
        {
            _isSynchronizingContainerSelection = false;
        }

        UpdateSelectedContent();
    }

    private TabItem? GetTabItemAt(int index)
    {
        if (index < 0 || index >= GetItemCount())
        {
            return null;
        }

        var item = GetItemAt(index);
        if (item is TabItem ownContainer)
        {
            return ownContainer;
        }

        if (ItemContainerGenerator.ContainerFromIndex(index) is TabItem generatedContainer)
        {
            return generatedContainer;
        }

        if (ItemsHost != null && index < ItemsHost.Children.Count)
        {
            return ItemsHost.Children[index] as TabItem;
        }

        return null;
    }

    private void UpdateSelectedContent()
    {
        var selectedIndex = SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= GetItemCount())
        {
            SetSelectedContentValues(null, null, null, null, null);
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        var logicalItem = GetItemAt(selectedIndex);
        var selectedTabItem = GetTabItemAt(selectedIndex);
        var content = selectedTabItem != null ? selectedTabItem.Content : logicalItem;

        var usesTabItemPresentation = selectedTabItem != null &&
            (selectedTabItem.HasLocalValue(ContentControl.ContentTemplateProperty) ||
             selectedTabItem.HasLocalValue(ContentControl.ContentTemplateSelectorProperty) ||
             selectedTabItem.HasLocalValue(ContentControl.ContentStringFormatProperty) ||
             selectedTabItem.ContentTemplate != null ||
             selectedTabItem.ContentTemplateSelector != null ||
             selectedTabItem.ContentStringFormat != null);

        var contentTemplate = usesTabItemPresentation ? selectedTabItem!.ContentTemplate : ContentTemplate;
        var contentTemplateSelector = usesTabItemPresentation
            ? selectedTabItem!.ContentTemplateSelector
            : ContentTemplateSelector;
        var contentStringFormat = usesTabItemPresentation
            ? selectedTabItem!.ContentStringFormat
            : ContentStringFormat;

        SetSelectedContentValues(
            content,
            contentTemplate,
            contentTemplateSelector,
            contentStringFormat,
            selectedTabItem);

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SetSelectedContentValues(
        object? content,
        DataTemplate? contentTemplate,
        DataTemplateSelector? contentTemplateSelector,
        string? contentStringFormat,
        TabItem? selectedTabItem)
    {
        SetValue(SelectedContentPropertyKey, content);
        SetValue(SelectedContentTemplatePropertyKey, contentTemplate);
        SetValue(SelectedContentTemplateSelectorPropertyKey, contentTemplateSelector);
        SetValue(SelectedContentStringFormatPropertyKey, contentStringFormat);

        SetValue(SelectedContentHorizontalAlignmentPropertyKey,
            selectedTabItem?.HorizontalContentAlignment ?? HorizontalContentAlignment);
        SetValue(SelectedContentVerticalAlignmentPropertyKey,
            selectedTabItem?.VerticalContentAlignment ?? VerticalContentAlignment);
        NotifySelectedContentAutomation();
    }

    private void NotifySelectedContentAutomation()
    {
        if (UIElementAutomationPeer.FromElement(this) is TabControlAutomationPeer peer)
            peer.OnSelectedContentChanged();
    }

    internal ContentPresenter? SelectedContentAutomationRoot => SelectedContent == null ? null : _selectedContentPresenter;

    private ContentPresenter? _selectedContentPresenter;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _selectedContentPresenter = GetTemplateChild("PART_SelectedContentHost") as ContentPresenter;
        UpdateSelectedContent();
        UpdateVisualStates();
    }

    protected override void OnTemplateChanging()
    {
        _selectedContentPresenter = null;
        base.OnTemplateChanging();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == IsKeyboardFocusWithinProperty)
            UpdateVisualStates();
        if (e.Property == HorizontalContentAlignmentProperty || e.Property == VerticalContentAlignmentProperty)
            UpdateSelectedContent();
    }

    private void UpdateVisualStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, TabStripPlacement.ToString(), false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsEnabled && IsKeyboardFocusWithin ? "Focused" : "Unfocused", false);
    }

    internal void OnSelectedTabContentChanged(TabItem tabItem, object? _)
    {
        if (ReferenceEquals(tabItem, GetTabItemAt(SelectedIndex)))
        {
            UpdateSelectedContent();
        }
    }

    internal void OnSelectedTabContentPresentationChanged(TabItem tabItem)
    {
        if (ReferenceEquals(tabItem, GetTabItemAt(SelectedIndex)))
        {
            UpdateSelectedContent();
        }
    }


}

/// <summary>
/// Represents an item in a TabControl.
/// </summary>
[TemplatePart(Name = "PART_HeaderPresenter", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "MouseOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Top", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Bottom", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Left", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Right", GroupName = "PlacementStates")]
public class TabItem : HeaderedContentControl
{
    static TabItem() => FocusableProperty.OverrideMetadata(typeof(TabItem), new PropertyMetadata(true));
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TabItemAutomationPeer(this);
    }

    private TabControl? _tabControl;

    internal TabControl? TabControl
    {
        get => _tabControl;
        set
        {
            if (ReferenceEquals(_tabControl, value))
            {
                return;
            }

            _tabControl = value;
            UpdateTabStripPlacement();
        }
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        Selector.IsSelectedProperty.AddOwner(typeof(TabItem),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault |
                FrameworkPropertyMetadataOptions.Journal,
                OnIsSelectedChanged));

    private static readonly DependencyPropertyKey TabStripPlacementPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(TabStripPlacement), typeof(Dock), typeof(TabItem),
            new PropertyMetadata(Dock.Top, OnVisualStatePropertyChanged));

    /// <summary>Identifies the read-only <see cref="TabStripPlacement"/> dependency property.</summary>
    public static readonly DependencyProperty TabStripPlacementProperty =
        TabStripPlacementPropertyKey.DependencyProperty;

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
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Gets the placement of the owning <see cref="TabControl"/>'s tab strip.</summary>
    public Dock TabStripPlacement => (Dock)(GetValue(TabStripPlacementProperty) ?? Dock.Top);

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
        get => (double)GetValue(IndicatorHeightProperty)!;
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
        // The template presents only Header. Content remains logical content and is
        // presented by the owning TabControl instead of becoming a TabItem visual child.
        UseTemplateContentManagement();
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        TouchHelper.SetIsRippleEnabled(this, true);
    }

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;
        TabControl?.SelectTab(this);
        e.Handled = true;
    }

    /// <summary>
    /// Override to prevent Content from being added as a visual child.
    /// TabItem only displays Header in the tab; Content is shown by TabControl in the content area.
    /// </summary>
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        TabControl?.OnSelectedTabContentChanged(this, newContent);
        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void OnContentTemplateChanged(DataTemplate? oldContentTemplate, DataTemplate? newContentTemplate)
    {
        base.OnContentTemplateChanged(oldContentTemplate, newContentTemplate);
        TabControl?.OnSelectedTabContentPresentationChanged(this);
    }

    /// <inheritdoc />
    protected override void OnContentTemplateSelectorChanged(
        DataTemplateSelector? oldContentTemplateSelector,
        DataTemplateSelector? newContentTemplateSelector)
    {
        base.OnContentTemplateSelectorChanged(oldContentTemplateSelector, newContentTemplateSelector);
        TabControl?.OnSelectedTabContentPresentationChanged(this);
    }

    /// <inheritdoc />
    protected override void OnContentStringFormatChanged(
        string? oldContentStringFormat,
        string? newContentStringFormat)
    {
        base.OnContentStringFormatChanged(oldContentStringFormat, newContentStringFormat);
        TabControl?.OnSelectedTabContentPresentationChanged(this);
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabItem tabItem)
        {
            tabItem.InvalidateVisual();
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || e.ChangedButton != MouseButton.Left) return;
        Focus();
        TabControl?.SelectTab(this);
        e.Handled = true;
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabItem tabItem)
        {
            var isSelected = (bool)(e.NewValue ?? false);
            if (isSelected)
            {
                tabItem.OnSelected(new RoutedEventArgs(Selector.SelectedEvent, tabItem));
            }
            else
            {
                tabItem.OnUnselected(new RoutedEventArgs(Selector.UnselectedEvent, tabItem));
            }

            tabItem.TabControl?.OnContainerSelectionChanged(tabItem, isSelected);
            tabItem.UpdateVisualStates();
        }
    }

    internal void UpdateTabStripPlacement() =>
        SetValue(TabStripPlacementPropertyKey, TabControl?.TabStripPlacement ?? Dock.Top);

    private static void OnVisualStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TabItem)d).UpdateVisualStates();

    /// <summary>Raises the attached <see cref="Selector.SelectedEvent"/> event.</summary>
    protected virtual void OnSelected(RoutedEventArgs e) => RaiseEvent(e);

    /// <summary>Raises the attached <see cref="Selector.UnselectedEvent"/> event.</summary>
    protected virtual void OnUnselected(RoutedEventArgs e) => RaiseEvent(e);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualStates();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this) && IsEnabled) TabControl?.SelectTab(this);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == IsMouseOverProperty || e.Property == IsKeyboardFocusedProperty)
            UpdateVisualStates();
        if (e.Property == HorizontalContentAlignmentProperty || e.Property == VerticalContentAlignmentProperty)
            TabControl?.OnSelectedTabContentPresentationChanged(this);
    }

    private void UpdateVisualStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, TabStripPlacement.ToString(), false);
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "MouseOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsEnabled && IsKeyboardFocused ? "Focused" : "Unfocused", false);
    }

}

/// <summary>
/// Represents a control with a header and content.
/// </summary>
public class HeaderedContentControl : ContentControl
{
    private static readonly DependencyPropertyKey HasHeaderPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasHeader), typeof(bool), typeof(HeaderedContentControl),
            new PropertyMetadata(false));

    /// <summary>Identifies the read-only <see cref="HasHeader"/> dependency property.</summary>
    public static readonly DependencyProperty HasHeaderProperty = HasHeaderPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(HeaderedContentControl),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    /// <summary>Identifies the <see cref="HeaderTemplate"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(HeaderedContentControl),
            new PropertyMetadata(null, OnHeaderTemplatePropertyChanged));

    /// <summary>Identifies the <see cref="HeaderTemplateSelector"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(HeaderedContentControl),
            new PropertyMetadata(null, OnHeaderTemplateSelectorPropertyChanged));

    /// <summary>Identifies the <see cref="HeaderStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register(nameof(HeaderStringFormat), typeof(string), typeof(HeaderedContentControl),
            new PropertyMetadata(null, OnHeaderStringFormatPropertyChanged));

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool HasHeader => (bool)(GetValue(HasHeaderProperty) ?? false);

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty);
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

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

    protected virtual void OnHeaderChanged(object? oldHeader, object? newHeader)
    {
        RemoveLogicalChild(oldHeader);
        AddLogicalChild(newHeader);
        InvalidateMeasure();
    }

    protected virtual void OnHeaderStringFormatChanged(string? oldHeaderStringFormat, string? newHeaderStringFormat)
    {
        InvalidateMeasure();
    }

    protected virtual void OnHeaderTemplateChanged(DataTemplate? oldHeaderTemplate, DataTemplate? newHeaderTemplate)
    {
        InvalidateMeasure();
    }

    protected virtual void OnHeaderTemplateSelectorChanged(
        DataTemplateSelector? oldHeaderTemplateSelector,
        DataTemplateSelector? newHeaderTemplateSelector)
    {
        InvalidateMeasure();
    }

    public override string ToString() => Header?.ToString() ?? base.ToString() ?? nameof(HeaderedContentControl);

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (HeaderedContentControl)d;
        control.SetValue(HasHeaderPropertyKey, e.NewValue != null);
        control.OnHeaderChanged(e.OldValue, e.NewValue);
    }

    private static void OnHeaderStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedContentControl)d).OnHeaderStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);

    private static void OnHeaderTemplatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedContentControl)d).OnHeaderTemplateChanged((DataTemplate?)e.OldValue, (DataTemplate?)e.NewValue);

    private static void OnHeaderTemplateSelectorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HeaderedContentControl)d).OnHeaderTemplateSelectorChanged(
            (DataTemplateSelector?)e.OldValue,
            (DataTemplateSelector?)e.NewValue);
}
