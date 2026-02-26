using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Represents the Application Menu (backstage view) for a Ribbon control.
/// </summary>
[ContentProperty("Items")]
public sealed class RibbonApplicationMenu : ItemsControl
{
    /// <summary>
    /// Gets or sets the small image source for the application button.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }

    /// <summary>
    /// Gets or sets the footer content.
    /// </summary>
    public object? FooterPaneContent { get; set; }

    /// <summary>
    /// Gets or sets the auxiliary pane content.
    /// </summary>
    public object? AuxiliaryPaneContent { get; set; }
}

/// <summary>
/// Represents a menu item in the RibbonApplicationMenu.
/// </summary>
public sealed class RibbonApplicationMenuItem : RibbonMenuButton
{
    /// <summary>
    /// Gets or sets the tooltip footer.
    /// </summary>
    public string? ToolTipFooterTitle { get; set; }

    /// <summary>
    /// Gets or sets the tooltip footer description.
    /// </summary>
    public string? ToolTipFooterDescription { get; set; }
}

/// <summary>
/// Represents a split menu item in the RibbonApplicationMenu.
/// </summary>
public sealed class RibbonApplicationSplitMenuItem : RibbonSplitButton
{
}

/// <summary>
/// Represents the Quick Access Toolbar for a Ribbon.
/// </summary>
[ContentProperty("Items")]
public sealed class RibbonQuickAccessToolBar : ItemsControl
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
public sealed class RibbonContextualTabGroup : Control
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(RibbonContextualTabGroup),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Visibility dependency property.
    /// </summary>
    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.Register("IsGroupVisible", typeof(bool), typeof(RibbonContextualTabGroup),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the header.
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the group.
    /// </summary>
    public Brush? Background { get; set; }
}
