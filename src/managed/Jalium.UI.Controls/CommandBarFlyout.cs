using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a specialized flyout that provides layout for AppBarButton and related command elements.
/// </summary>
public sealed class CommandBarFlyout : FlyoutBase
{
    /// <summary>
    /// Gets the collection of primary command elements for the CommandBarFlyout.
    /// </summary>
    public ObservableCollection<ICommandBarElement> PrimaryCommands { get; } = new();

    /// <summary>
    /// Gets the collection of secondary command elements for the CommandBarFlyout.
    /// </summary>
    public ObservableCollection<ICommandBarElement> SecondaryCommands { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the secondary commands are always expanded.
    /// </summary>
    public bool AlwaysExpanded { get; set; }

    /// <inheritdoc />
    protected override Control CreatePresenter()
    {
        var commandBar = new CommandBar();

        foreach (var cmd in PrimaryCommands)
            commandBar.PrimaryCommands.Add(cmd);

        foreach (var cmd in SecondaryCommands)
            commandBar.SecondaryCommands.Add(cmd);

        if (AlwaysExpanded)
            commandBar.IsOpen = true;

        return commandBar;
    }
}
