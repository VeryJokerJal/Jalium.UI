namespace Jalium.UI.Controls;

/// <summary>
/// Defines the compact state and overflow order for elements in a CommandBar.
/// </summary>
public interface ICommandBarElement
{
    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    bool IsCompact { get; set; }

    /// <summary>
    /// Gets or sets the priority of a command element's dynamic overflow behavior.
    /// </summary>
    int DynamicOverflowOrder { get; set; }
}

/// <summary>
/// Specifies whether the label is displayed next to or below the icon in an AppBarButton.
/// </summary>
public enum CommandBarLabelPosition
{
    /// <summary>
    /// The label is shown below the icon.
    /// </summary>
    Default,

    /// <summary>
    /// The label is not shown.
    /// </summary>
    Collapsed
}

/// <summary>
/// Specifies the display mode for a CommandBar when it is not open.
/// </summary>
public enum CommandBarClosedDisplayMode
{
    /// <summary>
    /// The command bar shows the icon and label of primary commands.
    /// </summary>
    Compact,

    /// <summary>
    /// Only the ellipsis (More) button is shown.
    /// </summary>
    Minimal,

    /// <summary>
    /// The command bar is not shown.
    /// </summary>
    Hidden
}

/// <summary>
/// Specifies the default label position for commands in a CommandBar.
/// </summary>
public enum CommandBarDefaultLabelPosition
{
    /// <summary>
    /// Labels are shown below the icon.
    /// </summary>
    Bottom,

    /// <summary>
    /// Labels are shown to the right of the icon.
    /// </summary>
    Right,

    /// <summary>
    /// Labels are not shown.
    /// </summary>
    Collapsed
}

/// <summary>
/// Specifies the visibility of the overflow button in a CommandBar.
/// </summary>
public enum CommandBarOverflowButtonVisibility
{
    /// <summary>
    /// The overflow button is shown automatically when there are secondary commands.
    /// </summary>
    Auto,

    /// <summary>
    /// The overflow button is always shown.
    /// </summary>
    Visible,

    /// <summary>
    /// The overflow button is not shown.
    /// </summary>
    Collapsed
}
