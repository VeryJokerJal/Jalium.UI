using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a container for a group of commands or controls.
/// </summary>
public class ToolBar : HeaderedItemsControl
{
    private static readonly ResourceKey s_buttonStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(ButtonStyleKey));
    private static readonly ResourceKey s_checkBoxStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(CheckBoxStyleKey));
    private static readonly ResourceKey s_comboBoxStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(ComboBoxStyleKey));
    private static readonly ResourceKey s_menuStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(MenuStyleKey));
    private static readonly ResourceKey s_radioButtonStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(RadioButtonStyleKey));
    private static readonly ResourceKey s_separatorStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(SeparatorStyleKey));
    private static readonly ResourceKey s_textBoxStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(TextBoxStyleKey));
    private static readonly ResourceKey s_toggleButtonStyleKey = new ComponentResourceKey(typeof(ToolBar), nameof(ToggleButtonStyleKey));
    private readonly ToolBarOverflowPanel _overflowPanel;
    private readonly List<UIElement> _containerOrder = new();

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ToolBarAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Band dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BandProperty =
        DependencyProperty.Register(nameof(Band), typeof(int), typeof(ToolBar),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the BandIndex dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BandIndexProperty =
        DependencyProperty.Register(nameof(BandIndex), typeof(int), typeof(ToolBar),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the IsOverflowOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOverflowOpenProperty =
        DependencyProperty.Register(nameof(IsOverflowOpen), typeof(bool), typeof(ToolBar),
            new PropertyMetadata(false, OnIsOverflowOpenChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    private static readonly DependencyPropertyKey OrientationPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            nameof(Orientation),
            typeof(Orientation),
            typeof(ToolBar),
            new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged),
            IsValidOrientation);

    public static readonly DependencyProperty OrientationProperty =
        OrientationPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the OverflowMode attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty OverflowModeProperty =
        DependencyProperty.RegisterAttached("OverflowMode", typeof(OverflowMode), typeof(ToolBar),
            new PropertyMetadata(OverflowMode.AsNeeded, OnOverflowModeChanged), IsValidOverflowMode);

    /// <summary>
    /// Identifies the IsOverflowItem attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    private static readonly DependencyPropertyKey IsOverflowItemPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsOverflowItem", typeof(bool), typeof(ToolBar),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsOverflowItemProperty =
        IsOverflowItemPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasOverflowItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasOverflowItems), typeof(bool), typeof(ToolBar),
            new PropertyMetadata(false));

    /// <summary>Identifies the read-only overflow state property.</summary>
    public static readonly DependencyProperty HasOverflowItemsProperty =
        HasOverflowItemsPropertyKey.DependencyProperty;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets a value that indicates where the toolbar should be located in the ToolBarTray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int Band
    {
        get => (int)GetValue(BandProperty)!;
        set => SetValue(BandProperty, value);
    }

    /// <summary>
    /// Gets or sets the band index number that indicates the position of the toolbar on the band.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int BandIndex
    {
        get => (int)GetValue(BandIndexProperty)!;
        set => SetValue(BandIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the ToolBar overflow area is currently visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOverflowOpen
    {
        get => (bool)GetValue(IsOverflowOpenProperty)!;
        set => SetValue(IsOverflowOpenProperty, value);
    }

    /// <summary>
    /// Gets a value that indicates whether the toolbar has items that are not visible.
    /// </summary>
    public bool HasOverflowItems => (bool)(GetValue(HasOverflowItemsProperty) ?? false);

    /// <summary>
    /// Gets or sets the orientation of the ToolBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
    }

    public static ResourceKey ButtonStyleKey => s_buttonStyleKey;
    public static ResourceKey CheckBoxStyleKey => s_checkBoxStyleKey;
    public static ResourceKey ComboBoxStyleKey => s_comboBoxStyleKey;
    public static ResourceKey MenuStyleKey => s_menuStyleKey;
    public static ResourceKey RadioButtonStyleKey => s_radioButtonStyleKey;
    public static ResourceKey SeparatorStyleKey => s_separatorStyleKey;
    public static ResourceKey TextBoxStyleKey => s_textBoxStyleKey;
    public static ResourceKey ToggleButtonStyleKey => s_toggleButtonStyleKey;

    #endregion

    #region Attached Properties

    /// <summary>
    /// Gets the value of the OverflowMode attached property for an object.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static OverflowMode GetOverflowMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (OverflowMode)(element.GetValue(OverflowModeProperty) ?? OverflowMode.AsNeeded);
    }

    /// <summary>
    /// Sets the value of the OverflowMode attached property for an object.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetOverflowMode(DependencyObject element, OverflowMode mode)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OverflowModeProperty, mode);
    }

    /// <summary>
    /// Gets the value of the IsOverflowItem attached property for an object.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsOverflowItem(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsOverflowItemProperty) ?? false);
    }

    internal static void SetIsOverflowItem(DependencyObject element, bool value)
    {
        element.SetValue(IsOverflowItemPropertyKey, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolBar"/> class.
    /// </summary>
    public ToolBar()
    {
        _overflowPanel = new ToolBarOverflowPanel
        {
            ToolBarOwner = this,
            Visibility = Visibility.Collapsed
        };
        AddVisualChild(_overflowPanel);
    }

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new ToolBarPanel
        {
            ToolBarOwner = this,
            Orientation = Orientation
        };
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element.Style != null)
        {
            return;
        }

        ResourceKey? key = element switch
        {
            CheckBox => CheckBoxStyleKey,
            RadioButton => RadioButtonStyleKey,
            ToggleButton => ToggleButtonStyleKey,
            Button => ButtonStyleKey,
            ComboBox => ComboBoxStyleKey,
            Menu => MenuStyleKey,
            Separator => SeparatorStyleKey,
            TextBox => TextBoxStyleKey,
            _ => null
        };

        if (key != null && TryFindResource(key) is Style style)
        {
            element.Style = style;
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RestoreAllItemsToMainPanel();
        base.OnItemsChanged(e);
    }

    /// <inheritdoc />
    protected override void RefreshItems()
    {
        RestoreAllItemsToMainPanel();
        base.RefreshItems();
        ConfigureMainPanel();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Template != null)
        {
            var templatedSize = base.MeasureOverride(availableSize);
            UpdateOverflowStateFromCurrentHost(availableSize);
            return templatedSize;
        }

        if (ItemsHost == null)
        {
            RefreshItems();
        }

        UpdateOverflowStateFromCurrentHost(availableSize);
        if (ItemsHost is not Panel mainPanel)
        {
            return default;
        }

        mainPanel.Measure(availableSize);
        _overflowPanel.Measure(new Size(
            double.IsFinite(availableSize.Width) ? Math.Max(availableSize.Width, 0) : 200,
            double.PositiveInfinity));
        return mainPanel.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Template != null)
        {
            var result = base.ArrangeOverride(finalSize);
            UpdateOverflowPanelVisibility();
            return result;
        }

        ItemsHost?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        UpdateOverflowPanelVisibility();
        if (_overflowPanel.Visibility == Visibility.Visible)
        {
            var overflowSize = _overflowPanel.DesiredSize;
            var origin = Orientation == Orientation.Horizontal
                ? new Point(0, finalSize.Height)
                : new Point(finalSize.Width, 0);
            _overflowPanel.Arrange(new Rect(origin.X, origin.Y, overflowSize.Width, overflowSize.Height));
        }
        else
        {
            _overflowPanel.Arrange(default);
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount
    {
        get
        {
            if (Template != null)
            {
                return base.VisualChildrenCount;
            }

            return ItemsHost == null ? 0 : 2;
        }
    }

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (Template != null)
        {
            return base.GetVisualChild(index);
        }

        if (ItemsHost == null)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index switch
        {
            0 => ItemsHost,
            1 => _overflowPanel,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    internal ToolBarOverflowPanel OverflowPanel => _overflowPanel;

    internal void SetOrientationFromTray(Orientation orientation) =>
        SetValue(OrientationPropertyKey, orientation);

    private void UpdateOverflowStateFromCurrentHost(Size availableSize)
    {
        RestoreAllItemsToMainPanel();
        if (ItemsHost is not ToolBarPanel mainPanel)
        {
            SetValue(HasOverflowItemsPropertyKey, false);
            return;
        }

        ConfigureMainPanel();
        _containerOrder.Clear();
        foreach (UIElement child in mainPanel.Children)
        {
            _containerOrder.Add(child);
        }

        var isHorizontal = Orientation == Orientation.Horizontal;
        var childConstraint = isHorizontal
            ? new Size(double.PositiveInfinity, availableSize.Height)
            : new Size(availableSize.Width, double.PositiveInfinity);
        foreach (var child in _containerOrder)
        {
            child.Measure(childConstraint);
        }

        var available = isHorizontal ? availableSize.Width : availableSize.Height;
        var neverExtent = _containerOrder
            .Where(child => GetOverflowMode(child) == OverflowMode.Never)
            .Sum(child => GetMainExtent(child, isHorizontal));
        var asNeededAvailable = double.IsFinite(available)
            ? Math.Max(0, available - neverExtent)
            : double.PositiveInfinity;
        var asNeededUsed = 0.0;
        var overflowItems = new List<UIElement>();
        foreach (var child in _containerOrder)
        {
            var mode = GetOverflowMode(child);
            var extent = GetMainExtent(child, isHorizontal);
            var overflow = mode == OverflowMode.Always ||
                (mode == OverflowMode.AsNeeded && asNeededUsed + extent > asNeededAvailable);
            if (mode == OverflowMode.AsNeeded && !overflow)
            {
                asNeededUsed += extent;
            }

            SetIsOverflowItem(child, overflow);
            if (overflow)
            {
                overflowItems.Add(child);
            }
        }

        foreach (var child in overflowItems)
        {
            mainPanel.Children.Remove(child);
            _overflowPanel.Children.Add(child);
        }

        mainPanel.SetOverflowItems(overflowItems);
        SetValue(HasOverflowItemsPropertyKey, overflowItems.Count > 0);
        if (overflowItems.Count == 0 && IsOverflowOpen)
        {
            SetCurrentValue(IsOverflowOpenProperty, false);
        }

        UpdateOverflowPanelVisibility();
    }

    private void RestoreAllItemsToMainPanel()
    {
        if (_overflowPanel.Children.Count == 0 || ItemsHost is not ToolBarPanel mainPanel)
        {
            return;
        }

        var ordered = _containerOrder.Count > 0
            ? _containerOrder.ToArray()
            : mainPanel.Children.Concat(_overflowPanel.Children).ToArray();
        mainPanel.Children.Clear();
        _overflowPanel.Children.Clear();
        foreach (var child in ordered)
        {
            SetIsOverflowItem(child, false);
            mainPanel.Children.Add(child);
        }

        mainPanel.SetOverflowItems(Array.Empty<UIElement>());
    }

    private void ConfigureMainPanel()
    {
        if (ItemsHost is ToolBarPanel panel)
        {
            panel.ToolBarOwner = this;
            panel.Orientation = Orientation;
        }

        _overflowPanel.ToolBarOwner = this;
    }

    private void UpdateOverflowPanelVisibility()
    {
        _overflowPanel.Visibility = HasOverflowItems && IsOverflowOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static double GetMainExtent(UIElement element, bool horizontal) =>
        horizontal ? element.DesiredSize.Width : element.DesiredSize.Height;

    private static bool IsValidOverflowMode(object? value) =>
        value is OverflowMode.AsNeeded or OverflowMode.Always or OverflowMode.Never;

    private static bool IsValidOrientation(object? value) =>
        value is Orientation.Horizontal or Orientation.Vertical;

    private static void OnOverflowModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        for (Visual? current = d as Visual; current != null; current = current.VisualParent)
        {
            if (current is ToolBarPanel { ToolBarOwner: { } owner })
            {
                owner.InvalidateMeasure();
                return;
            }

            if (current is ToolBarOverflowPanel { ToolBarOwner: { } overflowOwner })
            {
                overflowOwner.InvalidateMeasure();
                return;
            }
        }
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toolBar = (ToolBar)d;
        toolBar.ConfigureMainPanel();
        toolBar.InvalidateMeasure();
    }

    private static void OnIsOverflowOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toolBar = (ToolBar)d;
        toolBar.UpdateOverflowPanelVisibility();
        toolBar.InvalidateArrange();
    }
}

/// <summary>
/// Represents a container that handles the layout of a ToolBar.
/// </summary>
public class ToolBarTray : FrameworkElement
{
    private readonly ObservableCollection<ToolBar> _toolBars;
    private readonly HashSet<ToolBar> _attachedToolBars = new();

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ToolBarTray),
            new PropertyMetadata(Orientation.Horizontal, OnTrayOrientationChanged), IsValidTrayOrientation);

    /// <summary>
    /// Identifies the IsLocked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsLockedProperty =
        DependencyProperty.RegisterAttached(nameof(IsLocked), typeof(bool), typeof(ToolBarTray),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(ToolBarTray),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets the collection of ToolBar elements inside a ToolBarTray.
    /// </summary>
    public Collection<ToolBar> ToolBars => _toolBars;

    /// <summary>
    /// Gets or sets a value that specifies the orientation of a ToolBarTray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether ToolBar elements can be moved inside the ToolBarTray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty)!;
        set => SetValue(IsLockedProperty, value);
    }

    public static bool GetIsLocked(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsLockedProperty) ?? false);
    }

    public static void SetIsLocked(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsLockedProperty, value);
    }

    /// <summary>
    /// Gets or sets a brush to use for the background color of the ToolBarTray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolBarTray"/> class.
    /// </summary>
    public ToolBarTray()
    {
        _toolBars = new ObservableCollection<ToolBar>();
        _toolBars.CollectionChanged += OnToolBarsChanged;
    }

    private void OnToolBarsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var current = new HashSet<ToolBar>(_toolBars);
        foreach (var removed in _attachedToolBars.Where(toolBar => !current.Contains(toolBar)).ToArray())
        {
            RemoveVisualChild(removed);
            RemoveLogicalChild(removed);
            removed.SetOrientationFromTray(Orientation.Horizontal);
            _attachedToolBars.Remove(removed);
        }

        foreach (var added in current.Where(toolBar => !_attachedToolBars.Contains(toolBar)))
        {
            AddVisualChild(added);
            AddLogicalChild(added);
            added.SetOrientationFromTray(Orientation);
            _attachedToolBars.Add(added);
        }

        InvalidateMeasure();
    }

    private static void OnTrayOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tray = (ToolBarTray)d;
        var orientation = (Orientation)(e.NewValue ?? Orientation.Horizontal);
        foreach (var toolBar in tray._toolBars)
        {
            toolBar.SetOrientationFromTray(orientation);
        }

        tray.InvalidateMeasure();
    }

    private static bool IsValidTrayOrientation(object? value) =>
        value is Orientation.Horizontal or Orientation.Vertical;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var totalWidth = 0.0;
        var totalHeight = 0.0;
        var maxBand = 0;

        foreach (var toolBar in _toolBars)
        {
            maxBand = Math.Max(maxBand, toolBar.Band);
        }

        // Group toolbars by band
        for (var band = 0; band <= maxBand; band++)
        {
            var bandToolBars = _toolBars.Where(t => t.Band == band).OrderBy(t => t.BandIndex).ToList();
            var bandWidth = 0.0;
            var bandHeight = 0.0;

            foreach (var toolBar in bandToolBars)
            {
                toolBar.Measure(availableSize);
                if (Orientation == Orientation.Horizontal)
                {
                    bandWidth += toolBar.DesiredSize.Width;
                    bandHeight = Math.Max(bandHeight, toolBar.DesiredSize.Height);
                }
                else
                {
                    bandHeight += toolBar.DesiredSize.Height;
                    bandWidth = Math.Max(bandWidth, toolBar.DesiredSize.Width);
                }
            }

            if (Orientation == Orientation.Horizontal)
            {
                totalWidth = Math.Max(totalWidth, bandWidth);
                totalHeight += bandHeight;
            }
            else
            {
                totalWidth += bandWidth;
                totalHeight = Math.Max(totalHeight, bandHeight);
            }
        }

        return new Size(totalWidth, totalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var maxBand = _toolBars.Count > 0 ? _toolBars.Max(t => t.Band) : 0;
        var offset = 0.0;

        for (var band = 0; band <= maxBand; band++)
        {
            var bandToolBars = _toolBars.Where(t => t.Band == band).OrderBy(t => t.BandIndex).ToList();
            var bandSize = 0.0;
            var bandOffset = 0.0;

            foreach (var toolBar in bandToolBars)
            {
                Rect rect;
                if (Orientation == Orientation.Horizontal)
                {
                    rect = new Rect(bandOffset, offset, toolBar.DesiredSize.Width, toolBar.DesiredSize.Height);
                    bandOffset += toolBar.DesiredSize.Width;
                    bandSize = Math.Max(bandSize, toolBar.DesiredSize.Height);
                }
                else
                {
                    rect = new Rect(offset, bandOffset, toolBar.DesiredSize.Width, toolBar.DesiredSize.Height);
                    bandOffset += toolBar.DesiredSize.Height;
                    bandSize = Math.Max(bandSize, toolBar.DesiredSize.Width);
                }
                toolBar.Arrange(rect);
            }

            if (Orientation == Orientation.Horizontal)
                offset += bandSize;
            else
                offset += bandSize;
        }

        return finalSize;
    }
}

/// <summary>
/// Specifies how a ToolBar item is placed in the main ToolBar panel and in the overflow panel.
/// </summary>
public enum OverflowMode
{
    /// <summary>
    /// Item moves between the main panel and overflow panel, depending on the available space.
    /// </summary>
    AsNeeded,

    /// <summary>
    /// Item is permanently placed in the overflow panel.
    /// </summary>
    Always,

    /// <summary>
    /// Item is never placed in the overflow panel.
    /// </summary>
    Never
}
