namespace Jalium.UI.Controls;

/// <summary>
/// Defines how a group of items should look at each level.
/// </summary>
public sealed class GroupStyle
{
    /// <summary>
    /// Gets the default GroupStyle.
    /// </summary>
    public static GroupStyle Default { get; } = new();

    /// <summary>
    /// Gets or sets the style that is applied to the GroupItem generated for each item.
    /// </summary>
    public Style? ContainerStyle { get; set; }

    /// <summary>
    /// Gets or sets the style selector for the container style.
    /// </summary>
    public StyleSelector? ContainerStyleSelector { get; set; }

    /// <summary>
    /// Gets or sets the template that is used to display the group header.
    /// </summary>
    public DataTemplate? HeaderTemplate { get; set; }

    /// <summary>
    /// Gets or sets the template selector for the header template.
    /// </summary>
    public DataTemplateSelector? HeaderTemplateSelector { get; set; }

    /// <summary>
    /// Gets or sets a composite string that specifies how to format the header if it is displayed as a string.
    /// </summary>
    public string? HeaderStringFormat { get; set; }

    /// <summary>
    /// Gets or sets the template that is used to display the group panel.
    /// </summary>
    public ItemsPanelTemplate? Panel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the items in the group should be displayed
    /// without any visual expansion ("flat").
    /// </summary>
    public bool HidesIfEmpty { get; set; }

    /// <summary>
    /// Gets or sets a value that affects whether items in the corresponding level of grouping
    /// have alternating appearances.
    /// </summary>
    public int AlternationCount { get; set; }
}

/// <summary>
/// Represents a group item container in an ItemsControl.
/// </summary>
public class GroupItem : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GroupItemAutomationPeer(this);
    }
}
