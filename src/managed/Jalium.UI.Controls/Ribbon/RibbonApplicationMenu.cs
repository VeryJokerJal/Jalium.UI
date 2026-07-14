using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Represents the Application Menu (backstage view) for a Ribbon control.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonApplicationMenu : RibbonMenuButton
{
    public static readonly DependencyProperty AuxiliaryPaneContentProperty =
        DependencyProperty.Register(nameof(AuxiliaryPaneContent), typeof(object), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AuxiliaryPaneContentTemplateProperty =
        DependencyProperty.Register(nameof(AuxiliaryPaneContentTemplate), typeof(DataTemplate), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AuxiliaryPaneContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(AuxiliaryPaneContentTemplateSelector), typeof(DataTemplateSelector), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FooterPaneContentProperty =
        DependencyProperty.Register(nameof(FooterPaneContent), typeof(object), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FooterPaneContentTemplateProperty =
        DependencyProperty.Register(nameof(FooterPaneContentTemplate), typeof(DataTemplate), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FooterPaneContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(FooterPaneContentTemplateSelector), typeof(DataTemplateSelector), typeof(RibbonApplicationMenu),
            new PropertyMetadata(null));

    public RibbonApplicationMenu()
    {
        CanAddToQuickAccessToolBarDirectly = false;
        CanUserResizeHorizontally = false;
        CanUserResizeVertically = false;
    }

    public object? AuxiliaryPaneContent
    {
        get => GetValue(AuxiliaryPaneContentProperty);
        set => SetValue(AuxiliaryPaneContentProperty, value);
    }

    public DataTemplate? AuxiliaryPaneContentTemplate
    {
        get => (DataTemplate?)GetValue(AuxiliaryPaneContentTemplateProperty);
        set => SetValue(AuxiliaryPaneContentTemplateProperty, value);
    }

    public DataTemplateSelector? AuxiliaryPaneContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(AuxiliaryPaneContentTemplateSelectorProperty);
        set => SetValue(AuxiliaryPaneContentTemplateSelectorProperty, value);
    }

    public object? FooterPaneContent
    {
        get => GetValue(FooterPaneContentProperty);
        set => SetValue(FooterPaneContentProperty, value);
    }

    public DataTemplate? FooterPaneContentTemplate
    {
        get => (DataTemplate?)GetValue(FooterPaneContentTemplateProperty);
        set => SetValue(FooterPaneContentTemplateProperty, value);
    }

    public DataTemplateSelector? FooterPaneContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(FooterPaneContentTemplateSelectorProperty);
        set => SetValue(FooterPaneContentTemplateSelectorProperty, value);
    }

    protected override FrameworkElement GetContainerForItem(object item) => new RibbonApplicationMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item) =>
        item is RibbonApplicationMenuItem or RibbonApplicationSplitMenuItem or RibbonSeparator or RibbonGallery;

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        SetApplicationMenuLevel(element, RibbonApplicationMenuItemLevel.Top);
    }

    internal static void SetApplicationMenuLevel(FrameworkElement element, RibbonApplicationMenuItemLevel level)
    {
        switch (element)
        {
            case RibbonApplicationMenuItem menuItem:
                menuItem.Level = level;
                break;
            case RibbonApplicationSplitMenuItem splitMenuItem:
                splitMenuItem.Level = level;
                break;
        }
    }
}

/// <summary>
/// Represents a menu item in the RibbonApplicationMenu.
/// </summary>
public class RibbonApplicationMenuItem : RibbonMenuItem
{
    private static readonly DependencyPropertyKey LevelPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Level), typeof(RibbonApplicationMenuItemLevel),
            typeof(RibbonApplicationMenuItem), new PropertyMetadata(RibbonApplicationMenuItemLevel.Top));

    public static readonly DependencyProperty LevelProperty = LevelPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets the visual level of this application-menu item.
    /// </summary>
    public RibbonApplicationMenuItemLevel Level
    {
        get => (RibbonApplicationMenuItemLevel)(GetValue(LevelProperty) ?? RibbonApplicationMenuItemLevel.Top);
        internal set => SetValue(LevelPropertyKey, value);
    }

    protected override FrameworkElement GetContainerForItem(object item) => new RibbonApplicationMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item) =>
        item is RibbonApplicationMenuItem or RibbonApplicationSplitMenuItem or RibbonSeparator or RibbonGallery;

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        RibbonApplicationMenu.SetApplicationMenuLevel(element,
            Level == RibbonApplicationMenuItemLevel.Top
                ? RibbonApplicationMenuItemLevel.Middle
                : RibbonApplicationMenuItemLevel.Sub);
    }
}

/// <summary>
/// Represents a split menu item in the RibbonApplicationMenu.
/// </summary>
public class RibbonApplicationSplitMenuItem : RibbonSplitMenuItem
{
    private static readonly DependencyPropertyKey LevelPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Level), typeof(RibbonApplicationMenuItemLevel),
            typeof(RibbonApplicationSplitMenuItem), new PropertyMetadata(RibbonApplicationMenuItemLevel.Top));

    public static readonly DependencyProperty LevelProperty = LevelPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets the visual level of this application-menu item.
    /// </summary>
    public RibbonApplicationMenuItemLevel Level
    {
        get => (RibbonApplicationMenuItemLevel)(GetValue(LevelProperty) ?? RibbonApplicationMenuItemLevel.Top);
        internal set => SetValue(LevelPropertyKey, value);
    }

    protected override FrameworkElement GetContainerForItem(object item) => new RibbonApplicationMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item) =>
        item is RibbonApplicationMenuItem or RibbonApplicationSplitMenuItem or RibbonSeparator or RibbonGallery;

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        RibbonApplicationMenu.SetApplicationMenuLevel(element,
            Level == RibbonApplicationMenuItemLevel.Top
                ? RibbonApplicationMenuItemLevel.Middle
                : RibbonApplicationMenuItemLevel.Sub);
    }
}

/// <summary>
/// Represents the Quick Access Toolbar for a Ribbon.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonQuickAccessToolBar : ItemsControl
{
    /// <summary>
    /// Gets or sets whether the customize menu button is visible.
    /// </summary>
    public bool IsCustomizeMenuButtonVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the overflow button is visible.
    /// </summary>
    public bool IsOverflowOpen { get; set; }

    /// <summary>
    /// Gets or sets the customize menu items source.
    /// </summary>
    public object? CustomizeMenuItems { get; set; }
}

/// <summary>
/// Represents a contextual tab group header on a Ribbon.
/// </summary>
public class RibbonContextualTabGroup : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Group);

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(RibbonContextualTabGroup),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Visibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public new static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.Register("IsGroupVisible", typeof(bool), typeof(RibbonContextualTabGroup),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the group.
    /// </summary>
    public new Brush? Background { get; set; }

    /// <summary>
    /// Gets or sets whether the contextual group should be shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsGroupVisible
    {
        get => (bool)(GetValue(IsVisibleProperty) ?? true);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the contextual tab group is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public new bool IsVisible
    {
        get => IsGroupVisible;
        set => IsGroupVisible = value;
    }
}
