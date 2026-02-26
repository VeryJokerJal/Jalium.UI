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
    None,

    /// <summary>Upper middle selection handle.</summary>
    Top,

    /// <summary>Lower middle selection handle.</summary>
    Bottom,

    /// <summary>Middle left selection handle.</summary>
    Left,

    /// <summary>Middle right selection handle.</summary>
    Right,

    /// <summary>Upper-left corner selection handle.</summary>
    TopLeft,

    /// <summary>Upper-right corner selection handle.</summary>
    TopRight,

    /// <summary>Lower-left corner selection handle.</summary>
    BottomLeft,

    /// <summary>Lower-right corner selection handle.</summary>
    BottomRight,

    /// <summary>Within the bounds of the selection adorner.</summary>
    Selection
}

/// <summary>
/// Specifies whether the navigation UI of a Frame is visible.
/// </summary>
public enum NavigationUIVisibility
{
    /// <summary>The navigation UI is visible only when there is a navigation history.</summary>
    Automatic,

    /// <summary>The navigation UI is always visible.</summary>
    Visible,

    /// <summary>The navigation UI is never visible.</summary>
    Hidden
}

/// <summary>
/// Specifies whether a Frame uses its own journal.
/// </summary>
public enum JournalOwnership
{
    /// <summary>The journal ownership is determined automatically.</summary>
    Automatic,

    /// <summary>The Frame maintains its own journal.</summary>
    OwnsJournal,

    /// <summary>The Frame uses the journal of the nearest parent navigation host.</summary>
    UsesParentJournal
}

/// <summary>
/// Provides a base class for defining template resource keys.
/// </summary>
public abstract class TemplateKey
{
    /// <summary>Gets or sets the data type associated with this key.</summary>
    public object? DataType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateKey"/> class.
    /// </summary>
    protected TemplateKey(object? dataType)
    {
        DataType = dataType;
    }

    public override bool Equals(object? obj) =>
        obj is TemplateKey other && Equals(DataType, other.DataType);

    public override int GetHashCode() => DataType?.GetHashCode() ?? 0;

    public override string ToString() => DataType?.ToString() ?? string.Empty;
}

/// <summary>
/// Provides a resource key for an <see cref="ItemContainerTemplate"/>.
/// </summary>
public sealed class ItemContainerTemplateKey : TemplateKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplateKey"/> class.
    /// </summary>
    public ItemContainerTemplateKey() : base(typeof(ItemContainerTemplate)) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplateKey"/> class
    /// with the specified data type.
    /// </summary>
    public ItemContainerTemplateKey(object dataType) : base(dataType) { }
}
