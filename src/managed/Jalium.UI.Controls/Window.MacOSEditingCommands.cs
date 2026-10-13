using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI;

public partial class Window
{
    private bool CanExecuteMacOSEditingCommand(int value)
    {
        if (_isClosing || _managedTeardownStarted || !IsEnabled || Visibility != Visibility.Visible ||
            (uint)value > (uint)MacOSEditingCommand.Redo || GetTextInputTarget() is not { } target)
            return false;
        var command = (MacOSEditingCommand)value;
        switch (target)
        {
            case TextBoxBase text:
                return command switch
                {
                    MacOSEditingCommand.Copy => HasTextSelection(text),
                    MacOSEditingCommand.Cut => !text.IsReadOnly && HasTextSelection(text),
                    MacOSEditingCommand.Paste => !text.IsReadOnly && Clipboard.ContainsText(),
                    MacOSEditingCommand.SelectAll => text.HasTextForNativeEditing,
                    MacOSEditingCommand.Undo => !text.IsReadOnly && text.CanUndo,
                    MacOSEditingCommand.Redo => !text.IsReadOnly && text.CanRedo,
                    _ => false
                };
            case EditControl edit:
                return command switch
                {
                    MacOSEditingCommand.Copy => edit.SelectionLength > 0,
                    MacOSEditingCommand.Cut => !edit.IsReadOnly && edit.SelectionLength > 0,
                    MacOSEditingCommand.Paste => !edit.IsReadOnly && Clipboard.ContainsText(),
                    MacOSEditingCommand.SelectAll => edit.Text.Length > 0,
                    MacOSEditingCommand.Undo => !edit.IsReadOnly && edit.CanUndo,
                    MacOSEditingCommand.Redo => !edit.IsReadOnly && edit.CanRedo,
                    _ => false
                };
            case PasswordBox password:
                return command switch
                {
                    MacOSEditingCommand.Paste => !password.IsReadOnly && Clipboard.ContainsText(),
                    MacOSEditingCommand.SelectAll => password.Password.Length > 0,
                    MacOSEditingCommand.Undo => !password.IsReadOnly && password.CanUndo,
                    MacOSEditingCommand.Redo => !password.IsReadOnly && password.CanRedo,
                    _ => false
                };
            case Terminal terminal:
                return command switch
                {
                    MacOSEditingCommand.Copy => terminal.GetSelectedText().Length > 0,
                    MacOSEditingCommand.Paste => !terminal.IsReadOnly && terminal.IsProcessRunning && Clipboard.ContainsText(),
                    MacOSEditingCommand.SelectAll => true,
                    _ => false
                };
            case HexEditor hex:
                return command switch
                {
                    MacOSEditingCommand.Copy => hex.SelectionLength > 0 && hex.Data is { Length: > 0 },
                    MacOSEditingCommand.Paste => !hex.IsReadOnly && hex.Data != null && Clipboard.ContainsText(),
                    MacOSEditingCommand.SelectAll => hex.Data is { Length: > 0 },
                    _ => false
                };
            default:
                // Custom controls can expose the standard routed commands.
                RoutedCommand routed = command switch
                {
                    MacOSEditingCommand.Copy => ApplicationCommands.Copy,
                    MacOSEditingCommand.Cut => ApplicationCommands.Cut,
                    MacOSEditingCommand.Paste => ApplicationCommands.Paste,
                    MacOSEditingCommand.SelectAll => ApplicationCommands.SelectAll,
                    MacOSEditingCommand.Undo => ApplicationCommands.Undo,
                    _ => ApplicationCommands.Redo
                };
                return routed.CanExecute(null, target);
        }
    }

    private static bool HasTextSelection(TextBoxBase text) =>
        text is RichTextBox rich ? !rich.Selection.IsEmpty : text.SelectionLengthCore > 0;
}
