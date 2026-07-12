namespace Jalium.UI.Controls;

/// <summary>
/// Defines the different roles that a <see cref="MenuItem"/> can have.
/// </summary>
public enum MenuItemRole
{
    /// <summary>Top-level menu item that can invoke a command.</summary>
    TopLevelItem,

    /// <summary>Top-level header of a submenu.</summary>
    TopLevelHeader,

    /// <summary>Menu item in a submenu that can invoke a command.</summary>
    SubmenuItem,

    /// <summary>Header of a nested submenu.</summary>
    SubmenuHeader
}

/// <summary>
/// Specifies the formats that an <see cref="InkCanvas"/> can accept from the Clipboard.
/// </summary>
public enum InkCanvasClipboardFormat
{
    /// <summary>Ink serialized format.</summary>
    InkSerializedFormat,

    /// <summary>Text format.</summary>
    Text,

    /// <summary>XAML format.</summary>
    Xaml
}

/// <summary>
/// Specifies the result of a selection hit test on an <see cref="InkCanvas"/>.
/// </summary>
public enum InkCanvasSelectionHitResult
{
    /// <summary>No hit.</summary>
    None = 0,

    /// <summary>Upper-left corner selection handle.</summary>
    TopLeft = 1,

    /// <summary>Upper middle selection handle.</summary>
    Top = 2,

    /// <summary>Upper-right corner selection handle.</summary>
    TopRight = 3,

    /// <summary>Middle right selection handle.</summary>
    Right = 4,

    /// <summary>Lower-right corner selection handle.</summary>
    BottomRight = 5,

    /// <summary>Lower middle selection handle.</summary>
    Bottom = 6,

    /// <summary>Lower-left corner selection handle.</summary>
    BottomLeft = 7,

    /// <summary>Middle left selection handle.</summary>
    Left = 8,

    /// <summary>Within the bounds of the selection adorner.</summary>
    Selection = 9,
}

/// <summary>
/// Provides a resource key for an <see cref="ItemContainerTemplate"/>.
/// </summary>
public class ItemContainerTemplateKey : Jalium.UI.TemplateKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplateKey"/> class.
    /// </summary>
    public ItemContainerTemplateKey() : base(TemplateType.TableTemplate) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplateKey"/> class
    /// with the specified data type.
    /// </summary>
    public ItemContainerTemplateKey(object dataType) : base(TemplateType.TableTemplate, dataType) { }
}
