using Jalium.UI.Documents;
using Jalium.UI.Input;
using EditingCommands = Jalium.UI.Documents.EditingCommands;

namespace Jalium.UI.Tests;

public sealed class EditingCommandsParityTests
{
    [Fact]
    public void DocumentEditingCommandsAreStableAndOwnedByTheWpfNamespaceType()
    {
        RoutedUICommand[] commands =
        [
            EditingCommands.AlignLeft,
            EditingCommands.AlignCenter,
            EditingCommands.AlignRight,
            EditingCommands.AlignJustify,
            EditingCommands.ToggleBullets,
            EditingCommands.ToggleNumbering,
            EditingCommands.IncreaseIndentation,
            EditingCommands.DecreaseIndentation,
            EditingCommands.IncreaseFontSize,
            EditingCommands.DecreaseFontSize,
            EditingCommands.ToggleSubscript,
            EditingCommands.ToggleSuperscript,
            EditingCommands.CorrectSpellingError,
            EditingCommands.IgnoreSpellingError,
            EditingCommands.MoveUpByParagraph,
            EditingCommands.MoveDownByParagraph,
            EditingCommands.MoveUpByPage,
            EditingCommands.MoveDownByPage,
            EditingCommands.SelectUpByParagraph,
            EditingCommands.SelectDownByParagraph,
            EditingCommands.SelectUpByPage,
            EditingCommands.SelectDownByPage,
        ];

        Assert.True(commands.Length == 22);
        Assert.All(commands, command => Assert.Equal(typeof(EditingCommands), command.OwnerType));
        Assert.True(commands.Select(command => command.Name).Distinct().Count() == commands.Length);
        Assert.Same(EditingCommands.AlignLeft, EditingCommands.AlignLeft);
    }
}
